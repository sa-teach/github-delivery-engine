using DeliveryEngine.Delivery;
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
        Assert.Equal("https://github.com/owner/repo/pull/42", result.PullRequestUrl);
        Assert.Empty(result.Warnings);
        Assert.Equal(["create", "labels #42: bot,auto", "auto-merge #42"], github.Calls);
        Assert.Equal(new GitHub.PullRequestRequest("main", "fix/task-1", "Fix it", "Line 1\nLine 2"), github.CreatedRequest);
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
        Assert.Equal(["create"], github.Calls);
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
        Assert.Empty(github.Calls);
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
}