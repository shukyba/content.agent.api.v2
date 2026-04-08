using System.Text;
using System.Text.Json;
using ContentAgent.Api.Models;
using Google.GenAI;
using Google.GenAI.Types;
using SysFile = System.IO.File;

namespace ContentAgent.Api.Services;

public interface IGeminiService
{
    /// <param name="structuredAppendKeyPaths">Repo-relative paths where appendKey must use JSON <c>items</c> (see agent config).</param>
    /// <param name="auxiliaryFileRoot">Optional agent folder: if a path is missing under <paramref name="repoPath"/>, try the same relative path here (e.g. bundled schema next to config).</param>
    Task<List<FileEdit>> GetWebsiteEditsAsync(string agentTodoContent, string repoPath, IReadOnlyList<string>? schemaPaths = null, IReadOnlyList<string>? dataPaths = null, IReadOnlyList<string>? structuredAppendKeyPaths = null, string? auxiliaryFileRoot = null, IReadOnlyList<(string path, string key)>? excludedAppendKeys = null, CancellationToken cancellationToken = default);
}

public class GeminiService : IGeminiService
{
    /// <summary>dbo.LOG Message/Exception/Parameters are varchar(4000); keep a margin for template text.</summary>
    private const int LogFieldMaxChars = 3500;

    /// <summary>Short preview lines so Message + prefix stay under 4000.</summary>
    private const int LogResponsePreviewChars = 1800;

    /// <summary>Model id for v1beta (e.g. .../models/gemini-2.5-flash:generateContent).</summary>
    private const string ModelName = "gemini-2.5-flash";
    private const int MaxQuotaRetries = 2;
    private readonly string _apiKey;
    private readonly ILogger<GeminiService>? _logger;

    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public GeminiService(string apiKey, ILogger<GeminiService>? logger = null)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _logger = logger;
    }

    public async Task<List<FileEdit>> GetWebsiteEditsAsync(string agentTodoContent, string repoPath, IReadOnlyList<string>? schemaPaths = null, IReadOnlyList<string>? dataPaths = null, IReadOnlyList<string>? structuredAppendKeyPaths = null, string? auxiliaryFileRoot = null, IReadOnlyList<(string path, string key)>? excludedAppendKeys = null, CancellationToken cancellationToken = default)
    {
        var hasExplicitSchema = schemaPaths != null && schemaPaths.Count > 0;
        var hasExplicitData = dataPaths != null && dataPaths.Count > 0;

        IReadOnlyList<(string path, string fullContent)> schemaFiles = hasExplicitSchema
            ? GetFullTextFilesFromList(repoPath, schemaPaths!, auxiliaryFileRoot)
            : Array.Empty<(string, string)>();
        IReadOnlyList<(string path, string fullContent)> dataFiles = hasExplicitData
            ? GetFullTextFilesFromList(repoPath, dataPaths!, auxiliaryFileRoot)
            : Array.Empty<(string, string)>();

        var prompt = BuildPrompt(agentTodoContent, schemaFiles, dataFiles, structuredAppendKeyPaths, excludedAppendKeys);

        using var client = new Client(vertexAI: null, apiKey: _apiKey, credential: null, project: null, location: null, httpOptions: null);
        // Cannot use ResponseMimeType = "application/json" together with tools (Google Search) — API returns ClientError.
        var config = new GenerateContentConfig
        {
            MaxOutputTokens = 8192,
            Tools = new List<Tool> { new Tool { GoogleSearch = new GoogleSearch() } },
            // Gemini 2.5 may omit response.Text when thoughts are hidden; include thoughts so Parts carry visible text.
            ThinkingConfig = new ThinkingConfig { IncludeThoughts = true }
        };

        string? text = null;
        for (var attempt = 0; attempt <= MaxQuotaRetries; attempt++)
        {
            try
            {
                var response = await client.Models.GenerateContentAsync(ModelName, prompt, config, cancellationToken);
                text = ExtractModelText(response, _logger, out var textSegmentCount);
                if (!string.IsNullOrEmpty(text))
                {
                    var trimmed = text.Trim();
                    var leadCode = trimmed.Length > 0 ? (int)trimmed[0] : -1;
                    _logger?.LogInformation(
                        "Gemini raw response: length={Length}, textSegments={Segments}, leadingCharCode={LeadCode}, hasJsonFence={HasFence}, firstBracketIndex={BracketIdx}",
                        text.Length,
                        textSegmentCount,
                        leadCode,
                        trimmed.Contains("```", StringComparison.Ordinal),
                        trimmed.IndexOf('['));
                    _logger?.LogInformation(
                        "Gemini raw response preview: {Preview}",
                        TruncateForLog(trimmed, LogResponsePreviewChars));
                    if (trimmed.Length > 400)
                        _logger?.LogDebug(
                            "Gemini raw response tail: {Tail}",
                            TruncateForLog(trimmed[^Math.Min(600, trimmed.Length)..], 600));
                }

                break;
            }
            catch (Exception ex) when (IsQuotaOrRateLimit(ex) && attempt < MaxQuotaRetries)
            {
                var delay = TimeSpan.FromSeconds(60 * (attempt + 1));
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                // Avoid oversized Exception column on ADONetAppender (varchar(4000)).
                _logger?.LogError(
                    "Gemini GenerateContent failed: {Detail}",
                    TruncateForLog(ex.ToString(), LogFieldMaxChars));
                return new List<FileEdit>();
            }
        }

        if (string.IsNullOrEmpty(text))
        {
            _logger?.LogWarning("Gemini returned no usable text (response.Text empty and no Part.Text found)");
            return new List<FileEdit>();
        }

        var edits = ParseEditsFromJson(text, _logger);
        _logger?.LogInformation("Parsed {Count} file edit(s) from Gemini response", edits.Count);
        return edits;
    }

    private static string BuildPrompt(
        string agentTodoContent,
        IReadOnlyList<(string path, string fullContent)> schemaFiles,
        IReadOnlyList<(string path, string fullContent)> dataFiles,
        IReadOnlyList<string>? structuredAppendKeyPaths,
        IReadOnlyList<(string path, string key)>? excludedAppendKeys)
    {
        var schemaSection = string.Join("\n\n", schemaFiles.Select(f => $"--- {f.path} (full content) ---\n{f.fullContent}"));
        var schemaBlock = schemaFiles.Count > 0
            ? $"\n\nSCHEMA FILES (full content, structure / format docs):\n{schemaSection}"
            : "\n\nSCHEMA FILES: (none)";
        var dataSection = string.Join("\n\n", dataFiles.Select(f => $"--- {f.path} (full content) ---\n{f.fullContent}"));
        var dataBlock = dataFiles.Count > 0
            ? $"\n\nDATA FILES (full content, use for context):\n{dataSection}"
            : "\n\nDATA FILES: (none)";
        var structuredPathsLine = structuredAppendKeyPaths is { Count: > 0 }
            ? string.Join("; ", structuredAppendKeyPaths.Select(p => p.Trim().Replace('\\', '/')))
            : "(none - use raw value for all appendKey edits)";
        var exclusionBlock = excludedAppendKeys is { Count: > 0 }
            ? "\n\nEXCLUDED APPEND KEYS (already present in the repo clone — do NOT use appendKey with these exact path+key pairs; choose a different item from the todo or other edits. If the todo cannot be satisfied without these keys, return an empty JSON array [] or only non-conflicting edits.):\n"
              + string.Join("\n", excludedAppendKeys.Select(e =>
                  $"- path: {e.path.Trim().Replace('\\', '/')} key: {e.key.Trim()}"))
            : "";
        return $@"You are a website updater. Below is the todo.md content describing what changes to make, plus full schema files and data files when configured. You have Google Search grounding enabled; when the todo asks for fresh or current information (e.g. finding new events), use search to get up-to-date results before producing edits.

TODO.MD:
---
{agentTodoContent}
---
{schemaBlock}{dataBlock}
{exclusionBlock}

STRUCTURED APPEND-KEY PATHS (agent configuration - exact repo-relative paths):
{structuredPathsLine}

When these paths are listed, **appendKey** for those files must use a JSON **""items""** array: each element is an object whose property names and string values match the target TypeScript shape (plain text in JSON - no hand-written TS string quotes). The server emits safe TypeScript literals. Do not use raw ""value"" with embedded TS for those paths unless you cannot use ""items"".
When the list is (none), **appendKey** always uses ""value"" (snippet inserted before the top-level object closes) as usual.

When adding new entries to list or data files (e.g. CSV, TS arrays, keyed records), preserve all existing content and append or insert the new item in the same format as existing ones.
**CSV files (.csv):** For any CSV that **already exists** in the repo, **do not** use full replace (path + content)—the pipeline rejects it. Add data only by appending **one new line at a time** with ""editType"": ""appendCsvRow"" (preferred) or ""appendToArray"": ""value"" must be a **single** complete CSV line matching the file's columns. The server appends and **skips** if the first-column id already exists. (Only if the CSV path does not exist yet may you use one full replace to create the file—rare.)

Respond with a JSON array of file edits. Supported edit formats:

1) **Full replace**: use ""path"" and ""content"". **Not allowed for .csv files that already exist.** Use for small non-CSV files (or initial creation of a missing CSV only).

2) **Append to array** (TypeScript arrays): for large TS array files where you only add one new element, use ""path"", ""editType"": ""appendToArray"", ""key"" (optional, for logging), and ""value"": ""the exact text of one array element"". The pipeline inserts before the closing ""];"". Do not use ""content"" for this.

3) **Append CSV row** (CSV only): use ""path"" (must end in .csv), ""editType"": ""appendCsvRow"", ""key"" (optional), and ""value"": ""one single CSV data line"". Prefer this over appendToArray for .csv clarity.

4) **Append to keyed structure**: use ""path"", ""editType"": ""appendKey"", and ""key"": ""the-new-record-key"".
   - If **path** is listed under STRUCTURED APPEND-KEY PATHS above: include **""items""**: [ {{ ""fieldA"": ""plain text"", ""fieldB"": ""plain text"" }}, ... ] matching the file's object shape (omit ""value""). String fields may contain apostrophes and quotes as normal JSON.
   - Otherwise: use **""value""** only (snippet to insert before the top-level object closes).

Only include files you are changing; use exact paths from the schema/data sections when relevant. Return valid JSON only, no markdown or explanation.

Example (appendToArray): [{{""path"": ""src/data/items.ts"", ""editType"": ""appendToArray"", ""key"": ""item-1"", ""value"": ""{{ id: 'item-1', name: 'Example' }}""}}]
Example (appendCsvRow): [{{""path"": ""src/data/rows.csv"", ""editType"": ""appendCsvRow"", ""key"": ""row-1"", ""value"": ""row-1,Label,2026-01-01""}}]
Example (appendKey with items for a structured path): [{{""path"": ""src/data/keyedQna.ts"", ""editType"": ""appendKey"", ""key"": ""record-slug"", ""items"": [{{""question"": ""Hours?"", ""answer"": ""Doors open at 6pm; there isn't a fixed end time.""}}, {{""question"": ""Parking?"", ""answer"": ""Garage on site.""}}]}}]";
    }

    private static List<(string path, string fullContent)> GetFullTextFilesFromList(string repoPath, IReadOnlyList<string> relativePaths, string? auxiliaryFileRoot = null)
    {
        var list = new List<(string path, string fullContent)>();
        foreach (var p in relativePaths)
        {
            if (string.IsNullOrWhiteSpace(p))
                continue;
            var rel = p.Trim().Replace('\\', '/');
            var normalizedRel = rel.Replace('/', Path.DirectorySeparatorChar);
            var primary = Path.GetFullPath(Path.Combine(repoPath, normalizedRel));
            string fullContent;
            try
            {
                if (SysFile.Exists(primary))
                    fullContent = SysFile.ReadAllText(primary);
                else if (!string.IsNullOrEmpty(auxiliaryFileRoot))
                {
                    var alt = Path.GetFullPath(Path.Combine(auxiliaryFileRoot, normalizedRel));
                    fullContent = SysFile.Exists(alt) ? SysFile.ReadAllText(alt) : "(file not present yet)";
                }
                else
                    fullContent = "(file not present yet)";
            }
            catch
            {
                fullContent = "(unreadable)";
            }
            list.Add((rel, fullContent));
        }
        return list;
    }

    /// <summary>Strips a leading markdown fence (e.g. <c>```json</c> … <c>```</c>) so JSON can be parsed.</summary>
    private static string StripMarkdownCodeFence(string text)
    {
        var t = text.Trim();
        if (!t.StartsWith("```", StringComparison.Ordinal))
            return t;

        var firstNewline = t.IndexOf('\n');
        if (firstNewline < 0)
            return t;

        var body = t[(firstNewline + 1)..];
        var close = body.LastIndexOf("```", StringComparison.Ordinal);
        if (close >= 0)
            body = body[..close];

        return body.Trim();
    }

    /// <summary>Finds <c>```json</c> or a plain fence anywhere (after prose / thinking) and returns the inner body.</summary>
    private static string StripMarkdownCodeFenceAnywhere(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var jsonFence = text.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        var plainFence = text.IndexOf("```", StringComparison.Ordinal);
        var useJson = jsonFence >= 0;
        var idx = useJson ? jsonFence : plainFence;
        if (idx < 0)
            return text.Trim();

        var openLen = useJson ? "```json".Length : 3;
        var afterOpen = text[(idx + openLen)..];
        var firstNl = afterOpen.IndexOf('\n');
        if (firstNl >= 0)
            afterOpen = afterOpen[(firstNl + 1)..];

        var close = afterOpen.LastIndexOf("```", StringComparison.Ordinal);
        var body = close >= 0 ? afterOpen[..close] : afterOpen;
        return body.Trim();
    }

    private static bool StartsWithJsonLike(string s)
    {
        var t = s.TrimStart();
        return t.Length > 0 && (t[0] == '[' || t[0] == '{');
    }

    /// <summary>If the model wraps JSON in prose, take the outermost JSON array span.</summary>
    private static string TryIsolateJsonArray(string text)
    {
        var i = text.IndexOf('[');
        var j = text.LastIndexOf(']');
        if (i >= 0 && j > i)
            return text[i..(j + 1)];
        return text;
    }

    private static string TruncateForLog(string? s, int maxChars)
    {
        if (string.IsNullOrEmpty(s) || maxChars <= 0)
            return "";
        if (s.Length <= maxChars)
            return s;
        return s[..maxChars] + $"…(truncated, totalLen={s.Length})";
    }

    private List<FileEdit> ParseEditsFromJson(string text, ILogger<GeminiService>? logger)
    {
        try
        {
            var trimmed = text.Trim();
            // Model often returns "**thinking** ... ```json [ ... ] ```" — leading fence strip is a no-op then.
            var cleaned = StripMarkdownCodeFence(trimmed);
            if (!StartsWithJsonLike(cleaned))
                cleaned = StripMarkdownCodeFenceAnywhere(trimmed);
            if (!StartsWithJsonLike(cleaned))
                cleaned = TryIsolateJsonArray(cleaned);

            try
            {
                var list = JsonSerializer.Deserialize<List<FileEdit>>(cleaned, JsonReadOptions);
                if (list is { Count: > 0 })
                    return list;
                if (list is { Count: 0 })
                    logger?.LogInformation(
                        "Gemini JSON parse: deserialized empty array (cleanedLength={Len}, preview={Preview})",
                        cleaned.Length,
                        TruncateForLog(cleaned, 800));
            }
            catch (Exception ex)
            {
                logger?.LogDebug(
                    "First JSON parse pass failed; trying array slice. Error={Error} cleanedPreview={Preview}",
                    TruncateForLog(ex.ToString(), 1200),
                    TruncateForLog(cleaned, 1200));
            }

            var sliced = TryIsolateJsonArray(cleaned);
            try
            {
                var list = JsonSerializer.Deserialize<List<FileEdit>>(sliced, JsonReadOptions);
                return list ?? new List<FileEdit>();
            }
            catch (Exception ex)
            {
                var sliceLead = sliced.TrimStart();
                var leadCode = sliceLead.Length > 0 ? (int)sliceLead[0] : -1;
                logger?.LogWarning(
                    "Failed to parse Gemini file-edits JSON after fence strip and array isolation. sliceLength={SliceLen} leadCharCode={LeadCode} error={Error} slicePreview={Preview}",
                    sliced.Length,
                    leadCode,
                    TruncateForLog(ex.ToString(), 1500),
                    TruncateForLog(sliced, 1800));
                return new List<FileEdit>();
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(
                "Failed to normalize Gemini response for JSON parsing. error={Error} inputPreview={Preview}",
                TruncateForLog(ex.ToString(), 1500),
                TruncateForLog(text.Trim(), 1800));
            return new List<FileEdit>();
        }
    }

    /// <summary>
    /// <see cref="GenerateContentResponse.Text"/> only concatenates text parts from the first candidate and returns null
    /// when Google Search / thinking leaves only non-aggregated <see cref="Part.Text"/> on parts.
    /// </summary>
    private static string? ExtractModelText(GenerateContentResponse? response, ILogger<GeminiService>? logger, out int textSegmentCount)
    {
        textSegmentCount = 0;
        if (response == null)
            return null;

        var shortcut = response.Text?.Trim();
        if (!string.IsNullOrEmpty(shortcut))
        {
            textSegmentCount = 1;
            logger?.LogDebug("Gemini ExtractModelText: used response.Text shortcut (length={Length})", shortcut.Length);
            return shortcut;
        }

        var sb = new StringBuilder();
        if (response.Candidates != null)
        {
            foreach (var cand in response.Candidates)
            {
                var parts = cand.Content?.Parts;
                if (parts == null)
                    continue;
                foreach (var part in parts)
                {
                    if (string.IsNullOrEmpty(part.Text))
                        continue;
                    textSegmentCount++;
                    AppendPartText(sb, part);
                }
            }
        }

        if (sb.Length > 0)
            logger?.LogDebug("Gemini ExtractModelText: merged {Count} non-empty text part(s)", textSegmentCount);

        if (sb.Length == 0)
        {
            if (response.PromptFeedback != null)
            {
                logger?.LogWarning(
                    "Gemini returned no text; prompt feedback blockReason={Reason}, message={Message}",
                    response.PromptFeedback.BlockReason,
                    response.PromptFeedback.BlockReasonMessage);
            }
            else if (response.Candidates is { Count: > 0 })
            {
                var c0 = response.Candidates[0];
                logger?.LogWarning(
                    "Gemini candidate has no text parts; finishReason={Finish}, contentNull={ContentNull}",
                    c0.FinishReason,
                    c0.Content == null);
            }
            else
                logger?.LogWarning("Gemini response had no candidates");

            return null;
        }

        return sb.ToString().Trim();
    }

    private static void AppendPartText(StringBuilder sb, Part part)
    {
        if (string.IsNullOrEmpty(part.Text))
            return;
        if (sb.Length > 0)
            sb.AppendLine();
        sb.Append(part.Text);
    }

    private static bool IsQuotaOrRateLimit(Exception ex)
    {
        var msg = ex.ToString();
        return msg.Contains("quota", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("resource exhausted", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("429", StringComparison.OrdinalIgnoreCase);
    }
}
