namespace DeliveryEngine.Git;

public sealed class Worktree : IAsyncDisposable
{
    private readonly GitRunner _repo;

    public string Path { get; }

    public GitRunner Git { get; }

    private Worktree(GitRunner repo, string path)
    {
        _repo = repo;
        Path = path;
        Git = new GitRunner(path);
    }

    public static async Task<Worktree> CreateAsync(GitRunner repo, string branch, string startPoint)
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "delivery-engine-wt-" + Guid.NewGuid().ToString("N")[..12]);

        await repo.RunCheckedAsync("worktree", "add", "-b", branch, path, startPoint);

        return new Worktree(repo, path);
    }

    public async ValueTask DisposeAsync()
    {
        await _repo.RunAsync("worktree", "remove", "--force", Path);
        await _repo.RunAsync("worktree", "prune");

        if (Directory.Exists(Path))
        {
            foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(Path, recursive: true);
        }
    }
}