using System.Text.Json;
using ContentAgent.Api.Models;
using ContentAgent.Api.Services;

namespace ContentAgent.Api.Tests;

public class GeminiJsonRepairTests
{
    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    [Fact]
    public void Repair_invalid_backslash_apostrophe_then_deserialize_file_edits()
    {
        // JSON disallows \' inside double-quoted strings; LLMs often emit it for "Don't".
        var broken = "[{\"path\":\"p\",\"editType\":\"appendCsvRow\",\"key\":\"k\",\"value\":\"Don\\'t\"}]";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<List<FileEdit>>(broken, JsonReadOptions));

        var repaired = GeminiJsonRepair.RepairInvalidEscapeApostrophes(broken);
        var list = JsonSerializer.Deserialize<List<FileEdit>>(repaired, JsonReadOptions);
        Assert.NotNull(list);
        Assert.Single(list);
        Assert.Equal("Don't", list![0].Value);
    }

    [Fact]
    public void Repair_does_not_strip_escaped_backslash_before_apostrophe()
    {
        // Valid JSON: backslash + apostrophe in the string value (one literal backslash, then apostrophe).
        var json = "[{\"path\":\"p\",\"editType\":\"appendCsvRow\",\"key\":\"k\",\"value\":\"x\\\\'y\"}]";
        var repaired = GeminiJsonRepair.RepairInvalidEscapeApostrophes(json);
        var list = JsonSerializer.Deserialize<List<FileEdit>>(repaired, JsonReadOptions);
        Assert.NotNull(list);
        Assert.Equal(@"x\'y", list![0].Value);
    }
}
