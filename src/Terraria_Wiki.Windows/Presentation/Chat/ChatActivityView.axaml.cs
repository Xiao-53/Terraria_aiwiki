using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Terraria_Wiki.Windows.Presentation.Chat;

internal sealed partial class ChatActivityView : UserControl
{
    public ChatActivityView() => AvaloniaXamlLoader.Load(this);
}
