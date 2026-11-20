using DeliveryEngine.Planning;
using DeliveryEngine.Tasks;
using DeliveryEngine.Tests.Support;

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

    private static Task<(int Code, string Out, string Err)> Run(params string[] args) => RunWith(null, args);

    private static async Task<(int Code, string Out, string Err)> RunWith(FakeGitHubClient? github, params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = await Program.RunAsync(args, stdout, stderr, github is null ? null : _ => github);
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
        { "op": "delete", "file": "README.md" }
      ]
    }
    """;

    [Fact]
    public async Task DryRun_ValidTask_PrintsPlan()
    {
        var (code, output, err) = await Run("deliver", WriteTask(ValidJson), "--dry-run");

        Assert.Equal(Program.ExitOk, code);
        Assert.Empty(err);
        Assert.Contains("Создать ветку 'fix/t1' от 'main'", output);
        Assert.Contains("Записать файл 'a.txt' (5 Б)", output);
        Assert.Contains("Удалить файл 'README.md'", output);
        Assert.Contains("Закоммитить: \"fix: t1\"", output);
        Assert.Contains("Добавить метки: bot", output);
        Assert.Contains("auto-merge", output);
    }

    [Fact]
    public async Task DryRun_OptionBeforeFile_Works()
    {
        var (code, _, _) = await Run("deliver", "--dry-run", WriteTask(ValidJson));

        Assert.Equal(Program.ExitOk, code);
    }

    [Fact]
    public async Task DryRun_DoesNotTouchRepository()
    {
        using var repo = await TempGitRepo.CreateAsync();

        var (code, _, _) = await Run("deliver", WriteTask(ValidJson), "--repo", repo.Path, "--dry-run");

        Assert.Equal(Program.ExitOk, code);
        Assert.False((await repo.Git.RunAsync("rev-parse", "--verify", "--quiet", "refs/heads/fix/t1")).Success);
    }

    [Fact]
    public async Task Deliver_ValidTask_CreatesLocalBranch()
    {
        using var repo = await TempGitRepo.CreateAsync();

        var (code, output, err) = await Run("deliver", WriteTask(ValidJson), "--repo", repo.Path);

        Assert.Equal(Program.ExitOk, code);
        Assert.Empty(err);
        Assert.Contains("[Committed]", output);
        Assert.Contains("fix/t1", output);
        Assert.True((await repo.Git.RunAsync("rev-parse", "--verify", "--quiet", "refs/heads/fix/t1")).Success);
    }

    [Fact]
    public async Task Deliver_Failure_ReturnsDeliveryFailedCode()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.Git.RunCheckedAsync("branch", "fix/t1");

        var (code, output, err) = await Run("deliver", WriteTask(ValidJson), "--repo", repo.Path);

        Assert.Equal(Program.ExitDeliveryFailed, code);
        Assert.Empty(output);
        Assert.Contains("[BranchExists]", err);
    }
    
    [Fact]
    public async Task Deliver_WithPush_PushesToOrigin()
    {
        using var repo = await TempGitRepo.CreateAsync();
        var origin = await repo.AddOriginAsync();

        var (code, output, err) = await Run("deliver", WriteTask(ValidJson), "--repo", repo.Path, "--push");

        Assert.Equal(Program.ExitOk, code);
        Assert.Empty(err);
        Assert.Contains("[Pushed]", output);
        Assert.True((await origin.RunAsync("rev-parse", "--verify", "--quiet", "refs/heads/fix/t1")).Success);
    }

    [Fact]
    public async Task Deliver_WithoutPush_DoesNotTouchOrigin()
    {
        using var repo = await TempGitRepo.CreateAsync();
        var origin = await repo.AddOriginAsync();

        var (code, output, _) = await Run("deliver", WriteTask(ValidJson), "--repo", repo.Path);

        Assert.Equal(Program.ExitOk, code);
        Assert.Contains("--push", output);
        Assert.False((await origin.RunAsync("rev-parse", "--verify", "--quiet", "refs/heads/fix/t1")).Success);
    }
    
    [Fact]
    public async Task Deliver_WithPr_CreatesPullRequestAndPrintsUrl()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.AddOriginAsync();
        var github = new FakeGitHubClient();

        var (code, output, err) = await RunWith(github, "deliver", WriteTask(ValidJson), "--repo", repo.Path, "--pr");

        Assert.Equal(Program.ExitOk, code);
        Assert.Empty(err);
        Assert.Contains("[PullRequestCreated]", output);
        Assert.Contains("https://github.com/owner/repo/pull/42", output);
        Assert.Equal(["create", "labels #42: bot", "auto-merge #42"], github.Calls);
    }

    [Fact]
    public async Task Deliver_WithPr_AutoMergeFailure_ReturnsDeliveryFailedCode()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.AddOriginAsync();
        var github = new FakeGitHubClient { FailOn = "auto-merge" };

        var (code, _, err) = await RunWith(github, "deliver", WriteTask(ValidJson), "--repo", repo.Path, "--pr");

        Assert.Equal(Program.ExitDeliveryFailed, code);
        Assert.Contains("[AutoMergeFailed]", err);
        Assert.Contains("pull/42", err);
    }

    [Fact]
    public async Task InvalidTask_ReturnsErrors()
    {
        var (code, _, err) = await Run("deliver", WriteTask("""{ "id": "t1" }"""), "--dry-run");

        Assert.Equal(Program.ExitInvalidTask, code);
        Assert.Contains("branch:", err);
    }

    [Fact]
    public async Task BrokenJson_ReturnsInvalidTask()
    {
        var (code, _, err) = await Run("deliver", WriteTask("{"), "--dry-run");

        Assert.Equal(Program.ExitInvalidTask, code);
        Assert.Contains("Некорректный JSON", err);
    }

    [Theory]
    [InlineData]
    [InlineData("unknown")]
    [InlineData("deliver")]
    [InlineData("deliver", "a.json", "b.json")]
    [InlineData("deliver", "a.json", "--force")]
    [InlineData("deliver", "a.json", "--repo")]
    public async Task BadUsage_ReturnsUsageCode(params string[] args)
    {
        Assert.Equal(Program.ExitUsage, (await Run(args)).Code);
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
        return dir?.FullName ?? throw new InvalidOperationException("Не найден корень репозитория.");
    }
}