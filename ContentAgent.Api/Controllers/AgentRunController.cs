using System.Threading.Channels; //
using ContentAgent.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ContentAgent.Api.Controllers;

[ApiController]
[Route("api/agent")]
public class AgentRunController : ControllerBase
{
    private readonly Channel<bool> _channel;
    private readonly ISitemapSubmissionService _sitemapSubmissionService;
    private readonly IStagingPromotionService _stagingPromotionService;
    private readonly ILogger<AgentRunController> _logger;

    public AgentRunController(
        Channel<bool> channel,
        ISitemapSubmissionService sitemapSubmissionService,
        IStagingPromotionService stagingPromotionService,
        ILogger<AgentRunController> logger)
    {
        _channel = channel;
        _sitemapSubmissionService = sitemapSubmissionService;
        _stagingPromotionService = stagingPromotionService;
        _logger = logger;
    }

    [HttpPost("run")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Run(CancellationToken cancellationToken)
    {
        try
        {
            await _channel.Writer.WriteAsync(true, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue agent run");
            return StatusCode(500, new { status = "error", message = "Failed to start run" });
        }

        return Accepted(new { status = "started" });
    }

    /// <summary>
    /// For each agent with a <c>sitemap</c> section in <c>config.json</c>, submit/ping configured providers (Google via service account, Bing).
    /// </summary>
    [HttpPost("submit-sitemaps")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> SubmitSitemaps(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _sitemapSubmissionService.SubmitForAllAgentsAsync(cancellationToken);
            return Ok(new
            {
                status = "completed",
                agents = result.Agents.Select(a => new
                {
                    a.AgentId,
                    google = a.GoogleStatus,
                    bing = a.BingStatus,
                    errors = a.Errors
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sitemap submission run failed");
            return StatusCode(500, new { status = "error", message = ex.Message });
        }
    }

    /// <summary>
    /// Remote merge on GitHub for each agent folder: <c>staging</c> → <c>main</c>. No request body.
    /// </summary>
    [HttpPost("promote")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> PromoteStaging(CancellationToken cancellationToken)
    {
        try
        {
            var run = await _stagingPromotionService.PromoteAsync(cancellationToken);

            return Ok(new
            {
                status = "completed",
                baseBranch = "main",
                headBranch = "staging",
                agents = run.Agents.Select(a => new
                {
                    a.AgentId,
                    a.Status,
                    a.Owner,
                    a.Repo,
                    a.MergeCommitSha,
                    a.AlreadyUpToDate,
                    a.HttpStatus,
                    a.Error
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Promote staging run failed");
            return StatusCode(500, new { status = "error", message = ex.Message });
        }
    }
}
