using System.Globalization;
using System.Text;
using System.Xml;
using SkiaSharp;
using Svg.Skia;
using Svg.Skia.TypefaceProviders;

namespace ContentAgent.Video;

/// <summary>
/// Merges quiz text into question/answer SVG templates and rasterizes to PNG (TikTok 1080×1920) via Skia.
/// Titan One is loaded from <see cref="TitanOneFontFileName"/> when present.
/// </summary>
public static class QuizSlideSvgRenderer
{
    public const string QuestionTemplateRelativePath = "svg/tiktok-overlay-question.svg";

    public const string AnswerTemplateRelativePath = "svg/tiktok-overlay-answer.svg";

    /// <summary>Google Fonts OFL — place under <c>fonts/</c> next to the app.</summary>
    public const string TitanOneFontFileName = "TitanOne-Regular.ttf";

    public static string TitanOneFontRelativePath => Path.Combine("fonts", TitanOneFontFileName);

    /// <summary>
    /// Writes a merged question-phase SVG and renders it to PNG.
    /// </summary>
    public static void WriteQuestionPng(string applicationBasePath, QuizSlideItem slide, string outputPngPath)
    {
        var fontPath = Path.GetFullPath(Path.Combine(applicationBasePath, TitanOneFontRelativePath));
        var template = Path.GetFullPath(Path.Combine(applicationBasePath, QuestionTemplateRelativePath));
        if (!File.Exists(template))
            throw new FileNotFoundException("Question SVG template not found.", template);

        var xml = LoadSvg(template);
        SetTextById(xml, "question-label", string.IsNullOrWhiteSpace(slide.QuestionLabel) ? "Question:" : slide.QuestionLabel.Trim());

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

        var mergedSvgPath = Path.ChangeExtension(outputPngPath, ".merged.svg");
        SaveSvg(xml, mergedSvgPath);
        try
        {
            RenderSvgToPng(mergedSvgPath, outputPngPath, fontPath, VideoService.TikTokWidth, VideoService.TikTokHeight);
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
        var fontPath = Path.GetFullPath(Path.Combine(applicationBasePath, TitanOneFontRelativePath));
        var template = Path.GetFullPath(Path.Combine(applicationBasePath, AnswerTemplateRelativePath));
        if (!File.Exists(template))
            throw new FileNotFoundException("Answer SVG template not found.", template);

        var xml = LoadSvg(template);
        var answerText = slide.Options[slide.CorrectOptionIndex];
        SetTextById(xml, "answer-text", answerText.Trim());
        SetTextById(xml, "footer-day", "Day " + calendarDay.ToString(CultureInfo.InvariantCulture));

        var mergedSvgPath = Path.ChangeExtension(outputPngPath, ".merged.svg");
        SaveSvg(xml, mergedSvgPath);
        try
        {
            RenderSvgToPng(mergedSvgPath, outputPngPath, fontPath, VideoService.TikTokWidth, VideoService.TikTokHeight);
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

    private static void RenderSvgToPng(string svgPath, string pngPath, string titanOneTtfPath, int width, int height)
    {
        using var svg = new SKSvg();
        if (File.Exists(titanOneTtfPath))
            svg.Settings.TypefaceProviders = [new CustomTypefaceProvider(titanOneTtfPath)];

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

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        if (data == null)
            throw new InvalidOperationException("Failed to encode PNG.");

        Directory.CreateDirectory(Path.GetDirectoryName(pngPath)!);
        File.WriteAllBytes(pngPath, data.ToArray());
    }
}
