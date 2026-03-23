using Microsoft.Extensions.Configuration;

namespace ContentAgent.Api.Services;

public interface IStagingPromotionService
{
    /// <summary>
    /// For each folder under <c>agents/</c>, merge branch <c>staging</c> into <c>main</c> on GitHub using that agent's <c>config.json</c>.
    /// </summary>
    Task<StagingPromotionRunResult> PromoteAsync(CancellationToken cancellationToken = default);
}

public sealed class StagingPromotionRunResult
{
    public List<AgentPromotionResult> Agents { get; } = new();
}

public sealed class AgentPromotionResult
{
    public string AgentId { get; init; } = "";
    public string? Status { get; init; }
    public string? Owner { get; init; }
    public string? Repo { get; init; }
    public string? MergeCommitSha { get; init; }
    public bool AlreadyUpToDate { get; init; }
    public int? HttpStatus { get; init; }
    public string? Error { get; init; }
}

public sealed class StagingPromotionService : IStagingPromotionService
{
    private const string DefaultAgentsPath = "agents";
    private const string BaseBranch = "main";
    private const string HeadBranch = "staging";

    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly IGitHubMergeService _gitHubMergeService;
    private readonly ILogger<StagingPromotionService> _logger;

    public StagingPromotionService(
        IConfiguration configuration,
        IHostEnvironment hostEnvironment,
        IGitHubMergeService gitHubMergeService,
        ILogger<StagingPromotionService> logger)
    {
        _configuration = configuration;
        _hostEnvironment = hostEnvironment;
        _gitHubMergeService = gitHubMergeService;
        _logger = logger;
    }

    public async Task<StagingPromotionRunResult> PromoteAsync(CancellationToken cancellationToken = default)
    {
        var result = new StagingPromotionRunResult();

        var agentsRoot = Path.Combine(
            _hostEnvironment.ContentRootPath,
            _configuration["AgentsPath"] ?? DefaultAgentsPath);

        if (!Directory.Exists(agentsRoot))
        {
            _logger.LogWarning("Promote staging: agents folder not found at {Path}", agentsRoot);
            return result;
        }

        var foldersToProcess = Directory.GetDirectories(agentsRoot);
        _logger.LogInformation(
            "Promote staging: processing {Count} agent folder(s) | {Head} -> {Base}",
            foldersToProcess.Length,
            HeadBranch,
            BaseBranch);

        foreach (var agentFolder in foldersToProcess)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var agentId = new DirectoryInfo(agentFolder).Name;

            if (!AgentGitHubConfigHelper.TryLoadAgentGitHubSpec(
                    _hostEnvironment,
                    _configuration,
                    agentId,
                    out var spec,
                    out _,
                    out var loadError))
            {
                _logger.LogDebug("Promote staging: skip {AgentId} — {Reason}", agentId, loadError);
                result.Agents.Add(new AgentPromotionResult
                {
                    AgentId = agentId,
                    Status = "skipped",
                    Error = loadError
                });
                continue;
            }

            if (!AgentGitHubConfigHelper.TryParseGitHubOwnerRepo(spec!.Url, out var owner, out var repo, out var parseError))
            {
                _logger.LogWarning("Promote staging: skip {AgentId} — {Reason}", agentId, parseError);
                result.Agents.Add(new AgentPromotionResult
                {
                    AgentId = agentId,
                    Status = "skipped",
                    Error = parseError
                });
                continue;
            }

            var merge = await _gitHubMergeService.MergeBranchesAsync(
                owner,
                repo,
                spec.GithubToken!,
                BaseBranch,
                HeadBranch,
                cancellationToken);

            if (merge.Success)
            {
                result.Agents.Add(new AgentPromotionResult
                {
                    AgentId = agentId,
                    Status = merge.AlreadyUpToDate ? "already_up_to_date" : "merged",
                    Owner = owner,
                    Repo = repo,
                    MergeCommitSha = merge.MergeCommitSha,
                    AlreadyUpToDate = merge.AlreadyUpToDate,
                    HttpStatus = merge.StatusCode
                });
            }
            else
            {
                result.Agents.Add(new AgentPromotionResult
                {
                    AgentId = agentId,
                    Status = "error",
                    Owner = owner,
                    Repo = repo,
                    HttpStatus = merge.StatusCode,
                    Error = merge.Message,
                    MergeCommitSha = null
                });
            }
        }

        var merged = result.Agents.Count(a => a.Status is "merged" or "already_up_to_date");
        var errors = result.Agents.Count(a => a.Status == "error");
        _logger.LogInformation(
            "Promote staging run completed: {Total} row(s), merged/up-to-date={Ok}, errors={Err}",
            result.Agents.Count,
            merged,
            errors);

        return result;
    }
}
