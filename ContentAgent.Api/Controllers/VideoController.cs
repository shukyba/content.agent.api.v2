using System.Globalization;
using ContentAgent.Api.Services;
using ContentAgent.Video;
using Microsoft.AspNetCore.Mvc;

namespace ContentAgent.Api.Controllers;

[ApiController]
[Route("api/video")]
public class VideoController : ControllerBase
{
    private readonly ISlideHelloWorldVideoService _slideVideo;
    private readonly IBufferScheduleService _bufferSchedule;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<VideoController> _logger;

    public VideoController(
        ISlideHelloWorldVideoService slideVideo,
        IBufferScheduleService bufferSchedule,
        IWebHostEnvironment environment,
        ILogger<VideoController> logger)
    {
        _slideVideo = slideVideo;
        _bufferSchedule = bufferSchedule;
        _environment = environment;
        _logger = logger;
    }

    /// <summary>
    /// Renders a 1080x1920 (TikTok 9:16) clip from <c>quiz/quiz-slides.json</c>: picks the slide whose <c>day</c> matches today (local server date), blurred <c>salsa-festival.mp4</c>, dim overlay, audio from the bundled MP3. Output: <c>wwwroot/videos/{day}.mp4</c>, served at <c>/videos/{day}.mp4</c>.
    /// When Buffer is configured, queues a social post for the production video URL at the next 19:00 UTC (configurable).
    /// </summary>
    [HttpPost("")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateHelloWorldSlide(CancellationToken cancellationToken)
    {
        var webRoot = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var outputDir = Path.Combine(webRoot, "videos");
        Directory.CreateDirectory(outputDir);
        _logger.LogInformation("Creating quiz slide video in {Dir}", outputDir);

        var result = await _slideVideo.CreateHelloWorldSlideAsync(outputDir, cancellationToken);
        if (!result.Success)
        {
            _logger.LogWarning("Slide video failed: {Message}", result.ErrorMessage);
            return BadRequest(new { status = "error", message = result.ErrorMessage });
        }

        var fileName = Path.GetFileName(result.OutputPath!);
        var publicPath = "/videos/" + fileName;
        var publicUrl = $"{Request.Scheme}://{Request.Host.Value}{publicPath}";

        var calendarDay = int.TryParse(Path.GetFileNameWithoutExtension(fileName), NumberStyles.Integer, CultureInfo.InvariantCulture, out var d)
            ? d
            : DateTime.Today.Day;

        var bufferResult = await _bufferSchedule.ScheduleVideoPostAsync(
            publicPath,
            calendarDay,
            result.SocialPostCaption,
            cancellationToken);

        return Ok(new
        {
            status = "ok",
            path = result.OutputPath,
            publicPath,
            publicUrl,
            socialPostCaption = result.SocialPostCaption,
            buffer = new
            {
                attempted = bufferResult.Attempted,
                success = bufferResult.Success,
                scheduledAtUtc = bufferResult.ScheduledAtIso,
                error = bufferResult.ErrorMessage,
                updateIds = bufferResult.UpdateIds,
                postText = bufferResult.PostText
            }
        });
    }
}
