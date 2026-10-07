using Terraria_Wiki.Windows.Hosting;
using Terraria_Wiki.Windows.Presentation.Infrastructure;

namespace Terraria_Wiki.Windows.Presentation.Settings;

internal sealed class ProductDiagnosticsViewModel : ObservableObject, IDisposable
{
    private readonly ProductDiagnostics _diagnostics;
    private readonly Func<Uri, Task> _openExternalUri;
    private string _webView2Status = UiTextCatalog.Message("WaitingForStartupChecks");
    private string _statusMessage;

    internal ProductDiagnosticsViewModel(
        ProductDiagnostics diagnostics,
        Func<Uri, Task> openExternalUri)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _openExternalUri = openExternalUri ?? throw new ArgumentNullException(nameof(openExternalUri));
        _statusMessage = diagnostics.LogDirectory is null
            ? UiTextCatalog.Message("LocalDiagnosticLogsAreUnavailableTheOfflineWiki")
            : UiTextCatalog.Message("LogsIncludeVersionLifecycleWikiDownloadUpdateStages");
        _description = _statusMessage;
        OpenLogFolderCommand = new AsyncCommand(
            _ => OpenLogFolderAsync(),
            _ => LogDirectoryUri is not null);
    }

    public string WindowsStatus => $"{Environment.OSVersion.Version} · x64";
    public string WebView2Status
    {
        get => Text.Render(_webView2Status);
        private set => SetProperty(ref _webView2Status, UiTextCatalog.FromSafeMessage(value));
    }

    public string StatusMessage
    {
        get => Text.Render(_statusMessage);
        private set
        {
            SetProperty(ref _statusMessage, UiTextCatalog.FromSafeMessage(value));
            Notification.Show(!string.IsNullOrWhiteSpace(value));
        }
    }

    private readonly string _description;
    public string Description => Text.Render(_description);
    public StatusNotification Notification { get; } = new();

    public bool CanOpenLogFolder => LogDirectoryUri is not null;
    public AsyncCommand OpenLogFolderCommand { get; }

    internal void SetWebView2Status(WebView2RuntimeStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        WebView2Status = status.Available
            ? UiTextCatalog.Message("EvergreenVersion", status.Version ?? UiTextCatalog.Message("Available"))
            : UiTextCatalog.Message("NotDetectedInstallOrRepairFromMicrosoftS");
    }

    private Uri? LogDirectoryUri => _diagnostics.LogDirectory is { } directory
        ? new Uri(Path.EndsInDirectorySeparator(directory) ? directory : directory + Path.DirectorySeparatorChar)
        : null;

    private async Task OpenLogFolderAsync()
    {
        if (LogDirectoryUri is not { } uri)
            return;
        try
        {
            Directory.CreateDirectory(_diagnostics.LogDirectory!);
            await _openExternalUri(uri);
            StatusMessage = UiTextCatalog.Message("DiagnosticLogFolderOpened");
        }
        catch
        {
            StatusMessage = UiTextCatalog.Message("CouldNotOpenTheDiagnosticLogFolderApp");
        }
    }

    public void Dispose() => Notification.Dispose();
}
