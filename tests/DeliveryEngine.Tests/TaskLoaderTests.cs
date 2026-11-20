using DeliveryEngine.Tasks;

namespace DeliveryEngine.Tests;

public class TaskLoaderTests
{
    [Fact]
    public void Parse_FullTask_ReadsAllFields()
    {
        var task = TaskLoader.Parse("""
        {
          "id": "t1",
          "base": "develop",
          "branch": "fix/a",
          "commitMessage": "fix: a",
          "pr": { "title": "Fix A", "body": "b", "labels": ["bot"], "autoMerge": true },
          "changes": [
            { "op": "write", "file": "a.txt", "content": "x" },
            { "op": "delete", "file": "b.txt" },
            { "op": "patch", "diff": "--- a/c\n+++ b/c\n" }
          ]
        }
        """);

        Assert.Equal("t1", task.Id);
        Assert.Equal("develop", task.Base);
        Assert.Equal("fix/a", task.Branch);
        Assert.True(task.Pr!.AutoMerge);
        Assert.Equal(["bot"], task.Pr.Labels!);
        Assert.Equal(
            [ChangeOperation.Write, ChangeOperation.Delete, ChangeOperation.Patch],
            task.Changes!.Select(c => c.Op!.Value));
    }

    [Fact]
    public void Parse_BaseOmitted_DefaultsToMain()
    {
        var task = TaskLoader.Parse("""{ "id": "t1" }""");

        Assert.Equal("main", task.Base);
    }

    [Fact]
    public void Parse_AllowsCommentsAndTrailingCommas()
    {
        var task = TaskLoader.Parse("""
        {
          // комментарий
          "id": "t1",
        }
        """);

        Assert.Equal("t1", task.Id);
    }

    [Fact]
    public void Parse_UnknownField_Throws()
    {
        var ex = Assert.Throws<TaskLoadException>(() => TaskLoader.Parse("""{ "id": "t1", "brnach": "x" }"""));

        Assert.Contains("brnach", ex.Message);
    }

    [Fact]
    public void Parse_UnknownOperation_Throws()
    {
        var ex = Assert.Throws<TaskLoadException>(() =>
            TaskLoader.Parse("""{ "changes": [ { "op": "replace", "file": "a" } ] }"""));

        Assert.Contains("changes", ex.Message);
    }

    [Fact]
    public void Parse_NumericOperation_Throws()
    {
        Assert.Throws<TaskLoadException>(() => TaskLoader.Parse("""{ "changes": [ { "op": 0 } ] }"""));
    }

    [Fact]
    public void Parse_InvalidJson_Throws()
    {
        Assert.Throws<TaskLoadException>(() => TaskLoader.Parse("{ not json"));
    }

    [Fact]
    public void Load_MissingFile_Throws()
    {
        var ex = Assert.Throws<TaskLoadException>(() => TaskLoader.Load("no-such-file.json"));

        Assert.Contains("не найден", ex.Message);
    }
}