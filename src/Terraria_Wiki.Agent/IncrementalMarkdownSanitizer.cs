using System.Text;
using System.Text.RegularExpressions;

namespace Terraria_Wiki.Agent;

internal sealed class IncrementalMarkdownSanitizer
{
    private static readonly Regex Url = new(@"https?://[^\s<>()\]}]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private readonly StringBuilder _raw = new();
    internal string Output { get; private set; } = "";

    internal string Feed(string value)
    {
        _raw.Append(value);
        if (_raw.ToString().EnumerateRunes().Count() > 32768)
            throw new InvalidOperationException("AI response is too long.");
        return Publish(false);
    }

    internal string Finish() => Publish(true);

    private string Publish(bool final)
    {
        var raw = _raw.ToString();
        var end = raw.Length;
        if (!final)
        {
            var last = Url.Matches(raw).LastOrDefault();
            if (last is not null && last.Index + last.Length == end) end = last.Index;
            foreach (var scheme in new[] { "http://", "https://" })
                for (var length = 1; length < scheme.Length && length <= raw.Length; length++)
                    if (raw.EndsWith(scheme[..length], StringComparison.OrdinalIgnoreCase))
                        end = Math.Min(end, raw.Length - length);
            // Do not publish a dangling half of a surrogate pair.
            if (end > 0 && char.IsHighSurrogate(raw[end - 1])) end--;
        }
        var safe = Url.Replace(raw[..end], "[unverified-link-removed]").Trim();
        if (!safe.StartsWith(Output, StringComparison.Ordinal))
            throw new InvalidOperationException("AI stream is inconsistent.");
        var delta = safe[Output.Length..];
        Output = safe;
        return delta;
    }
}
