using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace ContentAgent.Api.Services;

/// <summary>Schedules video posts via Buffer GraphQL using <c>.txt</c> mutation templates under <see cref="BufferOptions.TemplatesDirectory"/>.</summary>
public sealed class BufferScheduleService : IBufferScheduleService
{
    public const string PlaceholderText = "<<<BUFFER_TEXT>>>";
    public const string PlaceholderDueAt = "<<<BUFFER_DUE_AT>>>";
    public const string PlaceholderVideoUrl = "<<<BUFFER_VIDEO_URL>>>";
    public const string PlaceholderMode = "<<<BUFFER_MODE>>>";
    public const string PlaceholderYouTubeTitle = "<<<BUFFER_YOUTUBE_TITLE>>>";
    public const string PlaceholderYouTubeCategory = "<<<BUFFER_YOUTUBE_CATEGORY>>>";
    public const string PlaceholderChannelId = "<<<BUFFER_CHANNEL_ID>>>";

    /// <summary>GraphQL <c>mode</c> for <c>&lt;&lt;&lt;BUFFER_MODE&gt;&gt;&gt;</c> in templates (Buffer enum).</summary>
    private const string DefaultBufferScheduleMode = "customScheduled";

    private readonly HttpClient _httpClient;
    private readonly BufferOptions _options;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<BufferScheduleService> _logger;

    public BufferScheduleService(
        HttpClient httpClient,
        IOptions<BufferOptions> options,
        IWebHostEnvironment environment,
        ILogger<BufferScheduleService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _environment = environment;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<BufferScheduleResult> ScheduleVideoPostAsync(
        string videoPublicAbsoluteUrl,
        string? questionCaption = null,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return new BufferScheduleResult(false, false, null, null, null, null);

        var token = _options.AccessToken?.Trim();
        if (string.IsNullOrEmpty(token))
        {
            _logger.LogInformation("Buffer scheduling skipped: AccessToken is not configured.");
            return new BufferScheduleResult(false, false, null, "Buffer AccessToken not configured.", null, null);
        }

        var videoUrl = videoPublicAbsoluteUrl?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(videoUrl) ||
            !(videoUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
              videoUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogWarning("Buffer scheduling skipped: video public URL is missing or not an absolute http(s) URL.");
            return new BufferScheduleResult(true, false, null, "Video public absolute URL is required for Buffer (e.g. https://host/videos/1.mp4).", null, null);
        }

        var text = BuildPostText(videoUrl, questionCaption);

        var hourUtc = Math.Clamp(_options.ScheduleHourUtc, 0, 23);
        var minuteUtc = Math.Clamp(_options.ScheduleMinuteUtc, 0, 59);
        // Next configured UTC wall time (today if still before that instant, otherwise tomorrow).
        var scheduledAt = BufferScheduling.NextUtcWallTime(hourUtc, minuteUtc);
        var dueAtIso = scheduledAt.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

        var endpoint = string.IsNullOrWhiteSpace(_options.GraphqlEndpoint)
            ? "https://api.buffer.com"
            : _options.GraphqlEndpoint.Trim();

        var categoryId = string.IsNullOrWhiteSpace(_options.YouTube.CategoryId)
            ? "22"
            : _options.YouTube.CategoryId.Trim();

        var tikTokPath = ResolveTemplatePath(_options.TikTok.Template);
        var youTubePath = ResolveTemplatePath(_options.YouTube.Template);
        var tikTokChannel = _options.TikTok.ChannelId?.Trim();
        var youTubeChannel = _options.YouTube.ChannelId?.Trim();

        var tikTokReady = tikTokPath is not null && !string.IsNullOrEmpty(tikTokChannel);
        var youTubeReady = youTubePath is not null && !string.IsNullOrEmpty(youTubeChannel);

        if (tikTokPath is not null && string.IsNullOrEmpty(tikTokChannel))
            _logger.LogInformation("Buffer TikTok template found but ChannelId is empty; skipping TikTok post.");
        if (youTubePath is not null && string.IsNullOrEmpty(youTubeChannel))
            _logger.LogInformation("Buffer YouTube template found but ChannelId is empty; skipping YouTube post.");

        if (!tikTokReady && !youTubeReady)
        {
            _logger.LogInformation(
                "Buffer scheduling skipped: need template file + ChannelId per platform under {Dir} (TikTok: {TikTokFile}, YouTube: {YouTubeFile}).",
                _options.TemplatesDirectory,
                _options.TikTok.Template,
                _options.YouTube.Template);
            return new BufferScheduleResult(
                false,
                false,
                null,
                "Buffer: no TikTok/YouTube post ready (missing template file and/or ChannelId).",
                null,
                null);
        }

        var postIds = new List<string>();
        var errors = new List<string>();

        var tasks = new List<Task<(string? PostId, string? Error)>>();

        if (tikTokReady)
        {
            var query = await BuildMutationFromTemplateAsync(
                tikTokPath!,
                text,
                title: text,
                dueAtIso,
                videoUrl,
                DefaultBufferScheduleMode,
                channelId: tikTokChannel!,
                includeYouTubePlaceholders: false,
                categoryId,
                cancellationToken).ConfigureAwait(false);
            if (query is null)
                errors.Add($"TikTok: invalid or unreadable template {tikTokPath}");
            else
                tasks.Add(SendGraphqlAsync(endpoint, token, query, "TikTok", videoUrl, cancellationToken));
        }

        if (youTubeReady)
        {
            var query = await BuildMutationFromTemplateAsync(
                youTubePath!,
                text,
                title: text,
                dueAtIso,
                videoUrl,
                DefaultBufferScheduleMode,
                channelId: youTubeChannel!,
                includeYouTubePlaceholders: true,
                categoryId,
                cancellationToken).ConfigureAwait(false);
            if (query is null)
                errors.Add($"YouTube: invalid or unreadable template {youTubePath}");
            else
                tasks.Add(SendGraphqlAsync(endpoint, token, query, "YouTube", videoUrl, cancellationToken));
        }

        foreach (var t in tasks)
        {
            var (postId, err) = await t.ConfigureAwait(false);
            if (!string.IsNullOrEmpty(postId))
                postIds.Add(postId);
            if (!string.IsNullOrEmpty(err))
                errors.Add(err);
        }

        var success = errors.Count == 0 && postIds.Count == tasks.Count;
        var errMsg = errors.Count > 0 ? string.Join(" | ", errors) : null;

        if (success)
            _logger.LogInformation("Buffer createPost succeeded for {Count} channel(s) at {DueAt} UTC.", postIds.Count, dueAtIso);
        else if (postIds.Count > 0)
            _logger.LogWarning("Buffer createPost partial success: posts {Posts}; errors: {Errors}", string.Join(", ", postIds), errMsg);
        else if (tasks.Count > 0)
            _logger.LogWarning("Buffer createPost failed: {Errors}", errMsg);

        return new BufferScheduleResult(
            Attempted: tikTokReady || youTubeReady,
            Success: success,
            ScheduledAtIso: dueAtIso,
            ErrorMessage: errMsg,
            UpdateIds: postIds.Count > 0 ? postIds : null,
            PostText: text);
    }

    /// <summary>Resolves template path: content root, then app base directory.</summary>
    private string? ResolveTemplatePath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;
        var subdir = string.IsNullOrWhiteSpace(_options.TemplatesDirectory)
            ? "buffer"
            : _options.TemplatesDirectory.Trim().TrimStart('/', '\\');

        var roots = new List<string>(2);
        if (!string.IsNullOrEmpty(_environment.ContentRootPath))
            roots.Add(_environment.ContentRootPath);
        roots.Add(AppContext.BaseDirectory);
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var full = Path.Combine(root, subdir, fileName.TrimStart('/', '\\'));
            if (File.Exists(full))
                return full;
        }

        return null;
    }

    private static async Task<string?> BuildMutationFromTemplateAsync(
        string absolutePath,
        string text,
        string title,
        string dueAtIso,
        string videoUrl,
        string modeToken,
        string channelId,
        bool includeYouTubePlaceholders,
        string categoryId,
        CancellationToken cancellationToken)
    {
        string raw;
        try
        {
            raw = await File.ReadAllTextAsync(absolutePath, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return ApplyBufferPlaceholders(raw, text, title, dueAtIso, videoUrl, modeToken, channelId, includeYouTubePlaceholders, categoryId);
    }

    /// <summary>Replaces <c>&lt;&lt;&lt;BUFFER_*&gt;&gt;&gt;</c> tokens with GraphQL string literals (or bare enum for mode).</summary>
    public static string ApplyBufferPlaceholders(
        string template,
        string text,
        string title,
        string dueAtIso,
        string videoUrl,
        string modeToken,
        string channelId,
        bool includeYouTubePlaceholders,
        string categoryId)
    {
        var s = template
            .Replace(PlaceholderText, ToGraphQlStringLiteral(text), StringComparison.Ordinal)
            .Replace(PlaceholderChannelId, ToGraphQlStringLiteral(channelId), StringComparison.Ordinal)
            .Replace(PlaceholderDueAt, ToGraphQlStringLiteral(dueAtIso), StringComparison.Ordinal)
            .Replace(PlaceholderVideoUrl, ToGraphQlStringLiteral(videoUrl), StringComparison.Ordinal)
            .Replace(PlaceholderMode, modeToken, StringComparison.Ordinal);

        if (includeYouTubePlaceholders)
        {
            s = s
                .Replace(PlaceholderYouTubeTitle, ToGraphQlStringLiteral(title), StringComparison.Ordinal)
                .Replace(PlaceholderYouTubeCategory, ToGraphQlStringLiteral(categoryId), StringComparison.Ordinal);
        }

        return s;
    }

    /// <summary>GraphQL string literal including surrounding double quotes, with escapes.</summary>
    public static string ToGraphQlStringLiteral(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append('\\').Append('u')
                            .Append(((ushort)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                        sb.Append(c);
                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

    private async Task<(string? PostId, string? Error)> SendGraphqlAsync(
        string endpoint,
        string bearerToken,
        string query,
        string label,
        string videoUrl,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(
            new Dictionary<string, string> { ["query"] = query },
            new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Buffer GraphQL request failed ({Label}) VideoUrl={VideoUrl}", label, videoUrl);
            return (null, $"{label}: {ex.Message} | VideoUrl={videoUrl}");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Buffer GraphQL response was not JSON ({Label}) VideoUrl={VideoUrl}: {Body}", label, videoUrl, body.Length > 500 ? body[..500] : body);
            return (null, $"{label}: invalid JSON response (HTTP {(int)response.StatusCode}). | VideoUrl={videoUrl}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.TryGetProperty("errors", out var gqlErrors) && gqlErrors.ValueKind == JsonValueKind.Array && gqlErrors.GetArrayLength() > 0)
            {
                var first = gqlErrors[0].TryGetProperty("message", out var m) ? m.GetString() : body;
                _logger.LogWarning("Buffer GraphQL errors ({Label}) VideoUrl={VideoUrl}: {Message}", label, videoUrl, first);
                return (null, $"{label}: {first} | VideoUrl={videoUrl}");
            }

            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                _logger.LogWarning("Buffer GraphQL missing data ({Label}) VideoUrl={VideoUrl}: {Body}", label, videoUrl, body.Length > 500 ? body[..500] : body);
                return (null, $"{label}: missing data in response (HTTP {(int)response.StatusCode}). | VideoUrl={videoUrl}");
            }

            if (!data.TryGetProperty("createPost", out var createPost))
            {
                _logger.LogWarning("Buffer GraphQL missing createPost ({Label}) VideoUrl={VideoUrl}", label, videoUrl);
                return (null, $"{label}: createPost missing in response. | VideoUrl={videoUrl}");
            }

            var typeName = createPost.TryGetProperty("__typename", out var tn) ? tn.GetString() : null;
            if (string.Equals(typeName, "MutationError", StringComparison.Ordinal))
            {
                var msg = createPost.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : body;
                _logger.LogWarning("Buffer createPost MutationError ({Label}) VideoUrl={VideoUrl}: {Message}", label, videoUrl, msg);
                return (null, $"{label}: {msg} | VideoUrl={videoUrl}");
            }

            if (createPost.TryGetProperty("post", out var post) &&
                post.ValueKind == JsonValueKind.Object &&
                post.TryGetProperty("id", out var idEl))
            {
                var id = idEl.GetString();
                if (!string.IsNullOrEmpty(id))
                    return (id, null);
            }

            // Buffer sometimes returns { "createPost": { "message": "..." } } without __typename (e.g. video URL not reachable).
            if (createPost.TryGetProperty("message", out var inlineMsgEl) && inlineMsgEl.ValueKind == JsonValueKind.String)
            {
                var inlineMsg = inlineMsgEl.GetString();
                if (!string.IsNullOrWhiteSpace(inlineMsg))
                {
                    _logger.LogWarning("Buffer createPost message ({Label}) VideoUrl={VideoUrl}: {Message}", label, videoUrl, inlineMsg);
                    return (null, $"{label}: {inlineMsg} | VideoUrl={videoUrl}");
                }
            }

            _logger.LogWarning("Buffer createPost unexpected shape ({Label}) VideoUrl={VideoUrl}: {Body}", label, videoUrl, body.Length > 500 ? body[..500] : body);
            return (null, $"{label}: could not read post id from response. | VideoUrl={videoUrl}");
        }
    }

    /// <summary>Post <c>text</c> and YouTube <c>title</c>: quiz caption when present; otherwise the public video URL.</summary>
    private static string BuildPostText(string videoUrl, string? questionCaption)
    {
        var caption = questionCaption?.Trim();
        return !string.IsNullOrEmpty(caption) ? caption : videoUrl;
    }
}
