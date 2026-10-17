using System.Text;
using System.Text.Json;

namespace Terraria_Wiki.Agent;

internal static class WikiAgentArguments
{
    internal static void Validate(string name, IReadOnlyDictionary<string, object?> arguments)
    {
        try
        {
            var json = JsonSerializer.SerializeToElement(arguments);
            var allowed = name == "search_wiki" ? new[] { "query", "max_results" }
                : new[] { "title", "focus", "cursor", "mode", "continuation_token" };
            if (json.EnumerateObject().Any(property => !allowed.Contains(property.Name))) Invalid();
            var required = name == "search_wiki" ? "query" : "title";
            if (!json.TryGetProperty(required, out var text) || text.ValueKind != JsonValueKind.String
                || required == "title" && string.IsNullOrWhiteSpace(text.GetString())) Invalid();
            if (json.TryGetProperty("max_results", out var maximum) && maximum.ValueKind != JsonValueKind.Null
                && (!maximum.TryGetInt32(out var count) || count is < 1 or > 100)) Invalid();
            if (json.TryGetProperty("focus", out var focus) && focus.ValueKind != JsonValueKind.Null
                && (focus.ValueKind != JsonValueKind.String || focus.GetString()!.EnumerateRunes().Count() > 32768)) Invalid();
            if (json.TryGetProperty("cursor", out var cursor) && cursor.ValueKind != JsonValueKind.Null
                && (!cursor.TryGetInt32(out var offset) || offset < 0)) Invalid();
            if (json.TryGetProperty("mode", out var mode) && mode.ValueKind != JsonValueKind.Null
                && (mode.ValueKind != JsonValueKind.String || mode.GetString() is not ("page" or "section"))) Invalid();
            if (json.TryGetProperty("continuation_token", out var token) && token.ValueKind is not (JsonValueKind.Null or JsonValueKind.String)) Invalid();
        }
        catch (WikiToolArgumentsException) { throw; }
        catch { Invalid(); }
    }

    private static void Invalid() => throw new WikiToolArgumentsException(
        "Invalid tool arguments. Search needs a string query and optional max_results (1..100). Read needs a nonempty title; optional focus is at most 32768 characters, cursor is nonnegative, and mode is page or section. Correct the arguments and try again.");
}
