namespace ContentAgent.Video;

/// <summary>Builds plain-text captions for social posts from quiz slide question fields.</summary>
public static class QuizSocialCaptionFormatter
{
    /// <summary>One block per slide, separated by a blank line (multiple slides in one video).</summary>
    public static string FormatSlides(IReadOnlyList<QuizSlideItem> slides)
    {
        if (slides.Count == 0)
            return string.Empty;

        return string.Join("\n\n", slides.Select(FormatSingle).Where(static s => !string.IsNullOrWhiteSpace(s)));
    }

    private static string FormatSingle(QuizSlideItem slide)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(slide.QuestionLabel))
            parts.Add(slide.QuestionLabel.Trim());

        foreach (var line in slide.QuestionLines)
        {
            if (!string.IsNullOrWhiteSpace(line))
                parts.Add(line.Trim());
        }

        return string.Join(" ", parts);
    }
}
