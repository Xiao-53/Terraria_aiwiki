using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace Terraria_Wiki.Windows.Presentation.Chat;

internal sealed partial class ChatView : UserControl
{
    public ChatView()
    {
        AvaloniaXamlLoader.Load(this);
        var questionBox = this.FindControl<TextBox>("QuestionBox")!;
        questionBox.AddHandler(
            InputElement.KeyDownEvent,
            OnQuestionKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private void OnQuestionKeyDown(object? sender, KeyEventArgs args)
    {
        var isComposing = sender is TextBox questionBox
                          && questionBox.GetVisualDescendants()
                              .OfType<TextPresenter>()
                              .Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText));
        if (!ChatInputBehavior.ShouldSend(args.Key, args.KeyModifiers, isComposing))
            return;
        if (DataContext is ChatViewModel viewModel && viewModel.SendCommand.CanExecute(null))
        {
            viewModel.SendCommand.Execute(null);
            args.Handled = true;
        }
    }
}
