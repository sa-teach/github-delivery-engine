using DeliveryEngine.GitHub;

namespace DeliveryEngine.Tests.Support;

public sealed class FakeGitHubClient : IGitHubClient
{
    public List<string> Calls { get; } = [];

    public PullRequestRequest? CreatedRequest { get; private set; }

    public string? FailOn { get; init; }

    public Task<PullRequestInfo> CreatePullRequestAsync(PullRequestRequest request)
    {
        Record("create");
        CreatedRequest = request;
        return Task.FromResult(new PullRequestInfo(42, "https://github.com/owner/repo/pull/42"));
    }

    public Task AddLabelsAsync(int number, IReadOnlyList<string> labels)
    {
        Record($"labels #{number}: {string.Join(",", labels)}");
        return Task.CompletedTask;
    }

    public Task EnableAutoMergeAsync(int number)
    {
        Record($"auto-merge #{number}");
        return Task.CompletedTask;
    }

    private void Record(string call)
    {
        Calls.Add(call);
        if (FailOn is not null && call.StartsWith(FailOn, StringComparison.Ordinal))
        {
            throw new GitHubException($"fake failure on {FailOn}");
        }
    }
}