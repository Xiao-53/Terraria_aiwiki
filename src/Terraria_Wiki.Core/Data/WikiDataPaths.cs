using Terraria_Wiki.Models;

namespace Terraria_Wiki.Core.Data;

public sealed class WikiDataPaths
{
    private WikiDataPaths(string rootPath, string dataFolder)
    {
        RootPath = rootPath;
        DataFolder = dataFolder;
        ManagerDatabasePath = Path.Combine(rootPath, "Manager.db");
        DataDirectory = Path.Combine(rootPath, dataFolder);
        ContentDatabasePath = Path.Combine(DataDirectory, "data.db");
        ResourceListPath = Path.Combine(DataDirectory, "res.txt");
        TemporaryResourceListPath = Path.Combine(DataDirectory, "temp_res.txt");
        PageListPath = Path.Combine(DataDirectory, "pages.txt");
        FailedPageListPath = Path.Combine(DataDirectory, "failed_pages.txt");
        TemporaryFailedPageListPath = Path.Combine(DataDirectory, "temp_failed_pages.txt");
        FailedResourceListPath = Path.Combine(DataDirectory, "failed_res.txt");
        TemporaryFailedResourceListPath = Path.Combine(DataDirectory, "temp_failed_res.txt");
        UpdatePageListPath = Path.Combine(DataDirectory, "update_pages.txt");
        UpdateResourceListPath = Path.Combine(DataDirectory, "update_res.txt");
        StagingRoot = Path.Combine(rootPath, "Temp");
    }

    public string RootPath { get; }
    public string DataFolder { get; }
    public string ManagerDatabasePath { get; }
    public string DataDirectory { get; }
    public string ContentDatabasePath { get; }
    public string ResourceListPath { get; }
    public string TemporaryResourceListPath { get; }
    public string PageListPath { get; }
    public string FailedPageListPath { get; }
    public string TemporaryFailedPageListPath { get; }
    public string FailedResourceListPath { get; }
    public string TemporaryFailedResourceListPath { get; }
    public string UpdatePageListPath { get; }
    public string UpdateResourceListPath { get; }
    public string StagingRoot { get; }

    public static WikiDataPaths Create(string rootPath, WikiBook book)
    {
        ArgumentNullException.ThrowIfNull(book);
        if (string.IsNullOrWhiteSpace(rootPath)) throw new ArgumentException("A data root is required.", nameof(rootPath));
        if (!Path.IsPathFullyQualified(rootPath)) throw new ArgumentException("The data root must be absolute.", nameof(rootPath));

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var folder = ValidateDataFolder(book.DataFolder);
        var dataDirectory = Path.GetFullPath(Path.Combine(root, folder));
        var rootPrefix = root + Path.DirectorySeparatorChar;
        if (!dataDirectory.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The Wiki data folder escapes the supplied data root.", nameof(book));

        return new WikiDataPaths(root, folder);
    }

    public void EnsureDataDirectory() => Directory.CreateDirectory(DataDirectory);

    public string CreateUniqueStagingDirectory(string purpose)
    {
        if (string.IsNullOrWhiteSpace(purpose) || purpose.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch != '-'))
            throw new ArgumentException("The staging purpose must contain only ASCII letters, digits, or '-'.", nameof(purpose));

        Directory.CreateDirectory(StagingRoot);
        var path = Path.Combine(StagingRoot, $"{purpose}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ValidateDataFolder(string? dataFolder)
    {
        if (string.IsNullOrWhiteSpace(dataFolder)) throw new ArgumentException("WikiBook.DataFolder is required.", nameof(dataFolder));
        if (Path.IsPathFullyQualified(dataFolder)
            || dataFolder is "." or ".."
            || dataFolder.Contains(Path.DirectorySeparatorChar)
            || dataFolder.Contains(Path.AltDirectorySeparatorChar)
            || dataFolder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("WikiBook.DataFolder must be one safe relative path segment.", nameof(dataFolder));
        return dataFolder;
    }
}
