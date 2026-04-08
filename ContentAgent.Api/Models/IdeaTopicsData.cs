using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContentAgent.Api.Models;

/// <summary>
/// Social Poster idea topics (all domains) from <c>Data/ideaTopics.json</c>: id, domainId, label.
/// Labels align with social-poster.site <c>topicSubjects</c>; regenerate via <c>scripts/export-idea-topics-for-api.ts</c>.
/// </summary>
public static class IdeaTopicsData
{
    private static readonly string RelativeJsonPath = Path.Combine("Data", "ideaTopics.json");

    private static readonly Lazy<IReadOnlyList<IdeaTopicItem>> LazyAll = new(LoadAll, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static IReadOnlyList<IdeaTopicItem> All => LazyAll.Value;

    private static IReadOnlyList<IdeaTopicItem> LoadAll()
    {
        var path = Path.Combine(AppContext.BaseDirectory, RelativeJsonPath);
        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"Idea topics file not found: {path}. Ensure Data/ideaTopics.json is copied to the output directory.");

        var json = File.ReadAllText(path);
        var file = JsonSerializer.Deserialize<IdeaTopicsFile>(json, JsonOptions)
            ?? throw new InvalidOperationException("ideaTopics.json deserialized to null.");

        if (file.Topics is not { Count: > 0 })
            throw new InvalidOperationException("ideaTopics.json must contain a non-empty \"topics\" array.");

        var list = new List<IdeaTopicItem>(file.Topics.Count);
        foreach (var row in file.Topics)
        {
            if (row.Id == Guid.Empty)
                continue;
            var domainId = row.DomainId?.Trim() ?? string.Empty;
            if (domainId.Length == 0)
                continue;
            var label = row.Label?.Trim() ?? string.Empty;
            if (label.Length == 0)
                continue;
            list.Add(new IdeaTopicItem(row.Id, domainId, label));
        }

        if (list.Count == 0)
            throw new InvalidOperationException("ideaTopics.json contained no valid topic rows.");

        return list;
    }

    public static bool TryGetLabel(Guid topicId, out string label)
    {
        foreach (var item in All)
        {
            if (item.Id == topicId)
            {
                label = item.Label;
                return true;
            }
        }

        label = string.Empty;
        return false;
    }

    private sealed class IdeaTopicsFile
    {
        [JsonPropertyName("topics")]
        public List<IdeaTopicRow>? Topics { get; set; }
    }

    private sealed class IdeaTopicRow
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        [JsonPropertyName("domainId")]
        public string? DomainId { get; set; }

        [JsonPropertyName("label")]
        public string? Label { get; set; }
    }
}

public sealed record IdeaTopicItem(Guid Id, string DomainId, string Label);
