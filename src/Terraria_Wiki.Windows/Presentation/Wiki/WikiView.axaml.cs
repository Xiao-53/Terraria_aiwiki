using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Terraria_Wiki.Windows.Presentation.Wiki;

internal sealed partial class WikiView : UserControl
{
    public WikiView() => AvaloniaXamlLoader.Load(this);
}
