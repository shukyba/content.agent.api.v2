namespace ContentAgent.Video;

/// <summary>
/// Optional absolute paths for large binaries not shipped in publish (e.g. Azure: <c>D:\home\data\...</c> or mounted storage).
/// Bind from configuration section <see cref="SectionName"/>. When a property is empty, the path under the app base directory is used.
/// </summary>
public sealed class VideoAssetPathOptions
{
    public const string SectionName = "Video";

    /// <summary>Full path to <c>ffmpeg.exe</c>. Default: <c>{appBase}/Lib/ffmpeg.exe</c>.</summary>
    public string? FfmpegPath { get; set; }

    /// <summary>Full path to background MP4 (e.g. <c>background-video.mp4</c>). Default: <c>{appBase}/mp4/background-video.mp4</c>.</summary>
    public string? BackgroundMp4Path { get; set; }

    /// <summary>Full path to audio MP3. Default: <c>{appBase}/mp3/</c> + <see cref="VideoService.DefaultMp3FileName"/>.</summary>
    public string? Mp3Path { get; set; }
}
