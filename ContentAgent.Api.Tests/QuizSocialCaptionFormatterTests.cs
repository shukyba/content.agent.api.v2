using ContentAgent.Video;

namespace ContentAgent.Api.Tests;

public sealed class QuizSocialCaptionFormatterTests
{
    [Fact]
    public void FormatSlides_JoinsLabelAndLines()
    {
        var slide = new QuizSlideItem
        {
            QuestionLabel = "Question:",
            QuestionLines = ["Line one", "Line two"],
            Options = ["A. x"],
            CorrectOptionIndex = 0,
            Day = 1
        };

        var s = QuizSocialCaptionFormatter.FormatSlides(new List<QuizSlideItem> { slide });

        Assert.Equal("Question: Line one Line two", s);
    }

    [Fact]
    public void FormatSlides_MultipleSlides_SeparatedByBlankLine()
    {
        var slides = new List<QuizSlideItem>
        {
            new()
            {
                Day = 1,
                QuestionLines = ["A"],
                Options = ["A. x"],
                CorrectOptionIndex = 0
            },
            new()
            {
                Day = 1,
                QuestionLines = ["B"],
                Options = ["A. y"],
                CorrectOptionIndex = 0
            }
        };

        var s = QuizSocialCaptionFormatter.FormatSlides(slides);
        Assert.Equal("A\n\nB", s);
    }
}
