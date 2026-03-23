using System.Text.RegularExpressions;

namespace ContentAgent.Api.Services;

/// <summary>
/// Heuristic detection of an existing object property key in TS/JS source before an <c>appendKey</c> insertion point.
/// Does not parse the full AST; same text inside string literals may cause false positives.
/// </summary>
public static class AppendKeyDuplicateDetector
{
    private static readonly Regex IdentifierKeyPattern = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Returns whether <paramref name="textBeforeInsert"/> likely already contains a property named <paramref name="key"/>.
    /// Checks single-quoted, double-quoted, and (when <paramref name="key"/> is a valid JS identifier) unquoted <c>key:</c> forms.
    /// </summary>
    public static bool PropertyKeyLikelyExists(string textBeforeInsert, string key)
    {
        if (string.IsNullOrWhiteSpace(textBeforeInsert) || string.IsNullOrWhiteSpace(key))
            return false;

        key = key.Trim();

        // TS single-quoted key: escape \ then ' for the literal inside quotes
        var sq = key.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);
        if (textBeforeInsert.IndexOf($"'{sq}':", StringComparison.Ordinal) >= 0)
            return true;

        var dq = key.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        if (textBeforeInsert.IndexOf($"\"{dq}\":", StringComparison.Ordinal) >= 0)
            return true;

        if (IdentifierKeyPattern.IsMatch(key)
            && Regex.IsMatch(textBeforeInsert, $@"\b{Regex.Escape(key)}\b\s*:", RegexOptions.CultureInvariant))
            return true;

        return false;
    }
}
