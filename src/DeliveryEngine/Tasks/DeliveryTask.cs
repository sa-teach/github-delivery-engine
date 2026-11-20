namespace DeliveryEngine.Tasks;

public sealed record DeliveryTask
{
    public string? Id { get; init; }
    public string Base { get; init; } = "main";
    public string? Branch { get; init; }
    public string? CommitMessage { get; init; }

    public PrOptions? Pr { get; init; }

    public IReadOnlyList<Change>? Changes { get; init; }
}

public sealed record PrOptions
{
    public string? Title { get; init; }
    public string? Body { get; init; }

    public IReadOnlyList<string>? Labels { get; init; }

    public bool AutoMerge { get; init; }
}

public sealed record Change
{
    public ChangeOperation? Op { get; init; }

    public string? File { get; init; }
    public string? Content { get; init; }
    public string? Diff { get; init; }
}

public enum ChangeOperation
{
    Write,
    Delete,
    Patch,
}