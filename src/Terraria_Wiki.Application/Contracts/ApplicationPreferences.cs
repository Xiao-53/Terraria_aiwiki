namespace Terraria_Wiki.Application;

public sealed record ApplicationPreferences
{
    public const string DefaultLanguage = "zh-cn";
    public const string DefaultAppTheme = "auto";
    public const string DefaultContentTheme = "auto";
    public const int DefaultSearchLimit = 50;
    public const int DefaultPageConcurrency = 2;
    public const int DefaultResourceConcurrency = 10;
    public const int DefaultRetryAttempts = 5;
    public const int DefaultActiveWikiId = 1;
    public const int DefaultWikiFullScanIntervalDays = 14;

    public string Language { get; init; } = DefaultLanguage;
    public string AppTheme { get; init; } = DefaultAppTheme;
    public string ContentTheme { get; init; } = DefaultContentTheme;
    public bool IsSideButtonBackEnabled { get; init; } = true;
    public int SearchLimit { get; init; } = DefaultSearchLimit;
    public int PageConcurrency { get; init; } = DefaultPageConcurrency;
    public int ResourceConcurrency { get; init; } = DefaultResourceConcurrency;
    public int RetryAttempts { get; init; } = DefaultRetryAttempts;
    // null disables periodic checks, but never recovery or identity checks.
    public int? WikiFullScanIntervalDays { get; init; } = DefaultWikiFullScanIntervalDays;
}
