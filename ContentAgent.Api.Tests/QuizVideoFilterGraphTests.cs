using System.Text;
using ContentAgent.Video;

namespace ContentAgent.Api.Tests;

public sealed class QuizVideoFilterGraphTests
{
    [Fact]
    public void TrimFilterComplex_RemovesTrailingSemicolon()
    {
        var sb = new StringBuilder();
        sb.Append("[0:v]null[base];");
        sb.Append("[base][2:v]overlay=0:0[vout];");

        var s = QuizVideoFilterGraph.TrimFilterComplex(sb);

        Assert.False(s.EndsWith(";", StringComparison.Ordinal));
        Assert.EndsWith("[vout]", s, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildOverlayFilterComplex_QuestionOnly_SingleSlide_EndsWithVout_NoTrailingSemicolon()
    {
        var doc = new QuizSlidesDocument
        {
            QuestionDurationSeconds = 15,
            AnswerDurationSeconds = 0,
            Slides =
            [
                new QuizSlideItem
                {
                    Day = 1,
                    QuestionLines = ["x"],
                    Options = ["A. a"],
                    CorrectOptionIndex = 0
                }
            ]
        };

        var graph = QuizVideoFilterGraph.BuildOverlayFilterComplex(doc, 15, 0, 15, overlayCount: 1);

        Assert.Contains("[vout]", graph, StringComparison.Ordinal);
        Assert.DoesNotContain(";;", graph, StringComparison.Ordinal);
        Assert.False(graph.TrimEnd().EndsWith(";", StringComparison.Ordinal));
        Assert.Contains("[base][2:v]overlay", graph, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildOverlayFilterComplex_QuestionOnly_TwoSlides_ChainsOverlays()
    {
        var doc = new QuizSlidesDocument
        {
            QuestionDurationSeconds = 10,
            AnswerDurationSeconds = 0,
            Slides =
            [
                new QuizSlideItem { Day = 1, QuestionLines = ["a"], Options = ["A. x"], CorrectOptionIndex = 0 },
                new QuizSlideItem { Day = 2, QuestionLines = ["b"], Options = ["A. y"], CorrectOptionIndex = 0 }
            ]
        };

        var graph = QuizVideoFilterGraph.BuildOverlayFilterComplex(doc, 10, 0, 20, overlayCount: 2);

        Assert.Contains("[base][2:v]overlay", graph, StringComparison.Ordinal);
        Assert.Contains("[l0][3:v]overlay", graph, StringComparison.Ordinal);
        Assert.Contains("[vout]", graph, StringComparison.Ordinal);
        Assert.False(graph.TrimEnd().EndsWith(";", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildOverlayFilterComplex_WithAnswer_TwoPngsPerSlide()
    {
        var doc = new QuizSlidesDocument
        {
            QuestionDurationSeconds = 5,
            AnswerDurationSeconds = 5,
            Slides =
            [
                new QuizSlideItem { Day = 1, QuestionLines = ["a"], Options = ["A. x"], CorrectOptionIndex = 0 }
            ]
        };

        var graph = QuizVideoFilterGraph.BuildOverlayFilterComplex(doc, 5, 5, 10, overlayCount: 2);

        Assert.Contains("[base][2:v]overlay", graph, StringComparison.Ordinal);
        Assert.Contains("[lq0][3:v]overlay", graph, StringComparison.Ordinal);
        Assert.False(graph.TrimEnd().EndsWith(";", StringComparison.Ordinal));
    }
}
