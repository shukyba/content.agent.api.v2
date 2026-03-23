namespace ContentAgent.Api.Services;

public interface IBufferScheduleService
{
    /// <summary>
    /// Queues Buffer <c>createPost</c> from plain <c>.txt</c> mutations (see <see cref="BufferScheduleService"/> placeholders) at the next UTC wall time from
    /// <see cref="BufferOptions.ScheduleHourUtc"/> and <see cref="BufferOptions.ScheduleMinuteUtc"/> (default 0; today if still before then, otherwise tomorrow).
    /// <paramref name="videoPublicAbsoluteUrl"/> is the full HTTPS URL Buffer will fetch (e.g. from the current request).
    /// Post <c>text</c> and YouTube <c>metadata.youtube.title</c> use <paramref name="questionCaption"/> when non-empty; otherwise the video URL.
    /// </summary>
    Task<BufferScheduleResult> ScheduleVideoPostAsync(
        string videoPublicAbsoluteUrl,
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
