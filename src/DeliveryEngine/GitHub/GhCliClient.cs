using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DeliveryEngine.GitHub;

// Работает через GitHub CLI (gh) и берет авторизацию из уже выполненного `gh auth login`
// Репозиторий gh определяет сам по remote в рабочей папке
public sealed partial class GhCliClient(string repoPath) : IGitHubClient
{
    public async Task<PullRequestInfo> CreatePullRequestAsync(PullRequestRequest request)
    {
        var output = await RunAsync(BuildCreateArgs(request), stdin: request.Body);
        return ParsePullRequestUrl(output);
    }

    public async Task AddLabelsAsync(int number, IReadOnlyList<string> labels)
    {
        var args = new List<string> { "pr", "edit", number.ToString() };
        foreach (var label in labels)
        {
            args.Add("--add-label");
            args.Add(label);
        }
        await RunAsync(args);
    }

    public Task EnableAutoMergeAsync(int number) =>
        RunAsync(["pr", "merge", number.ToString(), "--auto", "--merge"]);

    public async Task<PullRequestState?> FindPullRequestAsync(string head)
    {
        var output = await RunAsync(["pr", "list", "--head", head, "--state", "all", "--limit", "1", "--json", StateFields]);
        using var doc = JsonDocument.Parse(output);
        var items = doc.RootElement;
        return items.GetArrayLength() == 0 ? null : ParseState(items[0]);
    }

    public async Task<PullRequestState> GetPullRequestStateAsync(int number)
    {
        var output = await RunAsync(["pr", "view", number.ToString(), "--json", StateFields]);
        using var doc = JsonDocument.Parse(output);
        return ParseState(doc.RootElement);
    }

    private const string StateFields = "number,url,state,statusCheckRollup";

    public static PullRequestState ParseState(JsonElement pr)
    {
        var lifecycle = pr.GetProperty("state").GetString() switch
        {
            "OPEN" => PullRequestLifecycle.Open,
            "MERGED" => PullRequestLifecycle.Merged,
            "CLOSED" => PullRequestLifecycle.Closed,
            var other => throw new GitHubException($"Неизвестное состояние PR: {other}"),
        };

        var checks = pr.TryGetProperty("statusCheckRollup", out var rollup) && rollup.ValueKind == JsonValueKind.Array
            ? ParseChecks(rollup)
            : ChecksState.None;

        return new PullRequestState(pr.GetProperty("number").GetInt32(), pr.GetProperty("url").GetString()!, lifecycle, checks);
    }

    // В statusCheckRollup два вида записей: CheckRun (GitHub Actions) с полями status/conclusion
    // и StatusContext (старые commit statuses) с полем state.
    private static ChecksState ParseChecks(JsonElement rollup)
    {
        if (rollup.GetArrayLength() == 0)
        {
            return ChecksState.None;
        }

        var pending = false;
        foreach (var check in rollup.EnumerateArray())
        {
            var result = check.TryGetProperty("conclusion", out var conclusion) && conclusion.ValueKind == JsonValueKind.String && conclusion.GetString() != ""
                ? conclusion.GetString()
                : check.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.String
                    ? state.GetString()
                    : null;

            switch (result)
            {
                case "SUCCESS" or "NEUTRAL" or "SKIPPED":
                    break;
                case "FAILURE" or "ERROR" or "CANCELLED" or "TIMED_OUT" or "ACTION_REQUIRED" or "STARTUP_FAILURE" or "STALE":
                    return ChecksState.Failed;
                default:
                    pending = true;
                    break;
            }
        }

        return pending ? ChecksState.Pending : ChecksState.Passed;
    }

    public static IReadOnlyList<string> BuildCreateArgs(PullRequestRequest request) =>
    [
        "pr", "create",
        "--base", request.Base,
        "--head", request.Head,
        "--title", request.Title,
        // Тело через stdin: так переносы строк и кавычки доходят без искажений
        "--body-file", "-",
    ];

    public static PullRequestInfo ParsePullRequestUrl(string output)
    {
        var match = PullRequestUrl().Match(output);
        if (!match.Success)
        {
            throw new GitHubException($"gh не вернул ссылку на PR. Вывод: {output.Trim()}");
        }
        return new PullRequestInfo(int.Parse(match.Groups["number"].Value), match.Value);
    }

    private async Task<string> RunAsync(IEnumerable<string> args, string? stdin = null)
    {
        var psi = new ProcessStartInfo("gh")
        {
            WorkingDirectory = repoPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        psi.Environment["GH_PROMPT_DISABLED"] = "1";
        psi.Environment["GH_NO_UPDATE_NOTIFIER"] = "1";

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new GitHubException($"Не удалось запустить gh: {ex.Message}. Проверьте, что GitHub CLI установлен и есть в PATH.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin);
        }
        process.StandardInput.Close();

        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new GitHubException($"gh {string.Join(' ', psi.ArgumentList.Take(2))} завершился с кодом {process.ExitCode}: {stderr.Trim()}");
        }

        return stdout;
    }

    [GeneratedRegex(@"https://\S+/pull/(?<number>\d+)")]
    private static partial Regex PullRequestUrl();
}