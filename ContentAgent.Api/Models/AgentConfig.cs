using System.Text.Json.Serialization;

namespace ContentAgent.Api.Models;

public class AgentConfig
{
    [JsonPropertyName("repos")]
    public List<RepoEntry> Repos { get; set; } = new();

    [JsonPropertyName("geminiApiKey")]
    public string? GeminiApiKey { get; set; }
}

public class RepoEntry
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("githubToken")]
    public string? GithubToken { get; set; }
}
