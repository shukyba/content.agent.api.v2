using System.Text;

namespace ContentAgent.Api.Services;

/// <summary>
/// Fixes common LLM output mistakes so <see cref="System.Text.Json"/> can parse file-edit JSON arrays.
/// JSON does not allow <c>\'</c> inside double-quoted strings; models often emit it for English contractions.
/// </summary>
public static class GeminiJsonRepair
{
    /// <summary>
    /// Replaces invalid <c>\'</c> with a plain apostrophe when the backslash begins an odd-length run of
    /// consecutive backslashes (so <c>\\'</c> remains a backslash + apostrophe).
    /// </summary>
    public static string RepairInvalidEscapeApostrophes(string json)
    {
        if (string.IsNullOrEmpty(json))
            return json;

        var sb = new StringBuilder(json.Length);
        var inString = false;
        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];
            if (!inString)
            {
                sb.Append(c);
                if (c == '"')
                    inString = true;
                continue;
            }

            if (c == '\\' && i + 1 < json.Length && json[i + 1] == '\'')
            {
                var runStart = i;
                while (runStart > 0 && json[runStart - 1] == '\\')
                    runStart--;
                var runLen = i - runStart + 1;
                if (runLen % 2 == 1)
                {
                    sb.Append('\'');
                    i++;
                    continue;
                }
            }

            if (c == '"')
            {
                var bs = 0;
                for (var j = i - 1; j >= 0 && json[j] == '\\'; j--)
                    bs++;
                if (bs % 2 == 0)
                    inString = false;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }
}
