using System.Text;
using System.Text.Json;
using ContentAgent.Api.Models;
using Google.GenAI;
using Google.GenAI.Types;

namespace ContentAgent.Api.Services;

public sealed class IdeaGenerationService : IIdeaGenerationService
{
    private const string GeminiModelId = "gemini-2.5-flash";
    private const string GeminiApiKeyConfig = "GeminiApiKey";
    private const int IdeaCount = 10;
    private const int MaxOutputTokens = 2048;
    private const int MaxQuotaRetries = 0;

    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly IConfiguration _configuration;
    private readonly ILogger<IdeaGenerationService> _logger;

    public IdeaGenerationService(IConfiguration configuration, ILogger<IdeaGenerationService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<GenerateIdeasResponse> GenerateAsync(string topicLabel, string userInput, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var trimmedTopic = topicLabel?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmedTopic))
        {
            _logger.LogWarning("GenerateAsync called with empty topic label");
            return new GenerateIdeasResponse { Ideas = Array.Empty<string>() };
        }

        var key = _configuration[GeminiApiKeyConfig];
        if (string.IsNullOrWhiteSpace(key))
        {
            _logger.LogWarning("GeminiApiKey is not configured; cannot generate ideas");
            return new GenerateIdeasResponse { Ideas = Array.Empty<string>() };
        }

        var prompt = BuildPrompt(trimmedTopic, userInput);
        var config = new GenerateContentConfig
        {
            MaxOutputTokens = MaxOutputTokens,
        };

        string? text = null;
        for (var attempt = 0; attempt <= MaxQuotaRetries; attempt++)
        {
            try
            {
                using var client = new Client(vertexAI: null, apiKey: key, credential: null, project: null, location: null, httpOptions: null);
                var response = await client.Models.GenerateContentAsync(GeminiModelId, prompt, config, cancellationToken);
                text = ExtractModelText(response, _logger);
                break;
            }
            catch (Exception ex) when (GeminiTransientErrors.IsRetriable(ex) && attempt < MaxQuotaRetries)
            {
                var delay = TimeSpan.FromSeconds(60 * (attempt + 1));
                var reason = ex.Message.Length <= 500 ? ex.Message : ex.Message[..500] + "…";
                _logger.LogInformation(
                    "Gemini ideas GenerateContent retriable failure (attempt {Attempt} of {MaxAttempts}), waiting {DelaySeconds}s: {Reason}",
                    attempt + 1,
                    MaxQuotaRetries + 1,
                    (int)delay.TotalSeconds,
                    reason);
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gemini GenerateContent failed for ideas");
                return new GenerateIdeasResponse { Ideas = Array.Empty<string>() };
            }
        }

        if (string.IsNullOrEmpty(text))
        {
            _logger.LogWarning("Gemini returned no usable text for ideas");
            return new GenerateIdeasResponse { Ideas = Array.Empty<string>() };
        }

        var ideas = ParseIdeasFromText(text);
        return new GenerateIdeasResponse { Ideas = ideas };
    }

    private static string BuildPrompt(string topicLabel, string userInput)
    {
        var notes = string.IsNullOrWhiteSpace(userInput)
            ? "(אין הערות נוספות מהמשתמש.)"
            : $"הערות / הקשר מהמשתמש (שלב ברעיונות כשמתאים):\n{userInput.Trim()}";

        return $@"You generate social media post ideas for marketing in Hebrew.

Return ONLY valid JSON with this exact shape (no markdown fences, no explanation before or after):
{{""ideas"":[""...""]}}

The ""ideas"" array must contain exactly {IdeaCount} distinct, short, practical post ideas (one string each), in Hebrew.

נושא / נישה (נדל""ן בישראל):
{topicLabel}

{notes}

Each idea should be a single line suitable as a post angle or hook (not a full article).";
    }

    private IReadOnlyList<string> ParseIdeasFromText(string text)
    {
        var cleaned = StripMarkdownCodeFence(text.Trim());
        if (!StartsWithJsonLike(cleaned))
            cleaned = StripMarkdownCodeFenceAnywhere(text);

        try
        {
            var env = JsonSerializer.Deserialize<IdeasEnvelope>(cleaned, JsonReadOptions);
            if (env?.Ideas is { Length: > 0 })
                return NormalizeIdeas(env.Ideas);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "First JSON parse for ideas failed");
        }

        var sliced = TryIsolateJsonObject(cleaned);
        try
        {
            var env = JsonSerializer.Deserialize<IdeasEnvelope>(sliced, JsonReadOptions);
            if (env?.Ideas is { Length: > 0 })
                return NormalizeIdeas(env.Ideas);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Second JSON parse for ideas failed");
        }

        _logger.LogWarning("Could not parse ideas JSON; attempting line fallback");
        return LineFallback(text);
    }

    private static IReadOnlyList<string> NormalizeIdeas(string[] raw)
    {
        var list = new List<string>(raw.Length);
        foreach (var s in raw)
        {
            var t = s?.Trim();
            if (!string.IsNullOrEmpty(t))
                list.Add(t);
            if (list.Count >= 20)
                break;
        }
        return list;
    }

    private static IReadOnlyList<string> LineFallback(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var list = new List<string>();
        foreach (var line in lines)
        {
            var t = line.Trim().TrimStart('-', '*', ' ', '\t');
            if (t.Length > 0 && !t.StartsWith('{') && !t.StartsWith('}'))
                list.Add(t);
            if (list.Count >= IdeaCount)
                break;
        }
        return list;
    }

    private static bool StartsWithJsonLike(string s)
    {
        var t = s.TrimStart();
        return t.Length > 0 && t[0] == '{';
    }

    private static string TryIsolateJsonObject(string text)
    {
        var i = text.IndexOf('{');
        var j = text.LastIndexOf('}');
        if (i >= 0 && j > i)
            return text[i..(j + 1)];
        return text;
    }

    private static string StripMarkdownCodeFence(string text)
    {
        var t = text.Trim();
        if (!t.StartsWith("```", StringComparison.Ordinal))
            return t;

        var firstNewline = t.IndexOf('\n');
        if (firstNewline < 0)
            return t;

        var body = t[(firstNewline + 1)..];
        var close = body.LastIndexOf("```", StringComparison.Ordinal);
        if (close >= 0)
            body = body[..close];

        return body.Trim();
    }

    private static string StripMarkdownCodeFenceAnywhere(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var jsonFence = text.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        var plainFence = text.IndexOf("```", StringComparison.Ordinal);
        var useJson = jsonFence >= 0;
        var idx = useJson ? jsonFence : plainFence;
        if (idx < 0)
            return text.Trim();

        var openLen = useJson ? "```json".Length : 3;
        var afterOpen = text[(idx + openLen)..];
        var firstNl = afterOpen.IndexOf('\n');
        if (firstNl >= 0)
            afterOpen = afterOpen[(firstNl + 1)..];

        var close = afterOpen.LastIndexOf("```", StringComparison.Ordinal);
        var body = close >= 0 ? afterOpen[..close] : afterOpen;
        return body.Trim();
    }

    private static string? ExtractModelText(GenerateContentResponse? response, ILogger<IdeaGenerationService> logger)
    {
        if (response == null)
            return null;

        var shortcut = response.Text?.Trim();
        if (!string.IsNullOrEmpty(shortcut))
            return shortcut;

        var sb = new StringBuilder();
        if (response.Candidates != null)
        {
            foreach (var cand in response.Candidates)
            {
                var parts = cand.Content?.Parts;
                if (parts == null)
                    continue;
                foreach (var part in parts)
                {
                    if (string.IsNullOrEmpty(part.Text))
                        continue;
                    if (sb.Length > 0)
                        sb.AppendLine();
                    sb.Append(part.Text);
                }
            }
        }

        if (sb.Length == 0)
        {
            logger.LogWarning("Gemini ideas response had no text parts");
            return null;
        }

        return sb.ToString().Trim();
    }

    private sealed class IdeasEnvelope
    {
        public string[]? Ideas { get; set; }
    }
}
