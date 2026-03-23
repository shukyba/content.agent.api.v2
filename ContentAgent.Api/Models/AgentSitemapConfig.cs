using System.Text.Json.Serialization;

namespace ContentAgent.Api.Models;

/// <summary>Optional per-agent sitemap ping/submit settings (Google Search Console + Bing).</summary>
public class AgentSitemapNode
{
    [JsonPropertyName("google")]
    public GoogleSitemapSubmitOptions? Google { get; set; }

    [JsonPropertyName("bing")]
    public BingSitemapSubmitOptions? Bing { get; set; }
}

public class GoogleSitemapSubmitOptions
{
    /// <summary>When false, Google submit is skipped for this agent.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Search Console property URL (e.g. https://www.example.com/ or sc-domain:example.com).</summary>
    [JsonPropertyName("siteUrl")]
    public string SiteUrl { get; set; } = string.Empty;

    /// <summary>Public URL of the sitemap file (same as feed path in Webmasters API).</summary>
    [JsonPropertyName("sitemapUrl")]
    public string SitemapUrl { get; set; } = string.Empty;

    /// <summary>
    /// Path to the Google Cloud service account JSON key. Relative paths are resolved under the agent folder
    /// (e.g. <c>gsc-service-account.json</c> next to <c>config.json</c>).
    /// </summary>
    [JsonPropertyName("serviceAccountKeyPath")]
    public string? ServiceAccountKeyPath { get; set; }
}

public class BingSitemapSubmitOptions
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("sitemapUrl")]
    public string SitemapUrl { get; set; } = string.Empty;
}
