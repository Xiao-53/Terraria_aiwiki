using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Terraria_Wiki.Windows.Presentation.Settings;

// NumericUpDown owns the template and spinner; only committed integers reach Value bindings.
internal sealed class IntegerSettingUpDown : NumericUpDown
{
    private TextBox? _editor;
    private bool _syncing;

    protected override Type StyleKeyOverride => typeof(NumericUpDown);

    public IntegerSettingUpDown()
    {
        ParsingNumberStyle = NumberStyles.None;
        Maximum = int.MaxValue;
        ClipValueToMinMax = false;
        AddHandler(TextInputEvent, OnIntegerTextInput, RoutingStrategies.Tunnel);
    }

    internal static bool IsIntegerDraft(string? text) =>
        text is not null && text.All(char.IsAsciiDigit);

    internal static bool TryCommit(string? text, decimal minimum, decimal maximum, out decimal value) =>
        decimal.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value)
        && IsIntegerDraft(text) && value >= minimum && value <= maximum && value <= int.MaxValue;

    private void OnIntegerTextInput(object? sender, TextInputEventArgs args)
    {
        if (!IsIntegerDraft(args.Text)) args.Handled = true;
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs args)
    {
        _syncing = true;
        try
        {
            base.OnApplyTemplate(args);
            _editor = args.NameScope.Find<TextBox>("PART_TextBox");
        }
        finally { _syncing = false; }
    }

    protected override void OnTextChanged(string? oldValue, string? newValue)
    {
        if (_syncing) return;
        if (IsIntegerDraft(newValue)) return;
        // Text property covers paste and IME commits as well as ordinary typing.
        _syncing = true;
        try
        {
            SetCurrentValue(TextProperty, oldValue ?? Value?.ToString("0", CultureInfo.InvariantCulture));
            if (_editor is not null) _editor.Text = Text;
        }
        finally { _syncing = false; }
    }

    protected override void OnValueChanged(decimal? oldValue, decimal? newValue)
    {
        _syncing = true;
        try { base.OnValueChanged(oldValue, newValue); }
        finally { _syncing = false; }
    }

    internal bool CommitDraft()
    {
        var valid = TryCommit(Text, Minimum, Maximum, out var value);
        _syncing = true;
        try
        {
            if (valid) SetCurrentValue(ValueProperty, value);
            SetCurrentValue(TextProperty, Value?.ToString("0", CultureInfo.InvariantCulture));
            if (_editor is not null) _editor.Text = Text;
        }
        finally { _syncing = false; }
        return valid;
    }

    protected override void OnKeyDown(KeyEventArgs args)
    {
        if (args.Key == Key.Enter) { CommitDraft(); args.Handled = true; }
        else base.OnKeyDown(args);
    }

    protected override void OnLostFocus(FocusChangedEventArgs args)
    {
        CommitDraft();
        base.OnLostFocus(args);
    }

    protected override void OnSpin(SpinEventArgs args)
    {
        if (CommitDraft()) base.OnSpin(args);
    }
}
