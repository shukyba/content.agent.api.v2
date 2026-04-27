using System.Linq;
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
    /// <param name="structuredAppendArrayPaths">Repo-relative paths where appendToArray must use JSON <c>item</c> objects.</param>
    /// <param name="auxiliaryFileRoot">Optional agent folder: if a path is missing under <paramref name="repoPath"/>, try the same relative path here (e.g. bundled schema next to config).</param>
    Task<List<FileEdit>> GetWebsiteEditsAsync(string agentTodoContent, string repoPath, IReadOnlyList<string>? schemaPaths = null, IReadOnlyList<string>? dataPaths = null, IReadOnlyList<string>? structuredAppendKeyPaths = null, IReadOnlyList<string>? structuredAppendArrayPaths = null, string? auxiliaryFileRoot = null, IReadOnlyList<(string path, string key)>? excludedAppendKeys = null, CancellationToken cancellationToken = default);
}

public class GeminiService : IGeminiService
{
    /// <summary>dbo.LOG Message/Exception/Parameters are varchar(4000); keep a margin for template text.</summary>
    private const int LogFieldMaxChars = 3500;

    /// <summary>Short preview lines so Message + prefix stay under 4000.</summary>
    private const int LogResponsePreviewChars = 1800;

    /// <summary>Model id for v1beta (e.g. .../models/gemini-2.5-flash:generateContent).</summary>
    private const string ModelName = "gemini-2.5-flash";
    private const int MaxQuotaRetries = 0;

    /// <summary>Phase-2 prompt: max chars of phase-1 assistant text (incl. thoughts) for context.</summary>
    private const int Phase1ContextMaxChars = 16000;
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

    public async Task<List<FileEdit>> GetWebsiteEditsAsync(string agentTodoContent, string repoPath, IReadOnlyList<string>? schemaPaths = null, IReadOnlyList<string>? dataPaths = null, IReadOnlyList<string>? structuredAppendKeyPaths = null, IReadOnlyList<string>? structuredAppendArrayPaths = null, string? auxiliaryFileRoot = null, IReadOnlyList<(string path, string key)>? excludedAppendKeys = null, CancellationToken cancellationToken = default)
    {
        var hasExplicitSchema = schemaPaths != null && schemaPaths.Count > 0;
        var hasExplicitData = dataPaths != null && dataPaths.Count > 0;

        IReadOnlyList<(string path, string fullContent)> schemaFiles = hasExplicitSchema
            ? GetFullTextFilesFromList(repoPath, schemaPaths!, auxiliaryFileRoot)
            : Array.Empty<(string, string)>();
        IReadOnlyList<(string path, string fullContent)> dataFiles = hasExplicitData
            ? GetFullTextFilesFromList(repoPath, dataPaths!, auxiliaryFileRoot)
            : Array.Empty<(string, string)>();

        var prompt = BuildPrompt(agentTodoContent, schemaFiles, dataFiles, structuredAppendKeyPaths, structuredAppendArrayPaths, excludedAppendKeys);

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
        GenerateContentResponse? phase1Response = null;
        for (var attempt = 0; attempt <= MaxQuotaRetries; attempt++)
        {
            try
            {
                phase1Response = await client.Models.GenerateContentAsync(ModelName, prompt, config, cancellationToken);
                text = ExtractModelText(phase1Response, _logger, out var textSegmentCount);
                if (!string.IsNullOrEmpty(text))
                {
                    var trimmed = text.Trim();
                    var leadCode = trimmed.Length > 0 ? (int)trimmed[0] : -1;
                    _logger?.LogInformation(
                        "Gemini phase 1 (non-thought text): length={Length}, textSegments={Segments}, leadingCharCode={LeadCode}, hasJsonFence={HasFence}, firstBracketIndex={BracketIdx}",
                        text.Length,
                        textSegmentCount,
                        leadCode,
                        trimmed.Contains("```", StringComparison.Ordinal),
                        trimmed.IndexOf('['));
                    _logger?.LogInformation(
                        "Gemini phase 1 preview: {Preview}",
                        TruncateForLog(trimmed, LogResponsePreviewChars));
                    if (trimmed.Length > 400)
                        _logger?.LogDebug(
                            "Gemini phase 1 tail: {Tail}",
                            TruncateForLog(trimmed[^Math.Min(600, trimmed.Length)..], 600));
                }

                break;
            }
            catch (Exception ex) when (GeminiTransientErrors.IsRetriable(ex) && attempt < MaxQuotaRetries)
            {
                var delay = TimeSpan.FromSeconds(60 * (attempt + 1));
                _logger?.LogInformation(
                    "Gemini GenerateContent retriable failure (attempt {Attempt} of {MaxAttempts}), waiting {DelaySeconds}s: {Reason}",
                    attempt + 1,
                    MaxQuotaRetries + 1,
                    (int)delay.TotalSeconds,
                    TruncateForLog(ex.Message, 500));
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
            _logger?.LogWarning(
                "Gemini phase 1 returned no non-thought text (thought-only or empty); phase 2 will use full task + phase-1 context.");

        var phase1Context = BuildPhase1ContextForFollowUp(phase1Response);
        if (!TryParseEditsFromJson(text ?? "", out var edits, _logger))
        {
            _logger?.LogInformation(
                "Gemini phase 1 had no valid file-edits JSON; running phase 2 (JSON-only, no search, full task + phase-1 context).");
            try
            {
                var phase2Prompt = BuildJsonPhase2Prompt(prompt, phase1Context);
                var phase2Config = new GenerateContentConfig
                {
                    MaxOutputTokens = 8192,
                    // IncludeThoughts true: false often yields null Text/Parts on 2.5 Flash; skip thought parts in ExtractModelText.
                    ThinkingConfig = new ThinkingConfig { IncludeThoughts = true }
                };
                var phase2Response = await client.Models.GenerateContentAsync(ModelName, phase2Prompt, phase2Config, cancellationToken);
                var phase2Text = ExtractModelText(phase2Response, _logger, out _);
                if (!string.IsNullOrEmpty(phase2Text) && !TryParseEditsFromJson(phase2Text, out edits, _logger))
                    edits = new List<FileEdit>();
                else if (string.IsNullOrEmpty(phase2Text))
                    edits = new List<FileEdit>();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(
                    "Gemini phase 2 GenerateContent failed: {Detail}",
                    TruncateForLog(ex.ToString(), LogFieldMaxChars));
                edits = new List<FileEdit>();
            }
        }

        _logger?.LogInformation("Parsed {Count} file edit(s) from Gemini response", edits.Count);
        return edits;
    }

    /// <summary>Phase 2: repeat full website-updater instructions plus phase-1 research/thoughts so the model can emit JSON without search tools.</summary>
    private static string BuildJsonPhase2Prompt(string fullTaskPrompt, string phase1AssistantContext)
    {
        var ctx = string.IsNullOrWhiteSpace(phase1AssistantContext)
            ? "(No assistant text was returned in phase 1 — use the task and files below only.)"
            : TruncateForLog(phase1AssistantContext.Trim(), Phase1ContextMaxChars);

        return $@"{fullTaskPrompt}

---
PHASE 2 (second request — research step is done)
The previous API call was for online search and internal reasoning. Below is what the assistant produced (may include chain-of-thought and search planning). Use any useful facts from it, but do NOT copy its Markdown style for your answer.

PHASE 1 ASSISTANT OUTPUT (context only):
---
{ctx}
---

PHASE 2 OUTPUT CONTRACT (mandatory):
- This is the final step. Respond with NOTHING except one JSON array of file edits, as specified in the task above (path, editType, key, value, items, etc.).
- The first non-whitespace character of your entire message MUST be ""["" (U+005B).
- No Markdown, no headings, no **bold**, no code fences, no narration, no ""Initiating search"". If there are genuinely no edits to apply, output exactly: []";
    }

    /// <summary>All part texts from phase 1 (including thought parts) for phase-2 context.</summary>
    private static string BuildPhase1ContextForFollowUp(GenerateContentResponse? response)
    {
        if (response?.Candidates == null || response.Candidates.Count == 0)
            return "";

        var sb = new StringBuilder();
        foreach (var cand in response.Candidates)
        {
            var parts = cand.Content?.Parts;
            if (parts == null)
                continue;
            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part.Text))
                    continue;
                if (sb.Length > 0)
                    sb.AppendLine();
                if (IsThoughtPart(part))
                    sb.Append("[thought] ");
                sb.Append(part.Text);
            }
        }

        if (sb.Length == 0 && !string.IsNullOrEmpty(response.Text))
            return response.Text.Trim();

        return sb.ToString().Trim();
    }

    private static string BuildPrompt(
        string agentTodoContent,
        IReadOnlyList<(string path, string fullContent)> schemaFiles,
        IReadOnlyList<(string path, string fullContent)> dataFiles,
        IReadOnlyList<string>? structuredAppendKeyPaths,
        IReadOnlyList<string>? structuredAppendArrayPaths,
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
        var structuredArrayPathsLine = structuredAppendArrayPaths is { Count: > 0 }
            ? string.Join("; ", structuredAppendArrayPaths.Select(p => p.Trim().Replace('\\', '/')))
            : "(none - use raw value for appendToArray edits)";
        var exclusionBlock = excludedAppendKeys is { Count: > 0 }
            ? "\n\nEXCLUDED APPEND KEYS (already present in the repo clone — do NOT use appendKey with these exact path+key pairs; choose a different item from the todo or other edits. If the todo cannot be satisfied without these keys, return an empty JSON array [] or only non-conflicting edits.):\n"
              + string.Join("\n", excludedAppendKeys.Select(e =>
                  $"- path: {e.path.Trim().Replace('\\', '/')} key: {e.key.Trim()}"))
            : "";
        return $@"You are a website updater. Below is the todo.md content describing what changes to make, plus full schema files and data files when configured. You have Google Search grounding enabled; when the todo asks for fresh or current information (e.g. finding new events), use search to get up-to-date results before producing edits. Do not narrate your search or planning in the final message—only emit the JSON array described below.

OUTPUT CONTRACT (mandatory — the server parses your reply as JSON only):
- Your entire response must consist of a single JSON array and nothing else: no text before or after it.
- The first non-whitespace character of your entire response must be ""["" (U+005B). Do not start with Markdown (no **, no #, no bullet lines starting with * or -, no headings, no code fences like ```).
- Do not write explanations, status updates, or phrases such as ""Initiating search"" or ""I will populate""; perform search internally, then output only the array of file edits (or []).
- If you cannot produce edits or the todo is already satisfied, respond with exactly: []
- If you have edits, the array must contain one object per edit using the formats listed below.

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

STRUCTURED APPEND-ARRAY PATHS (agent configuration - exact repo-relative paths):
{structuredArrayPathsLine}

When these paths are listed, **appendToArray** for those files must use JSON **""item""** (a single object payload), not raw ""value"". The server emits safe TypeScript object literals and inserts before the closing ""];"".
When the list is (none), appendToArray uses raw ""value"" (one TS array element) as usual.

When adding new entries to list or data files (e.g. CSV, TS arrays, keyed records), preserve all existing content and append or insert the new item in the same format as existing ones.
**CSV files (.csv):** For any CSV that **already exists** in the repo, **do not** use full replace (path + content)—the pipeline rejects it. Add data only by appending **one new line at a time** with ""editType"": ""appendCsvRow"" (preferred) or ""appendToArray"": ""value"" must be a **single** complete CSV line matching the file's columns. The server appends and **skips** if the first-column id already exists. (Only if the CSV path does not exist yet may you use one full replace to create the file—rare.)

Respond with a JSON array of file edits. Supported edit formats:

1) **Full replace**: use ""path"" and ""content"". **Not allowed for .csv files that already exist.** Use for small non-CSV files (or initial creation of a missing CSV only).

2) **Append to array** (TypeScript arrays): for large TS array files where you only add one new element, use ""path"", ""editType"": ""appendToArray"", ""key"" (optional, for logging), and either:
   - ""item"": {{ ... }} for files listed under STRUCTURED APPEND-ARRAY PATHS (required there), or
   - ""value"": ""the exact text of one array element"" for all other files.
   The pipeline inserts before the closing ""];"". Do not use ""content"" for this.

3) **Append CSV row** (CSV only): use ""path"" (must end in .csv), ""editType"": ""appendCsvRow"", ""key"" (optional), and ""value"": ""one single CSV data line"". Prefer this over appendToArray for .csv clarity.

4) **Append to keyed structure**: use ""path"", ""editType"": ""appendKey"", and ""key"": ""the-new-record-key"".
   - If **path** is listed under STRUCTURED APPEND-KEY PATHS above: include **""items""**: [ {{ ""fieldA"": ""plain text"", ""fieldB"": ""plain text"" }}, ... ] matching the file's object shape (omit ""value""). String fields may contain apostrophes and quotes as normal JSON.
   - Otherwise: use **""value""** only (snippet to insert before the top-level object closes).

Only include files you are changing; use exact paths from the schema/data sections when relevant. Again: the full response must be valid JSON starting with ""["" — no Markdown and no prose outside that array.

Example (appendToArray): [{{""path"": ""src/data/items.ts"", ""editType"": ""appendToArray"", ""key"": ""item-1"", ""value"": ""{{ id: 'item-1', name: 'Example' }}""}}]
Example (appendToArray with item on structured array path): [{{""path"": ""src/data/festivals2026.es.data.ts"", ""editType"": ""appendToArray"", ""key"": ""festival-1"", ""item"": {{""id"": ""festival-1"", ""esSlug"": ""festival-ejemplo-2026"", ""startDate"": ""2026-06-01"", ""endDate"": ""2026-06-05"", ""country"": ""Spain""}}}}]
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

    /// <summary>
    /// Find the closing <c>]</c> for the array that starts at <paramref name="openBracketIndex"/>,
    /// respecting JSON string literals so brackets inside strings do not break depth.
    /// </summary>
    private static int FindMatchingArrayEnd(string s, int openBracketIndex)
    {
        if (openBracketIndex < 0 || openBracketIndex >= s.Length || s[openBracketIndex] != '[')
            return -1;

        var depth = 0;
        var inString = false;
        var escape = false;
        for (var i = openBracketIndex; i < s.Length; i++)
        {
            var c = s[i];
            if (inString)
            {
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (c == '\\')
                {
                    escape = true;
                    continue;
                }

                if (c == '"')
                    inString = false;
                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '[')
                depth++;
            else if (c == ']')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Extract JSON array spans that look like <c>[...]</c> with optional whitespace where the first element is
    /// <c>{{</c> or the array is empty <c>[]</c>. Skips false positives like <c>[year]</c> (first token is not <c>{{</c> or <c>]</c>).
    /// </summary>
    private static IEnumerable<string> EnumerateJsonArrayCandidates(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '[')
                continue;
            var j = i + 1;
            while (j < text.Length && char.IsWhiteSpace(text[j]))
                j++;
            if (j >= text.Length)
                continue;
            var next = text[j];
            if (next != '{' && next != ']')
                continue;

            var end = FindMatchingArrayEnd(text, i);
            if (end > i)
                yield return text[i..(end + 1)];
        }
    }

    private static bool TryDeserializeEditsArray(string json, JsonSerializerOptions options, out List<FileEdit>? list)
    {
        list = null;
        try
        {
            var deserialized = JsonSerializer.Deserialize<List<FileEdit>>(json, options);
            if (deserialized == null)
                return false;
            list = deserialized;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string TruncateForLog(string? s, int maxChars)
    {
        if (string.IsNullOrEmpty(s) || maxChars <= 0)
            return "";
        if (s.Length <= maxChars)
            return s;
        return s[..maxChars] + $"…(truncated, totalLen={s.Length})";
    }

    private bool TryParseEditsFromJson(string text, out List<FileEdit> edits, ILogger<GeminiService>? logger)
    {
        edits = new List<FileEdit>();
        try
        {
            var trimmed = text.Trim();
            var cleaned = StripMarkdownCodeFence(trimmed);
            if (!StartsWithJsonLike(cleaned))
                cleaned = StripMarkdownCodeFenceAnywhere(trimmed);
            if (!StartsWithJsonLike(cleaned))
                cleaned = trimmed;

            if (TryDeserializeEditsArray(cleaned, JsonReadOptions, out var direct) && direct != null)
            {
                edits = direct;
                if (edits.Count == 0)
                    logger?.LogInformation(
                        "Gemini JSON parse: deserialized empty array (cleanedLength={Len}, preview={Preview})",
                        cleaned.Length,
                        TruncateForLog(cleaned, 800));
                return true;
            }

            logger?.LogDebug(
                "Direct JSON parse failed; trying embedded JSON array candidates. cleanedPreview={Preview}",
                TruncateForLog(cleaned, 1200));

            foreach (var candidate in EnumerateJsonArrayCandidates(trimmed).OrderByDescending(static c => c.Length))
            {
                if (!TryDeserializeEditsArray(candidate, JsonReadOptions, out var list) || list == null)
                    continue;
                edits = list;
                if (edits.Count == 0)
                    logger?.LogInformation(
                        "Gemini JSON parse: deserialized empty array from embedded candidate (candidateLength={Len})",
                        candidate.Length);
                return true;
            }

            logger?.LogWarning(
                "Failed to parse Gemini file-edits JSON (no valid [{{...}}] or [] array found). preview={Preview}",
                TruncateForLog(trimmed, 1800));
            return false;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(
                "Failed to normalize Gemini response for JSON parsing. error={Error} inputPreview={Preview}",
                TruncateForLog(ex.ToString(), 1500),
                TruncateForLog(text.Trim(), 1800));
            return false;
        }
    }

    /// <summary>
    /// Concatenates <see cref="Part.Text"/> from non-thought parts only (Gemini 2.5 may emit thought-only completions).
    /// Uses <see cref="GenerateContentResponse.Text"/> only when there are no structured parts (SDK quirk with hidden thoughts).
    /// </summary>
    private static string? ExtractModelText(GenerateContentResponse? response, ILogger<GeminiService>? logger, out int textSegmentCount)
    {
        textSegmentCount = 0;
        if (response == null)
            return null;

        var anyPartsEnumerated = false;
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
                    anyPartsEnumerated = true;
                    if (string.IsNullOrEmpty(part.Text) || IsThoughtPart(part))
                        continue;
                    textSegmentCount++;
                    if (sb.Length > 0)
                        sb.AppendLine();
                    sb.Append(part.Text);
                }
            }
        }

        if (sb.Length > 0)
        {
            logger?.LogDebug("Gemini ExtractModelText: merged {Count} non-thought text part(s)", textSegmentCount);
            return sb.ToString().Trim();
        }

        // Parts existed but were only thoughts (or empty): do not use response.Text — would re-introduce thought prose as "the answer".
        if (anyPartsEnumerated)
        {
            if (response.PromptFeedback != null)
            {
                logger?.LogWarning(
                    "Gemini returned only thought parts or empty text; prompt feedback blockReason={Reason}, message={Message}",
                    response.PromptFeedback.BlockReason,
                    response.PromptFeedback.BlockReasonMessage);
            }
            else if (response.Candidates is { Count: > 0 })
            {
                var c0 = response.Candidates[0];
                logger?.LogDebug(
                    "Gemini ExtractModelText: no non-thought text parts; finishReason={Finish}",
                    c0.FinishReason);
            }

            return null;
        }

        var shortcut = response.Text?.Trim();
        if (!string.IsNullOrEmpty(shortcut))
        {
            textSegmentCount = 1;
            logger?.LogDebug("Gemini ExtractModelText: used response.Text fallback (length={Length})", shortcut.Length);
            return shortcut;
        }

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

    private static bool IsThoughtPart(Part part) => part.Thought == true;

}
