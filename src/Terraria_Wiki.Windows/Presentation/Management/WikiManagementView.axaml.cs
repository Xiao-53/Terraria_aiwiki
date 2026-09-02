using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Terraria_Wiki.Windows.Presentation.Management;

internal sealed partial class WikiManagementView : UserControl
{
    public WikiManagementView() => AvaloniaXamlLoader.Load(this);
}
