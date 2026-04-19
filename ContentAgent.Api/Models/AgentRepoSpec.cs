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

    /// <summary>Optional. Submit sitemaps to Google (service account) and/or Bing.</summary>
    [JsonPropertyName("sitemap")]
    public AgentSitemapNode? Sitemap { get; set; }

    /// <summary>
    /// Optional quality gate configuration evaluated against model edits before commit.
    /// Keeps project-specific quality rules in agent config instead of hardcoding in pipeline code.
    /// </summary>
    [JsonPropertyName("qualityGate")]
    public AgentQualityGateNode? QualityGate { get; set; }
}

public class AgentQualityGateNode
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("rules")]
    public List<AgentQualityRule>? Rules { get; set; }
}

public class AgentQualityRule
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Supported: "text", "items".</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>Optional editType filter (e.g. appendToArray, appendKey).</summary>
    [JsonPropertyName("editType")]
    public string? EditType { get; set; }

    /// <summary>If true, rule fails when no matching edit is present.</summary>
    [JsonPropertyName("requireMatch")]
    public bool RequireMatch { get; set; } = true;

    // -------- text rule options --------
    /// <summary>value | content | key</summary>
    [JsonPropertyName("textSource")]
    public string TextSource { get; set; } = "value";

    /// <summary>Optional regex to extract focused text from textSource (use named group "value" or first capture).</summary>
    [JsonPropertyName("extractRegex")]
    public string? ExtractRegex { get; set; }

    [JsonPropertyName("minLength")]
    public int? MinLength { get; set; }

    [JsonPropertyName("requiredTokens")]
    public List<string>? RequiredTokens { get; set; }

    [JsonPropertyName("minTokenMatches")]
    public int? MinTokenMatches { get; set; }

    [JsonPropertyName("forbiddenPhrases")]
    public List<string>? ForbiddenPhrases { get; set; }

    [JsonPropertyName("minRegexMatches")]
    public int? MinRegexMatches { get; set; }

    [JsonPropertyName("regexPattern")]
    public string? RegexPattern { get; set; }

    // -------- items rule options --------
    [JsonPropertyName("minItems")]
    public int? MinItems { get; set; }

    [JsonPropertyName("maxItems")]
    public int? MaxItems { get; set; }

    [JsonPropertyName("questionField")]
    public string? QuestionField { get; set; }

    [JsonPropertyName("answerField")]
    public string? AnswerField { get; set; }

    [JsonPropertyName("minQuestionLength")]
    public int? MinQuestionLength { get; set; }

    [JsonPropertyName("minAnswerLength")]
    public int? MinAnswerLength { get; set; }

    [JsonPropertyName("minConcreteAnswers")]
    public int? MinConcreteAnswers { get; set; }

    [JsonPropertyName("concreteRegexes")]
    public List<string>? ConcreteRegexes { get; set; }

    [JsonPropertyName("requiredTopicGroups")]
    public List<AgentQualityTopicGroup>? RequiredTopicGroups { get; set; }
}

public class AgentQualityTopicGroup
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("tokens")]
    public List<string>? Tokens { get; set; }
}
