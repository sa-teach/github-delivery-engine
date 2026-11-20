using DeliveryEngine.Delivery;
using DeliveryEngine.GitHub;
using DeliveryEngine.Tasks;
using DeliveryEngine.Tests.Support;

namespace DeliveryEngine.Tests;

public class PullRequestDeliveryTests
{
    private static DeliveryTask MakeTask(bool autoMerge = true, IReadOnlyList<string>? labels = null) =>
        TaskValidatorTests.ValidTask() with
        {
            Branch = "fix/task-1",
            Pr = new PrOptions { Title = "Fix it", Body = "Line 1\nLine 2", Labels = labels, AutoMerge = autoMerge },
            Changes = [new Change { Op = ChangeOperation.Write, File = "a.txt", Content = "x" }],
        };

    private static async Task<bool> OriginHasBranchAsync(Git.GitRunner origin) =>
        (await origin.RunAsync("rev-parse", "--verify", "--quiet", "refs/heads/fix/task-1")).Success;

    [Fact]
    public async Task CreatesPullRequestWithLabelsAndAutoMerge()
    {
        using var repo = await TempGitRepo.CreateAsync();
        var origin = await repo.AddOriginAsync();
        var github = new FakeGitHubClient();

        var result = await PullRequestDelivery.RunAsync(repo.Path, MakeTask(labels: ["bot", "auto"]), github);

        Assert.Equal(DeliveryStatus.PullRequestCreated, result.Status);
        Assert.Equal(FakeGitHubClient.Url, result.PullRequestUrl);
        Assert.Empty(result.Warnings);
        Assert.Equal(["find fix/task-1", "create", "labels #42: bot,auto", "auto-merge #42"], github.Calls);
        Assert.Equal(new PullRequestRequest("main", "fix/task-1", "Fix it", "Line 1\nLine 2"), github.CreatedRequest);
        Assert.True(await OriginHasBranchAsync(origin));
    }

    [Fact]
    public async Task WithoutAutoMerge_LeavesPullRequestForReview()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.AddOriginAsync();
        var github = new FakeGitHubClient();

        var result = await PullRequestDelivery.RunAsync(repo.Path, MakeTask(autoMerge: false), github);

        Assert.Equal(DeliveryStatus.PullRequestCreated, result.Status);
        Assert.Equal(["find fix/task-1", "create"], github.Calls);
        Assert.Contains("ручного ревью", result.Message);
    }

    [Fact]
    public async Task GitFailure_DoesNotCallGitHub()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.AddOriginAsync();
        await repo.Git.RunCheckedAsync("branch", "fix/task-1");
        var github = new FakeGitHubClient();

        var result = await PullRequestDelivery.RunAsync(repo.Path, MakeTask(), github);

        Assert.Equal(DeliveryStatus.BranchExists, result.Status);
        Assert.Equal(["find fix/task-1"], github.Calls);
    }

    [Fact]
    public async Task PullRequestFailure_RemovesPushedBranch()
    {
        using var repo = await TempGitRepo.CreateAsync();
        var origin = await repo.AddOriginAsync();
        var github = new FakeGitHubClient { FailOn = "create" };

        var result = await PullRequestDelivery.RunAsync(repo.Path, MakeTask(), github);

        Assert.Equal(DeliveryStatus.PullRequestFailed, result.Status);
        Assert.False(result.IsSuccess);
        Assert.False(await OriginHasBranchAsync(origin));
        Assert.False((await repo.Git.RunAsync("rev-parse", "--verify", "--quiet", "refs/heads/fix/task-1")).Success);
    }

    [Fact]
    public async Task LabelFailure_IsOnlyAWarning()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.AddOriginAsync();
        var github = new FakeGitHubClient { FailOn = "labels" };

        var result = await PullRequestDelivery.RunAsync(repo.Path, MakeTask(labels: ["missing-label"]), github);

        Assert.Equal(DeliveryStatus.PullRequestCreated, result.Status);
        Assert.Single(result.Warnings);
        Assert.Contains("auto-merge #42", github.Calls);
    }

    [Fact]
    public async Task AutoMergeFailure_KeepsPullRequestAndReportsFailure()
    {
        using var repo = await TempGitRepo.CreateAsync();
        var origin = await repo.AddOriginAsync();
        var github = new FakeGitHubClient { FailOn = "auto-merge" };

        var result = await PullRequestDelivery.RunAsync(repo.Path, MakeTask(), github);

        Assert.Equal(DeliveryStatus.AutoMergeFailed, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Equal("https://github.com/owner/repo/pull/42", result.PullRequestUrl);
        Assert.True(await OriginHasBranchAsync(origin));
    }

    private static readonly PullRequestOptions WaitFast = new(Wait: true, Timeout: TimeSpan.FromSeconds(5), PollInterval: TimeSpan.Zero);

    [Fact]
    public async Task ExistingOpenPullRequest_IsReportedWithoutPushing()
    {
        using var repo = await TempGitRepo.CreateAsync();
        var origin = await repo.AddOriginAsync();
        var github = new FakeGitHubClient { Existing = FakeGitHubClient.State(PullRequestLifecycle.Open) };

        var result = await PullRequestDelivery.RunAsync(repo.Path, MakeTask(), github);

        Assert.Equal(DeliveryStatus.PullRequestExists, result.Status);
        Assert.True(result.IsSuccess);
        Assert.Equal(FakeGitHubClient.Url, result.PullRequestUrl);
        Assert.Equal(["find fix/task-1"], github.Calls);
        Assert.False(await OriginHasBranchAsync(origin));
    }

    [Fact]
    public async Task ExistingMergedPullRequest_IsReportedAsAlreadyMerged()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.AddOriginAsync();
        var github = new FakeGitHubClient { Existing = FakeGitHubClient.State(PullRequestLifecycle.Merged) };

        var result = await PullRequestDelivery.RunAsync(repo.Path, MakeTask(), github);

        Assert.Equal(DeliveryStatus.AlreadyMerged, result.Status);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ExistingClosedPullRequest_NeedsHuman()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.AddOriginAsync();
        var github = new FakeGitHubClient { Existing = FakeGitHubClient.State(PullRequestLifecycle.Closed) };

        var result = await PullRequestDelivery.RunAsync(repo.Path, MakeTask(), github);

        Assert.Equal(DeliveryStatus.PullRequestClosed, result.Status);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Wait_UntilMerged()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.AddOriginAsync();
        var github = new FakeGitHubClient().WithStates(
            (PullRequestLifecycle.Open, ChecksState.None),
            (PullRequestLifecycle.Open, ChecksState.Pending),
            (PullRequestLifecycle.Open, ChecksState.Passed),
            (PullRequestLifecycle.Merged, ChecksState.Passed));

        var result = await PullRequestDelivery.RunAsync(repo.Path, MakeTask(), github, WaitFast);

        Assert.Equal(DeliveryStatus.Merged, result.Status);
        Assert.True(result.IsSuccess);
        Assert.Equal(4, github.Calls.Count(c => c == "state #42"));
    }

    [Fact]
    public async Task Wait_CiFailure_LeavesPullRequestOpen()
    {
        using var repo = await TempGitRepo.CreateAsync();
        var origin = await repo.AddOriginAsync();
        var github = new FakeGitHubClient().WithStates(
            (PullRequestLifecycle.Open, ChecksState.Pending),
            (PullRequestLifecycle.Open, ChecksState.Failed));

        var result = await PullRequestDelivery.RunAsync(repo.Path, MakeTask(), github, WaitFast);

        Assert.Equal(DeliveryStatus.CiFailed, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Equal(FakeGitHubClient.Url, result.PullRequestUrl);
        Assert.True(await OriginHasBranchAsync(origin));
    }

    [Fact]
    public async Task Wait_ClosedWhileWaiting()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.AddOriginAsync();
        var github = new FakeGitHubClient().WithStates((PullRequestLifecycle.Closed, ChecksState.Passed));

        var result = await PullRequestDelivery.RunAsync(repo.Path, MakeTask(), github, WaitFast);

        Assert.Equal(DeliveryStatus.PullRequestClosed, result.Status);
    }

    [Fact]
    public async Task Wait_Timeout_IsReported()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.AddOriginAsync();
        var github = new FakeGitHubClient().WithStates((PullRequestLifecycle.Open, ChecksState.Pending));
        var options = new PullRequestOptions(Wait: true, Timeout: TimeSpan.Zero, PollInterval: TimeSpan.Zero);

        var result = await PullRequestDelivery.RunAsync(repo.Path, MakeTask(), github, options);

        Assert.Equal(DeliveryStatus.WaitTimedOut, result.Status);
        Assert.Contains("Pending", result.Message);
    }

    [Fact]
    public async Task Wait_OnExistingOpenPullRequest_WaitsForIt()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.AddOriginAsync();
        var github = new FakeGitHubClient { Existing = FakeGitHubClient.State(PullRequestLifecycle.Open) }
            .WithStates((PullRequestLifecycle.Merged, ChecksState.Passed));

        var result = await PullRequestDelivery.RunAsync(repo.Path, MakeTask(), github, WaitFast);

        Assert.Equal(DeliveryStatus.Merged, result.Status);
        Assert.Equal(["find fix/task-1", "state #42"], github.Calls);
    }
}