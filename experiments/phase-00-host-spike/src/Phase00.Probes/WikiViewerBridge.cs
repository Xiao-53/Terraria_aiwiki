using System.Text.Json;
using SQLite;

namespace Phase00.Probes;

/// <summary>
/// The smallest behavior-equivalent bridge needed by the smoke test. It is a
/// characterization helper, not a replacement for the production service.
/// </summary>
public sealed class SqliteWikiViewerBridge
{
    private readonly string _databasePath;
    private readonly List<ViewerHistoryEntry> _history = [];
    private readonly object _historyGate = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    public SqliteWikiViewerBridge(string databasePath) => _databasePath = Path.GetFullPath(databasePath);

    public IReadOnlyList<ViewerHistoryEntry> History
    {
        get
        {
            lock (_historyGate) return _history.ToArray();
        }
    }
    public bool IsDarkTheme { get; private set; }

    public async Task<ViewerPage?> PageRedirectAsync(string title, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            using var connection = Open();
            var page = connection.Query<PageRow>("SELECT Title, Content, LastModified FROM WikiPage WHERE Title=? LIMIT 1", title).FirstOrDefault();
            if (page is null)
            {
                var redirect = connection.Query<RedirectRow>("SELECT ToTarget FROM WikiRedirect WHERE FromName=? LIMIT 1", title).FirstOrDefault();
                var target = redirect?.ToTarget?.Split('#', 2)[0];
                if (string.IsNullOrWhiteSpace(target)) return null;
                page = connection.Query<PageRow>("SELECT Title, Content, LastModified FROM WikiPage WHERE Title=? LIMIT 1", target).FirstOrDefault();
            }
            return page is null ? null : new ViewerPage(page.Title ?? string.Empty, page.Content ?? string.Empty, page.LastModified ?? string.Empty);
        }, cancellationToken);
    }

    public async Task<ViewerTitleAnchor> GetRedirectedTitleAndAnchorAsync(string input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        return await Task.Run(() =>
        {
            using var connection = Open();
            var value = input;
            if (!input.Contains('#', StringComparison.Ordinal))
            {
                var redirect = connection.Query<RedirectRow>("SELECT ToTarget FROM WikiRedirect WHERE FromName=? LIMIT 1", input).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(redirect?.ToTarget)) value = redirect.ToTarget!;
            }
            var split = value.Split('#', 2);
            return new ViewerTitleAnchor(split[0], split.Length == 2 ? split[1] : null);
        }, cancellationToken);
    }

    public Task SaveToTabHistoryAsync(string json, CancellationToken cancellationToken = default)
    {
        var entry = JsonSerializer.Deserialize<ViewerHistoryEntry>(json, JsonOptions)
            ?? throw new InvalidDataException("History message is null.");
        lock (_historyGate) _history.Add(entry);
        return Task.CompletedTask;
    }

    public void ChangeTheme(string isDarkTheme) => IsDarkTheme = string.Equals(isDarkTheme, "True", StringComparison.OrdinalIgnoreCase) || string.Equals(isDarkTheme, "true", StringComparison.OrdinalIgnoreCase);

    private SQLiteConnection Open() => new(_databasePath, SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex);

    private sealed class PageRow { public string? Title { get; set; } public string? Content { get; set; } public string? LastModified { get; set; } }
    private sealed class RedirectRow { public string? ToTarget { get; set; } }
}

public sealed record ViewerPage(string Title, string Content, string LastModified);
public sealed record ViewerTitleAnchor(string Title, string? Anchor);
public sealed record ViewerHistoryEntry(string? Title, double Position);
