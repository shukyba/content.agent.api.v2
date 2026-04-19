using System.Text.Json;
using ContentAgent.Api.Models;
using Microsoft.Extensions.Configuration;

namespace ContentAgent.Api.Services;

public interface IAgentPipelineService
{
    Task RunAsync(CancellationToken cancellationToken = default);
}

public class AgentPipelineService : IAgentPipelineService
{
    private const string TodoFileName = "todo.md";
    private const string ConfigFileName = "config.json";
    private const string DefaultAgentsPath = "agents";

    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly IGitService _gitService;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<AgentPipelineService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AgentPipelineService(
        IConfiguration configuration,
        IHostEnvironment hostEnvironment,
        IGitService gitService,
        ILoggerFactory loggerFactory,
        ILogger<AgentPipelineService> logger)
    {
        _configuration = configuration;
        _hostEnvironment = hostEnvironment;
        _gitService = gitService;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var geminiApiKey = _configuration["GeminiApiKey"];
        if (string.IsNullOrEmpty(geminiApiKey))
        {
            _logger.LogWarning("GeminiApiKey is missing (configure secrets.json, appsettings, environment variables, or user secrets)");
            return;
        }

        var agentsRoot = Path.Combine(
            _hostEnvironment.ContentRootPath,
            _configuration["AgentsPath"] ?? DefaultAgentsPath);

        if (!Directory.Exists(agentsRoot))
        {
            _logger.LogWarning("Agents folder not found at {Path}", agentsRoot);
            return;
        }

        var agentFolders = Directory.GetDirectories(agentsRoot);
        if (agentFolders.Length == 0)
        {
            _logger.LogInformation("No agent subfolders under {Path}", agentsRoot);
            return;
        }

        var baseClonePath = Path.Combine(Path.GetTempPath(), "ContentAgent", "repos");
        Directory.CreateDirectory(baseClonePath);

        var geminiService = new GeminiService(geminiApiKey, _loggerFactory.CreateLogger<GeminiService>());

        _logger.LogInformation("Starting agent pipeline run: {Count} agent(s) found", agentFolders.Length);

        var processedCount = 0;
        var successCount = 0;
        foreach (var agentFolder in agentFolders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var agentId = new DirectoryInfo(agentFolder).Name;
            try
            {
                bool succeeded = await ProcessAgentFolderAsync(agentFolder, agentId, geminiService, baseClonePath, cancellationToken);
                if (succeeded)
                    successCount++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing agent folder {AgentId}", agentId);
            }
            finally
            {
                processedCount++;
            }
        }

        _logger.LogInformation("Agent pipeline run completed: {Processed} processed, {Success} succeeded", processedCount, successCount);
    }

    private async Task<bool> ProcessAgentFolderAsync(
        string agentFolder,
        string agentId,
        GeminiService geminiService,
        string baseClonePath,
        CancellationToken cancellationToken)
    {
        var configPath = Path.Combine(agentFolder, ConfigFileName);
        if (!File.Exists(configPath))
        {
            _logger.LogDebug("Skipping {AgentId}: no {File}", agentId, ConfigFileName);
            return false;
        }

        AgentRepoSpec spec;
        try
        {
            var json = await File.ReadAllTextAsync(configPath, cancellationToken);
            spec = JsonSerializer.Deserialize<AgentRepoSpec>(json, JsonOptions) ?? new AgentRepoSpec();

            // Always bind schema/data from raw JSON — System.Text.Json sometimes fails to populate "schema" on AgentRepoSpec.
            // Order: schema → schemaFiles → legacy preview/files (same paths as old "preview" configs).
            var schemaFromJson = ConfigJsonHelpers.TryGetStringList(json, "schema", "schemaFiles", "preview", "files");
            if (schemaFromJson is { Count: > 0 })
                spec.Schema = schemaFromJson;

            var dataFromJson = ConfigJsonHelpers.TryGetStringList(json, "data");
            if (dataFromJson is { Count: > 0 })
                spec.Data = dataFromJson;

            var structuredFromJson = ConfigJsonHelpers.TryGetStringList(json, "structuredAppendKeyPaths");
            if (structuredFromJson is { Count: > 0 })
                spec.StructuredAppendKeyPaths = structuredFromJson;

            AgentGitHubConfigHelper.ApplyAgentGitHubTokenFromConfiguration(_configuration, agentId, spec);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid {File} in {AgentId}", ConfigFileName, agentId);
            return false;
        }

        if (string.IsNullOrWhiteSpace(spec.Url))
        {
            _logger.LogWarning("Agent {AgentId}: url is missing in {File}", agentId, ConfigFileName);
            return false;
        }

        var todoPath = Path.Combine(agentFolder, TodoFileName);
        if (!File.Exists(todoPath))
        {
            _logger.LogDebug("Skipping {AgentId}: no {File}", agentId, TodoFileName);
            return false;
        }

        var clonePath = Path.Combine(baseClonePath, agentId + "_" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            _logger.LogInformation("Processing agent {AgentId} -> {Url}", agentId, spec.Url);

            var cloned = _gitService.CloneAndSyncToStaging(spec.Url, spec.GithubToken, clonePath);
            if (cloned == null)
            {
                _logger.LogWarning("Clone failed for agent {AgentId}", agentId);
                return false;
            }

            _logger.LogInformation("Successfully cloned and synced repo for agent {AgentId}", agentId);

            var agentTodoContent = await File.ReadAllTextAsync(todoPath, cancellationToken);

            var schemaPaths = spec.Schema;
            if (schemaPaths is { Count: > 0 })
                _logger.LogDebug("Agent {AgentId}: {Count} schema path(s) configured", agentId, schemaPaths.Count);
            if (spec.Data is { Count: > 0 })
            {
                foreach (var dataPath in spec.Data)
                {
                    if (string.IsNullOrWhiteSpace(dataPath))
                        continue;
                    var fullDataPath = Path.GetFullPath(Path.Combine(clonePath, dataPath.Replace('\\', '/')));
                    var cloneRoot = Path.GetFullPath(clonePath);
                    var cloneRootPrefix = cloneRoot.EndsWith(Path.DirectorySeparatorChar) ? cloneRoot : cloneRoot + Path.DirectorySeparatorChar;
                    if (!fullDataPath.StartsWith(cloneRootPrefix, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(fullDataPath, cloneRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning("Agent {AgentId}: invalid data path outside repo: {DataPath}", agentId, dataPath);
                        return false;
                    }
                    if (!File.Exists(fullDataPath))
                        _logger.LogWarning("Agent {AgentId}: data file not found (will send placeholder to model): {DataPath}", agentId, dataPath);
                }
            }

            var duplicateRetryDelaySeconds = GetDuplicateKeyRetryDelaySeconds(_configuration);
            var maxDuplicateKeyRetries = GetMaxDuplicateKeyRetries(_configuration);
            var maxGeminiAttempts = maxDuplicateKeyRetries + 1;

            var excludedAppendKeys = new List<(string Path, string Key)>();
            var allAppliedEdits = new List<AppliedEditResult>();
            string? qualityFeedback = null;

            for (var attempt = 0; attempt < maxGeminiAttempts; attempt++)
            {
                var promptTodo = string.IsNullOrWhiteSpace(qualityFeedback)
                    ? agentTodoContent
                    : $"{agentTodoContent}\n\n{qualityFeedback}";

                var edits = await geminiService.GetWebsiteEditsAsync(
                    promptTodo,
                    clonePath,
                    schemaPaths,
                    spec.Data,
                    spec.StructuredAppendKeyPaths,
                    agentFolder,
                    excludedAppendKeys.Count > 0 ? excludedAppendKeys : null,
                    cancellationToken);

                if (edits.Count == 0)
                {
                    if (attempt == 0)
                        _logger.LogInformation("No edits from Gemini for agent {AgentId}", agentId);
                    break;
                }

                var qualityEvaluation = AgentQualityGateEvaluator.Evaluate(agentId, spec, edits);
                if (!qualityEvaluation.Passed)
                {
                    qualityFeedback = qualityEvaluation.FeedbackForModel;
                    _logger.LogWarning(
                        "Quality gate failed for agent {AgentId} on attempt {Attempt} of {Max}. Issues: {Issues}",
                        agentId,
                        attempt + 1,
                        maxGeminiAttempts,
                        string.Join(" | ", qualityEvaluation.Issues));

                    if (attempt < maxGeminiAttempts - 1)
                        continue;

                    _logger.LogWarning(
                        "Quality gate still failing after max retries for agent {AgentId}. Skipping commit.",
                        agentId);
                    return false;
                }

                _logger.LogInformation(
                    "Gemini returned {Count} edit(s) for agent {AgentId} (attempt {Attempt} of {Max})",
                    edits.Count,
                    agentId,
                    attempt + 1,
                    maxGeminiAttempts);

                var outcome = await ApplyEditsAsync(clonePath, edits, spec.StructuredAppendKeyPaths, cancellationToken);
                allAppliedEdits.AddRange(outcome.Applied);

                foreach (var skip in outcome.SkippedAppendKeyDuplicates)
                    TryAddExcludedAppendKey(excludedAppendKeys, skip.Path, skip.Key);

                if (outcome.SkippedAppendKeyDuplicates.Count == 0)
                    break;

                qualityFeedback = null;

                if (attempt >= maxGeminiAttempts - 1)
                    break;

                _logger.LogInformation(
                    "Duplicate appendKey skip(s) for agent {AgentId}; waiting {Seconds}s before Gemini retry ({Excluded} excluded path+key pair(s))",
                    agentId,
                    duplicateRetryDelaySeconds,
                    excludedAppendKeys.Count);
                await Task.Delay(TimeSpan.FromSeconds(duplicateRetryDelaySeconds), cancellationToken);
            }

            if (allAppliedEdits.Count == 0)
            {
                _logger.LogInformation("No valid edits were applied for agent {AgentId}", agentId);
                return false;
            }

            var modifiedPaths = allAppliedEdits.Select(e => e.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var (_, pushSucceeded) = _gitService.CreateBranchAndCommit(clonePath, spec.GithubToken, modifiedPaths);
            if (pushSucceeded)
            {
                _logger.LogInformation("Committed and pushed staging for agent {AgentId} ({EditCount} edit(s) applied)", agentId, allAppliedEdits.Count);
                return true;
            }
            else
            {
                _logger.LogWarning("Committed staging for agent {AgentId}, but push failed ({EditCount} edit(s) applied)", agentId, allAppliedEdits.Count);
                return false;
            }
        }
        finally
        {
            _gitService.TryDeleteClone(clonePath);
        }
    }

    private static bool TryGetNonEmptyStructuredItems(FileEdit edit, out JsonElement items)
    {
        items = default;
        if (!edit.Items.HasValue)
            return false;
        var el = edit.Items.Value;
        if (el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return false;
        if (el.ValueKind != JsonValueKind.Array || el.GetArrayLength() == 0)
            return false;
        items = el;
        return true;
    }

    private async Task<ApplyEditsOutcome> ApplyEditsAsync(
        string clonePath,
        List<FileEdit> edits,
        IReadOnlyList<string>? structuredAppendKeyPaths,
        CancellationToken cancellationToken)
    {
        var modifiedPaths = new List<AppliedEditResult>();
        var skippedAppendKeyDuplicates = new List<(string Path, string Key)>();
        var cloneRoot = Path.GetFullPath(clonePath);
        var cloneRootPrefix = cloneRoot.EndsWith(Path.DirectorySeparatorChar)
            ? cloneRoot
            : cloneRoot + Path.DirectorySeparatorChar;

        foreach (var edit in edits)
        {
            if (string.IsNullOrWhiteSpace(edit.Path))
                continue;
            var pathNorm = edit.Path.Replace('\\', '/');
            var fullPath = Path.GetFullPath(Path.Combine(clonePath, pathNorm));
            if (!fullPath.StartsWith(cloneRootPrefix, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(fullPath, cloneRoot, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Skipping edit outside repo path: {Path}", edit.Path);
                continue;
            }

            if (string.Equals(edit.EditType, "appendCsvRow", StringComparison.OrdinalIgnoreCase)
                && !pathNorm.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Skipping appendCsvRow: path must be a .csv file: {Path}", pathNorm);
                continue;
            }

            var isAppendToArray = string.Equals(edit.EditType, "appendToArray", StringComparison.OrdinalIgnoreCase);
            var isAppendCsvRow = string.Equals(edit.EditType, "appendCsvRow", StringComparison.OrdinalIgnoreCase);
            var isArrayOrCsvAppend = (isAppendToArray || isAppendCsvRow) && !string.IsNullOrWhiteSpace(edit.Value);

            if (string.Equals(edit.EditType, "appendKey", StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(fullPath))
                {
                    _logger.LogWarning("Skipping appendKey edit for missing file: {Path}", pathNorm);
                    continue;
                }

                var existing = await File.ReadAllTextAsync(fullPath, cancellationToken);
                var lastIdx = FindObjectClosingIndexAtFileEnd(existing);
                if (lastIdx < 0)
                {
                    _logger.LogWarning("Skipping appendKey edit; couldn't find top-level object closing in file: {Path}", pathNorm);
                    continue;
                }

                var structuredPath = StructuredAppendKeyHelper.MatchesStructuredAppendPath(pathNorm, structuredAppendKeyPaths);
                string? insertionPayload = null;
                if (structuredPath && TryGetNonEmptyStructuredItems(edit, out var itemsElement))
                {
                    if (string.IsNullOrWhiteSpace(edit.Key))
                    {
                        _logger.LogWarning("Skipping appendKey (structured path): non-empty key required. Path={Path}", pathNorm);
                        continue;
                    }

                    try
                    {
                        insertionPayload = StructuredAppendKeyHelper.FormatAppendBlock(edit.Key.Trim(), itemsElement);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Skipping appendKey (structured path): invalid items JSON. Path={Path}", pathNorm);
                        continue;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(edit.Value))
                {
                    if (structuredPath)
                    {
                        if (!StructuredAppendKeyHelper.TryRewriteLegacyValue(edit.Value, edit.Key, out var rewritten))
                        {
                            _logger.LogWarning(
                                "Skipping appendKey (structured path): could not parse/rewrite value; use a non-empty items array. Path={Path}",
                                pathNorm);
                            continue;
                        }

                        insertionPayload = rewritten;
                    }
                    else
                        insertionPayload = edit.Value.Trim();
                }
                else
                {
                    _logger.LogWarning(
                        structuredPath
                            ? "Skipping appendKey (structured path): provide non-empty items or parsable value. Path={Path}"
                            : "Skipping appendKey edit with empty value for path: {Path}",
                        pathNorm);
                    continue;
                }

                var before = existing[..lastIdx];
                var trimmedKey = edit.Key?.Trim();
                if (!string.IsNullOrEmpty(trimmedKey)
                    && AppendKeyDuplicateDetector.PropertyKeyLikelyExists(before, trimmedKey))
                {
                    _logger.LogWarning(
                        "Skipping appendKey; property key likely already exists: {Key} in {Path}",
                        trimmedKey,
                        pathNorm);
                    skippedAppendKeyDuplicates.Add((pathNorm, trimmedKey));
                    continue;
                }

                var after = existing[lastIdx..];
                var addComma = NeedsLeadingComma(before);
                var insertion = (addComma ? ",\n" : "\n") + insertionPayload + "\n";
                var newContent = before + insertion + after;
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                await File.WriteAllTextAsync(fullPath, newContent, cancellationToken);
                modifiedPaths.Add(new AppliedEditResult(pathNorm, "appendKey", edit.Key));
            }
            else if (isArrayOrCsvAppend)
            {
                string existing;
                if (!File.Exists(fullPath))
                {
                    if (!pathNorm.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning("Skipping appendToArray for missing file (CSV may be created by first appendCsvRow/appendToArray line): {Path}", pathNorm);
                        continue;
                    }

                    existing = string.Empty;
                }
                else
                    existing = await File.ReadAllTextAsync(fullPath, cancellationToken);

                // CSV: append one line (TS/JS arrays use ]; below). Full file replace for existing .csv is blocked below.
                if (pathNorm.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                {
                    var line = edit.Value!.Trim();
                    var newRowId = GetFirstCsvField(line);
                    if (string.IsNullOrEmpty(newRowId))
                    {
                        _logger.LogWarning("Skipping CSV append; could not parse row id (first field): {Path}", pathNorm);
                        continue;
                    }

                    var csvDuplicate = false;
                    foreach (var existingLine in existing.Split(["\r\n", "\n", "\r"], StringSplitOptions.None))
                    {
                        if (string.IsNullOrWhiteSpace(existingLine))
                            continue;
                        var existingId = GetFirstCsvField(existingLine);
                        if (string.IsNullOrEmpty(existingId))
                            continue;
                        if (string.Equals(existingId, newRowId, StringComparison.OrdinalIgnoreCase))
                        {
                            csvDuplicate = true;
                            break;
                        }
                    }

                    if (csvDuplicate)
                    {
                        _logger.LogWarning(
                            "Skipping CSV append; row id already exists: {Id} in {Path}",
                            newRowId,
                            pathNorm);
                        continue;
                    }

                    var needsNewline = existing.Length > 0 && !existing.EndsWith('\n') && !existing.EndsWith('\r');
                    var csvOut = existing + (needsNewline ? Environment.NewLine : "") + line + Environment.NewLine;
                    var dirCsv = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(dirCsv))
                        Directory.CreateDirectory(dirCsv);
                    await File.WriteAllTextAsync(fullPath, csvOut, cancellationToken);
                    modifiedPaths.Add(new AppliedEditResult(pathNorm, isAppendCsvRow ? "appendCsvRow" : "appendToArray", edit.Key));
                    continue;
                }

                var arrayClose = "];";
                var lastIdx = existing.LastIndexOf(arrayClose, StringComparison.Ordinal);
                if (lastIdx < 0)
                {
                    _logger.LogWarning("Skipping appendToArray edit; couldn't find array closing ]; in file: {Path}", pathNorm);
                    continue;
                }

                var before = existing[..lastIdx];
                var after = existing[lastIdx..];
                var addComma = NeedsLeadingComma(before);
                var insertion = (addComma ? ",\n  " : "\n  ") + (edit.Value ?? string.Empty).Trim() + "\n";
                var newContent = before + insertion + after;
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                await File.WriteAllTextAsync(fullPath, newContent, cancellationToken);
                modifiedPaths.Add(new AppliedEditResult(pathNorm, "appendToArray", edit.Key));
            }
            else
            {
                if (pathNorm.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath))
                {
                    _logger.LogWarning(
                        "Skipping full replace for existing .csv (forbidden). Use editType appendToArray or appendCsvRow with a single data line in value: {Path}",
                        pathNorm);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(edit.Content))
                {
                    _logger.LogWarning("Skipping full-replace edit with empty content for path: {Path}", pathNorm);
                    continue;
                }
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                await File.WriteAllTextAsync(fullPath, edit.Content, cancellationToken);
                modifiedPaths.Add(new AppliedEditResult(pathNorm, "full", null));
            }
        }
        return new ApplyEditsOutcome(modifiedPaths, skippedAppendKeyDuplicates);
    }

    /// <summary>First CSV field (festival id column): unquoted up to first comma, or quoted <c>"..."</c>.</summary>
    private static string GetFirstCsvField(string line)
    {
        line = line.Trim();
        if (line.Length == 0)
            return string.Empty;
        if (line[0] == '"')
        {
            var end = line.IndexOf('"', 1);
            return end > 0 ? line[1..end] : string.Empty;
        }

        var comma = line.IndexOf(',');
        return comma >= 0 ? line[..comma].Trim() : line.Trim();
    }

    /// <summary>
    /// Finds the <c>}</c>+<c>;</c> that actually terminates the file (only whitespace may follow).
    /// Plain <see cref="string.LastIndexOf(string)"/> is unsafe: FAQ answers can contain the substring <c>};</c>,
    /// which would insert new keys outside the top-level object and break TS parsing.
    /// </summary>
    private static int FindObjectClosingIndexAtFileEnd(string content)
    {
        var trimmed = content.TrimEnd();
        if (!trimmed.EndsWith("};", StringComparison.Ordinal))
            return -1;

        for (var searchEnd = content.Length - 1; searchEnd >= 1;)
        {
            var i = content.LastIndexOf("};", searchEnd, StringComparison.Ordinal);
            if (i < 0)
                return -1;
            if (IsOnlyWhitespaceFrom(content, i + 2))
                return i;
            searchEnd = i - 1;
        }

        return -1;
    }

    private static bool IsOnlyWhitespaceFrom(string s, int index)
    {
        for (var j = index; j < s.Length; j++)
        {
            if (!char.IsWhiteSpace(s[j]))
                return false;
        }
        return true;
    }

    private static bool NeedsLeadingComma(string textBeforeClosing)
    {
        for (var i = textBeforeClosing.Length - 1; i >= 0; i--)
        {
            var ch = textBeforeClosing[i];
            if (char.IsWhiteSpace(ch))
                continue;
            return ch != '{';
        }
        return false;
    }

    private static int GetDuplicateKeyRetryDelaySeconds(IConfiguration configuration)
    {
        var v = configuration["AgentPipeline:DuplicateKeyRetryDelaySeconds"];
        return int.TryParse(v, out var d) && d >= 0 ? d : 5;
    }

    private static int GetMaxDuplicateKeyRetries(IConfiguration configuration)
    {
        var v = configuration["AgentPipeline:MaxDuplicateKeyRetries"];
        return int.TryParse(v, out var m) && m >= 0 ? m : 2;
    }

    private static void TryAddExcludedAppendKey(List<(string Path, string Key)> list, string path, string key)
    {
        if (list.Any(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase)
                          && string.Equals(e.Key, key, StringComparison.Ordinal)))
            return;
        list.Add((path, key));
    }

    private sealed record ApplyEditsOutcome(
        List<AppliedEditResult> Applied,
        List<(string Path, string Key)> SkippedAppendKeyDuplicates);

    private sealed record AppliedEditResult(string Path, string Mode, string? Key);
}
