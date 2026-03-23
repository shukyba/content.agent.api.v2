namespace ContentAgent.Api.Services;

/// <summary>TikTok Buffer channel: template file name and GraphQL <c>channelId</c>.</summary>
public sealed class BufferTikTokOptions
{
    /// <summary>Mutation file under <see cref="BufferOptions.TemplatesDirectory"/> (e.g. <c>tiktok.txt</c>).</summary>
    public string Template { get; set; } = "tiktok.txt";

    /// <summary>Buffer channel id for <c>&lt;&lt;&lt;BUFFER_CHANNEL_ID&gt;&gt;&gt;</c> in the template.</summary>
    public string? ChannelId { get; set; }
}

/// <summary>YouTube Buffer channel: template, <c>channelId</c>, and <c>categoryId</c> for metadata.</summary>
public sealed class BufferYouTubeOptions
{
    public string Template { get; set; } = "youtube.txt";

    public string? ChannelId { get; set; }

    /// <summary>Value for <c>&lt;&lt;&lt;BUFFER_YOUTUBE_CATEGORY&gt;&gt;&gt;</c> in <c>youtube.txt</c>.</summary>
    public string CategoryId { get; set; } = "22";
}

/// <summary>Configuration for scheduling posts via the <see href="https://developers.buffer.com/">Buffer GraphQL API</see> (<c>createPost</c>).</summary>
public sealed class BufferOptions
{
    public const string SectionName = "Buffer";

    /// <summary>When false, video generation skips Buffer entirely.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>API token from Buffer Publish → Settings → API. Use Bearer auth — never commit real values.</summary>
    public string? AccessToken { get; set; }

    /// <summary>GraphQL HTTP endpoint (POST JSON <c>{ "query": "&lt;mutation&gt;" }</c>).</summary>
    public string GraphqlEndpoint { get; set; } = "https://api.buffer.com";

    /// <summary>Subfolder under content root / app base that holds template files.</summary>
    public string TemplatesDirectory { get; set; } = "buffer";

    public BufferTikTokOptions TikTok { get; set; } = new();

    public BufferYouTubeOptions YouTube { get; set; } = new();

    /// <summary>UTC hour (0–23) for <c>dueAt</c>: next occurrence today or tomorrow via <see cref="BufferScheduling.NextUtcWallTime"/>.</summary>
    public int ScheduleHourUtc { get; set; } = 21;

    /// <summary>UTC minute (0–59) paired with <see cref="ScheduleHourUtc"/>; default <c>0</c> (omit from appsettings unless non-zero).</summary>
    public int ScheduleMinuteUtc { get; set; } = 0;
}
