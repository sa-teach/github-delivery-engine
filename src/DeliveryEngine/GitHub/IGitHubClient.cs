namespace DeliveryEngine.GitHub;

public sealed record PullRequestRequest(string Base, string Head, string Title, string Body);

public sealed record PullRequestInfo(int Number, string Url);

public enum PullRequestLifecycle
{
    Open,
    Merged,
    Closed,
}

public enum ChecksState
{
    None,
    Pending,
    Passed,
    Failed,
}

public sealed record PullRequestState(int Number, string Url, PullRequestLifecycle Lifecycle, ChecksState Checks);

public sealed class GitHubException(string message) : Exception(message);

public interface IGitHubClient
{
    Task<PullRequestInfo> CreatePullRequestAsync(PullRequestRequest request);

    Task AddLabelsAsync(int number, IReadOnlyList<string> labels);

    Task EnableAutoMergeAsync(int number);

    // Последний PR (в любом состоянии) из ветки head, или null, если его нет
    Task<PullRequestState?> FindPullRequestAsync(string head);

    Task<PullRequestState> GetPullRequestStateAsync(int number);
}