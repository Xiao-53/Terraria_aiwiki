using Terraria_Wiki.Windows.Hosting;
using Terraria_Wiki.Windows.Presentation.Infrastructure;

namespace Terraria_Wiki.Windows.Presentation.Settings;

internal sealed class ProductDiagnosticsViewModel : ObservableObject
{
    private readonly ProductDiagnostics _diagnostics;
    private readonly Func<Uri, Task> _openExternalUri;
    private string _webView2Status = "等待启动检测";
    private string _statusMessage;

    internal ProductDiagnosticsViewModel(
        ProductDiagnostics diagnostics,
        Func<Uri, Task> openExternalUri)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _openExternalUri = openExternalUri ?? throw new ArgumentNullException(nameof(openExternalUri));
        _statusMessage = diagnostics.LogDirectory is null
            ? "本机诊断日志不可用；这不会影响离线 Wiki。"
            : "日志仅记录版本和生命周期事件，不记录密钥、问题内容、Wiki 页面或用户路径。";
        OpenLogFolderCommand = new AsyncCommand(
            _ => OpenLogFolderAsync(),
            _ => LogDirectoryUri is not null);
    }

    public string WindowsStatus => $"{Environment.OSVersion.Version} · x64";
    public string WebView2Status
    {
        get => _webView2Status;
        private set => SetProperty(ref _webView2Status, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool CanOpenLogFolder => LogDirectoryUri is not null;
    public AsyncCommand OpenLogFolderCommand { get; }

    internal void SetWebView2Status(WebView2RuntimeStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        WebView2Status = status.Available
            ? $"Evergreen {status.Version ?? "可用"}"
            : "未检测到；可从 Microsoft 官方页面安装或修复";
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
            StatusMessage = "已打开诊断日志文件夹。";
        }
        catch
        {
            StatusMessage = "无法打开诊断日志文件夹；应用功能不受影响。";
        }
    }
}
