using System.Globalization;
using System.Text;

namespace Terraria_Wiki.Core.Query;

internal static class WikiSearchText
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return string.Join(' ', value.Normalize(NormalizationForm.FormKC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public static IReadOnlyList<string> SplitConcepts(string? value) =>
        Normalize(value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static string EncodeForIndex(string? value) =>
        string.Join(' ', EncodeTokens(value));

    public static string EncodePhrase(string? value)
    {
        var encoded = EncodeForIndex(value);
        return encoded.Length == 0 ? string.Empty : $"\"{encoded}\"";
    }

    private static IEnumerable<string> EncodeTokens(string? value)
    {
        var normalized = Normalize(value).ToLowerInvariant();
        if (normalized.Length == 0) yield break;

        var word = new StringBuilder();
        foreach (var rune in normalized.EnumerateRunes())
        {
            if (IsHan(rune))
            {
                foreach (var token in FlushWord(word)) yield return token;
                yield return $"h{rune.Value:x}";
                continue;
            }

            if (Rune.IsLetterOrDigit(rune))
            {
                word.Append(rune.ToString());
                continue;
            }

            foreach (var token in FlushWord(word)) yield return token;
            if (!Rune.IsWhiteSpace(rune) && !Rune.IsControl(rune))
                yield return $"p{rune.Value:x}";
        }

        foreach (var token in FlushWord(word)) yield return token;
    }

    private static IEnumerable<string> FlushWord(StringBuilder word)
    {
        if (word.Length == 0) yield break;
        var bytes = Encoding.UTF8.GetBytes(word.ToString());
        word.Clear();
        yield return "w" + Convert.ToHexStringLower(bytes);
    }

    private static bool IsHan(Rune rune) => rune.Value is
        >= 0x3400 and <= 0x4DBF or
        >= 0x4E00 and <= 0x9FFF or
        >= 0xF900 and <= 0xFAFF or
        >= 0x20000 and <= 0x2FA1F;
}
