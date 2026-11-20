using DeliveryEngine.Git;
using DeliveryEngine.Tests.Support;

namespace DeliveryEngine.Tests;

public class WorktreeTests
{
    [Fact]
    public async Task Create_ChecksOutNewBranchInSeparateFolder()
    {
        using var repo = await TempGitRepo.CreateAsync();

        await using var worktree = await Worktree.CreateAsync(repo.Git, "fix/a", "main");

        Assert.NotEqual(repo.Path, worktree.Path);
        Assert.True(File.Exists(Path.Combine(worktree.Path, "README.md")));
        Assert.Equal("fix/a", await worktree.Git.RunCheckedAsync("rev-parse", "--abbrev-ref", "HEAD"));
    }

    [Fact]
    public async Task Create_DoesNotTouchMainWorkingCopy()
    {
        using var repo = await TempGitRepo.CreateAsync();
        repo.WriteFile("uncommitted.txt", "work in progress");

        await using (var worktree = await Worktree.CreateAsync(repo.Git, "fix/a", "main"))
        {
            File.WriteAllText(Path.Combine(worktree.Path, "new.txt"), "x");
        }

        Assert.Equal("main", await repo.Git.RunCheckedAsync("rev-parse", "--abbrev-ref", "HEAD"));
        Assert.Equal("work in progress", repo.ReadFile("uncommitted.txt"));
        Assert.False(File.Exists(repo.FullPath("new.txt")));
    }

    [Fact]
    public async Task Dispose_RemovesFolderButKeepsBranch()
    {
        using var repo = await TempGitRepo.CreateAsync();

        var worktree = await Worktree.CreateAsync(repo.Git, "fix/a", "main");
        var path = worktree.Path;
        await worktree.DisposeAsync();

        Assert.False(Directory.Exists(path));
        Assert.True((await repo.Git.RunAsync("rev-parse", "--verify", "refs/heads/fix/a")).Success);
        Assert.DoesNotContain(path, await repo.Git.RunCheckedAsync("worktree", "list"));
    }

    [Fact]
    public async Task Create_BranchAlreadyExists_Throws()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.Git.RunCheckedAsync("branch", "fix/a");

        await Assert.ThrowsAsync<GitException>(() => Worktree.CreateAsync(repo.Git, "fix/a", "main"));
    }

    [Fact]
    public async Task Create_UnknownStartPoint_Throws()
    {
        using var repo = await TempGitRepo.CreateAsync();

        await Assert.ThrowsAsync<GitException>(() => Worktree.CreateAsync(repo.Git, "fix/a", "no-such-branch"));
    }
}