using System.Text.Json;
using DeliveryEngine.GitHub;

namespace DeliveryEngine.Tests;

public class GhCliClientTests
{
    [Fact]
    public void BuildCreateArgs_PassesBodyThroughStdin()
    {
        var args = GhCliClient.BuildCreateArgs(new PullRequestRequest("main", "fix/a", "Fix \"quoted\" title", "body"));

        Assert.Equal(
            ["pr", "create", "--base", "main", "--head", "fix/a", "--title", "Fix \"quoted\" title", "--body-file", "-"],
            args);
    }

    [Fact]
    public void ParsePullRequestUrl_ReadsNumber()
    {
        var info = GhCliClient.ParsePullRequestUrl("\nCreating pull request...\nhttps://github.com/sa-teach/repo/pull/1070\n");

        Assert.Equal(1070, info.Number);
        Assert.Equal("https://github.com/sa-teach/repo/pull/1070", info.Url);
    }

    [Fact]
    public void ParsePullRequestUrl_NoUrl_Throws()
    {
        Assert.Throws<GitHubException>(() => GhCliClient.ParsePullRequestUrl("something went wrong"));
    }
    private static PullRequestState Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return GhCliClient.ParseState(doc.RootElement);
    }

    [Fact]
    public void ParseState_MergedWithoutChecks()
    {
        var state = Parse("""{ "number": 7, "url": "https://github.com/o/r/pull/7", "state": "MERGED", "statusCheckRollup": [] }""");

        Assert.Equal(new PullRequestState(7, "https://github.com/o/r/pull/7", PullRequestLifecycle.Merged, ChecksState.None), state);
    }

    [Fact]
    public void ParseState_CheckRunInProgress_IsPending()
    {
        var state = Parse("""
        { "number": 7, "url": "u", "state": "OPEN", "statusCheckRollup": [
          { "__typename": "CheckRun", "name": "test", "status": "COMPLETED", "conclusion": "SUCCESS" },
          { "__typename": "CheckRun", "name": "lint", "status": "IN_PROGRESS", "conclusion": "" }
        ] }
        """);

        Assert.Equal(PullRequestLifecycle.Open, state.Lifecycle);
        Assert.Equal(ChecksState.Pending, state.Checks);
    }

    [Fact]
    public void ParseState_AnyFailure_IsFailed()
    {
        var state = Parse("""
        { "number": 7, "url": "u", "state": "OPEN", "statusCheckRollup": [
          { "__typename": "CheckRun", "name": "lint", "status": "IN_PROGRESS", "conclusion": "" },
          { "__typename": "CheckRun", "name": "test", "status": "COMPLETED", "conclusion": "FAILURE" }
        ] }
        """);

        Assert.Equal(ChecksState.Failed, state.Checks);
    }

    [Fact]
    public void ParseState_AllSuccessOrSkipped_IsPassed()
    {
        var state = Parse("""
        { "number": 7, "url": "u", "state": "OPEN", "statusCheckRollup": [
          { "__typename": "CheckRun", "name": "test", "status": "COMPLETED", "conclusion": "SUCCESS" },
          { "__typename": "CheckRun", "name": "deploy", "status": "COMPLETED", "conclusion": "SKIPPED" },
          { "__typename": "StatusContext", "context": "ci/legacy", "state": "SUCCESS" }
        ] }
        """);

        Assert.Equal(ChecksState.Passed, state.Checks);
    }

    [Fact]
    public void ParseState_StatusContextError_IsFailed()
    {
        var state = Parse("""
        { "number": 7, "url": "u", "state": "OPEN", "statusCheckRollup": [
          { "__typename": "StatusContext", "context": "ci/legacy", "state": "ERROR" }
        ] }
        """);

        Assert.Equal(ChecksState.Failed, state.Checks);
    }    
}