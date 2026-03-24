namespace ContentAgent.Video;

/// <summary>
/// Builds a short slide-style MP4 (solid background + text + background music) using FFmpeg.
/// </summary>
public interface ISlideHelloWorldVideoService
{
    /// <summary>
    /// Creates a 1080x1920 (TikTok-style 9:16) video from <c>quiz/quiz-slides.json</c>: uses the slide whose <c>day</c> matches today’s calendar day (local time), blurred <c>mp4/background-video.mp4</c>, dim overlay, timed question/answer, bold-style captions, <c>mp3/soundtrack.mp3</c>. Writes <c>{day}.mp4</c> under <paramref name="outputDirectory"/> (e.g. <c>wwwroot/videos</c> for public URLs via static files).
    /// </summary>
    /// <returns>Output file path on success, or error message.</returns>
    Task<SlideVideoResult> CreateHelloWorldSlideAsync(
        string outputDirectory,
        CancellationToken cancellationToken = default,
        VideoRenderOptions? options = null);

    /// <summary>
    /// Loads quiz JSON for today’s slide (or <see cref="VideoRenderOptions.CalendarDay"/>): output filename <c>{day}.mp4</c> and social caption.
    /// Does not run FFmpeg. Use before generation to skip work when the file already exists.
    /// </summary>
    Task<TodaySlideMetadata> GetTodaySlideMetadataAsync(
        CancellationToken cancellationToken = default,
        VideoRenderOptions? options = null);
}

/// <summary>Video generation outcome. <see cref="SocialPostCaption"/> is question text for Buffer/social (from quiz JSON).</summary>
public sealed record SlideVideoResult(bool Success, string? OutputPath, string? ErrorMessage, string? SocialPostCaption = null);

/// <summary>Today’s slide from quiz JSON only (no rendering).</summary>
public sealed record TodaySlideMetadata(
    bool Success,
    int CalendarDay,
    string OutputFileName,
    string? SocialPostCaption,
    string? ErrorMessage);
