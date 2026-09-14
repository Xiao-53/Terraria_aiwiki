using System.Text.Json;

namespace Terraria_Wiki.Core.Query;

public sealed class WikiSearchAliasCatalog
{
    private const long MaxFileBytes = 256 * 1024;
    private const int MaxAliases = 2048;
    private readonly IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>> _aliases;

    private WikiSearchAliasCatalog(IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>> aliases) =>
        _aliases = aliases;

    public static WikiSearchAliasCatalog Empty { get; } = new(
        new Dictionary<int, IReadOnlyDictionary<string, string>>());

    public static WikiSearchAliasCatalog Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return Empty;
        try
        {
            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > MaxFileBytes) return Empty;
            using var stream = File.OpenRead(path);
            var document = JsonSerializer.Deserialize<AliasDocument>(stream, JsonOptions);
            if (document is null || document.Version != 1 || document.Wikis is null) return Empty;

            var count = 0;
            var result = new Dictionary<int, IReadOnlyDictionary<string, string>>();
            foreach (var wiki in document.Wikis)
            {
                if (wiki.WikiId <= 0 || wiki.Aliases is null) continue;
                var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var conflicts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var record in wiki.Aliases)
                {
                    if (++count > MaxAliases) return Empty;
                    var alias = WikiSearchText.Normalize(record.Alias);
                    var target = WikiSearchText.Normalize(record.TargetTitle);
                    if (alias.Length == 0 || target.Length == 0 || target.Contains('#')) continue;
                    if (aliases.TryGetValue(alias, out var existing)
                        && !string.Equals(existing, target, StringComparison.Ordinal))
                    {
                        aliases.Remove(alias);
                        conflicts.Add(alias);
                        continue;
                    }
                    if (!conflicts.Contains(alias)) aliases[alias] = target;
                }
                result[wiki.WikiId] = aliases;
            }
            return new WikiSearchAliasCatalog(result);
        }
        catch
        {
            return Empty;
        }
    }

    public bool TryResolve(int wikiId, string alias, out string target)
    {
        target = string.Empty;
        return _aliases.TryGetValue(wikiId, out var aliases)
            && aliases.TryGetValue(WikiSearchText.Normalize(alias), out target!);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private sealed class AliasDocument
    {
        public int Version { get; set; }
        public List<WikiAliases>? Wikis { get; set; }
    }

    private sealed class WikiAliases
    {
        public int WikiId { get; set; }
        public List<AliasRecord>? Aliases { get; set; }
    }

    private sealed class AliasRecord
    {
        public string? Alias { get; set; }
        public string? TargetTitle { get; set; }
    }
}
