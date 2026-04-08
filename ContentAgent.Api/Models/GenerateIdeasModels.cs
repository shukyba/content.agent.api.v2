using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ContentAgent.Api.Models;

public sealed class GenerateIdeasRequest
{
    [Required]
    [JsonPropertyName("topicId")]
    public Guid TopicId { get; set; }

    /// <summary>Free-text context from the user (optional).</summary>
    [JsonPropertyName("userInput")]
    [MaxLength(4000)]
    public string UserInput { get; set; } = string.Empty;
}

public sealed class GenerateIdeasResponse
{
    [JsonPropertyName("ideas")]
    public IReadOnlyList<string> Ideas { get; set; } = Array.Empty<string>();
}
