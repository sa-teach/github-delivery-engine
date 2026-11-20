using DeliveryEngine.Git;
using DeliveryEngine.GitHub;
using DeliveryEngine.Tasks;

namespace DeliveryEngine.Delivery;

public static class PullRequestDelivery
{
    public const string Remote = "origin";

    public static async Task<DeliveryResult> RunAsync(string repoPath, DeliveryTask task, IGitHubClient github)
    {
        var pushed = await GitDelivery.RunAsync(repoPath, task, new DeliveryOptions(Remote));
        if (pushed.Status != DeliveryStatus.Pushed)
        {
            return pushed;
        }

        var branch = task.Branch!;
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

        var message = pr.AutoMerge
            ? $"PR #{info.Number} создан, auto-merge включён."
            : $"PR #{info.Number} создан и ждёт ручного ревью.";

        return new(DeliveryStatus.PullRequestCreated, message, pushed.CommitSha)
        {
            PullRequestUrl = info.Url,
            Warnings = warnings,
        };
    }
}