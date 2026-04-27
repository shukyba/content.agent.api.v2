using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContentAgent.Api.Models;

public class FileEdit
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>Full new file content. Use when replacing a whole file or when the file is small.</summary>
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    /// <summary>Optional. appendKey | appendToArray | appendCsvRow (CSV-only, same as appendToArray for one line).</summary>
    [JsonPropertyName("editType")]
    public string? EditType { get; set; }

    /// <summary>For appendKey/appendToArray/appendCsvRow: record key or id for logging (e.g. slug).</summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    /// <summary>For appendKey: text before closing };. For appendToArray: one array element before ];. For CSV: one full CSV data line.</summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    /// <summary>
    /// For <c>appendToArray</c> on configured structured array paths: JSON object for one array item.
    /// The pipeline emits a safe TypeScript object literal from this payload.
    /// </summary>
    [JsonPropertyName("item")]
    public JsonElement? Item { get; set; }

    /// <summary>
    /// For <c>appendKey</c> on paths listed in agent <c>structuredAppendKeyPaths</c>: JSON array of objects (plain string fields in JSON).
    /// The pipeline emits valid TS literals—preferred over hand-written <see cref="Value"/> for those paths.
    /// </summary>
    [JsonPropertyName("items")]
    public JsonElement? Items { get; set; }
}
