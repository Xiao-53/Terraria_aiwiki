namespace Terraria_Wiki.Core.Operations;

public enum WikiUpdateMode { Automatic, Full, Quick }

public sealed record WikiOperationOptions(
    int MaxSearchResults = 50,
    int PageConcurrency = 2,
    int ResourceConcurrency = 10,
    int MaxRetryAttempts = 5,
    int? FullScanIntervalDays = 14)
{
    public Func<string, CancellationToken, Task<bool>>? ConfirmFullScanAsync { get; init; }
    public WikiUpdateMode UpdateMode { get; init; }

    public WikiOperationOptions Validate()
    {
        if (!Enum.IsDefined(UpdateMode)) throw new ArgumentOutOfRangeException(nameof(UpdateMode));
        if (MaxSearchResults <= 0) throw new ArgumentOutOfRangeException(nameof(MaxSearchResults));
        if (PageConcurrency is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(PageConcurrency));
        if (ResourceConcurrency is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(ResourceConcurrency));
        if (MaxRetryAttempts is < 0 or > 20) throw new ArgumentOutOfRangeException(nameof(MaxRetryAttempts));
        if (FullScanIntervalDays is <= 0) throw new ArgumentOutOfRangeException(nameof(FullScanIntervalDays));
        return this;
    }
}

public sealed record WikiProgress(
    string Code,
    int Current = 0,
    int Total = 0,
    string? Detail = null,
    Exception? Error = null);

public sealed record WikiOperationSummary(
    int Pages = 0,
    int Redirects = 0,
    int Resources = 0,
    int Failed = 0,
    bool Changed = false,
    int Checked = 0,
    int Skipped = 0,
    int Deleted = 0,
    bool UsedFullScan = false);

public sealed class WikiOperationException : Exception
{
    public WikiOperationException(string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;

    public string Code { get; }
}
