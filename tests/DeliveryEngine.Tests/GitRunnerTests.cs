using DeliveryEngine.Git;
using DeliveryEngine.Tests.Support;

namespace DeliveryEngine.Tests;

public class GitRunnerTests
{
    [Fact]
    public async Task Run_Success_ReturnsOutput()
    {
        using var repo = await TempGitRepo.CreateAsync();

        var result = await repo.Git.RunAsync("rev-parse", "--abbrev-ref", "HEAD");

        Assert.True(result.Success);
        Assert.Equal("main", result.StdOut.Trim());
    }

    [Fact]
    public async Task Run_Failure_ReturnsExitCodeAndStdErr()
    {
        using var repo = await TempGitRepo.CreateAsync();

        var result = await repo.Git.RunAsync("rev-parse", "--verify", "no-such-branch");

        Assert.False(result.Success);
        Assert.NotEqual(0, result.ExitCode);
        Assert.NotEmpty(result.StdErr);
    }

    [Fact]
    public async Task Run_UsesWorkingDirectory()
    {
        using var repo = await TempGitRepo.CreateAsync();

        var topLevel = await repo.Git.RunCheckedAsync("rev-parse", "--show-toplevel");

        Assert.Equal(
            Path.GetFullPath(repo.Path).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(topLevel).TrimEnd(Path.DirectorySeparatorChar),
            ignoreCase: OperatingSystem.IsWindows());
    }

    [Fact]
    public async Task Run_PassesStdin()
    {
        using var repo = await TempGitRepo.CreateAsync();

        var result = await repo.Git.RunAsync(["hash-object", "--stdin"], stdin: "hello\n");

        // Хэш blob-объекта "hello\n" в git всегда один и тот же.
        Assert.Equal("ce013625030ba8dba906f756967f9e9ca394464a", result.StdOut.Trim());
    }

    [Fact]
    public async Task Run_ArgumentsWithSpacesAndQuotes_ArePassedAsIs()
    {
        using var repo = await TempGitRepo.CreateAsync();
        repo.WriteFile("a.txt", "x");
        await repo.Git.RunCheckedAsync("add", "a.txt");

        const string message = "fix: message with \"quotes\" and spaces";
        await repo.Git.RunCheckedAsync("commit", "-m", message);

        Assert.Equal(message, await repo.Git.RunCheckedAsync("log", "-1", "--format=%s"));
    }

    [Fact]
    public async Task RunChecked_Failure_Throws()
    {
        using var repo = await TempGitRepo.CreateAsync();

        var ex = await Assert.ThrowsAsync<GitException>(() => repo.Git.RunCheckedAsync("checkout", "no-such-branch"));

        Assert.Contains("git checkout no-such-branch", ex.Message);
    }

    [Fact]
    public async Task Run_NotARepository_Fails()
    {
        var dir = Directory.CreateTempSubdirectory("delivery-engine-norepo-").FullName;
        try
        {
            var result = await new GitRunner(dir).RunAsync("rev-parse", "--git-dir");

            Assert.False(result.Success);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}