using DeliveryEngine.Git;

namespace DeliveryEngine.Tests.Support;

public sealed class TempGitRepo : IDisposable
{
    private readonly List<string> _extraDirs = [];

    public string Path { get; }

    public GitRunner Git { get; }

    private TempGitRepo(string path)
    {
        Path = path;
        Git = new GitRunner(path);
    }

    public static async Task<TempGitRepo> CreateAsync()
    {
        var repo = new TempGitRepo(Directory.CreateTempSubdirectory("delivery-engine-repo-").FullName);

        await repo.Git.RunCheckedAsync("init", "--initial-branch=main");
        await repo.Git.RunCheckedAsync("config", "user.name", "Test");
        await repo.Git.RunCheckedAsync("config", "user.email", "test@example.com");
        await repo.Git.RunCheckedAsync("config", "commit.gpgsign", "false");
        await repo.Git.RunCheckedAsync("config", "core.autocrlf", "false");

        repo.WriteFile("README.md", "# Test repo\n");
        await repo.Git.RunCheckedAsync("add", "-A");
        await repo.Git.RunCheckedAsync("commit", "-m", "initial");

        return repo;
    }

    public async Task<GitRunner> AddOriginAsync()
    {
        var bare = Directory.CreateTempSubdirectory("delivery-engine-origin-").FullName;
        _extraDirs.Add(bare);

        var origin = new GitRunner(bare);
        await origin.RunCheckedAsync("init", "--bare", "--initial-branch=main");
        await Git.RunCheckedAsync("remote", "add", "origin", bare);
        await Git.RunCheckedAsync("push", "origin", "main");

        return origin;
    }

    public string FullPath(string relative) => System.IO.Path.Combine(Path, relative);

    public void WriteFile(string relative, string content)
    {
        var full = FullPath(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    public string ReadFile(string relative) => File.ReadAllText(FullPath(relative));

    public void Dispose()
    {
        foreach (var dir in _extraDirs.Append(Path))
        {
            DeleteDirectory(dir);
        }
    }

    // git помечает файлы в .git/objects как read-only, и Directory.Delete на них падает
    private static void DeleteDirectory(string dir)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(dir, recursive: true);
    }
}