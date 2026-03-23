namespace ContentAgent.Api.Services;

/// <summary>Configuration for scheduling posts via the Buffer REST API (<c>POST /1/updates/create.json</c>).</summary>
public sealed class BufferOptions
{
    public const string SectionName = "Buffer";

    /// <summary>When false, video generation skips Buffer entirely.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>OAuth access token. Set via environment / Key Vault / user secrets — never commit real values.</summary>
    public string? AccessToken { get; set; }

    /// <summary>Public origin for quiz MP4s (HTTPS, no trailing slash). Used for Buffer <c>media[link]</c>, not the request host.</summary>
    public string PublicVideoBaseUrl { get; set; } =
        "https://content-agent-api-cag4htcafud5hpak.westeurope-01.azurewebsites.net";

    /// <summary>Buffer profile ids from <c>GET /1/profiles.json</c> (one post is created per id).</summary>
    public List<string> ProfileIds { get; set; } = new();

    /// <summary>Optional fallback when no quiz question caption is supplied. Placeholders: <c>{url}</c>, <c>{day}</c>.</summary>
    public string PostTextTemplate { get; set; } = "";

    public int ScheduleHourUtc { get; set; } = 19;

    public int ScheduleMinuteUtc { get; set; } = 0;
}
