using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Terraria_Wiki.Windows.Presentation.Reader;

internal sealed partial class ReaderView : UserControl
{
    public ReaderView()
    {
        AvaloniaXamlLoader.Load(this);
        WebView = this.FindControl<NativeWebView>("ViewerWebView")!;
    }

    public NativeWebView WebView { get; }
}
