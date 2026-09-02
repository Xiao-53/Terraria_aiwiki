namespace Terraria_Wiki.Core.Operations;

public sealed record WikiOperationOptions(
    int MaxSearchResults = 50,
    int PageConcurrency = 2,
    int ResourceConcurrency = 10,
    int MaxRetryAttempts = 5)
{
    public WikiOperationOptions Validate()
    {
        if (MaxSearchResults <= 0) throw new ArgumentOutOfRangeException(nameof(MaxSearchResults));
        if (PageConcurrency is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(PageConcurrency));
        if (ResourceConcurrency is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(ResourceConcurrency));
        if (MaxRetryAttempts is < 0 or > 20) throw new ArgumentOutOfRangeException(nameof(MaxRetryAttempts));
        return this;
    }
}

public sealed record WikiProgress(
    string Code,
    int Current = 0,
    int Total = 0,
    string? Detail = null);

public sealed record WikiOperationSummary(
    int Pages = 0,
    int Redirects = 0,
    int Resources = 0,
    int Failed = 0,
    bool Changed = false);

public sealed class WikiOperationException : Exception
{
    public WikiOperationException(string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;

    public string Code { get; }
}
