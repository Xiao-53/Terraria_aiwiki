using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;

namespace Terraria_Wiki.Windows.Presentation.Reader;

internal sealed partial class ReaderView : UserControl
{
    public ReaderView()
    {
        AvaloniaXamlLoader.Load(this);
        WebView = this.FindControl<NativeWebView>("ViewerWebView")!;
        WebView.EnvironmentRequested += OnWebViewEnvironmentRequested;
    }

    public NativeWebView WebView { get; }

    internal static string GetDefaultUserDataFolder()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
            throw new InvalidOperationException("Windows LocalApplicationData is unavailable.");
        return Path.GetFullPath(Path.Combine(localAppData, "Terraria AI Wiki", "WebView2"));
    }

    private static void OnWebViewEnvironmentRequested(
        object? sender,
        WebViewEnvironmentRequestedEventArgs args)
    {
        if (args is WindowsWebView2EnvironmentRequestedEventArgs webView2)
            webView2.UserDataFolder = GetDefaultUserDataFolder();
    }
}
