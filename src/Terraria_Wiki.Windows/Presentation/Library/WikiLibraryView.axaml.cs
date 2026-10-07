using Avalonia.Controls;
using Terraria_Wiki.Windows.Presentation.Infrastructure;

namespace Terraria_Wiki.Windows.Presentation.Library;

internal sealed partial class WikiLibraryView : UserControl
{
    private ConfirmationDialogHost? _confirmationHost;

    public WikiLibraryView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            _confirmationHost?.Dispose();
            _confirmationHost = null;
            AttachConfirmationHost();
        };
        AttachedToVisualTree += (_, _) => AttachConfirmationHost();
        DetachedFromVisualTree += (_, _) => { _confirmationHost?.Dispose(); _confirmationHost = null; };
    }

    private void AttachConfirmationHost()
    {
        if (_confirmationHost is not null || DataContext is not WikiLibraryViewModel model || TopLevel.GetTopLevel(this) is null) return;
        _confirmationHost = new ConfirmationDialogHost(this, model,
            () => model.IsDeleteConfirmationVisible || model.IsClearConfirmationVisible,
            () => model.IsDeleteConfirmationVisible
                ? (model.DeleteConfirmationText, model.Text.Get("DeletedConversationsCannotBeOpenedFromHistoryOr"), model.Text.Get("ConfirmDeletion"), true)
                : (model.Text.Get("ClearAllUnfavoritedConversations"), model.Text.Get("FavoriteConversationsWillBeKeptWikiPagesAnd"), model.Text.Get("ConfirmClearing2"), true),
            () => model.IsDeleteConfirmationVisible ? model.ConfirmDeleteCommand : model.ConfirmClearHistoryCommand,
            model.CancelClearHistory);
    }
}
