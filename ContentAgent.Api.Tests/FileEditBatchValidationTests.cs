using ContentAgent.Api.Models;
using ContentAgent.Api.Services;

namespace ContentAgent.Api.Tests;

public class FileEditBatchValidationTests
{
    [Fact]
    public void Null_and_empty_lists_are_valid()
    {
        Assert.True(FileEditBatchValidation.LooksLikeFileEditBatch(null));
        Assert.True(FileEditBatchValidation.LooksLikeFileEditBatch(new List<FileEdit>()));
    }

    [Fact]
    public void All_entries_have_path_is_valid()
    {
        var list = new List<FileEdit>
        {
            new() { Path = "src/a.ts", EditType = "appendToArray" },
            new() { Path = "src/b.csv", EditType = "appendCsvRow" },
        };
        Assert.True(FileEditBatchValidation.LooksLikeFileEditBatch(list));
    }

    [Fact]
    public void Missing_path_rejects_FAQ_like_batch()
    {
        var list = new List<FileEdit>
        {
            new() { Path = "", EditType = null, Key = null },
            new() { Path = "   ", EditType = null, Key = null },
        };
        Assert.False(FileEditBatchValidation.LooksLikeFileEditBatch(list));
    }

    [Fact]
    public void Mixed_path_and_empty_is_invalid()
    {
        var list = new List<FileEdit>
        {
            new() { Path = "src/data/festivalData.ts", EditType = "appendKey" },
            new() { Path = "", EditType = null },
        };
        Assert.False(FileEditBatchValidation.LooksLikeFileEditBatch(list));
    }
}
