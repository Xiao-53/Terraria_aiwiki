using Terraria_Wiki.Application;
using Terraria_Wiki.Core.Net;
using Terraria_Wiki.Models;

namespace Terraria_Wiki.WikiTools.TestHost;

internal sealed class TestHostSettingsStore : IApplicationSettingsStore
{
    private ApplicationPreferences? _preferences;
    private int? _activeWikiId;

    public Task<ApplicationPreferences?> LoadPreferencesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_preferences);
    }

    public Task SavePreferencesAsync(
        ApplicationPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _preferences = preferences;
        return Task.CompletedTask;
    }

    public Task<int?> LoadActiveWikiIdAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_activeWikiId);
    }

    public Task SaveActiveWikiIdAsync(int wikiId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _activeWikiId = wikiId;
        return Task.CompletedTask;
    }
}

internal sealed class RejectingTestHostTransport : IWikiHttpTransport
{
    public Task<string> GetStringAsync(
        WikiBook book,
        Uri uri,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The test Host does not perform remote HTTP requests.");

    public Task<WikiHttpContentResponse> GetContentAsync(
        WikiBook book,
        Uri uri,
        DateTimeOffset? ifModifiedSince = null,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The test Host does not perform remote HTTP requests.");
}
