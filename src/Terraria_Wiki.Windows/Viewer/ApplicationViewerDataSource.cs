using Terraria_Wiki.Application;

namespace Terraria_Wiki.Windows.Viewer;

internal sealed class ApplicationViewerDataSource : IViewerDataSource
{
    private readonly Func<WikiApplication> _application;

    public ApplicationViewerDataSource(Func<WikiApplication> application) =>
        _application = application ?? throw new ArgumentNullException(nameof(application));

    public async Task<ViewerAsset?> ReadAssetAsync(
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var asset = await _application().Query.ReadAssetAsync(fileName, cancellationToken).ConfigureAwait(false);
        return asset is null ? null : new ViewerAsset(asset.FileName, asset.Data, asset.MimeType);
    }
}
