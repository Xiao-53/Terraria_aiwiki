using Terraria_Wiki.Application.Internal;

namespace Terraria_Wiki.Application;

public sealed class ApplicationSettingsUseCases
{
    private static readonly HashSet<string> Languages = new(StringComparer.Ordinal)
    {
        "zh-cn",
        "en-us",
    };

    private static readonly HashSet<string> AppThemes = new(StringComparer.Ordinal)
    {
        "auto",
        "light",
        "dark",
    };

    private static readonly HashSet<string> ContentThemes = new(StringComparer.Ordinal)
    {
        "auto",
        "original",
    };

    private static readonly HashSet<string> StartupPages = new(StringComparer.Ordinal)
    {
        "chat",
        "wiki",
    };

    private readonly ApplicationState _state;
    private readonly SemaphoreSlim _settingsGate = new(1, 1);

    internal ApplicationSettingsUseCases(ApplicationState state) => _state = state;

    public async Task<ApplicationPreferences> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_state.Preferences is not null)
            return _state.Preferences;

        return await LoadAndRepairAsync(cancellationToken);
    }

    public async Task<ApplicationPreferences> SaveAsync(
        ApplicationPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        Validate(preferences);

        await _settingsGate.WaitAsync(cancellationToken);
        try
        {
            await _state.SettingsStore.SavePreferencesAsync(preferences, cancellationToken);
            _state.Preferences = preferences;
            return preferences;
        }
        finally
        {
            _settingsGate.Release();
        }
    }

    internal async Task<ApplicationPreferences> LoadAndRepairAsync(
        CancellationToken cancellationToken = default)
    {
        await _settingsGate.WaitAsync(cancellationToken);
        try
        {
            if (_state.Preferences is not null)
                return _state.Preferences;

            var stored = await _state.SettingsStore.LoadPreferencesAsync(cancellationToken);
            var repaired = Repair(stored);
            if (stored is null || stored != repaired)
                await _state.SettingsStore.SavePreferencesAsync(repaired, cancellationToken);
            _state.Preferences = repaired;
            return repaired;
        }
        finally
        {
            _settingsGate.Release();
        }
    }

    private static ApplicationPreferences Repair(ApplicationPreferences? stored)
    {
        if (stored is null)
            return new ApplicationPreferences();

        return stored with
        {
            Language = Languages.Contains(stored.Language)
                ? stored.Language
                : ApplicationPreferences.DefaultLanguage,
            AppTheme = AppThemes.Contains(stored.AppTheme)
                ? stored.AppTheme
                : ApplicationPreferences.DefaultAppTheme,
            ContentTheme = ContentThemes.Contains(stored.ContentTheme)
                ? stored.ContentTheme
                : ApplicationPreferences.DefaultContentTheme,
            StartupPage = StartupPages.Contains(stored.StartupPage)
                ? stored.StartupPage
                : ApplicationPreferences.DefaultStartupPage,
            SearchLimit = stored.SearchLimit is >= 1 and <= 100
                ? stored.SearchLimit
                : ApplicationPreferences.DefaultSearchLimit,
            AiSearchLimit = stored.AiSearchLimit is >= 1 and <= 100
                ? stored.AiSearchLimit
                : ApplicationPreferences.DefaultAiSearchLimit,
            PageConcurrency = stored.PageConcurrency is >= 1 and <= 3
                ? stored.PageConcurrency
                : ApplicationPreferences.DefaultPageConcurrency,
            ResourceConcurrency = stored.ResourceConcurrency is >= 1 and <= 20
                ? stored.ResourceConcurrency
                : ApplicationPreferences.DefaultResourceConcurrency,
            RetryAttempts = stored.RetryAttempts is >= 0 and <= 20
                ? stored.RetryAttempts
                : ApplicationPreferences.DefaultRetryAttempts,
            WikiFullScanIntervalDays = stored.WikiFullScanIntervalDays is <= 0
                ? ApplicationPreferences.DefaultWikiFullScanIntervalDays
                : stored.WikiFullScanIntervalDays,
        };
    }

    private static void Validate(ApplicationPreferences preferences)
    {
        if (!Languages.Contains(preferences.Language))
            throw Invalid(nameof(preferences.Language));
        if (!AppThemes.Contains(preferences.AppTheme))
            throw Invalid(nameof(preferences.AppTheme));
        if (!ContentThemes.Contains(preferences.ContentTheme))
            throw Invalid(nameof(preferences.ContentTheme));
        if (!StartupPages.Contains(preferences.StartupPage))
            throw Invalid(nameof(preferences.StartupPage));
        if (preferences.SearchLimit is < 1 or > 100)
            throw Invalid(nameof(preferences.SearchLimit));
        if (preferences.AiSearchLimit is < 1 or > 100)
            throw Invalid(nameof(preferences.AiSearchLimit));
        if (preferences.PageConcurrency is < 1 or > 3)
            throw Invalid(nameof(preferences.PageConcurrency));
        if (preferences.ResourceConcurrency is < 1 or > 20)
            throw Invalid(nameof(preferences.ResourceConcurrency));
        if (preferences.RetryAttempts is < 0 or > 20)
            throw Invalid(nameof(preferences.RetryAttempts));
        if (preferences.WikiFullScanIntervalDays is <= 0)
            throw Invalid(nameof(preferences.WikiFullScanIntervalDays));
    }

    private static ApplicationUseCaseException Invalid(string propertyName) => new(
        "invalid-preferences",
        $"Application preference '{propertyName}' is invalid.");
}
