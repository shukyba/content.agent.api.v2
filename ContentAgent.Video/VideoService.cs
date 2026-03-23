using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ContentAgent.Video;

/// <summary>
/// Expects <c>Lib/ffmpeg.exe</c>, <c>mp4/salsa-festival.mp4</c>, <c>mp3/</c> default track, <c>quiz/quiz-slides.json</c>,
/// <c>svg/tiktok-overlay-question.svg</c>, <c>svg/tiktok-overlay-answer.svg</c>, and optionally <c>fonts/TitanOne-Regular.ttf</c>
/// next to the host application base directory.
/// </summary>
public sealed class VideoService : ISlideHelloWorldVideoService
{
    /// <summary>TikTok-style vertical 9:16 (recommended upload size).</summary>
    public const int TikTokWidth = 1080;

    public const int TikTokHeight = 1920;

    public const string DefaultBackgroundMp4FileName = "salsa-festival.mp4";

    public const string DefaultMp3FileName = "LA MAXIMA 79 - CUCHI CUCHI (Official Channel).mp3";

    public const string DefaultQuizJsonRelativePath = "quiz/quiz-slides.json";

    private static readonly JsonSerializerOptions QuizJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly string _applicationBasePath;
    private readonly ILogger<VideoService> _logger;

    public VideoService(string applicationBasePath, ILogger<VideoService> logger)
    {
        _applicationBasePath = applicationBasePath ?? throw new ArgumentNullException(nameof(applicationBasePath));
        _logger = logger;
    }

    public async Task<SlideVideoResult> CreateHelloWorldSlideAsync(
        string outputDirectory,
        CancellationToken cancellationToken = default,
        VideoRenderOptions? options = null)
    {
        var ffmpeg = Path.GetFullPath(Path.Combine(_applicationBasePath, "Lib", "ffmpeg.exe"));
        var backgroundMp4 = Path.GetFullPath(Path.Combine(_applicationBasePath, "mp4", DefaultBackgroundMp4FileName));
        var mp3 = Path.GetFullPath(Path.Combine(_applicationBasePath, "mp3", DefaultMp3FileName));
        var quizPath = Path.GetFullPath(
            string.IsNullOrWhiteSpace(options?.QuizJsonPath)
                ? Path.Combine(_applicationBasePath, DefaultQuizJsonRelativePath)
                : options!.QuizJsonPath!);
        var fontPath = Path.GetFullPath(Path.Combine(_applicationBasePath, QuizSlideSvgRenderer.TitanOneFontRelativePath));

        if (!File.Exists(ffmpeg))
            return new SlideVideoResult(false, null, $"FFmpeg not found at {ffmpeg}. Add Lib/ffmpeg.exe under ContentAgent.Video.");

        if (!File.Exists(backgroundMp4))
            return new SlideVideoResult(false, null, $"Background video not found at {backgroundMp4}. Add mp4/{DefaultBackgroundMp4FileName} under ContentAgent.Video.");

        if (!File.Exists(mp3))
            return new SlideVideoResult(false, null, $"MP3 not found at {mp3}. Add the file under ContentAgent.Video/mp3/.");

        if (!File.Exists(quizPath))
            return new SlideVideoResult(false, null, $"Quiz JSON not found at {quizPath}. Add {DefaultQuizJsonRelativePath}.");

        if (!File.Exists(fontPath))
            _logger.LogWarning("Titan One font not found at {Font}; SVG text may fall back to a system sans-serif.", fontPath);

        QuizSlidesDocument doc;
        try
        {
            await using var stream = File.OpenRead(quizPath);
            doc = await JsonSerializer.DeserializeAsync<QuizSlidesDocument>(stream, QuizJsonOptions, cancellationToken)
                  ?? new QuizSlidesDocument();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse quiz JSON");
            return new SlideVideoResult(false, null, $"Invalid quiz JSON: {ex.Message}");
        }

        var validation = ValidateQuizDocument(doc);
        if (validation != null)
            return new SlideVideoResult(false, null, validation);

        var calendarDay = options?.CalendarDay ?? DateTime.Today.Day;
        if (calendarDay is < 1 or > 31)
            return new SlideVideoResult(false, null, "Calendar day override must be between 1 and 31.");

        var slidesForToday = doc.Slides.Where(s => s.Day == calendarDay).ToList();
        if (slidesForToday.Count == 0)
            return new SlideVideoResult(false, null, $"No slide with \"day\": {calendarDay} in the quiz JSON.");

        var docForRender = new QuizSlidesDocument
        {
            QuestionDurationSeconds = doc.QuestionDurationSeconds,
            AnswerDurationSeconds = doc.AnswerDurationSeconds,
            Slides = slidesForToday
        };

        var qSec = Math.Clamp(doc.QuestionDurationSeconds, 1, 120);
        var aSec = Math.Clamp(doc.AnswerDurationSeconds, 0, 120);
        var blockSeconds = qSec + aSec;
        var totalSeconds = blockSeconds * docForRender.Slides.Count;

        Directory.CreateDirectory(outputDirectory);
        var outFile = Path.Combine(outputDirectory, $"{calendarDay.ToString(CultureInfo.InvariantCulture)}.mp4");
        outFile = Path.GetFullPath(outFile);

        var tempPngDir = Path.Combine(Path.GetTempPath(), "contentagent-quiz-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(tempPngDir);

        List<string> pngPaths;
        try
        {
            pngPaths = RenderSlidePngs(_applicationBasePath, docForRender, calendarDay, tempPngDir, aSec);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render quiz SVG overlays to PNG");
            TryDeleteDirectory(tempPngDir);
            return new SlideVideoResult(false, null, $"SVG/PNG render failed: {ex.Message}");
        }

        var filterComplex = QuizVideoFilterGraph.BuildOverlayFilterComplex(docForRender, qSec, aSec, totalSeconds, pngPaths.Count);
        var duration = totalSeconds.ToString(CultureInfo.InvariantCulture);

        var args = new List<string>
        {
            "-y",
            "-hide_banner",
            "-loglevel", "error",
            "-stream_loop", "-1",
            "-i", backgroundMp4,
            "-stream_loop", "-1",
            "-i", mp3
        };

        foreach (var png in pngPaths)
        {
            args.AddRange(new[] { "-loop", "1", "-framerate", "30", "-t", duration, "-i", png });
        }

        args.AddRange(new[]
        {
            "-filter_complex", filterComplex,
            "-map", "[vout]",
            "-map", "1:a:0",
            "-t", duration,
            "-c:v", "libx264",
            "-preset", "fast",
            "-pix_fmt", "yuv420p",
            "-c:a", "aac",
            "-b:a", "192k",
            "-movflags", "+faststart",
            outFile
        });

        _logger.LogInformation(
            "Starting FFmpeg for quiz video day {Day} -> {Out} ({Slides} slide(s), {Total}s, SVG overlays)",
            calendarDay,
            outFile,
            docForRender.Slides.Count,
            totalSeconds);

        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi };
        try
        {
            proc.Start();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start FFmpeg");
            TryDeleteDirectory(tempPngDir);
            return new SlideVideoResult(false, null, $"Failed to start FFmpeg: {ex.Message}");
        }

        await using (cancellationToken.Register(() =>
                     {
                         try
                         {
                             if (!proc.HasExited)
                                 proc.Kill(entireProcessTree: true);
                         }
                         catch
                         {
                             /* ignore */
                         }
                     }))
        {
            var err = await proc.StandardError.ReadToEndAsync(cancellationToken);
            await proc.WaitForExitAsync(cancellationToken);

            if (proc.ExitCode != 0)
            {
                _logger.LogWarning("FFmpeg exited {Code}: {Err}", proc.ExitCode, err);
                TryDeleteDirectory(tempPngDir);
                return new SlideVideoResult(false, null, string.IsNullOrWhiteSpace(err) ? $"FFmpeg exited with code {proc.ExitCode}" : err.Trim());
            }
        }

        TryDeleteDirectory(tempPngDir);

        if (!File.Exists(outFile))
            return new SlideVideoResult(false, null, "FFmpeg reported success but output file is missing.");

        var socialCaption = QuizSocialCaptionFormatter.FormatSlides(docForRender.Slides);
        return new SlideVideoResult(true, outFile, null, socialCaption);
    }

    private static List<string> RenderSlidePngs(string applicationBasePath, QuizSlidesDocument doc, int calendarDay, string tempPngDir, int answerDurationSeconds)
    {
        var perSlide = answerDurationSeconds > 0 ? 2 : 1;
        var list = new List<string>(doc.Slides.Count * perSlide);
        for (var i = 0; i < doc.Slides.Count; i++)
        {
            var slide = doc.Slides[i];
            var qPng = Path.Combine(tempPngDir, $"slide-{i.ToString(CultureInfo.InvariantCulture)}-q.png");
            QuizSlideSvgRenderer.WriteQuestionPng(applicationBasePath, slide, qPng);
            list.Add(qPng);
            if (answerDurationSeconds > 0)
            {
                var aPng = Path.Combine(tempPngDir, $"slide-{i.ToString(CultureInfo.InvariantCulture)}-a.png");
                QuizSlideSvgRenderer.WriteAnswerPng(applicationBasePath, slide, calendarDay, aPng);
                list.Add(aPng);
            }
        }

        return list;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            /* ignore */
        }
    }

    private static string? ValidateQuizDocument(QuizSlidesDocument doc)
    {
        if (doc.Slides.Count == 0)
            return "Quiz JSON must contain at least one entry in \"slides\".";

        var seenDays = new HashSet<int>();
        for (var i = 0; i < doc.Slides.Count; i++)
        {
            var s = doc.Slides[i];
            if (s.Day < 1 || s.Day > 31)
                return $"Slide {i}: \"day\" must be between 1 and 31.";

            if (!seenDays.Add(s.Day))
                return $"Duplicate \"day\" value {s.Day} in quiz-slides.json; each day must appear once.";

            if (s.QuestionLines.Count == 0)
                return $"Slide {i}: \"questionLines\" must contain at least one string.";

            if (s.Options.Count == 0)
                return $"Slide {i}: \"options\" must contain at least one string.";

            if (s.CorrectOptionIndex < 0 || s.CorrectOptionIndex >= s.Options.Count)
                return $"Slide {i}: \"correctOptionIndex\" must be between 0 and {s.Options.Count - 1}.";
        }

        return null;
    }
}
