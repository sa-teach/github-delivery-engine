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
}