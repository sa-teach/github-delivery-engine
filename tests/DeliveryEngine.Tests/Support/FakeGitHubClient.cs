using DeliveryEngine.GitHub;

namespace DeliveryEngine.Tests.Support;

public sealed class FakeGitHubClient : IGitHubClient
{
    public const string Url = "https://github.com/owner/repo/pull/42";

    private readonly Queue<PullRequestState> _states = new();

    public List<string> Calls { get; } = [];

    public PullRequestRequest? CreatedRequest { get; private set; }

    public string? FailOn { get; init; }

    // PR, который "уже есть" в репозитории до запуска.
    public PullRequestState? Existing { get; init; }

    // Состояния, которые по очереди вернет GetPullRequestStateAsync. Последнее повторяется.
    public FakeGitHubClient WithStates(params (PullRequestLifecycle Lifecycle, ChecksState Checks)[] states)
    {
        foreach (var (lifecycle, checks) in states)
        {
            _states.Enqueue(new PullRequestState(42, Url, lifecycle, checks));
        }
        return this;
    }

    public static PullRequestState State(PullRequestLifecycle lifecycle, ChecksState checks = ChecksState.None) =>
        new(42, Url, lifecycle, checks);

    public Task<PullRequestInfo> CreatePullRequestAsync(PullRequestRequest request)
    {
        Record("create");
        CreatedRequest = request;
        return Task.FromResult(new PullRequestInfo(42, Url));
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

    public Task<PullRequestState?> FindPullRequestAsync(string head)
    {
        Record($"find {head}");
        return Task.FromResult(Existing);
    }

    public Task<PullRequestState> GetPullRequestStateAsync(int number)
    {
        Record($"state #{number}");
        var state = _states.Count > 1 ? _states.Dequeue() : _states.Peek();
        return Task.FromResult(state);
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