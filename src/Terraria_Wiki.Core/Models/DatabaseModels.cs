using SQLite;
using Terraria_Wiki.Services;

namespace Terraria_Wiki.Models;

public class WikiBook
{
    [PrimaryKey]
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsPageDownloaded { get; set; }
    public bool IsResourceDownloaded { get; set; }
    public int PageCount { get; set; }
    public int ResourceCount { get; set; }
    public int RedirectCount { get; set; }
    public long DataSize { get; set; }
    public DateTime UpdateTime { get; set; }
    public string ApiBaseUrl { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string RedirectListUrl { get; set; } = string.Empty;
    public int MainNamespace { get; set; }
    public string AdditionalNamespaces { get; set; } = string.Empty;
    public string JunkXPath { get; set; } = string.Empty;
    public string DataFolder { get; set; } = string.Empty;
    public string DefaultPageContent { get; set; } = string.Empty;
    public string DefaultPageTitle { get; set; } = string.Empty;
}

public class WikiPage
{
    [PrimaryKey]
    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
}

public class WikiRedirect
{
    [PrimaryKey]
    public string FromName { get; set; } = string.Empty;

    [Indexed]
    public string ToTarget { get; set; } = string.Empty;
}

public class WikiHistory
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public string WikiTitle { get; set; } = string.Empty;

    public DateTime ReadAt { get; set; }

    [Indexed]
    public string DateKey { get; set; } = string.Empty;
}

public class WikiFavorite
{
    [PrimaryKey]
    public string WikiTitle { get; set; } = string.Empty;

    public DateTime FavoritedAt { get; set; }
}

public class WikiAsset
{
    [PrimaryKey]
    public string FileName { get; set; } = string.Empty;

    public byte[] Data { get; set; } = [];
    public string MimeType { get; set; } = string.Empty;
    public DateTime? LastModified { get; set; } = DateTime.MinValue;
}

public class ManagerDbService : DatabaseService
{
    public ManagerDbService(string dbPath)
        : base(dbPath, DbMode.Manager)
    {
    }
}

public class ContentDbService : DatabaseService
{
    public ContentDbService(string dbPath)
        : base(dbPath, DbMode.Content)
    {
    }
}
