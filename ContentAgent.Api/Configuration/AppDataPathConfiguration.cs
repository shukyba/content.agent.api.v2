using Microsoft.Extensions.Configuration;

namespace ContentAgent.Api.Configuration;

/// <summary>Shared <c>RootDirectory</c> for large on-disk assets (video binaries, GSC keys) when section-specific paths are omitted.</summary>
internal static class AppDataPathConfiguration
{
    public const string RootDirectoryKey = "RootDirectory";

    /// <summary>
    /// Non-empty <c>Sitemap:GoogleServiceAccountKeyRoot</c> wins. If that key is absent from configuration, uses <see cref="RootDirectoryKey"/>.
    /// If the key is present but empty/whitespace, returns <c>null</c> (resolve keys next to the agent folder).
    /// </summary>
    public static string? ResolveGoogleServiceAccountKeyRoot(IConfiguration configuration)
    {
        var raw = configuration["Sitemap:GoogleServiceAccountKeyRoot"];
        if (raw is null)
        {
            var rd = configuration[RootDirectoryKey]?.Trim();
            return string.IsNullOrEmpty(rd) ? null : rd;
        }

        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return raw.Trim();
    }
}
