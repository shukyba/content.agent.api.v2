using System.Text.Json.Serialization;

namespace ContentAgent.Video;

/// <summary>Root document for <c>quiz/quiz-slides.json</c>.</summary>
public sealed class QuizSlidesDocument
{
    /// <summary>Seconds to show the question + options (default 15).</summary>
    [JsonPropertyName("questionDurationSeconds")]
    public int QuestionDurationSeconds { get; set; } = 15;

    /// <summary>Seconds to show the correct answer; 0 = question only (no answer overlay).</summary>
    [JsonPropertyName("answerDurationSeconds")]
    public int AnswerDurationSeconds { get; set; }

    /// <summary>Ordered quiz items; each uses a question block and optionally an answer block.</summary>
    [JsonPropertyName("slides")]
    public List<QuizSlideItem> Slides { get; set; } = new();
}

/// <summary>One quiz with multi-line question and multiple-choice options.</summary>
public sealed class QuizSlideItem
{
    /// <summary>Day of month (1–31). The daily run picks the slide where <see cref="Day"/> equals today’s calendar day (local time).</summary>
    [JsonPropertyName("day")]
    public int Day { get; set; }

    /// <summary>Small header above the question (e.g. <c>Question:</c>).</summary>
    [JsonPropertyName("questionLabel")]
    public string? QuestionLabel { get; set; }

    /// <summary>Lines shown centered under the label, during the question segment.</summary>
    [JsonPropertyName("questionLines")]
    public List<string> QuestionLines { get; set; } = new();

    /// <summary>Typically A. / B. / C. / D. lines; shown during the question segment.</summary>
    [JsonPropertyName("options")]
    public List<string> Options { get; set; } = new();

    /// <summary>0-based index into <see cref="Options"/> for the answer slide.</summary>
    [JsonPropertyName("correctOptionIndex")]
    public int CorrectOptionIndex { get; set; }
}
