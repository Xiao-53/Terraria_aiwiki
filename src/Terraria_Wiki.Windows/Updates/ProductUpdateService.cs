using Velopack;
using Velopack.Sources;

namespace Terraria_Wiki.Windows.Updates;

internal sealed record ProductUpdateCapabilities(
    bool CanUpdate,
    bool IsPortable,
    string CurrentVersion,
    Uri? ReleasesUri,
    string StatusMessage);

internal sealed record ProductUpdateAvailable(string Version);

internal interface IProductUpdateService
{
    ProductUpdateCapabilities Capabilities { get; }
    Task<ProductUpdateAvailable?> CheckAsync(CancellationToken cancellationToken = default);
    Task DownloadAsync(IProgress<int> progress, CancellationToken cancellationToken = default);
    void ApplyAndRestart();
}

internal sealed class VelopackProductUpdateService : IProductUpdateService
{
    private readonly UpdateManager? _manager;
    private UpdateInfo? _pending;

    internal VelopackProductUpdateService(string? repositoryUrl = null)
    {
        repositoryUrl ??= ProductMetadata.RepositoryUrl;
        if (!ProductMetadata.IsGitHubRepositoryUrl(repositoryUrl))
        {
            Capabilities = new ProductUpdateCapabilities(
                false,
                false,
                ProductMetadata.Version,
                null,
                "此构建未配置公开 GitHub Releases 更新源。");
            return;
        }

        var source = new GithubSource(repositoryUrl!, accessToken: null, prerelease: false);
        _manager = new UpdateManager(source, new UpdateOptions
        {
            AllowVersionDowngrade = false,
            ExplicitChannel = ProductMetadata.StableChannel,
        });
        var canUpdate = _manager.IsInstalled;
        Capabilities = new ProductUpdateCapabilities(
            canUpdate,
            _manager.IsPortable,
            _manager.CurrentVersion?.ToString() ?? ProductMetadata.Version,
            new Uri(repositoryUrl!.TrimEnd('/') + "/releases", UriKind.Absolute),
            canUpdate
                ? "仅在点击检查后访问公开 GitHub Releases；不会静默下载。"
                : "当前运行位置不支持原位更新，请从 GitHub Releases 手动下载。" );
    }

    public ProductUpdateCapabilities Capabilities { get; }

    public async Task<ProductUpdateAvailable?> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        var manager = RequireManager();
        var update = await manager.CheckForUpdatesAsync()
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        _pending = update;
        return update is null
            ? null
            : new ProductUpdateAvailable(update.TargetFullRelease.Version.ToString());
    }

    public async Task DownloadAsync(
        IProgress<int> progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var manager = RequireManager();
        var pending = _pending
            ?? throw new InvalidOperationException("No product update has been checked.");
        await manager.DownloadUpdatesAsync(
                pending,
                value => progress.Report(value),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public void ApplyAndRestart()
    {
        var manager = RequireManager();
        var pending = _pending
            ?? throw new InvalidOperationException("No product update has been downloaded.");
        manager.ApplyUpdatesAndRestart(pending.TargetFullRelease);
    }

    private UpdateManager RequireManager()
    {
        if (_manager is null || !Capabilities.CanUpdate)
            throw new InvalidOperationException("Product update is unavailable for this build.");
        return _manager;
    }
}
