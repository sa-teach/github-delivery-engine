using System.Diagnostics;
using DeliveryEngine.Git;
using DeliveryEngine.GitHub;
using DeliveryEngine.Tasks;

namespace DeliveryEngine.Delivery;

// Wait = true: после создания PR ждать, пока он смержится, упадет на CI или выйдет время.
public sealed record PullRequestOptions(bool Wait = false, TimeSpan? Timeout = null, TimeSpan? PollInterval = null)
{
    public TimeSpan EffectiveTimeout => Timeout ?? TimeSpan.FromMinutes(30);

    public TimeSpan EffectivePollInterval => PollInterval ?? TimeSpan.FromSeconds(15);
}

public static class PullRequestDelivery
{
    public const string Remote = "origin";

    public static async Task<DeliveryResult> RunAsync(
        string repoPath,
        DeliveryTask task,
        IGitHubClient github,
        PullRequestOptions? options = null)
    {
        options ??= new PullRequestOptions();
        var branch = task.Branch!;

        // Повторный запуск: если PR из этой ветки уже есть, ничего не создаём заново, а сообщаем его состояние.
        var existing = await github.FindPullRequestAsync(branch);
        if (existing is not null)
        {
            return existing.Lifecycle switch
            {
                PullRequestLifecycle.Merged => FromState(DeliveryStatus.AlreadyMerged, $"PR #{existing.Number} уже смёржен.", existing),
                PullRequestLifecycle.Closed => FromState(DeliveryStatus.PullRequestClosed, $"PR #{existing.Number} закрыт без слияния. Нужно решение человека.", existing),
                _ when options.Wait => await WaitAsync(github, existing.Number, existing.Url, null, [], options),
                _ => FromState(DeliveryStatus.PullRequestExists, $"PR #{existing.Number} уже открыт.", existing),
            };
        }

        var pushed = await GitDelivery.RunAsync(repoPath, task, new DeliveryOptions(Remote));
        if (pushed.Status != DeliveryStatus.Pushed)
        {
            return pushed;
        }

        var pr = task.Pr!;

        PullRequestInfo info;
        try
        {
            info = await github.CreatePullRequestAsync(new PullRequestRequest(task.Base, branch, pr.Title!, pr.Body ?? ""));
        }
        catch (GitHubException ex)
        {
            // PR не создан — убираем запушенную ветку, чтобы повторный запуск начался с нуля.
            var repo = new GitRunner(repoPath);
            await repo.RunAsync("push", Remote, "--delete", $"refs/heads/{branch}");
            await repo.RunAsync("branch", "-D", branch);
            return new(DeliveryStatus.PullRequestFailed, $"PR не создан: {ex.Message}", pushed.CommitSha);
        }

        var warnings = new List<string>();

        // Метки — не повод считать доставку проваленной: PR уже есть, метку можно добавить руками.
        if (pr.Labels is { Count: > 0 } labels)
        {
            try
            {
                await github.AddLabelsAsync(info.Number, labels);
            }
            catch (GitHubException ex)
            {
                warnings.Add($"Метки не добавлены: {ex.Message}");
            }
        }

        if (pr.AutoMerge)
        {
            try
            {
                await github.EnableAutoMergeAsync(info.Number);
            }
            catch (GitHubException ex)
            {
                return new(DeliveryStatus.AutoMergeFailed, $"PR #{info.Number} создан, но auto-merge не включён: {ex.Message}", pushed.CommitSha)
                {
                    PullRequestUrl = info.Url,
                    Warnings = warnings,
                };
            }
        }

        if (options.Wait)
        {
            return await WaitAsync(github, info.Number, info.Url, pushed.CommitSha, warnings, options);
        }

        var message = pr.AutoMerge
            ? $"PR #{info.Number} создан, auto-merge включён."
            : $"PR #{info.Number} создан и ждёт ручного ревью.";

        return new(DeliveryStatus.PullRequestCreated, message, pushed.CommitSha)
        {
            PullRequestUrl = info.Url,
            Warnings = warnings,
        };
    }

    private static async Task<DeliveryResult> WaitAsync(
        IGitHubClient github,
        int number,
        string url,
        string? commitSha,
        IReadOnlyList<string> warnings,
        PullRequestOptions options)
    {
        var elapsed = Stopwatch.StartNew();

        while (true)
        {
            var state = await github.GetPullRequestStateAsync(number);

            var finished = state switch
            {
                { Lifecycle: PullRequestLifecycle.Merged } => (DeliveryStatus.Merged, $"PR #{number} смёржен."),
                { Lifecycle: PullRequestLifecycle.Closed } => (DeliveryStatus.PullRequestClosed, $"PR #{number} закрыт без слияния."),
                { Checks: ChecksState.Failed } => (DeliveryStatus.CiFailed, $"PR #{number}: проверки CI не прошли. PR оставлен открытым."),
                _ => ((DeliveryStatus, string)?)null,
            };

            if (finished is var (status, message))
            {
                return new(status, message, commitSha) { PullRequestUrl = url, Warnings = warnings };
            }

            if (elapsed.Elapsed >= options.EffectiveTimeout)
            {
                return new(DeliveryStatus.WaitTimedOut,
                    $"PR #{number} не завершился за {options.EffectiveTimeout.TotalMinutes:0.#} мин (CI: {state.Checks}). PR оставлен как есть.",
                    commitSha)
                {
                    PullRequestUrl = url,
                    Warnings = warnings,
                };
            }

            await Task.Delay(options.EffectivePollInterval);
        }
    }

    private static DeliveryResult FromState(DeliveryStatus status, string message, PullRequestState state) =>
        new(status, message) { PullRequestUrl = state.Url };
}