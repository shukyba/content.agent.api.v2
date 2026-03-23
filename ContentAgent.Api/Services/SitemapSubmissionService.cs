using System.Net.Http.Headers;
using System.Text.Json;
using ContentAgent.Api.Models;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;

namespace ContentAgent.Api.Services;

public interface ISitemapSubmissionService
{
    /// <summary>
    /// For each agent under <c>agents/</c>, if <see cref="AgentRepoSpec.Sitemap"/> is configured, run Google/Bing steps.
    /// </summary>
    Task<SitemapSubmissionRunResult> SubmitForAllAgentsAsync(CancellationToken cancellationToken = default);
}

public sealed class SitemapSubmissionRunResult
{
    public List<AgentSitemapSubmissionResult> Agents { get; } = new();
}

public sealed class AgentSitemapSubmissionResult
{
    public string AgentId { get; init; } = "";
    public string? GoogleStatus { get; init; }
    public string? BingStatus { get; init; }
    public List<string> Errors { get; init; } = new();
}

public sealed class SitemapSubmissionService : ISitemapSubmissionService
{
    public const string WebmastersScope = "https://www.googleapis.com/auth/webmasters";

    private const string ConfigFileName = "config.json";
    private const string DefaultAgentsPath = "agents";

    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly HttpClient _httpClient;
    private readonly ILogger<SitemapSubmissionService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SitemapSubmissionService(
        IConfiguration configuration,
        IHostEnvironment hostEnvironment,
        HttpClient httpClient,
        ILogger<SitemapSubmissionService> logger)
    {
        _configuration = configuration;
        _hostEnvironment = hostEnvironment;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<SitemapSubmissionRunResult> SubmitForAllAgentsAsync(CancellationToken cancellationToken = default)
    {
        var result = new SitemapSubmissionRunResult();

        var agentsRoot = Path.Combine(
            _hostEnvironment.ContentRootPath,
            _configuration["AgentsPath"] ?? DefaultAgentsPath);

        if (!Directory.Exists(agentsRoot))
        {
            _logger.LogWarning("Agents folder not found at {Path}", agentsRoot);
            return result;
        }

        var agentFolders = Directory.GetDirectories(agentsRoot);
        _logger.LogInformation("Sitemap submission run started: scanning {Count} agent folder(s) under {Path}", agentFolders.Length, agentsRoot);

        foreach (var agentFolder in agentFolders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var agentId = new DirectoryInfo(agentFolder).Name;

            var configPath = Path.Combine(agentFolder, ConfigFileName);
            if (!File.Exists(configPath))
            {
                _logger.LogDebug("Sitemap submit: skipping {AgentId}, no {File}", agentId, ConfigFileName);
                continue;
            }

            AgentRepoSpec spec;
            try
            {
                var json = await File.ReadAllTextAsync(configPath, cancellationToken);
                spec = JsonSerializer.Deserialize<AgentRepoSpec>(json, JsonOptions) ?? new AgentRepoSpec();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Sitemap submit: invalid config for {AgentId}", agentId);
                result.Agents.Add(new AgentSitemapSubmissionResult
                {
                    AgentId = agentId,
                    Errors = new List<string> { $"Invalid config.json: {ex.Message}" }
                });
                continue;
            }

            if (spec.Sitemap is null)
            {
                _logger.LogDebug("Sitemap submit: skipping {AgentId}, no sitemap section", agentId);
                continue;
            }

            var googleOn = spec.Sitemap.Google is { Enabled: true };
            var bingOn = spec.Sitemap.Bing is { Enabled: true };
            if (!googleOn && !bingOn)
            {
                _logger.LogDebug("Sitemap submit: skipping {AgentId}, sitemap providers disabled", agentId);
                continue;
            }

            string? googleStatus = null;
            string? bingStatus = null;
            var errors = new List<string>();

            if (googleOn && spec.Sitemap.Google is not null)
            {
                var (status, err) = await SubmitGoogleAsync(agentId, agentFolder, spec.Sitemap.Google, cancellationToken);
                googleStatus = status;
                if (err is not null)
                    errors.Add(err);
            }

            if (bingOn && spec.Sitemap.Bing is not null)
            {
                var (status, err) = await SubmitBingAsync(agentId, spec.Sitemap.Bing, cancellationToken);
                bingStatus = status;
                if (err is not null)
                    errors.Add(err);
            }

            result.Agents.Add(new AgentSitemapSubmissionResult
            {
                AgentId = agentId,
                GoogleStatus = googleStatus,
                BingStatus = bingStatus,
                Errors = errors
            });

            LogAgentSitemapSummary(agentId, googleStatus, bingStatus, errors);
        }

        _logger.LogInformation(
            "Sitemap submission run completed: {Processed} agent(s) with sitemap steps executed",
            result.Agents.Count);

        return result;
    }

    private async Task<(string Status, string? Error)> SubmitGoogleAsync(
        string agentId,
        string agentFolder,
        GoogleSitemapSubmitOptions options,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.SiteUrl))
        {
            _logger.LogWarning(
                "Sitemap Google SKIPPED | agent={AgentId} | reason=siteUrl missing",
                agentId);
            return ("skipped", "Google: siteUrl is missing");
        }

        if (string.IsNullOrWhiteSpace(options.SitemapUrl))
        {
            _logger.LogWarning(
                "Sitemap Google SKIPPED | agent={AgentId} | reason=sitemapUrl missing",
                agentId);
            return ("skipped", "Google: sitemapUrl is missing");
        }

        if (string.IsNullOrWhiteSpace(options.ServiceAccountKeyPath))
        {
            _logger.LogWarning(
                "Sitemap Google SKIPPED | agent={AgentId} | reason=serviceAccountKeyPath missing",
                agentId);
            return ("skipped", "Google: serviceAccountKeyPath is missing");
        }

        var keyPath = Path.IsPathRooted(options.ServiceAccountKeyPath)
            ? options.ServiceAccountKeyPath
            : Path.GetFullPath(Path.Combine(agentFolder, options.ServiceAccountKeyPath));

        if (!File.Exists(keyPath))
        {
            _logger.LogWarning(
                "Sitemap Google FAILED | agent={AgentId} | reason=service account key file not found | path={KeyPath}",
                agentId,
                keyPath);
            return ("error", $"Google: service account key file not found: {keyPath}");
        }

        try
        {
            var credential = CredentialFactory.FromFile(keyPath, JsonCredentialParameters.ServiceAccountCredentialType)
                .CreateScoped(WebmastersScope);

            if (credential.UnderlyingCredential is not ITokenAccess tokenAccess)
            {
                _logger.LogError(
                    "Sitemap Google FAILED | agent={AgentId} | reason=unsupported credential type for token request",
                    agentId);
                return ("error", "Google: unsupported credential type for token request");
            }

            var accessToken = await tokenAccess.GetAccessTokenForRequestAsync(
                "https://www.googleapis.com/",
                cancellationToken);

            if (string.IsNullOrEmpty(accessToken))
            {
                _logger.LogError(
                    "Sitemap Google FAILED | agent={AgentId} | reason=empty access token from Google OAuth",
                    agentId);
                return ("error", "Google: failed to obtain access token");
            }

            var siteEnc = Uri.EscapeDataString(options.SiteUrl.Trim());
            var mapEnc = Uri.EscapeDataString(options.SitemapUrl.Trim());
            var putUrl = $"https://www.googleapis.com/webmasters/v3/sites/{siteEnc}/sitemaps/{mapEnc}";

            using var request = new HttpRequestMessage(HttpMethod.Put, putUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var snippet = body.Length > 500 ? body[..500] : body;
                _logger.LogWarning(
                    "Sitemap Google FAILED | agent={AgentId} | siteUrl={SiteUrl} | sitemapUrl={SitemapUrl} | http={HttpStatus} | body={BodySnippet}",
                    agentId,
                    options.SiteUrl,
                    options.SitemapUrl,
                    (int)response.StatusCode,
                    snippet);
                return ("error", $"Google: HTTP {(int)response.StatusCode} — {Truncate(body, 300)}");
            }

            _logger.LogInformation(
                "Sitemap Google SUCCESS | agent={AgentId} | siteUrl={SiteUrl} | sitemapUrl={SitemapUrl}",
                agentId,
                options.SiteUrl,
                options.SitemapUrl);
            return ("ok", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Sitemap Google FAILED | agent={AgentId} | siteUrl={SiteUrl} | sitemapUrl={SitemapUrl} | exception",
                agentId,
                options.SiteUrl,
                options.SitemapUrl);
            return ("error", $"Google: {ex.Message}");
        }
    }

    private async Task<(string Status, string? Error)> SubmitBingAsync(
        string agentId,
        BingSitemapSubmitOptions options,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.SitemapUrl))
        {
            _logger.LogWarning(
                "Sitemap Bing SKIPPED | agent={AgentId} | reason=sitemapUrl missing",
                agentId);
            return ("skipped", "Bing: sitemapUrl is missing");
        }

        try
        {
            var url = "https://www.bing.com/ping?sitemap=" + Uri.EscapeDataString(options.SitemapUrl.Trim());
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var snippet = body.Length > 500 ? body[..500] : body;
                _logger.LogWarning(
                    "Sitemap Bing FAILED | agent={AgentId} | sitemapUrl={SitemapUrl} | http={HttpStatus} | body={BodySnippet}",
                    agentId,
                    options.SitemapUrl,
                    (int)response.StatusCode,
                    snippet);
                return ("error", $"Bing: HTTP {(int)response.StatusCode} — {Truncate(body, 300)}");
            }

            _logger.LogInformation(
                "Sitemap Bing SUCCESS | agent={AgentId} | sitemapUrl={SitemapUrl}",
                agentId,
                options.SitemapUrl);
            return ("ok", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Sitemap Bing FAILED | agent={AgentId} | sitemapUrl={SitemapUrl} | exception",
                agentId,
                options.SitemapUrl);
            return ("error", $"Bing: {ex.Message}");
        }
    }

    private void LogAgentSitemapSummary(
        string agentId,
        string? googleStatus,
        string? bingStatus,
        List<string> errors)
    {
        var g = googleStatus ?? "—";
        var b = bingStatus ?? "—";
        if (errors.Count == 0)
        {
            _logger.LogInformation(
                "Sitemap submission summary | agent={AgentId} | Google={GoogleStatus} | Bing={BingStatus}",
                agentId,
                g,
                b);
        }
        else
        {
            _logger.LogWarning(
                "Sitemap submission summary | agent={AgentId} | Google={GoogleStatus} | Bing={BingStatus} | errors={Errors}",
                agentId,
                g,
                b,
                string.Join(" | ", errors));
        }
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
            return s;
        return s[..max] + "…";
    }
}
