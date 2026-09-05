using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;

namespace Terraria_Wiki.Windows.Presentation.Settings;

internal sealed class SecretTextBox : TextBox
{
    protected override Type StyleKeyOverride => typeof(TextBox);

    protected override AutomationPeer OnCreateAutomationPeer() =>
        new SecretTextBoxAutomationPeer(this);
}

internal sealed class SecretTextBoxAutomationPeer : ControlAutomationPeer, IValueProvider
{
    public SecretTextBoxAutomationPeer(SecretTextBox owner)
        : base(owner) => owner.PropertyChanged += OnOwnerPropertyChanged;

    private new SecretTextBox Owner => (SecretTextBox)base.Owner;

    public bool IsReadOnly => Owner.IsReadOnly;
    public string Value => Mask(Owner.Text);
    public void SetValue(string? value) => Owner.Text = value;

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Edit;
    protected override string? GetPlaceholderTextCore() => Owner.PlaceholderText;
    protected override IReadOnlyList<AutomationPeer>? GetChildrenCore() => null;

    private void OnOwnerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == TextBox.TextProperty)
        {
            RaisePropertyChangedEvent(
                ValuePatternIdentifiers.ValueProperty,
                Mask(args.OldValue as string),
                Mask(args.NewValue as string));
        }
    }

    private static string Mask(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : new string('●', value.Length);
}
