using System.Globalization;
using System.Text;
using System.Xml;
using SkiaSharp;
using Svg.Skia;
using Svg.Skia.TypefaceProviders;

namespace ContentAgent.Video;

/// <summary>
/// Merges quiz text into question/answer SVG templates and rasterizes to PNG (TikTok 1080×1920) via Skia.
/// Righteous is loaded from <see cref="RighteousFontFileName"/> when present.
/// </summary>
public static class QuizSlideSvgRenderer
{
    public const string QuestionTemplateRelativePath = "svg/tiktok-overlay-question.svg";

    public const string AnswerTemplateRelativePath = "svg/tiktok-overlay-answer.svg";

    /// <summary>Google Fonts OFL — place under <c>fonts/</c> next to the app.</summary>
    public const string RighteousFontFileName = "Righteous-Regular.ttf";

    public static string RighteousFontRelativePath => Path.Combine("fonts", RighteousFontFileName);

    private static readonly bool FontDiagnosticOverlayEnabled =
        string.Equals(Environment.GetEnvironmentVariable("VIDEO_FONT_DIAGNOSTIC"), "1", StringComparison.OrdinalIgnoreCase);

    /// <summary>Geometry for Skia text copied from merged SVG <c>&lt;text&gt;</c> attributes (single layout source).</summary>
    private readonly struct SvgTextLayout
    {
        public float X { get; init; }
        public float Y { get; init; }
        public float FontSize { get; init; }
        public float Opacity { get; init; }
        public bool IsCentered { get; init; }
    }

    /// <summary>
    /// Writes a merged question-phase SVG and renders it to PNG.
    /// </summary>
    public static void WriteQuestionPng(string applicationBasePath, QuizSlideItem slide, string outputPngPath)
    {
        var fontPath = Path.GetFullPath(Path.Combine(applicationBasePath, RighteousFontRelativePath));
        var template = Path.GetFullPath(Path.Combine(applicationBasePath, QuestionTemplateRelativePath));
        if (!File.Exists(template))
            throw new FileNotFoundException("Question SVG template not found.", template);

        var xml = LoadSvg(template);
        var questionLabel = string.IsNullOrWhiteSpace(slide.QuestionLabel) ? "Question:" : slide.QuestionLabel.Trim();
        SetTextById(xml, "question-label", questionLabel);

        var lines = slide.QuestionLines.Where(static l => !string.IsNullOrWhiteSpace(l)).Select(static l => l.Trim()).ToList();
        for (var i = 0; i < 3; i++)
        {
            var id = "question-line-" + i.ToString(CultureInfo.InvariantCulture);
            if (i < lines.Count)
            {
                SetTextById(xml, id, lines[i]);
                SetAttributeById(xml, id, "opacity", i == 2 && lines.Count == 2 ? "0" : "1");
            }
            else
            {
                SetTextById(xml, id, string.Empty);
                SetAttributeById(xml, id, "opacity", "0");
            }
        }

        var opts = slide.Options;
        for (var o = 0; o < 4; o++)
        {
            var show = o < opts.Count && !string.IsNullOrWhiteSpace(opts[o]);
            var textId = "option-text-" + o.ToString(CultureInfo.InvariantCulture);
            var bgId = "option-bg-" + o.ToString(CultureInfo.InvariantCulture);
            if (show)
                SetTextById(xml, textId, opts[o].Trim());
            else
            {
                SetTextById(xml, textId, string.Empty);
                SetDisplayNoneById(xml, textId);
                SetDisplayNoneById(xml, bgId);
            }
        }

        if (!TryGetSvgTextLayout(xml, "question-label", out var labelLayout))
            throw new InvalidOperationException("SVG template must define <text id=\"question-label\"> with x, y, and font-size.");

        var lineLayouts = new SvgTextLayout[3];
        for (var i = 0; i < 3; i++)
        {
            var id = "question-line-" + i.ToString(CultureInfo.InvariantCulture);
            if (!TryGetSvgTextLayout(xml, id, out lineLayouts[i]))
                throw new InvalidOperationException($"SVG template must define <text id=\"{id}\"> with x, y, and font-size.");
        }

        var optionLayouts = new SvgTextLayout[4];
        for (var o = 0; o < 4; o++)
        {
            var id = "option-text-" + o.ToString(CultureInfo.InvariantCulture);
            if (!TryGetSvgTextLayout(xml, id, out optionLayouts[o]))
                throw new InvalidOperationException($"SVG template must define <text id=\"{id}\"> with x, y, and font-size.");
        }

        // Hide all dynamic SVG text; we draw text directly with Skia + explicit typeface for deterministic font output.
        SetDisplayNoneById(xml, "question-label");
        SetDisplayNoneById(xml, "question-line-0");
        SetDisplayNoneById(xml, "question-line-1");
        SetDisplayNoneById(xml, "question-line-2");
        SetDisplayNoneById(xml, "option-text-0");
        SetDisplayNoneById(xml, "option-text-1");
        SetDisplayNoneById(xml, "option-text-2");
        SetDisplayNoneById(xml, "option-text-3");

        var mergedSvgPath = Path.ChangeExtension(outputPngPath, ".merged.svg");
        SaveSvg(xml, mergedSvgPath);
        try
        {
            RenderSvgToPng(
                mergedSvgPath,
                outputPngPath,
                fontPath,
                VideoService.TikTokWidth,
                VideoService.TikTokHeight,
                canvas =>
                {
                    DrawTextFromLayout(canvas, questionLabel, labelLayout, fontPath);
                    if (lines.Count > 0) DrawTextFromLayout(canvas, lines[0], lineLayouts[0], fontPath);
                    if (lines.Count > 1) DrawTextFromLayout(canvas, lines[1], lineLayouts[1], fontPath);
                    if (lines.Count > 2) DrawTextFromLayout(canvas, lines[2], lineLayouts[2], fontPath);

                    for (var o = 0; o < Math.Min(4, opts.Count); o++)
                    {
                        var optionText = opts[o]?.Trim();
                        if (string.IsNullOrWhiteSpace(optionText))
                            continue;
                        DrawTextFromLayout(canvas, optionText!, optionLayouts[o], fontPath);
                    }

                    if (FontDiagnosticOverlayEnabled)
                        DrawFontDiagnosticOverlay(canvas, fontPath);
                });
        }
        finally
        {
            try
            {
                File.Delete(mergedSvgPath);
            }
            catch
            {
                /* ignore */
            }
        }
    }

    /// <summary>
    /// Writes a merged answer-phase SVG and renders it to PNG.
    /// </summary>
    public static void WriteAnswerPng(string applicationBasePath, QuizSlideItem slide, int calendarDay, string outputPngPath)
    {
        var fontPath = Path.GetFullPath(Path.Combine(applicationBasePath, RighteousFontRelativePath));
        var template = Path.GetFullPath(Path.Combine(applicationBasePath, AnswerTemplateRelativePath));
        if (!File.Exists(template))
            throw new FileNotFoundException("Answer SVG template not found.", template);

        var xml = LoadSvg(template);
        var answerText = slide.Options[slide.CorrectOptionIndex];
        SetTextById(xml, "answer-text", answerText.Trim());
        SetTextById(xml, "footer-day", "Day " + calendarDay.ToString(CultureInfo.InvariantCulture));

        if (!TryGetSvgTextLayout(xml, "answer-heading", out var headingLayout))
            throw new InvalidOperationException("SVG template must define <text id=\"answer-heading\"> with x, y, and font-size.");
        if (!TryGetSvgTextLayout(xml, "answer-text", out var answerLayout))
            throw new InvalidOperationException("SVG template must define <text id=\"answer-text\"> with x, y, and font-size.");
        if (!TryGetSvgTextLayout(xml, "footer-day", out var footerLayout))
            throw new InvalidOperationException("SVG template must define <text id=\"footer-day\"> with x, y, and font-size.");

        SetDisplayNoneById(xml, "answer-heading");
        SetDisplayNoneById(xml, "answer-text");
        SetDisplayNoneById(xml, "footer-day");

        var mergedSvgPath = Path.ChangeExtension(outputPngPath, ".merged.svg");
        SaveSvg(xml, mergedSvgPath);
        try
        {
            RenderSvgToPng(
                mergedSvgPath,
                outputPngPath,
                fontPath,
                VideoService.TikTokWidth,
                VideoService.TikTokHeight,
                canvas =>
                {
                    DrawTextFromLayout(canvas, "Answer", headingLayout, fontPath);
                    DrawTextFromLayout(canvas, answerText.Trim(), answerLayout, fontPath);
                    DrawTextFromLayout(canvas, "Day " + calendarDay.ToString(CultureInfo.InvariantCulture), footerLayout, fontPath);

                    if (FontDiagnosticOverlayEnabled)
                        DrawFontDiagnosticOverlay(canvas, fontPath);
                });
        }
        finally
        {
            try
            {
                File.Delete(mergedSvgPath);
            }
            catch
            {
                /* ignore */
            }
        }
    }

    private static XmlDocument LoadSvg(string path)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.Load(path);
        return doc;
    }

    private static void SaveSvg(XmlDocument doc, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = XmlWriter.Create(path, new XmlWriterSettings { Indent = true, OmitXmlDeclaration = false });
        doc.Save(writer);
    }

    private static void SetTextById(XmlDocument doc, string elementId, string text)
    {
        var n = doc.SelectSingleNode($"//*[@id='{elementId}']");
        if (n is not XmlElement el)
            return;
        el.InnerText = SanitizeForXml(text);
    }

    /// <summary>Strips characters that are not allowed in XML 1.0 text (e.g. control chars from pasted quiz content).</summary>
    private static string SanitizeForXml(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (XmlConvert.IsXmlChar(c))
                sb.Append(c);
        }

        return sb.ToString();
    }

    private static void SetAttributeById(XmlDocument doc, string elementId, string attrName, string value)
    {
        var n = doc.SelectSingleNode($"//*[@id='{elementId}']");
        if (n is not XmlElement el)
            return;
        el.SetAttribute(attrName, value);
    }

    private static void SetDisplayNoneById(XmlDocument doc, string elementId)
    {
        var n = doc.SelectSingleNode($"//*[@id='{elementId}']");
        if (n is not XmlElement el)
            return;
        el.SetAttribute("display", "none");
    }

    /// <summary>Reads <c>x</c>, <c>y</c>, <c>font-size</c>, <c>text-anchor</c>, <c>opacity</c> from a <c>&lt;text&gt;</c> node.</summary>
    private static bool TryGetSvgTextLayout(XmlDocument doc, string elementId, out SvgTextLayout layout)
    {
        layout = default;
        var n = doc.SelectSingleNode($"//*[@id='{elementId}']");
        if (n is not XmlElement el)
            return false;
        if (!TryParseSvgLength(el.GetAttribute("x"), out var x))
            return false;
        if (!TryParseSvgLength(el.GetAttribute("y"), out var y))
            return false;
        if (!TryParseSvgLength(el.GetAttribute("font-size"), out var fontSize))
            return false;
        var anchor = el.GetAttribute("text-anchor");
        var isCentered = string.Equals(anchor, "middle", StringComparison.OrdinalIgnoreCase);
        var opacity = 1f;
        if (el.HasAttribute("opacity") &&
            float.TryParse(el.GetAttribute("opacity"), NumberStyles.Float, CultureInfo.InvariantCulture, out var op))
            opacity = op;
        layout = new SvgTextLayout
        {
            X = x,
            Y = y,
            FontSize = fontSize,
            Opacity = opacity,
            IsCentered = isCentered
        };
        return true;
    }

    private static bool TryParseSvgLength(string raw, out float value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        raw = raw.Trim();
        if (raw.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            raw = raw[..^2].Trim();
        return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static void DrawTextFromLayout(SKCanvas canvas, string text, SvgTextLayout layout, string fontPath)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        if (layout.IsCentered)
            DrawCenteredText(canvas, text, layout.X, layout.Y, layout.FontSize, fontPath, layout.Opacity);
        else
            DrawLeftText(canvas, text, layout.X, layout.Y, layout.FontSize, fontPath, layout.Opacity);
    }

    private static void RenderSvgToPng(string svgPath, string pngPath, string righteousTtfPath, int width, int height, Action<SKCanvas>? overlay = null)
    {
        using var svg = new SKSvg();
        if (File.Exists(righteousTtfPath))
            svg.Settings.TypefaceProviders = [new CustomTypefaceProvider(righteousTtfPath)];

        using var pic = svg.Load(svgPath);
        if (pic == null)
            throw new InvalidOperationException("SKSvg failed to load: " + svgPath);

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var rect = pic.CullRect;
        if (rect.Width > 0 && rect.Height > 0)
        {
            canvas.Save();
            canvas.Scale(width / rect.Width, height / rect.Height);
            canvas.Translate(-rect.Left, -rect.Top);
            canvas.DrawPicture(pic);
            canvas.Restore();
        }

        overlay?.Invoke(canvas);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        if (data == null)
            throw new InvalidOperationException("Failed to encode PNG.");

        Directory.CreateDirectory(Path.GetDirectoryName(pngPath)!);
        File.WriteAllBytes(pngPath, data.ToArray());
    }

    private static void DrawCenteredText(SKCanvas canvas, string text, float centerX, float baselineY, float fontSize, string righteousTtfPath, float opacity = 1f)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        using var paint = BuildPaint(fontSize, righteousTtfPath, opacity);
        var width = paint.MeasureText(text);
        canvas.DrawText(text, centerX - (width / 2f), baselineY, paint);
    }

    private static void DrawLeftText(SKCanvas canvas, string text, float leftX, float baselineY, float fontSize, string righteousTtfPath, float opacity = 1f)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        using var paint = BuildPaint(fontSize, righteousTtfPath, opacity);
        canvas.DrawText(text, leftX, baselineY, paint);
    }

    private static SKPaint BuildPaint(float fontSize, string righteousTtfPath, float opacity = 1f)
    {
        var paint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.White.WithAlpha((byte)Math.Clamp((int)(opacity * 255f), 0, 255)),
            TextSize = fontSize,
            Typeface = File.Exists(righteousTtfPath) ? SKTypeface.FromFile(righteousTtfPath) : SKTypeface.Default
        };
        return paint;
    }

    private static void DrawFontDiagnosticOverlay(SKCanvas canvas, string righteousTtfPath)
    {
        DrawLeftText(canvas, "DIAG TitanOne", 60, 1230, 44, righteousTtfPath);
        DrawLeftText(canvas, "DIAG Arial", 60, 1288, 44, righteousTtfPath);
        DrawLeftText(canvas, "DIAG Monospace", 60, 1346, 44, righteousTtfPath);

        using var arial = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.White,
            TextSize = 44,
            Typeface = SKTypeface.FromFamilyName("Arial")
        };
        canvas.DrawText("DIAG Arial", 60, 1288, arial);

        using var mono = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.White,
            TextSize = 44,
            Typeface = SKTypeface.FromFamilyName("Consolas")
        };
        canvas.DrawText("DIAG Monospace", 60, 1346, mono);
    }
}
