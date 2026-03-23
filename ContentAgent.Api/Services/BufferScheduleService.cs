using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ContentAgent.Api.Services;

/// <summary>Schedules link-based posts via <see href="https://buffer.com/developers/api/updates">Buffer Updates API</see>.</summary>
public sealed class BufferScheduleService : IBufferScheduleService
{
    private const string CreateUpdatesPath = "https://api.bufferapp.com/1/updates/create.json";

    private readonly HttpClient _httpClient;
    private readonly BufferOptions _options;
    private readonly ILogger<BufferScheduleService> _logger;

    public BufferScheduleService(
        HttpClient httpClient,
        IOptions<BufferOptions> options,
        ILogger<BufferScheduleService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<BufferScheduleResult> ScheduleVideoPostAsync(
        string videoPublicPath,
        int calendarDay,
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

        var profileIds = _options.ProfileIds.Where(static id => !string.IsNullOrWhiteSpace(id)).Select(static id => id.Trim()).ToList();
        if (profileIds.Count == 0)
        {
            _logger.LogInformation("Buffer scheduling skipped: no ProfileIds configured.");
            return new BufferScheduleResult(false, false, null, "Buffer ProfileIds not configured.", null, null);
        }

        var baseUrl = _options.PublicVideoBaseUrl.TrimEnd('/');
        if (string.IsNullOrEmpty(baseUrl))
        {
            _logger.LogWarning("Buffer PublicVideoBaseUrl is empty.");
            return new BufferScheduleResult(true, false, null, "Buffer PublicVideoBaseUrl is empty.", null, null);
        }

        var path = videoPublicPath.StartsWith('/') ? videoPublicPath : "/" + videoPublicPath;
        var videoUrl = baseUrl + path;
        var text = BuildPostText(videoUrl, calendarDay, questionCaption);

        var scheduledAt = BufferScheduling.NextUtcWallTime(_options.ScheduleHourUtc, _options.ScheduleMinuteUtc);
        var scheduledAtIso = scheduledAt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        var form = new List<KeyValuePair<string, string>>
        {
            new("text", text),
            new("shorten", "false"),
            new("scheduled_at", scheduledAtIso),
            new("media[link]", videoUrl)
        };

        foreach (var pid in profileIds)
            form.Add(new KeyValuePair<string, string>("profile_ids[]", pid));

        using var content = new FormUrlEncodedContent(form);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

        var requestUri = $"{CreateUpdatesPath}?access_token={Uri.EscapeDataString(token)}";
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync(requestUri, content, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Buffer API request failed");
            return new BufferScheduleResult(true, false, scheduledAtIso, ex.Message, null, null);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Buffer response was not JSON: {Body}", body.Length > 500 ? body[..500] : body);
            return new BufferScheduleResult(true, false, scheduledAtIso, $"Invalid Buffer response (HTTP {(int)response.StatusCode}).", null, text);
        }

        using (doc)
        {
            var root = doc.RootElement;
            var apiSuccess = root.TryGetProperty("success", out var succ) && succ.ValueKind == JsonValueKind.True;
            var message = root.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : null;

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Buffer API HTTP {Status}: {Message}", (int)response.StatusCode, message ?? body);
                return new BufferScheduleResult(true, false, scheduledAtIso, message ?? body, null, text);
            }

            if (!apiSuccess)
            {
                _logger.LogWarning("Buffer create update failed: {Message}", message ?? body);
                return new BufferScheduleResult(true, false, scheduledAtIso, message ?? body, null, text);
            }

            var ids = new List<string>();
            if (root.TryGetProperty("updates", out var updates) && updates.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in updates.EnumerateArray())
                {
                    if (el.TryGetProperty("id", out var idProp) && idProp.GetString() is { } id)
                        ids.Add(id);
                }
            }

            _logger.LogInformation("Buffer queued post for {ScheduledAt} UTC, updates: {Count}", scheduledAtIso, ids.Count);
            return new BufferScheduleResult(true, true, scheduledAtIso, null, ids, text);
        }
    }

    private string BuildPostText(string videoUrl, int calendarDay, string? questionCaption)
    {
        var question = questionCaption?.Trim();
        if (!string.IsNullOrEmpty(question))
            return question + "\n\n" + videoUrl;

        var tpl = _options.PostTextTemplate?.Trim();
        if (!string.IsNullOrEmpty(tpl))
        {
            return tpl
                .Replace("{url}", videoUrl, StringComparison.Ordinal)
                .Replace("{day}", calendarDay.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        return videoUrl;
    }
}
