namespace Terraria_Wiki.Application.Tests;

public sealed class ApplicationSettingsTests
{
    [Fact]
    public async Task MissingSettingsUseFixedDefaultsAndArePersisted()
    {
        await using var fixture = await ApplicationFixture.CreateAsync(initialize: false);

        var settings = await fixture.Application.Settings.GetAsync();

        Assert.Equal(new ApplicationPreferences(), settings);
        Assert.Equal(settings, fixture.Settings.Preferences);
        Assert.Equal(1, fixture.Settings.PreferencesSaveCount);
        Assert.Null(fixture.Settings.ActiveWikiId);
    }

    [Fact]
    public async Task ValidSettingsAreSavedAsOneValueWithoutChangingActiveWiki()
    {
        await using var fixture = await ApplicationFixture.CreateAsync(activeWikiId: 3, initialize: false);
        var value = new ApplicationPreferences
        {
            Language = "en-us",
            AppTheme = "dark",
            ContentTheme = "original",
            IsSideButtonBackEnabled = false,
            SearchLimit = 100,
            AiSearchLimit = 17,
            PageConcurrency = 3,
            ResourceConcurrency = 20,
            RetryAttempts = 0,
        };

        var saved = await fixture.Application.Settings.SaveAsync(value);

        Assert.Equal(value, saved);
        Assert.Equal(value, fixture.Settings.Preferences);
        Assert.Equal(3, fixture.Settings.ActiveWikiId);
        Assert.Equal(0, fixture.Settings.ActiveWikiSaveCount);
    }

    [Fact]
    public async Task InvalidStoredFieldsAreIndividuallyRepairedAndWrittenBack()
    {
        var invalid = new ApplicationPreferences
        {
            Language = "fr-fr",
            AppTheme = "blue",
            ContentTheme = "dark",
            IsSideButtonBackEnabled = false,
            SearchLimit = 101,
            AiSearchLimit = 0,
            PageConcurrency = 0,
            ResourceConcurrency = 21,
            RetryAttempts = -1,
            WikiFullScanIntervalDays = 0,
        };
        await using var fixture = await ApplicationFixture.CreateAsync(invalid, initialize: false);

        var repaired = await fixture.Application.Settings.GetAsync();

        Assert.Equal(ApplicationPreferences.DefaultLanguage, repaired.Language);
        Assert.Equal(ApplicationPreferences.DefaultAppTheme, repaired.AppTheme);
        Assert.Equal(ApplicationPreferences.DefaultContentTheme, repaired.ContentTheme);
        Assert.False(repaired.IsSideButtonBackEnabled);
        Assert.Equal(50, repaired.SearchLimit);
        Assert.Equal(5, repaired.AiSearchLimit);
        Assert.Equal(2, repaired.PageConcurrency);
        Assert.Equal(10, repaired.ResourceConcurrency);
        Assert.Equal(5, repaired.RetryAttempts);
        Assert.Equal(14, repaired.WikiFullScanIntervalDays);
        Assert.Equal(repaired, fixture.Settings.Preferences);
        Assert.Equal(1, fixture.Settings.PreferencesSaveCount);
    }

    [Theory]
    [MemberData(nameof(InvalidValues))]
    public async Task InvalidExplicitSettingsAreRejectedWithoutWriting(ApplicationPreferences invalid)
    {
        var original = new ApplicationPreferences();
        await using var fixture = await ApplicationFixture.CreateAsync(original, initialize: false);

        var error = await Assert.ThrowsAsync<ApplicationUseCaseException>(
            () => fixture.Application.Settings.SaveAsync(invalid));

        Assert.Equal("invalid-preferences", error.Code);
        Assert.Equal(original, fixture.Settings.Preferences);
        Assert.Equal(0, fixture.Settings.PreferencesSaveCount);
    }

    public static TheoryData<ApplicationPreferences> InvalidValues => new()
    {
        new ApplicationPreferences { Language = "zh" },
        new ApplicationPreferences { AppTheme = "original" },
        new ApplicationPreferences { ContentTheme = "dark" },
        new ApplicationPreferences { SearchLimit = 0 },
        new ApplicationPreferences { SearchLimit = 101 },
        new ApplicationPreferences { AiSearchLimit = 0 },
        new ApplicationPreferences { AiSearchLimit = 101 },
        new ApplicationPreferences { PageConcurrency = 4 },
        new ApplicationPreferences { ResourceConcurrency = 0 },
        new ApplicationPreferences { RetryAttempts = 21 },
        new ApplicationPreferences { WikiFullScanIntervalDays = 0 },
        new ApplicationPreferences { WikiFullScanIntervalDays = -1 },
    };

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(int.MaxValue)]
    public async Task Full_scan_interval_is_saved_without_touching_content_state(int? days)
    {
        await using var fixture = await ApplicationFixture.CreateAsync();
        var database = fixture.State.ContentDatabase;
        await database.SaveItemAsync(new Terraria_Wiki.Models.WikiDatabaseMetadata { Key = "sync_state", Value = "preserve exactly" });
        var settings = await fixture.Application.Settings.GetAsync();
        var updated = await fixture.Application.Settings.SaveAsync(settings with { WikiFullScanIntervalDays = days });
        Assert.Equal(days, updated.WikiFullScanIntervalDays);
        Assert.Equal(days, fixture.Settings.Preferences!.WikiFullScanIntervalDays);
        Assert.Equal("preserve exactly", (await database.GetItemAsync<Terraria_Wiki.Models.WikiDatabaseMetadata>("sync_state"))!.Value);
    }
}
