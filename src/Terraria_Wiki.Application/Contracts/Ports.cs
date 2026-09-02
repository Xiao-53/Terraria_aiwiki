namespace Terraria_Wiki.Application;

public interface IApplicationSettingsStore
{
    Task<ApplicationPreferences?> LoadPreferencesAsync(CancellationToken cancellationToken = default);
    Task SavePreferencesAsync(ApplicationPreferences preferences, CancellationToken cancellationToken = default);
    Task<int?> LoadActiveWikiIdAsync(CancellationToken cancellationToken = default);
    Task SaveActiveWikiIdAsync(int wikiId, CancellationToken cancellationToken = default);
}

public interface IWikiDataConsumerCoordinator
{
    ValueTask<IAsyncDisposable> PauseAsync(CancellationToken cancellationToken = default);
}
