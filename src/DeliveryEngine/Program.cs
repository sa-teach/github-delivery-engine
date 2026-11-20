using DeliveryEngine.Planning;
using DeliveryEngine.Tasks;

namespace DeliveryEngine;

public static class Program
{
    public const int ExitOk = 0;
    public const int ExitInvalidTask = 1;
    public const int ExitUsage = 2;

    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        return Run(args, Console.Out, Console.Error);
    }

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
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

        var unknownOption = rest.FirstOrDefault(a => a.StartsWith('-'));
        if (unknownOption is not null)
        {
            stderr.WriteLine($"Неизвестный параметр: {unknownOption}");
            return ExitUsage;
        }

        if (rest.Count != 1)
        {
            stderr.WriteLine("Укажите ровно один файл задачи");
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

        if (!dryRun)
        {
            stderr.WriteLine("Запустите с --dry-run.");
            return ExitUsage;
        }

        stdout.WriteLine($"Задача '{task.Id}' корректна. План:");
        var steps = DeliveryPlanner.BuildPlan(task);
        for (var i = 0; i < steps.Count; i++)
        {
            stdout.WriteLine($"  {i + 1}. {steps[i]}");
        }

        return ExitOk;
    }

    private static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("Использование:");
        writer.WriteLine("  DeliveryEngine deliver <task.json> --dry-run");
    }
}