using System.Globalization;
using System.Text;

namespace ContentAgent.Video;

/// <summary>Builds FFmpeg <c>-filter_complex</c> for quiz PNG overlays (testable).</summary>
public static class QuizVideoFilterGraph
{
    /// <summary>Scale, blur base video; timed full-frame PNG overlays (question, and optionally answer per slide).</summary>
    public static string BuildOverlayFilterComplex(QuizSlidesDocument doc, int questionSeconds, int answerSeconds, int totalSeconds, int overlayCount)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"[0:v]fps=30,scale={VideoService.TikTokWidth}:{VideoService.TikTokHeight}:force_original_aspect_ratio=increase,");
        sb.Append(CultureInfo.InvariantCulture, $"crop={VideoService.TikTokWidth}:{VideoService.TikTokHeight},setsar=1,");
        sb.Append(CultureInfo.InvariantCulture, $"trim=duration={totalSeconds},setpts=PTS-STARTPTS,");
        sb.Append("boxblur=luma_radius=18:luma_power=3[base];");

        var label = "base";

        if (answerSeconds <= 0)
        {
            if (overlayCount != doc.Slides.Count)
                throw new ArgumentException("PNG count must match slide count when answer duration is 0.", nameof(overlayCount));

            for (var i = 0; i < doc.Slides.Count; i++)
            {
                var t0 = i * questionSeconds;
                var tEnd = t0 + questionSeconds;
                var inputQ = 2 + i;
                var isLast = i == doc.Slides.Count - 1;
                var next = isLast ? "vout" : $"l{i.ToString(CultureInfo.InvariantCulture)}";
                sb.Append(CultureInfo.InvariantCulture,
                    $"[{label}][{inputQ.ToString(CultureInfo.InvariantCulture)}:v]overlay=0:0:format=auto:shortest=1:enable='between(t,{t0.ToString(CultureInfo.InvariantCulture)},{tEnd.ToString(CultureInfo.InvariantCulture)})'[{next}];");
                label = next;
            }

            return TrimFilterComplex(sb);
        }

        if (overlayCount != doc.Slides.Count * 2)
            throw new ArgumentException("PNG count must be 2 × slide count when answer duration is positive.", nameof(overlayCount));

        var block = questionSeconds + answerSeconds;
        var segmentIndex = 0;

        for (var i = 0; i < doc.Slides.Count; i++)
        {
            var t0 = i * block;
            var tQEnd = t0 + questionSeconds;
            var tEnd = t0 + block;

            var inputQ = 2 + segmentIndex;
            segmentIndex++;
            var inputA = 2 + segmentIndex;
            segmentIndex++;

            var afterQuestion = $"lq{i.ToString(CultureInfo.InvariantCulture)}";
            sb.Append(CultureInfo.InvariantCulture, $"[{label}][{inputQ.ToString(CultureInfo.InvariantCulture)}:v]overlay=0:0:format=auto:shortest=1:enable='between(t,{t0.ToString(CultureInfo.InvariantCulture)},{tQEnd.ToString(CultureInfo.InvariantCulture)})'[{afterQuestion}];");

            var isLast = i == doc.Slides.Count - 1;
            var afterAnswer = isLast ? "vout" : $"la{i.ToString(CultureInfo.InvariantCulture)}";
            sb.Append(CultureInfo.InvariantCulture,
                $"[{afterQuestion}][{inputA.ToString(CultureInfo.InvariantCulture)}:v]overlay=0:0:format=auto:shortest=1:enable='between(t,{tQEnd.ToString(CultureInfo.InvariantCulture)},{tEnd.ToString(CultureInfo.InvariantCulture)})'[{afterAnswer}];");

            label = afterAnswer;
        }

        return TrimFilterComplex(sb);
    }

    /// <summary>FFmpeg treats a trailing <c>;</c> as an extra empty filter graph (error: No such filter: '').</summary>
    public static string TrimFilterComplex(StringBuilder sb) =>
        sb.ToString().TrimEnd().TrimEnd(';').TrimEnd();
}
