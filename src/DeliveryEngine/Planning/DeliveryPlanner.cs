using DeliveryEngine.Tasks;

namespace DeliveryEngine.Planning;

public static class DeliveryPlanner
{
    public static IReadOnlyList<string> BuildPlan(DeliveryTask task)
    {
        var steps = new List<string>
        {
            $"Обновить '{task.Base}' из origin",
            $"Создать ветку '{task.Branch}' от '{task.Base}'",
        };

        foreach (var change in task.Changes ?? [])
        {
            steps.Add(change.Op switch
            {
                ChangeOperation.Write => $"Записать файл '{TaskValidator.NormalizePath(change.File!)}' ({FormatSize(change.Content!)})",
                ChangeOperation.Delete => $"Удалить файл '{TaskValidator.NormalizePath(change.File!)}'",
                ChangeOperation.Patch => $"Применить patch ({CountLines(change.Diff!)} строк diff)",
                _ => throw new InvalidOperationException($"Неизвестная операция: {change.Op}"),
            });
        }

        steps.Add($"Закоммитить: \"{FirstLine(task.CommitMessage!)}\"");
        steps.Add($"Запушить ветку '{task.Branch}' в origin");
        steps.Add($"Открыть PR '{task.Branch}' → '{task.Base}': \"{task.Pr!.Title}\"");

        if (task.Pr.Labels is { Count: > 0 } labels)
        {
            steps.Add($"Добавить метки: {string.Join(", ", labels)}");
        }

        steps.Add(task.Pr.AutoMerge
            ? "Включить auto-merge и дождаться результата CI"
            : "Оставить PR открытым для ручного ревью");

        return steps;
    }

    private static string FormatSize(string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetByteCount(content);
        return bytes < 1024 ? $"{bytes} Б" : $"{bytes / 1024.0:0.#} КБ";
    }

    private static int CountLines(string text) => text.TrimEnd('\n').Split('\n').Length;

    private static string FirstLine(string text) => text.Split('\n')[0].TrimEnd('\r');
}