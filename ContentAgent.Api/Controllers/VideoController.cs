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
    /// If that file already exists, skips FFmpeg and proceeds to Buffer scheduling (caption from quiz JSON).
    /// When Buffer is configured, queues TikTok/YouTube <c>createPost</c> (GraphQL) using this request&apos;s public video URL at the next UTC slot from <c>Buffer:ScheduleHourUtc</c> (minute defaults to 0 in code).
    /// </summary>
    [HttpPost("")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateHelloWorldSlide(CancellationToken cancellationToken)
    {
        var webRoot = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var outputDir = Path.Combine(webRoot, "videos");
        Directory.CreateDirectory(outputDir);
        _logger.LogInformation("Quiz slide video output directory {Dir}", outputDir);

        var meta = await _slideVideo.GetTodaySlideMetadataAsync(cancellationToken);
        if (!meta.Success)
        {
            _logger.LogWarning("Quiz metadata failed: {Message}", meta.ErrorMessage);
            return BadRequest(new { status = "error", message = meta.ErrorMessage });
        }

        var expectedPath = Path.GetFullPath(Path.Combine(outputDir, meta.OutputFileName));
        SlideVideoResult result;
        var videoSkipped = false;
        if (System.IO.File.Exists(expectedPath))
        {
            _logger.LogInformation("Quiz video already exists at {Path}; skipping FFmpeg.", expectedPath);
            result = new SlideVideoResult(true, expectedPath, null, meta.SocialPostCaption);
            videoSkipped = true;
        }
        else
        {
            result = await _slideVideo.CreateHelloWorldSlideAsync(outputDir, cancellationToken);
            if (!result.Success)
            {
                _logger.LogWarning("Slide video failed: {Message}", result.ErrorMessage);
                return BadRequest(new { status = "error", message = result.ErrorMessage });
            }
        }

        var fileName = Path.GetFileName(result.OutputPath!);
        var publicPath = "/videos/" + fileName;
        var publicUrl = $"{Request.Scheme}://{Request.Host.Value}{publicPath}";

        var bufferResult = await _bufferSchedule.ScheduleVideoPostAsync(
            publicUrl,
            result.SocialPostCaption,
            cancellationToken);

        return Ok(new
        {
            status = "ok",
            videoSkipped,
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
