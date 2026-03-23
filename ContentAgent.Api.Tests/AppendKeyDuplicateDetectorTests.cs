using ContentAgent.Api.Services;

namespace ContentAgent.Api.Tests;

public class AppendKeyDuplicateDetectorTests
{
    [Fact]
    public void PropertyKeyLikelyExists_single_quoted_slug_returns_true()
    {
        const string before = """
            export const x = {
              'atlanta-salsa-bachata-festival-2026': [],
            """;
        Assert.True(AppendKeyDuplicateDetector.PropertyKeyLikelyExists(before, "atlanta-salsa-bachata-festival-2026"));
    }

    [Fact]
    public void PropertyKeyLikelyExists_double_quoted_slug_returns_true()
    {
        const string before = """
            const data = {
              "my-record-key": { a: 1 },
            """;
        Assert.True(AppendKeyDuplicateDetector.PropertyKeyLikelyExists(before, "my-record-key"));
    }

    [Fact]
    public void PropertyKeyLikelyExists_identifier_key_returns_true()
    {
        const string before = """
            export const m = {
              recordId: [],
            """;
        Assert.True(AppendKeyDuplicateDetector.PropertyKeyLikelyExists(before, "recordId"));
    }

    [Fact]
    public void PropertyKeyLikelyExists_key_absent_returns_false()
    {
        const string before = """
            export const x = {
              'other-fest': [],
            """;
        Assert.False(AppendKeyDuplicateDetector.PropertyKeyLikelyExists(before, "atlanta-salsa-bachata-festival-2026"));
    }

    [Fact]
    public void PropertyKeyLikelyExists_whitespace_key_returns_false()
    {
        Assert.False(AppendKeyDuplicateDetector.PropertyKeyLikelyExists("{ foo: 1 }", "   "));
    }

    [Fact]
    public void PropertyKeyLikelyExists_key_with_apostrophe_single_quoted_ts_form()
    {
        // TS source uses backslash-escaped quote inside the key literal.
        const string before = @"export const x = {
  'it\'s-fine': [],
";
        Assert.True(AppendKeyDuplicateDetector.PropertyKeyLikelyExists(before, "it's-fine"));
    }
}
