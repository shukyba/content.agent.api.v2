namespace ContentAgent.Video;

/// <summary>Optional overrides for <see cref="ISlideHelloWorldVideoService.CreateHelloWorldSlideAsync"/> (e.g. tests).</summary>
public sealed class VideoRenderOptions
{
    /// <summary>If set, selects slides with this calendar day (1–31) instead of <see cref="DateTime.Today"/> (local time).</summary>
    public int? CalendarDay { get; init; }

    /// <summary>If set, load quiz JSON from this path instead of <c>quiz/quiz-slides.json</c> under the app base directory.</summary>
    public string? QuizJsonPath { get; init; }
}
