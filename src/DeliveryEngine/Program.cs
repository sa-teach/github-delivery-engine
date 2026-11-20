using DeliveryEngine.Delivery;
using DeliveryEngine.Git;
using DeliveryEngine.Planning;
using DeliveryEngine.Tasks;

namespace DeliveryEngine;

public static class Program
{
    public const int ExitOk = 0;
    public const int ExitInvalidTask = 1;
    public const int ExitUsage = 2;
    public const int ExitDeliveryFailed = 3;

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        return await RunAsync(args, Console.Out, Console.Error);
    }

    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintUsage(stdout);
            return args.Length == 0 ? ExitUsage : ExitOk;
        }

        if (args[0] != "deliver")
        {
            stderr.WriteLine($"Неизвестная команда: {args[0]}");
            PrintUsage(stderr);
            return ExitUsage;
        }

        var rest = args.Skip(1).ToList();
        var dryRun = rest.Remove("--dry-run");
        var push = rest.Remove("--push");

        var repoPath = Directory.GetCurrentDirectory();
        var repoIndex = rest.IndexOf("--repo");
        if (repoIndex >= 0)
        {
            if (repoIndex + 1 >= rest.Count)
            {
                stderr.WriteLine("После --repo нужно указать путь к репозиторию.");
                return ExitUsage;
            }
            repoPath = rest[repoIndex + 1];
            rest.RemoveRange(repoIndex, 2);
        }

        var unknownOption = rest.FirstOrDefault(a => a.StartsWith('-'));
        if (unknownOption is not null)
        {
            stderr.WriteLine($"Неизвестный параметр: {unknownOption}");
            return ExitUsage;
        }

        if (rest.Count != 1)
        {
            stderr.WriteLine("Укажите ровно один файл задачи.");
            PrintUsage(stderr);
            return ExitUsage;
        }

        DeliveryTask task;
        try
        {
            task = TaskLoader.Load(rest[0]);
        }
        catch (TaskLoadException ex)
        {
            stderr.WriteLine(ex.Message);
            return ExitInvalidTask;
        }

        var errors = TaskValidator.Validate(task);
        if (errors.Count > 0)
        {
            stderr.WriteLine($"Задача не прошла проверку ({errors.Count}):");
            foreach (var error in errors)
            {
                stderr.WriteLine($"  - {error}");
            }
            return ExitInvalidTask;
        }

        if (dryRun)
        {
            stdout.WriteLine($"Задача '{task.Id}' корректна. План:");
            var steps = DeliveryPlanner.BuildPlan(task);
            for (var i = 0; i < steps.Count; i++)
            {
                stdout.WriteLine($"  {i + 1}. {steps[i]}");
            }
            return ExitOk;
        }

        DeliveryResult result;
        try
        {
            var options = new DeliveryOptions(Remote: push ? "origin" : null);
            result = await GitDelivery.RunAsync(repoPath, task, options);
        }
        catch (GitException ex)
        {
            stderr.WriteLine($"Ошибка git: {ex.Message}");
            return ExitDeliveryFailed;
        }

        var writer = result.IsSuccess ? stdout : stderr;
        writer.WriteLine($"[{result.Status}] {result.Message}");

        if (result.Status == DeliveryStatus.Committed)
        {
            stdout.WriteLine("Ветка создана только локально. Чтобы отправить ее в origin, добавьте --push.");
        }
        else if (result.Status == DeliveryStatus.Pushed)
        {
            stdout.WriteLine("PR пока не создается...");
        }

        return result.IsSuccess ? ExitOk : ExitDeliveryFailed;
    }

    private static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("Использование:");
        writer.WriteLine("  DeliveryEngine deliver <task.json> [--repo <путь>] [--push] [--dry-run]\n");
        writer.WriteLine("  --repo     путь к git-репозиторию (по умолчанию текущая папка)");
        writer.WriteLine("  --push     взять свежий base из origin и запушить ветку туда");
        writer.WriteLine("  --dry-run  только проверить задачу и показать план");
    }
}