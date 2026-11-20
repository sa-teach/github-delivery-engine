using System.Diagnostics;
using System.Text;

namespace DeliveryEngine.Git;

public sealed record GitResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;
}

public sealed class GitException(string message) : Exception(message);

public sealed class GitRunner(string workingDirectory)
{
    public string WorkingDirectory { get; } = workingDirectory;

    public async Task<GitResult> RunAsync(IEnumerable<string> args, string? stdin = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = WorkingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            // Без BOM: иначе git увидит лишние байты в начале patch.
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        // Git не должен ничего спрашивать интерактивно: движок работает без человека.
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new GitException($"Не удалось запустить git: {ex.Message}. Проверьте, что git установлен и есть в PATH.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin.AsMemory(), ct);
        }
        process.StandardInput.Close();

        await process.WaitForExitAsync(ct);

        return new GitResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    public Task<GitResult> RunAsync(params string[] args) => RunAsync(args, stdin: null);

    public async Task<string> RunCheckedAsync(params string[] args)
    {
        var result = await RunAsync(args);
        if (!result.Success)
        {
            throw new GitException($"git {string.Join(' ', args)} завершился с кодом {result.ExitCode}: {result.StdErr.Trim()}");
        }
        return result.StdOut.TrimEnd();
    }
}