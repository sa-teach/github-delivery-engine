using System.Text;
using DeliveryEngine.Git;
using DeliveryEngine.Tasks;

namespace DeliveryEngine.Delivery;

public sealed class ChangeApplyException(string message) : Exception(message);

public static class ChangeApplier
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static async Task ApplyAsync(GitRunner worktree, IReadOnlyList<Change> changes)
    {
        var root = System.IO.Path.GetFullPath(worktree.WorkingDirectory);

        for (var i = 0; i < changes.Count; i++)
        {
            var change = changes[i];
            var prefix = $"changes[{i}]";

            switch (change.Op)
            {
                case ChangeOperation.Write:
                    {
                        var full = ResolveInside(root, change.File!, prefix);
                        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
                        await File.WriteAllTextAsync(full, change.Content!, Utf8NoBom);
                        break;
                    }

                case ChangeOperation.Delete:
                    {
                        var full = ResolveInside(root, change.File!, prefix);
                        if (!File.Exists(full))
                        {
                            throw new ChangeApplyException($"{prefix}: файл '{change.File}' не существует, удалять нечего.");
                        }
                        File.Delete(full);
                        break;
                    }

                case ChangeOperation.Patch:
                    {
                        var result = await worktree.RunAsync(["apply", "--whitespace=nowarn", "-"], stdin: change.Diff);
                        if (!result.Success)
                        {
                            throw new ChangeApplyException($"{prefix}: patch не применился: {result.StdErr.Trim()}");
                        }
                        break;
                    }

                default:
                    throw new ChangeApplyException($"{prefix}: неизвестная операция {change.Op}.");
            }
        }
    }

    // дополнительно чекаю, что ни одна папка на пути не симлинк
    private static string ResolveInside(string root, string relative, string prefix)
    {
        var segments = TaskValidator.NormalizePath(relative).Split('/');
        var current = root;

        foreach (var segment in segments)
        {
            current = System.IO.Path.Combine(current, segment);
            var info = new FileInfo(current);
            if (info.Exists || Directory.Exists(current))
            {
                if (info.LinkTarget is not null || new DirectoryInfo(current).LinkTarget is not null)
                {
                    throw new ChangeApplyException($"{prefix}: путь '{relative}' проходит через симлинк, это запрещено.");
                }
            }
        }

        var full = System.IO.Path.GetFullPath(current);
        if (!full.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ChangeApplyException($"{prefix}: путь '{relative}' выходит за пределы репозитория.");
        }

        return full;
    }
}