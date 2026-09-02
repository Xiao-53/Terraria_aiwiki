using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Terraria_Wiki.Windows.Presentation.Favorites;

internal sealed partial class FavoritesView : UserControl
{
    public FavoritesView() => AvaloniaXamlLoader.Load(this);
}
