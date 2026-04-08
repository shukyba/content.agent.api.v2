using ContentAgent.Api.Models;
using ContentAgent.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ContentAgent.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class IdeasController : ControllerBase
{
    private readonly IIdeaGenerationService _ideaGenerationService;

    public IdeasController(IIdeaGenerationService ideaGenerationService)
    {
        _ideaGenerationService = ideaGenerationService;
    }
         
    /// <summary>All Social Poster topics (every domain), with optional <c>domainId</c> filter.</summary>
    [HttpGet("topics")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> GetTopics([FromQuery] string? domainId)
    {
        IEnumerable<IdeaTopicItem> q = IdeaTopicsData.All;
        if (!string.IsNullOrWhiteSpace(domainId)) 
        {
            var d = domainId.Trim();
            q = q.Where(t => string.Equals(t.DomainId, d, StringComparison.OrdinalIgnoreCase));
        }
         ///
        var topics = q.Select(t => new { id = t.Id, domainId = t.DomainId, label = t.Label }).ToList();
        return Ok(new { topics });
    }

    [HttpPost("generate")]
    [ProducesResponseType(typeof(GenerateIdeasResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GenerateIdeasResponse>> Generate(
        [FromBody] GenerateIdeasRequest request,
        CancellationToken cancellationToken)
    {
        if (!IdeaTopicsData.TryGetLabel(request.TopicId, out var label))
            return BadRequest(new { error = "Unknown topicId" });

        var result = await _ideaGenerationService.GenerateAsync(label, request.UserInput ?? string.Empty, cancellationToken);
        return Ok(result);
    }
}
