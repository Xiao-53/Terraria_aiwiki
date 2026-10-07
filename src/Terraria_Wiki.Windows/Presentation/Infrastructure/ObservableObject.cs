global using Terraria_Wiki.Windows.Presentation.Localization;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Terraria_Wiki.Windows.Presentation.Infrastructure;

internal abstract class ObservableObject : INotifyPropertyChanged
{
    protected ObservableObject() => Text.Register(this);
    public UiTextCatalog Text { get; private set; } = new();

    public void UseTextCatalog(UiTextCatalog text)
    {
        if (ReferenceEquals(Text, text)) return;
        Text = text;
        Text.Register(this);
        OnTextCatalogChanged();
        RefreshLocalizedText();
    }

    protected virtual void OnTextCatalogChanged() { }
    internal void RefreshLocalizedText() => OnPropertyChanged(string.Empty);

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
