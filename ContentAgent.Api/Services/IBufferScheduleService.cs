namespace ContentAgent.Api.Services;

public interface IBufferScheduleService
{
    /// <summary>
    /// Queues a Buffer update with <paramref name="videoPublicPath"/> (e.g. <c>/videos/22.mp4</c>) at the next
    /// configured UTC wall time (default 19:00): today if still before that time, otherwise tomorrow.
    /// Post body is <paramref name="questionCaption"/> plus the video URL (when caption is non-empty); otherwise falls back to <see cref="BufferOptions.PostTextTemplate"/>.
    /// </summary>
    Task<BufferScheduleResult> ScheduleVideoPostAsync(
        string videoPublicPath,
        int calendarDay,
        string? questionCaption = null,
        CancellationToken cancellationToken = default);
}

public sealed record BufferScheduleResult(
    bool Attempted,
    bool Success,
    string? ScheduledAtIso,
    string? ErrorMessage,
    IReadOnlyList<string>? UpdateIds,
    string? PostText = null);
