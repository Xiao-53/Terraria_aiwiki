using System.Collections;
using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Terraria_Wiki.Windows.Presentation.Infrastructure;

namespace Terraria_Wiki.Windows.Presentation.Localization;

// Only strings explicitly created here carry a message descriptor. Wiki/user/model
// strings are never translated, even when their content equals a UI resource.
internal sealed class UiTextCatalog
{
    internal static readonly ResourceManager Resources = new(
        "Terraria_Wiki.Windows.Presentation.Localization.UiStrings", typeof(UiTextCatalog).Assembly);
    private sealed record MessageData(string? Key, object?[] Arguments, string? Separator = null);
    private static readonly ConditionalWeakTable<string, MessageData> Messages = new();
    private static readonly Lazy<Dictionary<string, string>> SafeMessages = new(() =>
        NeutralEntries.Where(entry => !entry.Value.Contains('{'))
            .GroupBy(entry => entry.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Key, StringComparer.Ordinal));
    private readonly List<WeakReference<ObservableObject>> _models = [];
    private readonly List<WeakReference<IResourceDictionary>> _dictionaries = [];

    public string Language { get; private set; } = "zh-cn";
    public CultureInfo Culture => CultureInfo.GetCultureInfo(Language == "en-us" ? "en-US" : "zh-CN");
    public event EventHandler? LanguageChanged;

    internal static IEnumerable<KeyValuePair<string, string>> NeutralEntries =>
        Resources.GetResourceSet(CultureInfo.InvariantCulture, true, true)!
            .Cast<DictionaryEntry>().Select(entry => new KeyValuePair<string, string>(
                (string)entry.Key, (string)entry.Value!));

    public string Get(string key) => Resources.GetString(key, Culture)
        ?? Resources.GetString(key, CultureInfo.InvariantCulture) ?? "…";

    // Preserve key/arguments alongside the existing string-based Presentation APIs.
    // Weak keys keep progress messages bounded by their actual string lifetime.
    public static string Message(string key, params object?[] arguments) =>
        Remember(new MessageData(key, arguments));

    public static string Join(string separator, params string[] parts) =>
        Remember(new MessageData(null, parts.Cast<object?>().ToArray(), separator));

    public static string FromSafeMessage(string value)
    {
        if (Messages.TryGetValue(value, out _)) return value;
        if (SafeMessages.Value.TryGetValue(value, out var key)) return Message(key);
        // The host appends this fixed recovery sentence to trusted AI errors.
        // Translate only when the entire preceding safe summary is recognized.
        const string suffix = " 离线搜索和阅读仍可使用。";
        if (value.EndsWith(suffix, StringComparison.Ordinal)
            && SafeMessages.Value.TryGetValue(value[..^suffix.Length], out key))
            return Join("", Message(key), Message("OfflineSearchAndReadingRemainAvailable"));
        return value;
    }

    private static string Remember(MessageData message, CultureInfo? culture = null)
    {
        var value = new string(RenderMessage(message, culture ?? CultureInfo.GetCultureInfo("zh-CN")).AsSpan());
        if (value.Length == 0) return string.Empty;
        Messages.Add(value, message);
        return value;
    }

    public string Render(string value) => Messages.TryGetValue(value, out var message)
        ? Remember(message, Culture) : value;

    private static string RenderMessage(MessageData message, CultureInfo culture)
    {
        var arguments = message.Arguments.Select(argument => argument is string text
            && Messages.TryGetValue(text, out var nested) ? RenderMessage(nested, culture) : argument).ToArray();
        if (message.Key is null)
            return string.Join(message.Separator, arguments);
        var template = Resources.GetString(message.Key, culture)
            ?? Resources.GetString(message.Key, CultureInfo.InvariantCulture) ?? "…";
        return arguments.Length == 0 ? template : string.Format(culture, template, arguments);
    }

    internal void Register(ObservableObject model)
    {
        _models.RemoveAll(reference => !reference.TryGetTarget(out _));
        _models.Add(new WeakReference<ObservableObject>(model));
    }

    public void Attach(IResourceDictionary dictionary)
    {
        _dictionaries.RemoveAll(reference => !reference.TryGetTarget(out _));
        _dictionaries.Add(new WeakReference<IResourceDictionary>(dictionary));
        Populate(dictionary);
    }

    private void Populate(IResourceDictionary dictionary)
    {
        foreach (var entry in NeutralEntries)
            dictionary["Ui." + entry.Key] = Get(entry.Key);
    }

    public void SetLanguage(string language)
    {
        var normalized = language == "en-us" ? "en-us" : "zh-cn";
        if (Language == normalized) return;
        Language = normalized;
        foreach (var reference in _dictionaries.ToArray())
            if (reference.TryGetTarget(out var dictionary)) Populate(dictionary);
        foreach (var reference in _models.ToArray())
            if (reference.TryGetTarget(out var model) && ReferenceEquals(model.Text, this))
                model.RefreshLocalizedText();
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}

internal static class LocalizedModelExtensions
{
    public static T WithText<T>(this T model, UiTextCatalog text) where T : ObservableObject
    {
        model.UseTextCatalog(text);
        return model;
    }
}
