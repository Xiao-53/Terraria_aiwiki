using Terraria_Wiki.Application.Internal;
using Terraria_Wiki.Core.Net;

namespace Terraria_Wiki.Application;

public sealed class WikiApplication : IAsyncDisposable
{
    private readonly ApplicationState _state;

    internal ApplicationState State => _state;

    public WikiApplication(
        string dataRoot,
        IApplicationSettingsStore settingsStore,
        IWikiDataConsumerCoordinator consumerCoordinator,
        IWikiHttpTransport httpTransport,
        TimeProvider? timeProvider = null)
    {
        _state = new ApplicationState(
            dataRoot,
            settingsStore,
            consumerCoordinator,
            httpTransport,
            timeProvider ?? TimeProvider.System);

        Settings = new ApplicationSettingsUseCases(_state);
        ActiveWiki = new ActiveWikiUseCases(_state, Settings);
        Query = new WikiQueryUseCases(_state);
        Library = new WikiLibraryUseCases(_state);
        Data = new WikiDataUseCases(_state);
    }

    public ApplicationSettingsUseCases Settings { get; }
    public ActiveWikiUseCases ActiveWiki { get; }
    public WikiQueryUseCases Query { get; }
    public WikiLibraryUseCases Library { get; }
    public WikiDataUseCases Data { get; }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _state.ContentDatabase.CloseConnection();
        }
        finally
        {
            await _state.ManagerDatabase.CloseConnection();
            _state.CoordinationGate.Dispose();
        }
    }
}
