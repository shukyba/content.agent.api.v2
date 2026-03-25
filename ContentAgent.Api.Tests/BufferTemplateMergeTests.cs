using ContentAgent.Api.Services;
using Xunit;

namespace ContentAgent.Api.Tests;

public sealed class BufferTemplateMergeTests
{
    [Fact]
    public void ToGraphQlStringLiteral_escapes_quotes_and_newlines()
    {
        Assert.Equal("\"\"", BufferScheduleService.ToGraphQlStringLiteral(""));
        Assert.Equal("\"a\"", BufferScheduleService.ToGraphQlStringLiteral("a"));
        Assert.Equal("\"say \\\"hi\\\"\"", BufferScheduleService.ToGraphQlStringLiteral("say \"hi\""));
        Assert.Equal("\"a\\nb\"", BufferScheduleService.ToGraphQlStringLiteral("a\nb"));
        Assert.Equal("\"a\\\\b\"", BufferScheduleService.ToGraphQlStringLiteral("a\\b"));
    }

    [Fact]
    public void ApplyBufferPlaceholders_tiktok_tokens()
    {
        const string tpl = """
            mutation CreatePost {
              text: <<<BUFFER_TEXT>>>,
              ch: <<<BUFFER_CHANNEL_ID>>>,
              dueAt: <<<BUFFER_DUE_AT>>>,
              mode: <<<BUFFER_MODE>>>,
              url:<<<BUFFER_VIDEO_URL>>>
            }
            """;

        var q = BufferScheduleService.ApplyBufferPlaceholders(
            tpl,
            text: "Hello",
            title: "ignored",
            dueAtIso: "2026-03-24T21:00:00.000Z",
            videoUrl: "https://host/v.mp4",
            modeToken: "customScheduled",
            channelId: "tiktok-channel-1",
            includeYouTubePlaceholders: false,
            categoryId: "22");

        Assert.Contains("text: \"Hello\"", q, StringComparison.Ordinal);
        Assert.Contains("dueAt: \"2026-03-24T21:00:00.000Z\"", q, StringComparison.Ordinal);
        Assert.Contains("ch: \"tiktok-channel-1\"", q, StringComparison.Ordinal);
        Assert.Contains("mode: customScheduled", q, StringComparison.Ordinal);
        Assert.Contains("url:\"https://host/v.mp4\"", q, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyBufferPlaceholders_facebook_tokens_like_tiktok()
    {
        const string tpl = """
            metadata: { facebook: { type: reel } }
            text: <<<BUFFER_TEXT>>>,
            ch: <<<BUFFER_CHANNEL_ID>>>,
            url:<<<BUFFER_VIDEO_URL>>>
            """;

        var q = BufferScheduleService.ApplyBufferPlaceholders(
            tpl,
            text: "Hello FB",
            title: "ignored",
            dueAtIso: "2026-03-27T10:28:47.545Z",
            videoUrl: "https://host/v.mp4",
            modeToken: "customScheduled",
            channelId: "fb-channel-1",
            includeYouTubePlaceholders: false,
            categoryId: "22");

        Assert.Contains("text: \"Hello FB\"", q, StringComparison.Ordinal);
        Assert.Contains("ch: \"fb-channel-1\"", q, StringComparison.Ordinal);
        Assert.Contains("url:\"https://host/v.mp4\"", q, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyBufferPlaceholders_youtube_includes_title_and_category()
    {
        const string tpl = """
            title: <<<BUFFER_YOUTUBE_TITLE>>>,
            categoryId: <<<BUFFER_YOUTUBE_CATEGORY>>>
            """;

        var q = BufferScheduleService.ApplyBufferPlaceholders(
            tpl,
            text: "t",
            title: "My Title",
            dueAtIso: "d",
            videoUrl: "u",
            modeToken: "m",
            channelId: "yt-id",
            includeYouTubePlaceholders: true,
            categoryId: "22");

        Assert.Contains("title: \"My Title\"", q, StringComparison.Ordinal);
        Assert.Contains("categoryId: \"22\"", q, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildMutationFromTemplate_roundtrip_temp_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"buffer-test-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, """
            mutation CreatePost {
              x: <<<BUFFER_TEXT>>>
              m: <<<BUFFER_MODE>>>
            }
            """);

        try
        {
            var raw = await File.ReadAllTextAsync(path);
            var q = BufferScheduleService.ApplyBufferPlaceholders(
                raw,
                text: "Hi",
                title: "t",
                dueAtIso: "d",
                videoUrl: "u",
                modeToken: "automatic",
                channelId: "c1",
                includeYouTubePlaceholders: false,
                categoryId: "22");

            Assert.StartsWith("mutation CreatePost", q.TrimStart(), StringComparison.Ordinal);
            Assert.Contains("x: \"Hi\"", q, StringComparison.Ordinal);
            Assert.Contains("m: automatic", q, StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                /* ignore */
            }
        }
    }
}
