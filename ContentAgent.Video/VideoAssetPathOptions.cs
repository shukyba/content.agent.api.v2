namespace ContentAgent.Video;

/// <summary>
/// Optional layout for large binaries not shipped in publish (e.g. Azure: <c>D:\home\data\</c> or mounted storage).
/// Bind from configuration section <see cref="SectionName"/>. When <c>Video:AssetRoot</c> is omitted, the API host may set it from <c>RootDirectory</c>; when empty, paths resolve under the app base directory.
/// </summary>
public sealed class VideoAssetPathOptions
{
    public const string SectionName = "Video";

    /// <summary>
    /// Root folder containing <c>Lib/ffmpeg.exe</c>, <c>mp4/</c>, and <c>mp3/</c> (same layout as next to the published app).
    /// Omit this key to inherit <c>RootDirectory</c> from the API host. Individual paths below override when set.
    /// </summary>
    public string? AssetRoot { get; set; }

    /// <summary>Full path to <c>ffmpeg.exe</c>; when empty, <c>{AssetRoot}/Lib/ffmpeg.exe</c> or <c>{appBase}/Lib/ffmpeg.exe</c>.</summary>
    public string? FfmpegPath { get; set; }

    /// <summary>Full path to background MP4; when empty, under <see cref="AssetRoot"/> or app base.</summary>
    public string? BackgroundMp4Path { get; set; }

    /// <summary>Full path to audio MP3; when empty, under <see cref="AssetRoot"/> or app base.</summary>
    public string? Mp3Path { get; set; }
}
