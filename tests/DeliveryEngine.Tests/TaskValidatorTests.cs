using DeliveryEngine.Tasks;

namespace DeliveryEngine.Tests;

public class TaskValidatorTests
{
    internal static DeliveryTask ValidTask() => new()
    {
        Id = "task-1",
        Branch = "fix/task-1",
        CommitMessage = "fix: something",
        Pr = new PrOptions { Title = "Fix something" },
        Changes = [new Change { Op = ChangeOperation.Write, File = "src/a.cs", Content = "x" }],
    };

    [Fact]
    public void ValidTask_HasNoErrors()
    {
        Assert.Empty(TaskValidator.Validate(ValidTask()));
    }

    [Fact]
    public void EmptyTask_ReportsAllRequiredFields()
    {
        var errors = TaskValidator.Validate(new DeliveryTask());

        Assert.Contains(errors, e => e.StartsWith("id:"));
        Assert.Contains(errors, e => e.StartsWith("branch:"));
        Assert.Contains(errors, e => e.StartsWith("commitMessage:"));
        Assert.Contains(errors, e => e.StartsWith("pr:"));
        Assert.Contains(errors, e => e.StartsWith("changes:"));
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("кириллица")]
    [InlineData("a/b")]
    public void Id_WithForbiddenChars_IsRejected(string id)
    {
        var errors = TaskValidator.Validate(ValidTask() with { Id = id });

        Assert.Contains(errors, e => e.StartsWith("id:"));
    }

    [Fact]
    public void PrWithoutTitle_IsRejected()
    {
        var errors = TaskValidator.Validate(ValidTask() with { Pr = new PrOptions() });

        Assert.Contains(errors, e => e.StartsWith("pr.title:"));
    }

    [Theory]
    [InlineData("feature/ok")]
    [InlineData("fix-123")]
    [InlineData("release/1.0.0")]
    public void Branch_ValidNames_AreAccepted(string branch)
    {
        Assert.Empty(TaskValidator.Validate(ValidTask() with { Branch = branch }));
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("a..b")]
    [InlineData("-starts-with-dash")]
    [InlineData("ends/")]
    [InlineData("ends.lock")]
    [InlineData("a:b")]
    [InlineData("a~1")]
    [InlineData("a//b")]
    [InlineData("a@{b")]
    public void Branch_InvalidNames_AreRejected(string branch)
    {
        var errors = TaskValidator.Validate(ValidTask() with { Branch = branch });

        Assert.Contains(errors, e => e.StartsWith("branch:"));
    }

    [Fact]
    public void Branch_SameAsBase_IsRejected()
    {
        var errors = TaskValidator.Validate(ValidTask() with { Branch = "main" });

        Assert.Contains(errors, e => e.Contains("не может совпадать"));
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("C:/Windows/x")]
    [InlineData(@"C:\Windows\x")]
    [InlineData("../outside.txt")]
    [InlineData("src/../../outside.txt")]
    [InlineData(@"src\..\..\outside.txt")]
    [InlineData("./a.txt")]
    [InlineData(".git/config")]
    [InlineData("sub/.GIT/hooks/pre-commit")]
    [InlineData("dir/")]
    public void File_UnsafePaths_AreRejected(string file)
    {
        var task = ValidTask() with
        {
            Changes = [new Change { Op = ChangeOperation.Write, File = file, Content = "x" }],
        };

        var errors = TaskValidator.Validate(task);

        Assert.Contains(errors, e => e.StartsWith("changes[0].file:"));
    }

    [Theory]
    [InlineData("a.txt")]
    [InlineData("src/Program.cs")]
    [InlineData(@"src\Program.cs")]
    [InlineData(".github/workflows/ci.yml")]
    [InlineData(".gitignore")]
    public void File_SafePaths_AreAccepted(string file)
    {
        var task = ValidTask() with
        {
            Changes = [new Change { Op = ChangeOperation.Write, File = file, Content = "x" }],
        };

        Assert.Empty(TaskValidator.Validate(task));
    }

    [Fact]
    public void SameFileTwice_IsRejected_EvenWithDifferentSlashes()
    {
        var task = ValidTask() with
        {
            Changes =
            [
                new Change { Op = ChangeOperation.Write, File = "src/a.cs", Content = "x" },
                new Change { Op = ChangeOperation.Delete, File = @"src\a.cs" },
            ],
        };

        var errors = TaskValidator.Validate(task);

        Assert.Contains(errors, e => e.StartsWith("changes[1].file:") && e.Contains("уже изменяется"));
    }

    [Fact]
    public void Write_WithEmptyContent_IsAllowed()
    {
        var task = ValidTask() with
        {
            Changes = [new Change { Op = ChangeOperation.Write, File = "empty.txt", Content = "" }],
        };

        Assert.Empty(TaskValidator.Validate(task));
    }

    [Fact]
    public void Write_WithoutContent_IsRejected()
    {
        var task = ValidTask() with { Changes = [new Change { Op = ChangeOperation.Write, File = "a.txt" }] };

        Assert.Contains(TaskValidator.Validate(task), e => e.StartsWith("changes[0].content:"));
    }

    [Fact]
    public void Delete_WithContent_IsRejected()
    {
        var task = ValidTask() with
        {
            Changes = [new Change { Op = ChangeOperation.Delete, File = "a.txt", Content = "x" }],
        };

        Assert.Contains(TaskValidator.Validate(task), e => e.StartsWith("changes[0]:"));
    }

    [Fact]
    public void Patch_WithoutDiff_IsRejected()
    {
        var task = ValidTask() with { Changes = [new Change { Op = ChangeOperation.Patch }] };

        Assert.Contains(TaskValidator.Validate(task), e => e.StartsWith("changes[0].diff:"));
    }

    [Fact]
    public void Patch_WithFile_IsRejected()
    {
        var task = ValidTask() with
        {
            Changes = [new Change { Op = ChangeOperation.Patch, File = "a.txt", Diff = "--- a\n+++ b\n" }],
        };

        Assert.Contains(TaskValidator.Validate(task), e => e.StartsWith("changes[0]:"));
    }

    [Fact]
    public void Change_WithoutOp_IsRejected()
    {
        var task = ValidTask() with { Changes = [new Change { File = "a.txt" }] };

        Assert.Contains(TaskValidator.Validate(task), e => e.StartsWith("changes[0].op:"));
    }
}