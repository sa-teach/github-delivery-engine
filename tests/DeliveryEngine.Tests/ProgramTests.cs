using DeliveryEngine.Planning;
using DeliveryEngine.Tasks;

namespace DeliveryEngine.Tests;

public sealed class ProgramTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("delivery-engine-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string WriteTask(string json)
    {
        var path = Path.Combine(_dir, "task.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = Program.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private const string ValidJson = """
    {
      "id": "t1",
      "branch": "fix/t1",
      "commitMessage": "fix: t1\n\nDetails",
      "pr": { "title": "Fix T1", "labels": ["bot"], "autoMerge": true },
      "changes": [
        { "op": "write", "file": "a.txt", "content": "hello" },
        { "op": "delete", "file": "b.txt" }
      ]
    }
    """;

    [Fact]
    public void DryRun_ValidTask_PrintsPlan()
    {
        var (code, output, err) = Run("deliver", WriteTask(ValidJson), "--dry-run");

        Assert.Equal(Program.ExitOk, code);
        Assert.Empty(err);
        Assert.Contains("Создать ветку 'fix/t1' от 'main'", output);
        Assert.Contains("Записать файл 'a.txt' (5 Б)", output);
        Assert.Contains("Удалить файл 'b.txt'", output);
        Assert.Contains("Закоммитить: \"fix: t1\"", output);
        Assert.Contains("Добавить метки: bot", output);
        Assert.Contains("auto-merge", output);
    }

    [Fact]
    public void DryRun_OptionBeforeFile_Works()
    {
        var (code, _, _) = Run("deliver", "--dry-run", WriteTask(ValidJson));

        Assert.Equal(Program.ExitOk, code);
    }

    [Fact]
    public void InvalidTask_ReturnsErrors()
    {
        var (code, _, err) = Run("deliver", WriteTask("""{ "id": "t1" }"""), "--dry-run");

        Assert.Equal(Program.ExitInvalidTask, code);
        Assert.Contains("branch:", err);
    }

    [Fact]
    public void BrokenJson_ReturnsInvalidTask()
    {
        var (code, _, err) = Run("deliver", WriteTask("{"), "--dry-run");

        Assert.Equal(Program.ExitInvalidTask, code);
        Assert.Contains("Некорректный JSON", err);
    }

    [Fact]
    public void WithoutDryRun_RealDeliveryIsNotAvailableYet()
    {
        var (code, output, err) = Run("deliver", WriteTask(ValidJson));

        Assert.Equal(Program.ExitUsage, code);
        Assert.Empty(output);
        Assert.Contains("--dry-run", err);
    }

    [Theory]
    [InlineData]
    [InlineData("unknown")]
    [InlineData("deliver")]
    [InlineData("deliver", "a.json", "b.json")]
    [InlineData("deliver", "a.json", "--force")]
    public void BadUsage_ReturnsUsageCode(params string[] args)
    {
        Assert.Equal(Program.ExitUsage, Run(args).Code);
    }

    [Fact]
    public void ExampleTask_IsValid()
    {
        var path = Path.Combine(FindRepoRoot(), "examples", "task.example.json");
        var task = TaskLoader.Load(path);

        Assert.Empty(TaskValidator.Validate(task));
        Assert.NotEmpty(DeliveryPlanner.BuildPlan(task));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GitHubDeliveryEngine.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Не найден корень репозитория");
    }
}