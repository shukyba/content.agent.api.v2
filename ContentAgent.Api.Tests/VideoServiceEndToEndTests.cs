using ContentAgent.Video;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContentAgent.Api.Tests;

public sealed class VideoServiceEndToEndTests
{
    /// <summary>Minimal quiz matching <see cref="TestCalendarDay"/> so the test does not depend on the real calendar.</summary>
    private const int TestCalendarDay = 7;

    private static readonly string MinimalQuizJson =
        $$"""
        {
          "questionDurationSeconds": 15,
          "answerDurationSeconds": 0,
          "slides": [
            {
              "day": {{TestCalendarDay}},
              "questionLabel": "Question:",
              "questionLines": ["Integration", "test"],
              "options": ["A. One", "B. Two", "C. Three", "D. Four"],
              "correctOptionIndex": 0
            }
          ]
        }
        """;

    [SkippableFact]
    public async Task CreateHelloWorldSlideAsync_WritesMp4_WhenBundledAssetsExist()
    {
        var assetRoot = TryResolveVideoAssetRoot();
        Skip.If(assetRoot is null,
            "Video assets not found. Ensure ffmpeg, mp4, mp3, quiz, svg, and fonts are under the test output or Api bin/Debug/net8.0.");

        var tempDir = Path.Combine(Path.GetTempPath(), "contentagent-video-test-" + Guid.NewGuid().ToString("N"));
        var quizPath = Path.Combine(tempDir, "quiz-test.json");
        Directory.CreateDirectory(tempDir);
        try
        {
            await File.WriteAllTextAsync(quizPath, MinimalQuizJson);

            var outDir = Path.Combine(tempDir, "out");
            Directory.CreateDirectory(outDir);

            var service = new VideoService(assetRoot, NullLogger<VideoService>.Instance);
            var result = await service.CreateHelloWorldSlideAsync(
                outDir,
                CancellationToken.None,
                new VideoRenderOptions { CalendarDay = TestCalendarDay, QuizJsonPath = quizPath });

            Assert.True(result.Success, result.ErrorMessage ?? "(no message)");
            Assert.NotNull(result.OutputPath);
            Assert.True(File.Exists(result.OutputPath), "Expected MP4 at " + result.OutputPath);
            var len = new FileInfo(result.OutputPath).Length;
            Assert.True(len > 10_000, $"Expected non-trivial MP4 size, got {len} bytes.");
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                /* ignore */
            }
        }
    }

    /// <summary>Testhost output (copied assets) or sibling Api build output.</summary>
    private static string? TryResolveVideoAssetRoot()
    {
        var candidates = new List<string>
        {
            AppContext.BaseDirectory,
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "bin", "Debug", "net8.0")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "bin", "Release", "net8.0"))
        };

        foreach (var dir in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(dir))
                continue;

            var ffmpeg = Path.Combine(dir, "Lib", "ffmpeg.exe");
            var mp4 = Path.Combine(dir, "mp4", VideoService.DefaultBackgroundMp4FileName);
            var mp3 = Path.Combine(dir, "mp3", VideoService.DefaultMp3FileName);
            var questionSvg = Path.Combine(dir, "svg", "tiktok-overlay-question.svg");
            var quiz = Path.Combine(dir, "quiz", "quiz-slides.json");

            if (File.Exists(ffmpeg) && File.Exists(mp4) && File.Exists(mp3) && File.Exists(questionSvg) && File.Exists(quiz))
                return dir;
        }

        return null;
    }
}
