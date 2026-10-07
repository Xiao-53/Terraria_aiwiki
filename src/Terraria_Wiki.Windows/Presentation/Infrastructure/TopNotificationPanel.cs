using Avalonia;
using Avalonia.Controls;

namespace Terraria_Wiki.Windows.Presentation.Infrastructure;

// Shares the body's layout slot without contributing to its measured size.
internal sealed class TopNotificationPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        // Our desired size stays zero even when a message becomes visible or wraps.
        // Ensure those changes still arrange the children in the same body slot.
        InvalidateArrange();
        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
            child.Arrange(new Rect(0, 0, finalSize.Width, child.DesiredSize.Height));
        return finalSize;
    }
}
