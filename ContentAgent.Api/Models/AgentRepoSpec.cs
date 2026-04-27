using System.Text.Json.Serialization;

namespace ContentAgent.Api.Models;

/// <summary>Per-agent folder: config.json (url + optional githubToken and/or <c>AgentGitHubTokens</c> in secrets + optional schema/data file lists).</summary>
public class AgentRepoSpec
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("githubToken")]
    public string? GithubToken { get; set; }

    /// <summary>Optional. Small files to send in full (e.g. .schema structure docs).</summary>
    [JsonPropertyName("schema")]
    public List<string>? Schema { get; set; }

    /// <summary>Optional. Data files to send in full (e.g. CSV).</summary>
    [JsonPropertyName("data")]
    public List<string>? Data { get; set; }

    /// <summary>
    /// Optional. Repo-relative paths where <c>appendKey</c> must use a JSON <c>items</c> array (objects with string/primitive fields);
    /// the pipeline emits TypeScript with safe string escaping. Empty = all appendKey edits use raw <c>value</c>.
    /// </summary>
    [JsonPropertyName("structuredAppendKeyPaths")]
    public List<string>? StructuredAppendKeyPaths { get; set; }

    /// <summary>
    /// Optional. Repo-relative paths where <c>appendToArray</c> should use JSON <c>item</c> (object payload);
    /// the pipeline emits TypeScript with safe string escaping.
    /// </summary>
    [JsonPropertyName("structuredAppendArrayPaths")]
    public List<string>? StructuredAppendArrayPaths { get; set; }

    /// <summary>Optional. Submit sitemaps to Google (service account) and/or Bing.</summary>
    [JsonPropertyName("sitemap")]
    public AgentSitemapNode? Sitemap { get; set; }
}
