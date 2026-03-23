using System.Text.Json;

namespace ContentAgent.Api.Models;

/// <summary>Parses string arrays from config JSON with explicit property lookup (avoids STJ quirks on some keys).</summary>
public static class ConfigJsonHelpers
{
    /// <summary>
    /// Tries each property name in order; returns the first that exists as a JSON array of non-empty strings.
    /// Skips keys that are null or wrong type and continues to the next candidate name.
    /// </summary>
    public static List<string>? TryGetStringList(string json, params string[] propertyNames)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var name in propertyNames)
            {
                foreach (var prop in root.EnumerateObject())
                {
                    if (!string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (prop.Value.ValueKind != JsonValueKind.Array)
                        break; // this key isn't an array — try next property *name*

                    var list = new List<string>();
                    foreach (var item in prop.Value.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.String)
                            continue;
                        var s = item.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                            list.Add(s.Trim());
                    }

                    if (list.Count > 0)
                        return list;

                    break; // empty array — try next property *name*
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }
}
