using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ContentAgent.Api.Services;

/// <summary>
/// For configured repo paths, builds <c>appendKey</c> snippets as TypeScript object literals with
/// JSON-style string escaping so model-authored prose cannot break the build. Domain-agnostic.
/// </summary>
public static class StructuredAppendKeyHelper
{
    private static readonly JsonSerializerOptions TsLiteralOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>True if <paramref name="pathNorm"/> (forward slashes) matches an entry in <paramref name="configured"/> (exact match after normalization).</summary>
    public static bool MatchesStructuredAppendPath(string pathNorm, IReadOnlyList<string>? configured)
    {
        if (configured == null || configured.Count == 0)
            return false;
        foreach (var p in configured)
        {
            if (string.IsNullOrWhiteSpace(p))
                continue;
            var c = NormalizePath(p);
            if (string.Equals(pathNorm, c, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static string NormalizePath(string path) =>
        path.Trim().Replace('\\', '/');

    /// <summary>Formats <c>  'key': [ { prop: "...", ... }, ... ]</c> with safely escaped string values.</summary>
    public static string FormatAppendBlock(string recordKey, JsonElement itemsArray)
    {
        if (itemsArray.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("items must be a JSON array.", nameof(itemsArray));

        var sb = new StringBuilder();
        sb.Append("  ").Append(SafeTsRecordKey(recordKey)).Append(": [\n");
        var first = true;
        foreach (var el in itemsArray.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Each item in items must be a JSON object.", nameof(itemsArray));

            if (!first)
                sb.Append(",\n");
            first = false;
            sb.Append("    { ");
            var firstProp = true;
            foreach (var prop in el.EnumerateObject())
            {
                if (!firstProp)
                    sb.Append(", ");
                firstProp = false;
                sb.Append(prop.Name).Append(": ").Append(FormatTsValue(prop.Value));
            }

            sb.Append(" }");
        }

        sb.Append("\n  ]");
        return sb.ToString();
    }

    /// <summary>TS record key: single-quoted id, or JSON string if it contains a single quote.</summary>
    public static string SafeTsRecordKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            return "''";
        if (key.Contains('\'', StringComparison.Ordinal))
            return JsonSerializer.Serialize(key, TsLiteralOptions);
        return $"'{key}'";
    }

    private static string FormatTsValue(JsonElement v) =>
        v.ValueKind switch
        {
            JsonValueKind.String => JsonSerializer.Serialize(v.GetString() ?? string.Empty, TsLiteralOptions),
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            JsonValueKind.Object or JsonValueKind.Array => v.GetRawText(),
            _ => JsonSerializer.Serialize(v.GetRawText(), TsLiteralOptions)
        };

    /// <summary>
    /// Parses a legacy append snippet (<c>'slug': [ ... ]</c> or array-only with <paramref name="preferredKey"/>)
    /// and re-emits with safe literals. Fails if TS is invalid (e.g. unescaped quotes in strings).
    /// </summary>
    public static bool TryRewriteLegacyValue(string value, string? preferredKey, out string rewritten)
    {
        rewritten = string.Empty;
        var s = value.AsSpan().Trim();
        if (s.IsEmpty)
            return false;

        var i = 0;
        SkipWs(s, ref i);
        if (i >= s.Length)
            return false;

        string key;
        if (i < s.Length && s[i] == '[')
        {
            if (string.IsNullOrWhiteSpace(preferredKey))
                return false;
            key = preferredKey.Trim();
            i++;
        }
        else
        {
            if (!TryParseRecordKey(s, ref i, out var parsedKey))
                return false;
            key = string.IsNullOrWhiteSpace(preferredKey) ? parsedKey : preferredKey.Trim();
            if (string.IsNullOrEmpty(key))
                return false;

            SkipWs(s, ref i);
            if (i >= s.Length || s[i] != ':')
                return false;
            i++;
            SkipWs(s, ref i);
            if (i >= s.Length || s[i] != '[')
                return false;
            i++;
        }

        if (!TryParseArrayOfTsObjects(s, ref i, out var nodes))
            return false;

        if (nodes.Count == 0)
            return false;

        var arr = new JsonArray();
        foreach (var n in nodes)
            arr.Add(n);

        var json = arr.ToJsonString();
        var root = JsonSerializer.Deserialize<JsonElement>(json);
        if (root.ValueKind != JsonValueKind.Array)
            return false;
        rewritten = FormatAppendBlock(key, root);
        return true;
    }

    private static bool TryParseArrayOfTsObjects(ReadOnlySpan<char> s, ref int i, out List<JsonObject> objects)
    {
        objects = new List<JsonObject>();
        while (true)
        {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']')
            {
                i++;
                SkipWs(s, ref i);
                break;
            }

            if (i >= s.Length || s[i] != '{')
                return false;
            i++;

            if (!TryParseTsObjectBody(s, ref i, out var obj))
                return false;
            objects.Add(obj);

            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ',')
                i++;
        }

        return true;
    }

    private static bool TryParseTsObjectBody(ReadOnlySpan<char> s, ref int i, out JsonObject obj)
    {
        obj = new JsonObject();
        SkipWs(s, ref i);
        while (true)
        {
            if (i < s.Length && s[i] == '}')
            {
                i++;
                return true;
            }

            if (!TryParseIdentifier(s, ref i, out var propName))
                return false;

            SkipWs(s, ref i);
            if (i >= s.Length || s[i] != ':')
                return false;
            i++;
            SkipWs(s, ref i);

            if (!TryParseTsLiteral(s, ref i, out var node) || node is null)
                return false;

            obj[propName] = node;

            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ',')
            {
                i++;
                SkipWs(s, ref i);
                continue;
            }

            if (i < s.Length && s[i] == '}')
            {
                i++;
                return true;
            }

            return false;
        }
    }

    private static bool TryParseIdentifier(ReadOnlySpan<char> s, ref int i, out string name)
    {
        name = string.Empty;
        if (i >= s.Length || (!char.IsLetter(s[i]) && s[i] != '_' && s[i] != '$'))
            return false;
        var start = i;
        i++;
        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_'))
            i++;
        name = s[start..i].ToString();
        return name.Length > 0;
    }

    private static bool TryParseTsLiteral(ReadOnlySpan<char> s, ref int i, out JsonNode? node)
    {
        node = null;
        if (i >= s.Length)
            return false;

        if (s[i] == '\'' || s[i] == '"')
        {
            if (!TryReadJsStringLiteral(s, ref i, out var decoded))
                return false;
            node = decoded;
            return true;
        }

        if (i + 4 <= s.Length && s.Slice(i, 4).Equals("true", StringComparison.Ordinal))
        {
            i += 4;
            node = true;
            return true;
        }

        if (i + 5 <= s.Length && s.Slice(i, 5).Equals("false", StringComparison.Ordinal))
        {
            i += 5;
            node = false;
            return true;
        }

        if (i + 4 <= s.Length && s.Slice(i, 4).Equals("null", StringComparison.Ordinal))
        {
            i += 4;
            node = JsonNode.Parse("null");
            return true;
        }

        if (char.IsDigit(s[i]) || (s[i] == '-' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
        {
            if (!TryReadJsNumber(s, ref i, out var numText))
                return false;
            try
            {
                node = JsonNode.Parse(numText);
                return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    private static bool TryReadJsNumber(ReadOnlySpan<char> s, ref int i, out string numText)
    {
        numText = string.Empty;
        var start = i;
        if (i < s.Length && s[i] == '-')
            i++;
        while (i < s.Length && char.IsDigit(s[i]))
            i++;
        if (i < s.Length && s[i] == '.')
        {
            i++;
            while (i < s.Length && char.IsDigit(s[i]))
                i++;
        }

        if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
        {
            i++;
            if (i < s.Length && (s[i] == '+' || s[i] == '-'))
                i++;
            while (i < s.Length && char.IsDigit(s[i]))
                i++;
        }

        if (i == start || (i == start + 1 && s[start] == '-'))
            return false;
        numText = s[start..i].ToString();
        return double.TryParse(numText, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }

    private static void SkipWs(ReadOnlySpan<char> s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i]))
            i++;
    }

    private static bool TryParseRecordKey(ReadOnlySpan<char> s, ref int i, out string key)
    {
        key = string.Empty;
        if (i >= s.Length)
            return false;

        if (s[i] == '\'' || s[i] == '"')
            return TryReadJsStringLiteral(s, ref i, out key);

        var start = i;
        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_' || s[i] == '-'))
            i++;
        if (i == start)
            return false;
        key = s[start..i].ToString();
        return true;
    }

    private static bool TryReadJsStringLiteral(ReadOnlySpan<char> s, ref int i, out string decoded)
    {
        decoded = string.Empty;
        if (i >= s.Length)
            return false;

        var q = s[i];
        if (q != '\'' && q != '"')
            return false;

        i++;
        var sb = new StringBuilder();
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '\\' && i + 1 < s.Length)
            {
                var n = s[i + 1];
                i += 2;
                switch (n)
                {
                    case 'n':
                        sb.Append('\n');
                        break;
                    case 'r':
                        sb.Append('\r');
                        break;
                    case 't':
                        sb.Append('\t');
                        break;
                    case '\\':
                        sb.Append('\\');
                        break;
                    case '\'':
                        sb.Append('\'');
                        break;
                    case '"':
                        sb.Append('"');
                        break;
                    case 'u':
                        if (i + 4 <= s.Length && TryReadHex4(s, i, out var cp))
                        {
                            sb.Append((char)cp);
                            i += 4;
                        }
                        else
                            sb.Append('u');
                        break;
                    default:
                        sb.Append(n);
                        break;
                }

                continue;
            }

            if (c == q)
            {
                i++;
                decoded = sb.ToString();
                return true;
            }

            sb.Append(c);
            i++;
        }

        return false;
    }

    private static bool TryReadHex4(ReadOnlySpan<char> s, int start, out int codePoint)
    {
        codePoint = 0;
        if (start + 4 > s.Length)
            return false;
        var v = 0;
        for (var k = 0; k < 4; k++)
        {
            var d = HexDigitValue(s[start + k]);
            if (d < 0)
                return false;
            v = (v << 4) + d;
        }

        codePoint = v;
        return true;
    }

    private static int HexDigitValue(char c)
    {
        if (c is >= '0' and <= '9')
            return c - '0';
        if (c is >= 'a' and <= 'f')
            return 10 + (c - 'a');
        if (c is >= 'A' and <= 'F')
            return 10 + (c - 'A');
        return -1;
    }
}
