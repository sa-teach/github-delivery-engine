namespace DeliveryEngine.GitHub;

public sealed record PullRequestRequest(string Base, string Head, string Title, string Body);

public sealed record PullRequestInfo(int Number, string Url);

public sealed class GitHubException(string message) : Exception(message);

public interface IGitHubClient
{
    Task<PullRequestInfo> CreatePullRequestAsync(PullRequestRequest request);

    Task AddLabelsAsync(int number, IReadOnlyList<string> labels);

    Task EnableAutoMergeAsync(int number);
}