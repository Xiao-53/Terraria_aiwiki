using Avalonia.Input;

namespace Terraria_Wiki.Windows.Presentation.Chat;

internal static class ChatInputBehavior
{
    public static bool ShouldSend(Key key, KeyModifiers modifiers, bool isComposing = false) =>
        !isComposing && key == Key.Enter && !modifiers.HasFlag(KeyModifiers.Shift);
}
