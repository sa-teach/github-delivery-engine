using System.Text.RegularExpressions;

namespace DeliveryEngine.Tasks;

public static partial class TaskValidator
{
    public static IReadOnlyList<string> Validate(DeliveryTask task)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(task.Id))
        {
            errors.Add("id: обязательное поле.");
        }
        else if (!IdPattern().IsMatch(task.Id))
        {
            errors.Add("id: допустимы только латинские буквы, цифры, '.', '_' и '-'.");
        }

        ValidateBranchName("base", task.Base, errors);
        ValidateBranchName("branch", task.Branch, errors);

        if (task.Branch is not null && task.Branch == task.Base)
        {
            errors.Add("branch: рабочая ветка не может совпадать с base.");
        }

        if (string.IsNullOrWhiteSpace(task.CommitMessage))
        {
            errors.Add("commitMessage: обязательное поле.");
        }

        if (task.Pr is null)
        {
            errors.Add("pr: обязательный раздел.");
        }
        else if (string.IsNullOrWhiteSpace(task.Pr.Title))
        {
            errors.Add("pr.title: обязательное поле.");
        }

        if (task.Changes is null || task.Changes.Count == 0)
        {
            errors.Add("changes: нужно хотя бы одно изменение.");
        }
        else
        {
            ValidateChanges(task.Changes, errors);
        }

        return errors;
    }

    private static void ValidateChanges(IReadOnlyList<Change> changes, List<string> errors)
    {
        var touchedFiles = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < changes.Count; i++)
        {
            var change = changes[i];
            var prefix = $"changes[{i}]";

            if (change is null)
            {
                errors.Add($"{prefix}: пустой элемент.");
                continue;
            }

            switch (change.Op)
            {
                case null:
                    errors.Add($"{prefix}.op: обязательное поле (write, delete или patch).");
                    break;

                case ChangeOperation.Write:
                    ValidateFilePath(prefix, change.File, touchedFiles, errors);
                    if (change.Content is null)
                    {
                        errors.Add($"{prefix}.content: обязательно для write (может быть пустой строкой).");
                    }
                    if (change.Diff is not null)
                    {
                        errors.Add($"{prefix}.diff: не используется в write.");
                    }
                    break;

                case ChangeOperation.Delete:
                    ValidateFilePath(prefix, change.File, touchedFiles, errors);
                    if (change.Content is not null || change.Diff is not null)
                    {
                        errors.Add($"{prefix}: в delete не должно быть content или diff.");
                    }
                    break;

                case ChangeOperation.Patch:
                    if (string.IsNullOrWhiteSpace(change.Diff))
                    {
                        errors.Add($"{prefix}.diff: обязательно для patch.");
                    }
                    if (change.File is not null || change.Content is not null)
                    {
                        errors.Add($"{prefix}: в patch пути берутся из diff, поля file и content не используются.");
                    }
                    break;
            }
        }
    }

    private static void ValidateFilePath(string prefix, string? file, HashSet<string> touchedFiles, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            errors.Add($"{prefix}.file: обязательное поле.");
            return;
        }

        var error = CheckRepoRelativePath(file);
        if (error is not null)
        {
            errors.Add($"{prefix}.file: {error}");
            return;
        }

        var normalized = NormalizePath(file);
        if (!touchedFiles.Add(normalized))
        {
            errors.Add($"{prefix}.file: файл '{normalized}' уже изменяется в этой задаче.");
        }
    }

    public static string NormalizePath(string path) => path.Replace('\\', '/');

    public static string? CheckRepoRelativePath(string path)
    {
        var normalized = NormalizePath(path);

        if (normalized.StartsWith('/') || Path.IsPathRooted(path) || DriveLetter().IsMatch(normalized))
        {
            return "путь должен быть относительным к корню репозитория.";
        }

        var segments = normalized.Split('/');
        foreach (var segment in segments)
        {
            if (segment.Length == 0)
            {
                return "путь содержит пустой сегмент ('//' или '/' в конце).";
            }
            if (segment is "." or "..")
            {
                return "сегменты '.' и '..' запрещены.";
            }
            if (segment.Equals(".git", StringComparison.OrdinalIgnoreCase))
            {
                return "изменять служебную папку .git запрещено.";
            }
        }

        return null;
    }

    private static void ValidateBranchName(string field, string? name, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add($"{field}: обязательное поле.");
            return;
        }

        // Упрощённая версия правил git check-ref-format.
        if (name.StartsWith('-') || name.StartsWith('/') || name.EndsWith('/') || name.EndsWith('.')
            || name.EndsWith(".lock", StringComparison.Ordinal)
            || name.Contains("..", StringComparison.Ordinal) || name.Contains("//", StringComparison.Ordinal)
            || name.Contains("@{", StringComparison.Ordinal) || name == "@"
            || ForbiddenBranchChars().IsMatch(name))
        {
            errors.Add($"{field}: '{name}' не является допустимым именем ветки git.");
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9._-]+$")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^[A-Za-z]:")]
    private static partial Regex DriveLetter();

    [GeneratedRegex(@"[\x00-\x20\x7F~^:?*\[\\]")]
    private static partial Regex ForbiddenBranchChars();
}