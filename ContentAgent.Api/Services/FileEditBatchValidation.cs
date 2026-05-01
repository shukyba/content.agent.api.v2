using ContentAgent.Api.Models;

namespace ContentAgent.Api.Services;

/// <summary>
/// Ensures a deserialized JSON array is a batch of file edits, not a nested array (e.g. FAQ <c>items</c>)
/// mistakenly parsed as <see cref="FileEdit"/> rows (those have no <c>path</c>).
/// </summary>
public static class FileEditBatchValidation
{
    public static bool LooksLikeFileEditBatch(List<FileEdit>? list)
    {
        if (list == null || list.Count == 0)
            return true;
        foreach (var e in list)
        {
            if (string.IsNullOrWhiteSpace(e.Path))
                return false;
        }

        return true;
    }
}
