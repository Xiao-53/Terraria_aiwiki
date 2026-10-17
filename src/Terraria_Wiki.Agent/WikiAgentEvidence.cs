using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModelContextProtocol.Protocol;

namespace Terraria_Wiki.Agent;

// Validation of the existing wire contract belongs here, not in Core. Only
// validated structured content is projected to the model; MCP's text copy is ignored.
internal sealed class WikiAgentEvidence
{
    private static readonly Regex Html = new(@"<(?:!doctype\b|/?[A-Za-z][^>]*)>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Version = new("^[A-F0-9]{64}$", RegexOptions.CultureInvariant);
    private readonly Dictionary<string, (string Id, int Batch)> _blocks = [];
    private readonly Dictionary<string, (string Id, int Batch)> _catalogs = [];
    private readonly HashSet<string> _sources = new(StringComparer.OrdinalIgnoreCase);
    internal List<WikiAgentSource> Sources { get; } = [];

    internal WikiAgentToolResult Process(string name, IReadOnlyDictionary<string, object?> arguments, CallToolResult result, int batch)
    {
        if (result.IsError == true)
        {
            if (result.Content.Count == 1 && result.Content[0] is TextContentBlock { Text: "invalid-arguments" })
                throw new WikiToolArgumentsException(name == "search_wiki"
                    ? "Invalid search_wiki arguments. Provide query as a string and omit max_results or use an integer from 1 through 100. Correct the arguments and try again."
                    : "Invalid read_wiki arguments. Provide a nonempty title and optional focus (at most 32768 characters). Use page or section mode; section needs a returned title#anchor. For continuation use the returned cursor/token with unchanged title/focus/mode. If expired, restart in page mode without the old cursor/token; do not combine content versions.");
            throw new WikiAgentException("wiki_tool_unavailable");
        }
        try
        {
            if (result.StructuredContent is not { } structured) Invalid();
            var data = JsonNode.Parse(result.StructuredContent!.Value.GetRawText())!.AsObject();
            return name == "search_wiki" ? Search(data) : Read(data, arguments, batch);
        }
        catch (WikiAgentException) { throw; }
        catch { throw new WikiAgentException("wiki_tool_unavailable"); }
    }

    private static WikiAgentToolResult Search(JsonObject data)
    {
        Shape(data, "results");
        var results = data["results"]!.AsArray();
        foreach (var node in results)
        {
            var item = node!.AsObject();
            Shape(item, "title redirect_target summary match_type highlights", "canonical_title match_scope matched_sections matched_sections_truncated");
            foreach (var key in new[] { "title", "redirect_target", "summary" }) Plain(Str(item, key));
            OneOf(Str(item, "match_type"), "exact_title", "exact_redirect", "title_prefix", "redirect_prefix", "title_contains", "redirect_contains", "body_fts");
            foreach (var range in item["highlights"]!.AsArray())
            {
                var highlight = range!.AsObject();
                Shape(highlight, "start length");
                Nonnegative(Int(highlight, "start")); Nonnegative(Int(highlight, "length"));
            }
            var nav = new[] { "canonical_title", "match_scope", "matched_sections", "matched_sections_truncated" };
            if (nav.Any(item.ContainsKey))
            {
                if (!nav.All(key => item[key] is not null)) Invalid();
                Plain(Str(item, "canonical_title"), 1);
                OneOf(Str(item, "match_scope"), "same_region", "same_section", "same_page", "partial");
                _ = Bool(item, "matched_sections_truncated");
                var sections = item["matched_sections"]!.AsArray();
                var seen = new HashSet<(string, string)>();
                var count = 0;
                if (sections.Count > 5) Invalid();
                foreach (var section in sections)
                {
                    var entry = section!.AsObject(); Shape(entry, "section_path anchor");
                    var path = Str(entry, "section_path"); var anchor = Str(entry, "anchor");
                    Plain(path); Plain(anchor);
                    count += Length(path) + Length(anchor);
                    if (!seen.Add((path, anchor))) Invalid();
                }
                if (count > 2048) Invalid();
            }
            item.Remove("highlights");
        }
        return new(data, "succeeded");
    }

    private WikiAgentToolResult Read(JsonObject data, IReadOnlyDictionary<string, object?> arguments, int batch)
    {
        Shape(data, "requested_title requested_page_title status is_found title content_format blocks included_characters original_characters is_complete available_sections last_modified is_redirect redirect_from_title redirect_target_title redirect_target_anchor anchor", "next_cursor scope");
        foreach (var key in new[] { "requested_title", "requested_page_title", "title", "redirect_from_title", "redirect_target_title", "redirect_target_anchor", "anchor" })
            if (data[key] is not null) Plain(Str(data, key));
        if (!arguments.TryGetValue("title", out var requested) || Str(data, "requested_title") != requested?.ToString()) Invalid();
        var status = Str(data, "status"); OneOf(status, "found", "not_found");
        var found = Bool(data, "is_found");
        var complete = Bool(data, "is_complete");
        _ = Bool(data, "is_redirect");
        var included = Int(data, "included_characters");
        Nonnegative(included); Nonnegative(Int(data, "original_characters"));
        if (included > 16384) Invalid();
        var cursor = data["next_cursor"]?.GetValue<int>();
        if (cursor < 0 || complete != (cursor is null)) Invalid();
        var blocks = data["blocks"]!.AsArray();
        var characters = 0;
        foreach (var value in blocks)
        {
            var block = value!.AsObject(); Shape(block, "section_path kind text");
            Plain(Str(block, "section_path"), 1, 4096); Plain(Str(block, "text"), 1, 16384);
            OneOf(Str(block, "kind"), "heading", "paragraph", "list_item", "definition", "table_row", "code");
            characters += Length(Str(block, "text"));
        }
        if (characters != included) Invalid();
        var available = data["available_sections"]!.AsArray();
        if (available.Count > 64 || available.Sum(value => Length(value!.GetValue<string>())) > 4096) Invalid();
        foreach (var section in available) Plain(section!.GetValue<string>(), 1);
        var scope = data["scope"]?.AsObject();
        var format = data["content_format"]?.GetValue<string>();
        if ((format == "wiki_blocks_v2") != (scope is not null)) Invalid();
        JsonArray catalog = available;
        if (scope is not null)
        {
            Shape(scope, "mode selected_section anchor_found page_complete content_version continuation_token sections");
            var mode = Str(scope, "mode"); OneOf(mode, "page", "section");
            var selected = scope["selected_section"]?.GetValue<string>();
            if (selected is not null) Plain(selected);
            var anchorFound = Bool(scope, "anchor_found");
            if (!Version.IsMatch(Str(scope, "content_version"))) Invalid();
            var token = scope["continuation_token"]?.GetValue<string>();
            if (token is not null && !Version.IsMatch(token)) Invalid();
            if (complete != (token is null) || Bool(scope, "page_complete") != (mode == "page" && complete)) Invalid();
            if (mode == "section" && (anchorFound != (selected is not null) || !anchorFound && (blocks.Count != 0 || !complete))) Invalid();
            if (mode == "page" && selected is not null) Invalid();
            catalog = scope["sections"]!.AsArray();
            var size = 0;
            if (catalog.Count > 64) Invalid();
            foreach (var node in catalog)
            {
                var entry = node!.AsObject(); Shape(entry, "path anchor");
                Plain(Str(entry, "path"), 1);
                var anchor = entry["anchor"]?.GetValue<string>();
                if (anchor is not null) Plain(anchor);
                size += Length(Str(entry, "path")) + Length(anchor ?? "");
            }
            if (size > 4096) Invalid();
        }
        DateTimeOffset? modified = data["last_modified"] is { } date ? DateTimeOffset.Parse(date.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture) : null;
        if (status == "not_found")
        {
            if (found || data["title"] is not null || format is not null || blocks.Count != 0 || included != 0
                || Int(data, "original_characters") != 0 || !complete || cursor is not null || available.Count != 0 || scope is not null) Invalid();
            return new(new JsonObject { ["status"] = "not_found", ["title"] = Str(data, "requested_title") }, "not_found");
        }
        if (!found || data["title"] is null || format is not ("wiki_blocks_v1" or "wiki_blocks_v2")
            || blocks.Count == 0 && !(scope is not null && Str(scope, "mode") == "section" && !Bool(scope, "anchor_found"))) Invalid();
        var expectedMode = arguments.GetValueOrDefault("mode")?.ToString();
        if (scope?["mode"]?.GetValue<string>() != expectedMode) Invalid();
        var title = Str(data, "title");
        var anchorValue = data["anchor"]?.GetValue<string>();
        if (blocks.Count != 0 && _sources.Add(title + "\0" + anchorValue))
            Sources.Add(new(Str(data, "requested_title"), title, anchorValue, modified));
        var version = scope?["content_version"]?.GetValue<string>() ?? modified?.ToString("O");
        var groups = new JsonArray();
        JsonObject? group = null;
        foreach (var node in blocks)
        {
            var block = node!.AsObject();
            var section = Str(block, "section_path");
            var key = JsonSerializer.Serialize(new[] { title, version, modified?.ToString("O"), section, Str(block, "kind"), Str(block, "text") });
            JsonObject projected;
            if (version is not null && _blocks.TryGetValue(key, out var previous) && previous.Batch < batch)
                projected = new() { ["ref"] = previous.Id };
            else
            {
                var id = _blocks.TryGetValue(key, out var existing) ? existing.Id : "e" + (_blocks.Count + 1);
                _blocks.TryAdd(key, (id, batch));
                projected = new() { ["id"] = id, ["kind"] = Str(block, "kind"), ["text"] = Str(block, "text") };
            }
            if (group?["section"]?.GetValue<string>() != section)
            {
                group = new() { ["section"] = section, ["blocks"] = new JsonArray() }; groups.Add(group);
            }
            group!["blocks"]!.AsArray().Add(projected);
        }
        var projection = new JsonObject
        {
            ["status"] = "found", ["title"] = title, ["anchor"] = anchorValue,
            ["evidence"] = groups, ["is_complete"] = complete, ["next_cursor"] = cursor,
        };
        if (scope is not null)
        {
            var projectedScope = (JsonObject)scope.DeepClone();
            projectedScope.Remove("content_version"); projectedScope.Remove("sections");
            projection["scope"] = projectedScope;
        }
        var catalogKey = JsonSerializer.Serialize(new[] { title, version, modified?.ToString("O"), catalog.ToJsonString() });
        if (version is not null && _catalogs.TryGetValue(catalogKey, out var prior) && prior.Batch < batch)
            projection["catalog_ref"] = prior.Id;
        else
        {
            projection["available_sections"] = catalog.DeepClone();
            if (catalog.Count != 0 && version is not null)
            {
                var id = _catalogs.TryGetValue(catalogKey, out var existing) ? existing.Id : "c" + (_catalogs.Count + 1);
                _catalogs.TryAdd(catalogKey, (id, batch)); projection["catalog_id"] = id;
            }
        }
        return new(projection, blocks.Count == 0 ? "not_found" : "succeeded");
    }

    private static int Length(string value) => value.EnumerateRunes().Count();
    private static void Plain(string value, int minimum = 0, int maximum = int.MaxValue)
    {
        if (Length(value) < minimum || Length(value) > maximum || Html.IsMatch(value)) Invalid();
    }
    private static string Str(JsonObject value, string key) => value[key]!.GetValue<string>();
    private static int Int(JsonObject value, string key) => value[key]!.GetValue<int>();
    private static bool Bool(JsonObject value, string key) => value[key]!.GetValue<bool>();
    private static void Nonnegative(int value) { if (value < 0) Invalid(); }
    private static void OneOf(string value, params string[] allowed) { if (!allowed.Contains(value)) Invalid(); }
    private static void Shape(JsonObject value, string required, string optional = "")
    {
        var keys = required.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var allowed = keys.Concat(optional.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToHashSet();
        if (!keys.All(value.ContainsKey) || value.Any(pair => !allowed.Contains(pair.Key))) Invalid();
    }
    private static void Invalid() => throw new WikiAgentException("wiki_tool_unavailable");
}
