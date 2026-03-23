using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ContentAgent.Api.Models;
using Microsoft.Extensions.Configuration;

namespace ContentAgent.Api.Services;

public interface IGitHubMergeService
{
    /// <summary>
    /// Calls GitHub <c>POST /repos/{owner}/{repo}/merges</c> to merge <paramref name="headBranch"/> into <paramref name="baseBranch"/>.
    /// </summary>
    Task<GitHubMergeResult> MergeBranchesAsync(
        string owner,
        string repo,
        string githubToken,
        string baseBranch,
        string headBranch,
        CancellationToken cancellationToken = default);
}

public sealed class GitHubMergeResult
{
    public bool Success { get; init; }
    public bool AlreadyUpToDate { get; init; }
    public int StatusCode { get; init; }
    public string? MergeCommitSha { get; init; }
    public string? Message { get; init; }
    public string? ErrorBody { get; init; }
}

public sealed class GitHubMergeService : IGitHubMergeService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubMergeService> _logger;

    public GitHubMergeService(HttpClient httpClient, ILogger<GitHubMergeService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<GitHubMergeResult> MergeBranchesAsync(
        string owner,
        string repo,
        string githubToken,
        string baseBranch,
        string headBranch,
        CancellationToken cancellationToken = default)
    {
        var url = $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/merges";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", githubToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("ContentAgent.Api/1.0");

        var body = new MergeRequestBody
        {
            Base = baseBranch,
            Head = headBranch,
            CommitMessage = $"Merge {headBranch} into {baseBranch} (Content Agent API)"
        };
        request.Content = JsonContent.Create(body, options: JsonOptions);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.Created)
        {
            string? sha = null;
            try
            {
                using var doc = JsonDocument.Parse(responseText);
                if (doc.RootElement.TryGetProperty("sha", out var shaEl))
                    sha = shaEl.GetString();
            }
            catch
            {
                // ignore parse errors; merge still succeeded
            }

            _logger.LogInformation(
                "GitHub merge SUCCESS | {Owner}/{Repo} | {Head} -> {Base} | sha={Sha}",
                owner,
                repo,
                headBranch,
                baseBranch,
                sha ?? "?");

            return new GitHubMergeResult
            {
                Success = true,
                AlreadyUpToDate = false,
                StatusCode = (int)response.StatusCode,
                MergeCommitSha = sha,
                Message = "Merge commit created on GitHub."
            };
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            _logger.LogInformation(
                "GitHub merge SKIPPED (already up to date) | {Owner}/{Repo} | {Head} -> {Base}",
                owner,
                repo,
                headBranch,
                baseBranch);

            return new GitHubMergeResult
            {
                Success = true,
                AlreadyUpToDate = true,
                StatusCode = (int)response.StatusCode,
                Message = "Nothing to merge; base already contains head."
            };
        }

        _logger.LogWarning(
            "GitHub merge FAILED | {Owner}/{Repo} | {Head} -> {Base} | http={Status} | body={Body}",
            owner,
            repo,
            headBranch,
            baseBranch,
            (int)response.StatusCode,
            responseText.Length > 800 ? responseText[..800] + "…" : responseText);

        return new GitHubMergeResult
        {
            Success = false,
            StatusCode = (int)response.StatusCode,
            Message = $"GitHub API returned {(int)response.StatusCode}.",
            ErrorBody = responseText.Length > 2000 ? responseText[..2000] + "…" : responseText
        };
    }

    private sealed class MergeRequestBody
    {
        [JsonPropertyName("base")]
        public string Base { get; set; } = "";

        [JsonPropertyName("head")]
        public string Head { get; set; } = "";

        [JsonPropertyName("commit_message")]
        public string CommitMessage { get; set; } = "";
    }
}

/// <summary>Resolves <c>agents/{agentId}/config.json</c> and parses <c>github.com/owner/repo</c>.</summary>
public static class AgentGitHubConfigHelper
{
    private const string ConfigFileName = "config.json";
    private const string DefaultAgentsPath = "agents";

    /// <summary>HTTPS github.com URLs; repo name may contain dots (e.g. <c>dance.site</c>). Optional <c>.git</c> stripped after match.</summary>
    private static readonly Regex GitHubRepoPath = new(
        @"^https?://github\.com/(?<owner>[^/]+)/(?<repo>[^/?#]+)/?(?:[?#].*)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static bool TryLoadAgentGitHubSpec(
        IHostEnvironment hostEnvironment,
        IConfiguration configuration,
        string agentId,
        out AgentRepoSpec? spec,
        out string? agentsFolder,
        out string? error)
    {
        spec = null;
        agentsFolder = null;
        error = null;

        if (string.IsNullOrWhiteSpace(agentId))
        {
            error = "agentId is required.";
            return false;
        }

        var invalid = Path.GetInvalidFileNameChars();
        if (agentId.IndexOfAny(invalid) >= 0 || agentId.Contains('/') || agentId.Contains('\\'))
        {
            error = "agentId contains invalid path characters.";
            return false;
        }

        var agentsRoot = Path.Combine(
            hostEnvironment.ContentRootPath,
            configuration["AgentsPath"] ?? DefaultAgentsPath);

        agentsFolder = Path.Combine(agentsRoot, agentId);
        var configPath = Path.Combine(agentsFolder, ConfigFileName);

        if (!File.Exists(configPath))
        {
            error = $"config.json not found for agent '{agentId}'.";
            return false;
        }

        try
        {
            var json = File.ReadAllText(configPath);
            spec = JsonSerializer.Deserialize<AgentRepoSpec>(json, JsonOptions) ?? new AgentRepoSpec();
        }
        catch (Exception ex)
        {
            error = $"Invalid config.json: {ex.Message}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(spec.Url))
        {
            error = "config url is missing.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(spec.GithubToken))
        {
            error = "config githubToken is missing.";
            return false;
        }

        return true;
    }

    public static bool TryParseGitHubOwnerRepo(string repoUrl, out string owner, out string repo, out string? error)
    {
        owner = "";
        repo = "";
        error = null;

        var trimmed = repoUrl.Trim();
        var m = GitHubRepoPath.Match(trimmed);
        if (!m.Success)
        {
            error = "url must be a github.com repository (e.g. https://github.com/owner/repo).";
            return false;
        }

        owner = m.Groups["owner"].Value;
        repo = m.Groups["repo"].Value;
        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            repo = repo[..^4];
        return true;
    }
}
