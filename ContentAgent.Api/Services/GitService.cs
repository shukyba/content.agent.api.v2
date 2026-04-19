using System.Text.RegularExpressions;
using ContentAgent.Api.Models;
using LibGit2Sharp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ContentAgent.Api.Services;

public interface IGitService
{
    string? Clone(string repoUrl, string? githubToken, string targetPath);
    /// <summary>Clone repo, sync <c>staging</c> to <c>origin/staging</c> (if present), then merge <c>origin/main</c> into <c>staging</c>.</summary>
    string? CloneAndSyncToStaging(string repoUrl, string? githubToken, string targetPath);
    void SyncMainToStaging(string repoPath, string? githubToken);
    (string BranchName, bool PushSucceeded) CreateBranchAndCommit(string repoPath, string? githubToken, List<string> modifiedPaths);
    void TryDeleteClone(string path);
}

public class GitService : IGitService
{
    private static readonly Regex ExtractRepoName = new(@"(?:/|:)([^/]+?)(?:\.git)?$", RegexOptions.Compiled);
    private readonly IConfiguration _configuration;
    private readonly ILogger<GitService> _logger;

    public GitService(IConfiguration configuration, ILogger<GitService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Shallow clone depth: <c>0</c> = full history (default LibGit2Sharp). <c>1</c> = single commit layer (smallest .git; may fail merges if merge-base is missing). Typical: 20–100.
    /// </summary>
    private int GetGitCloneDepth()
    {
        var v = _configuration["AgentPipeline:GitCloneDepth"];
        if (!int.TryParse(v, out var depth) || depth < 0)
            return 50;
        return depth;
    }

    public string? Clone(string repoUrl, string? githubToken, string targetPath)
    {
        var cloneDepth = GetGitCloneDepth();
        var fetchOptions = new FetchOptions();
        if (!string.IsNullOrEmpty(githubToken))
        {
            fetchOptions.CredentialsProvider = (_, __, ___) =>
                new UsernamePasswordCredentials
                {
                    Username = "git",
                    Password = githubToken
                };
        }

        if (cloneDepth > 0)
            fetchOptions.Depth = cloneDepth;

        var options = new CloneOptions(fetchOptions);

        try
        {
            Repository.Clone(repoUrl, targetPath, options);
            _logger.LogInformation(
                "Successfully cloned repository to {Path} (GitCloneDepth={Depth})",
                targetPath,
                cloneDepth > 0 ? cloneDepth.ToString() : "full");
            return targetPath;
        }
        catch (LibGit2SharpException ex)
        {
            _logger.LogWarning(ex, "Git clone failed for {Url} to {Path}", repoUrl, targetPath);
            return null;
        }
    }

    public string? CloneAndSyncToStaging(string repoUrl, string? githubToken, string targetPath)
    {
        var cloned = Clone(repoUrl, githubToken, targetPath);
        if (cloned == null)
            return null;
        SyncMainToStaging(cloned, githubToken);
        return cloned;
    }

    public void TryDeleteClone(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // ignore cleanup failures
        }
    }

    public void SyncMainToStaging(string repoPath, string? githubToken)
    {
        using var repo = new Repository(repoPath);

        var fetchOptions = new FetchOptions();
        if (!string.IsNullOrEmpty(githubToken))
        {
            fetchOptions.CredentialsProvider = (_, __, ___) =>
                new UsernamePasswordCredentials { Username = "git", Password = githubToken };
        }

        var remote = repo.Network.Remotes["origin"];
        if (remote != null)
            Commands.Fetch(repo, remote.Name, Array.Empty<string>(), fetchOptions, null);

        var mainBranch = repo.Branches["origin/main"] ?? repo.Branches["origin/master"]
            ?? repo.Branches["main"] ?? repo.Branches["master"];
        if (mainBranch == null)
            return;

        var mainTip = mainBranch.Tip;
        if (mainTip == null)
            return;

        // Base local `staging` on `origin/staging` when it exists so each agent run stacks on prior pushes.
        // Then merge `origin/main` so schema / mainline changes (e.g. on main) land on staging before edits.
        var originStaging = repo.Branches["origin/staging"];
        var stagingTip = originStaging?.Tip ?? mainTip;
        if (stagingTip == null)
            return;

        const string stagingName = "staging";
        Branch? staging = repo.Branches[stagingName];
        if (staging == null)
        {
            staging = repo.CreateBranch(stagingName, stagingTip);
        }
        else
        {
            Commands.Checkout(repo, staging);
            repo.Reset(ResetMode.Hard, stagingTip);
            staging = repo.Branches[stagingName];
        }

        Commands.Checkout(repo, staging!);

        if (originStaging != null)
        {
            var signature = new Signature("Content Agent", "agent@content.local", DateTimeOffset.UtcNow);
            var preMergeTip = repo.Head.Tip;
            try
            {
                var mergeResult = repo.Merge(mainBranch, signature, new MergeOptions());
                switch (mergeResult.Status)
                {
                    case MergeStatus.Conflicts:
                        _logger.LogWarning(
                            "Merge origin/main into staging produced conflicts; staying at origin/staging without merge. Resolve on GitHub and re-run.");
                        repo.Reset(ResetMode.Hard, preMergeTip);
                        break;
                    case MergeStatus.UpToDate:
                    case MergeStatus.FastForward:
                    case MergeStatus.NonFastForward:
                        break;
                }
            }
            catch (LibGit2SharpException ex)
            {
                _logger.LogWarning(ex, "Merge origin/main into staging failed; staying at origin/staging tip");
                repo.Reset(ResetMode.Hard, preMergeTip);
            }
        }
    }

    public (string BranchName, bool PushSucceeded) CreateBranchAndCommit(string repoPath, string? githubToken, List<string> modifiedPaths)
    {
        const string branchName = "staging";
        var signature = new Signature("Content Agent", "agent@content.local", DateTimeOffset.UtcNow);

        using var repo = new Repository(repoPath);

        var fetchOptions = new FetchOptions();
        if (!string.IsNullOrEmpty(githubToken))
        {
            fetchOptions.CredentialsProvider = (_, __, ___) =>
                new UsernamePasswordCredentials { Username = "git", Password = githubToken };
        }

        var remote = repo.Network.Remotes["origin"];
        if (remote != null)
            Commands.Fetch(repo, remote.Name, Array.Empty<string>(), fetchOptions, null);

        Branch? branch = repo.Branches[branchName];
        if (branch == null)
        {
            var remoteBranch = repo.Branches["origin/" + branchName];
            if (remoteBranch != null)
                branch = repo.CreateBranch(branchName, remoteBranch.Tip);
            else
                branch = repo.CreateBranch(branchName);
        }

        Commands.Checkout(repo, branch);

        foreach (var path in modifiedPaths)
        {
            var fullPath = Path.Combine(repoPath, path);
            if (File.Exists(fullPath))
                repo.Index.Add(path);
        }

        repo.Index.Write();

        repo.Commit($"Agent update: {branchName}", signature, signature);

        if (remote == null)
            return (branchName, false);

        var pushOptions = new PushOptions();
        if (!string.IsNullOrEmpty(githubToken))
        {
            pushOptions.CredentialsProvider = (_, __, ___) =>
                new UsernamePasswordCredentials
                {
                    Username = "git",
                    Password = githubToken
                };
        }

        var pushSucceeded = false;
        try
        {
            repo.Network.Push(remote, branch.CanonicalName, pushOptions);
            pushSucceeded = true;
        }
        catch (LibGit2SharpException ex)
        {
            _logger.LogWarning(ex, "Git push failed; commit exists only in temp clone");
        }

        return (branchName, pushSucceeded);
    }

    public static string GetRepoNameFromUrl(string repoUrl)
    {
        var match = ExtractRepoName.Match(repoUrl.Trim());
        return match.Success ? match.Groups[1].Value : "repo";
    }

}
