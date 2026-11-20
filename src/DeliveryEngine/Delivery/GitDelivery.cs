using DeliveryEngine.Git;
using DeliveryEngine.Tasks;

namespace DeliveryEngine.Delivery;

public enum DeliveryStatus
{
    Committed,
    Pushed,
    NotARepository,
    BaseNotFound,
    BranchExists,
    RemoteBranchExists,
    FetchFailed,
    ChangeFailed,
    NothingToCommit,
    CommitFailed,
    PushFailed,
}

public sealed record DeliveryResult(DeliveryStatus Status, string Message, string? CommitSha = null)
{
    public bool IsSuccess => Status is DeliveryStatus.Committed or DeliveryStatus.Pushed;
}

// Remote = null: только локальный коммит, от локальной ветки base
// Remote = "origin": base берется из origin, результат пушится туда же
public sealed record DeliveryOptions(string? Remote = null);

public static class GitDelivery
{
    public static async Task<DeliveryResult> RunAsync(string repoPath, DeliveryTask task, DeliveryOptions? options = null)
    {
        options ??= new DeliveryOptions();

        if (!Directory.Exists(repoPath))
        {
            return new(DeliveryStatus.NotARepository, $"Папка не найдена: {repoPath}");
        }

        var repo = new GitRunner(repoPath);
        var branch = task.Branch!;

        if (!(await repo.RunAsync("rev-parse", "--git-dir")).Success)
        {
            return new(DeliveryStatus.NotARepository, $"'{repoPath}' не является git-репозиторием.");
        }

        // ветку не удаляем и не перезаписываем - вдруг это результат прошлого запуска
        if (await RefExistsAsync(repo, $"refs/heads/{branch}"))
        {
            return new(DeliveryStatus.BranchExists, $"Ветка '{branch}' уже существует локально.");
        }

        string startPoint;
        if (options.Remote is { } remote)
        {
            var fetch = await repo.RunAsync("fetch", remote, $"+refs/heads/{task.Base}:refs/remotes/{remote}/{task.Base}");
            if (!fetch.Success)
            {
                return new(DeliveryStatus.FetchFailed, $"Не удалось получить '{task.Base}' из {remote}: {fetch.StdErr.Trim()}");
            }

            // ls-remote --exit-code: 0 — ветка есть, 2 — ветки нет, остальное — ошибка связи.
            var lsRemote = await repo.RunAsync("ls-remote", "--exit-code", "--heads", remote, $"refs/heads/{branch}");
            if (lsRemote.ExitCode == 0)
            {
                return new(DeliveryStatus.RemoteBranchExists, $"Ветка '{branch}' уже есть в {remote}.");
            }
            if (lsRemote.ExitCode != 2)
            {
                return new(DeliveryStatus.FetchFailed, $"Не удалось проверить ветки в {remote}: {lsRemote.StdErr.Trim()}");
            }

            startPoint = $"refs/remotes/{remote}/{task.Base}";
        }
        else
        {
            if (!await RefExistsAsync(repo, $"refs/heads/{task.Base}"))
            {
                return new(DeliveryStatus.BaseNotFound, $"Ветка '{task.Base}' не найдена.");
            }
            startPoint = $"refs/heads/{task.Base}";
        }

        // При любой неудаче убираем созданную ветку, чтобы повторный запуск начинался с чистого листа
        DeliveryResult result;
        try
        {
            result = await CommitInWorktreeAsync(repo, task, startPoint);
            if (result.IsSuccess && options.Remote is not null)
            {
                result = await PushAsync(repo, branch, options.Remote, result.CommitSha!);
            }
        }
        catch
        {
            await repo.RunAsync("branch", "-D", branch);
            throw;
        }

        if (!result.IsSuccess)
        {
            await repo.RunAsync("branch", "-D", branch);
        }

        return result;
    }

    private static async Task<DeliveryResult> CommitInWorktreeAsync(GitRunner repo, DeliveryTask task, string startPoint)
    {
        await using var worktree = await Worktree.CreateAsync(repo, task.Branch!, startPoint);

        try
        {
            await ChangeApplier.ApplyAsync(worktree.Git, task.Changes!);
        }
        catch (ChangeApplyException ex)
        {
            return new(DeliveryStatus.ChangeFailed, ex.Message);
        }

        await worktree.Git.RunCheckedAsync("add", "-A");

        if ((await worktree.Git.RunAsync("diff", "--cached", "--quiet")).Success)
        {
            return new(DeliveryStatus.NothingToCommit, "Изменения ничего не меняют: файлы уже в таком состоянии.");
        }

        // Сообщение через stdin, чтобы многострочный текст передался без искажений.
        var commit = await worktree.Git.RunAsync(["commit", "--file=-"], stdin: task.CommitMessage);
        if (!commit.Success)
        {
            var details = string.Join(" ", commit.StdErr.Trim(), commit.StdOut.Trim()).Trim();
            return new(DeliveryStatus.CommitFailed, $"git commit не удался: {details}");
        }

        var sha = await worktree.Git.RunCheckedAsync("rev-parse", "HEAD");
        return new(DeliveryStatus.Committed, $"Ветка '{task.Branch}' создана, коммит {sha[..7]}.", sha);
    }

    private static async Task<DeliveryResult> PushAsync(GitRunner repo, string branch, string remote, string sha)
    {
        // Без -f: если ветка в remote появилась после проверки, push просто откажет
        var push = await repo.RunAsync("push", remote, $"refs/heads/{branch}:refs/heads/{branch}");
        if (!push.Success)
        {
            return new(DeliveryStatus.PushFailed, $"git push не удался: {push.StdErr.Trim()}");
        }

        return new(DeliveryStatus.Pushed, $"Ветка '{branch}' запушена в {remote}, коммит {sha[..7]}.", sha);
    }

    private static async Task<bool> RefExistsAsync(GitRunner repo, string refName) =>
        (await repo.RunAsync("rev-parse", "--verify", "--quiet", refName + "^{commit}")).Success;
}