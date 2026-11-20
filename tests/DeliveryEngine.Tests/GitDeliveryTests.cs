using DeliveryEngine.Delivery;
using DeliveryEngine.Tasks;
using DeliveryEngine.Tests.Support;

namespace DeliveryEngine.Tests;

public class GitDeliveryTests
{
    private static DeliveryTask MakeTask(params Change[] changes) => TaskValidatorTests.ValidTask() with
    {
        Branch = "fix/task-1",
        CommitMessage = "fix: something\n\nLonger description\non two lines.",
        Changes = changes,
    };

    private static Change Write(string file, string content) => new() { Op = ChangeOperation.Write, File = file, Content = content };

    private static Change Delete(string file) => new() { Op = ChangeOperation.Delete, File = file };

    private static async Task<bool> BranchExistsAsync(TempGitRepo repo, string branch) =>
        (await repo.Git.RunAsync("rev-parse", "--verify", "--quiet", $"refs/heads/{branch}")).Success;

    private static readonly DeliveryOptions ToOrigin = new(Remote: "origin");

    private static void WriteRejectingHook(string hooksDir, string name)
    {
        Directory.CreateDirectory(hooksDir);
        var hook = Path.Combine(hooksDir, name);
        File.WriteAllText(hook, "#!/bin/sh\necho 'hook says no' >&2\nexit 1\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public async Task ValidTask_CreatesBranchWithCommit()
    {
        using var repo = await TempGitRepo.CreateAsync();

        var result = await GitDelivery.RunAsync(repo.Path, MakeTask(Write("src/a.txt", "hello\n"), Delete("README.md")));

        Assert.Equal(DeliveryStatus.Committed, result.Status);
        Assert.Equal(await repo.Git.RunCheckedAsync("rev-parse", "fix/task-1"), result.CommitSha);

        var files = await repo.Git.RunCheckedAsync("ls-tree", "-r", "--name-only", "fix/task-1");
        Assert.Equal(["src/a.txt"], files.Split('\n'));
        Assert.Equal("hello\n", await repo.Git.RunCheckedAsync("show", "fix/task-1:src/a.txt") + "\n");
    }

    [Fact]
    public async Task Commit_HasFullMultilineMessageAndParentIsBase()
    {
        using var repo = await TempGitRepo.CreateAsync();
        var mainSha = await repo.Git.RunCheckedAsync("rev-parse", "main");

        await GitDelivery.RunAsync(repo.Path, MakeTask(Write("a.txt", "x")));

        Assert.Equal(
            "fix: something\n\nLonger description\non two lines.",
            await repo.Git.RunCheckedAsync("log", "-1", "--format=%B", "fix/task-1"));
        Assert.Equal(mainSha, await repo.Git.RunCheckedAsync("rev-parse", "fix/task-1~1"));
    }

    [Fact]
    public async Task MainWorkingCopy_StaysUntouched()
    {
        using var repo = await TempGitRepo.CreateAsync();
        repo.WriteFile("README.md", "local uncommitted edit\n");

        await GitDelivery.RunAsync(repo.Path, MakeTask(Write("a.txt", "x")));

        Assert.Equal("main", await repo.Git.RunCheckedAsync("rev-parse", "--abbrev-ref", "HEAD"));
        Assert.Equal("local uncommitted edit\n", repo.ReadFile("README.md"));
        Assert.False(File.Exists(repo.FullPath("a.txt")));
        Assert.Single((await repo.Git.RunCheckedAsync("worktree", "list")).Split('\n'));
    }

    [Fact]
    public async Task BranchAlreadyExists_IsReportedAndBranchIsKept()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.Git.RunCheckedAsync("branch", "fix/task-1");
        var before = await repo.Git.RunCheckedAsync("rev-parse", "fix/task-1");

        var result = await GitDelivery.RunAsync(repo.Path, MakeTask(Write("a.txt", "x")));

        Assert.Equal(DeliveryStatus.BranchExists, result.Status);
        Assert.Equal(before, await repo.Git.RunCheckedAsync("rev-parse", "fix/task-1"));
    }

    [Fact]
    public async Task SecondRun_OfSameTask_ReportsBranchExists()
    {
        using var repo = await TempGitRepo.CreateAsync();
        var task = MakeTask(Write("a.txt", "x"));

        var first = await GitDelivery.RunAsync(repo.Path, task);
        var second = await GitDelivery.RunAsync(repo.Path, task);

        Assert.Equal(DeliveryStatus.Committed, first.Status);
        Assert.Equal(DeliveryStatus.BranchExists, second.Status);
        Assert.Equal(first.CommitSha, await repo.Git.RunCheckedAsync("rev-parse", "fix/task-1"));
    }

    [Fact]
    public async Task BaseNotFound_IsReported()
    {
        using var repo = await TempGitRepo.CreateAsync();

        var result = await GitDelivery.RunAsync(repo.Path, MakeTask(Write("a.txt", "x")) with { Base = "develop" });

        Assert.Equal(DeliveryStatus.BaseNotFound, result.Status);
    }

    [Fact]
    public async Task NotARepository_IsReported()
    {
        var dir = Directory.CreateTempSubdirectory("delivery-engine-norepo-").FullName;
        try
        {
            var result = await GitDelivery.RunAsync(dir, MakeTask(Write("a.txt", "x")));

            Assert.Equal(DeliveryStatus.NotARepository, result.Status);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task MissingFolder_IsReportedAsNotARepository()
    {
        var result = await GitDelivery.RunAsync(Path.Combine(Path.GetTempPath(), "no-such-folder-" + Guid.NewGuid()), MakeTask(Write("a.txt", "x")));

        Assert.Equal(DeliveryStatus.NotARepository, result.Status);
    }

    [Fact]
    public async Task FailedChange_LeavesNoBranchAndNoWorktree()
    {
        using var repo = await TempGitRepo.CreateAsync();

        var result = await GitDelivery.RunAsync(repo.Path, MakeTask(Write("a.txt", "x"), Delete("no-such.txt")));

        Assert.Equal(DeliveryStatus.ChangeFailed, result.Status);
        Assert.Contains("changes[1]", result.Message);
        Assert.False(await BranchExistsAsync(repo, "fix/task-1"));
        Assert.Single((await repo.Git.RunCheckedAsync("worktree", "list")).Split('\n'));
    }

    [Fact]
    public async Task ChangesThatChangeNothing_AreReportedAndBranchIsRemoved()
    {
        using var repo = await TempGitRepo.CreateAsync();

        var result = await GitDelivery.RunAsync(repo.Path, MakeTask(Write("README.md", "# Test repo\n")));

        Assert.Equal(DeliveryStatus.NothingToCommit, result.Status);
        Assert.False(await BranchExistsAsync(repo, "fix/task-1"));
    }

    [Fact]
    public async Task FailingPreCommitHook_IsReportedAsCommitFailed()
    {
        using var repo = await TempGitRepo.CreateAsync();
        WriteRejectingHook(repo.FullPath(".git/hooks"), "pre-commit");

        var result = await GitDelivery.RunAsync(repo.Path, MakeTask(Write("a.txt", "x")));

        Assert.Equal(DeliveryStatus.CommitFailed, result.Status);
        Assert.Contains("hook says no", result.Message);
        Assert.False(await BranchExistsAsync(repo, "fix/task-1"));
    }

    [Fact]
    public async Task Push_CreatesBranchInOrigin()
    {
        using var repo = await TempGitRepo.CreateAsync();
        var origin = await repo.AddOriginAsync();

        var result = await GitDelivery.RunAsync(repo.Path, MakeTask(Write("a.txt", "x")), ToOrigin);

        Assert.Equal(DeliveryStatus.Pushed, result.Status);
        Assert.Equal(result.CommitSha, await origin.RunCheckedAsync("rev-parse", "refs/heads/fix/task-1"));
    }

    [Fact]
    public async Task Push_StartsFromFreshOriginBase_NotFromStaleLocalMain()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.AddOriginAsync();

        repo.WriteFile("newer.txt", "someone else's change\n");
        await repo.Git.RunCheckedAsync("add", "-A");
        await repo.Git.RunCheckedAsync("commit", "-m", "newer commit on origin");
        await repo.Git.RunCheckedAsync("push", "origin", "main");
        var originMain = await repo.Git.RunCheckedAsync("rev-parse", "HEAD");
        await repo.Git.RunCheckedAsync("reset", "--hard", "HEAD~1");

        var result = await GitDelivery.RunAsync(repo.Path, MakeTask(Write("a.txt", "x")), ToOrigin);

        Assert.Equal(DeliveryStatus.Pushed, result.Status);
        Assert.Equal(originMain, await repo.Git.RunCheckedAsync("rev-parse", "fix/task-1~1"));
        Assert.NotEqual(originMain, await repo.Git.RunCheckedAsync("rev-parse", "main"));
    }

    [Fact]
    public async Task Push_BranchAlreadyInOrigin_IsReportedAndNothingIsChanged()
    {
        using var repo = await TempGitRepo.CreateAsync();
        var origin = await repo.AddOriginAsync();
        await repo.Git.RunCheckedAsync("push", "origin", "main:refs/heads/fix/task-1");
        var before = await origin.RunCheckedAsync("rev-parse", "refs/heads/fix/task-1");

        var result = await GitDelivery.RunAsync(repo.Path, MakeTask(Write("a.txt", "x")), ToOrigin);

        Assert.Equal(DeliveryStatus.RemoteBranchExists, result.Status);
        Assert.Equal(before, await origin.RunCheckedAsync("rev-parse", "refs/heads/fix/task-1"));
        Assert.False(await BranchExistsAsync(repo, "fix/task-1"));
    }

    [Fact]
    public async Task Push_SecondRun_ReportsBranchExists()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.AddOriginAsync();
        var task = MakeTask(Write("a.txt", "x"));

        var first = await GitDelivery.RunAsync(repo.Path, task, ToOrigin);
        var second = await GitDelivery.RunAsync(repo.Path, task, ToOrigin);

        Assert.Equal(DeliveryStatus.Pushed, first.Status);
        Assert.Equal(DeliveryStatus.BranchExists, second.Status);
    }

    [Fact]
    public async Task Push_RejectedByOrigin_IsReportedAndLocalBranchIsRemoved()
    {
        using var repo = await TempGitRepo.CreateAsync();
        var origin = await repo.AddOriginAsync();
        WriteRejectingHook(Path.Combine(origin.WorkingDirectory, "hooks"), "pre-receive");

        var result = await GitDelivery.RunAsync(repo.Path, MakeTask(Write("a.txt", "x")), ToOrigin);

        Assert.Equal(DeliveryStatus.PushFailed, result.Status);
        Assert.Contains("hook says no", result.Message);
        Assert.False(await BranchExistsAsync(repo, "fix/task-1"));
    }

    [Fact]
    public async Task Push_UnreachableOrigin_IsReportedAsFetchFailed()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.Git.RunCheckedAsync("remote", "add", "origin", Path.Combine(Path.GetTempPath(), "no-such-origin-" + Guid.NewGuid()));

        var result = await GitDelivery.RunAsync(repo.Path, MakeTask(Write("a.txt", "x")), ToOrigin);

        Assert.Equal(DeliveryStatus.FetchFailed, result.Status);
        Assert.False(await BranchExistsAsync(repo, "fix/task-1"));
    }

    [Fact]
    public async Task Push_BaseMissingInOrigin_IsReportedAsFetchFailed()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.AddOriginAsync();

        var result = await GitDelivery.RunAsync(repo.Path, MakeTask(Write("a.txt", "x")) with { Base = "develop" }, ToOrigin);

        Assert.Equal(DeliveryStatus.FetchFailed, result.Status);
        Assert.Contains("develop", result.Message);
    }

    [Fact]
    public async Task WithoutRemote_NothingIsPushed()
    {
        using var repo = await TempGitRepo.CreateAsync();
        var origin = await repo.AddOriginAsync();

        var result = await GitDelivery.RunAsync(repo.Path, MakeTask(Write("a.txt", "x")));

        Assert.Equal(DeliveryStatus.Committed, result.Status);
        Assert.False((await origin.RunAsync("rev-parse", "--verify", "--quiet", "refs/heads/fix/task-1")).Success);
    }
}