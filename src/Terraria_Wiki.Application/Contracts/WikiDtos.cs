using Terraria_Wiki.Core.Packages;
using Terraria_Wiki.Core.Query;

namespace Terraria_Wiki.Application;

public sealed record WikiBookSnapshot(
    int Id,
    string Title,
    string Description,
    bool IsPageDownloaded,
    bool IsResourceDownloaded,
    int PageCount,
    int ResourceCount,
    int RedirectCount,
    long DataSize,
    DateTime UpdateTime,
    string DataFolder,
    string DefaultPageTitle);

public sealed record WikiPageListItem(string Title, DateTime LastModified);

public sealed record WikiReadResponse(
    string RequestedTitle,
    string RequestedPageTitle,
    WikiReadStatus Status,
    string? Title,
    string? Content,
    DateTime? LastModified,
    string? RedirectFromTitle,
    string? RedirectTargetTitle,
    string? RedirectTargetAnchor,
    string? Anchor)
{
    public bool IsFound => Status == WikiReadStatus.Found;
    public bool IsRedirect => RedirectFromTitle is not null;
}

public sealed record WikiAssetContent(
    string FileName,
    byte[] Data,
    string MimeType,
    DateTime? LastModified);

public sealed record WikiFavoriteItem(string WikiTitle, DateTime FavoritedAt);

public sealed record GlobalWikiFavoriteItem(
    int WikiId,
    string WikiDisplayTitle,
    string PageTitle,
    DateTime FavoritedAt);

public sealed record GlobalWikiFavoriteIssue(int WikiId, string WikiDisplayTitle, string Code);

public sealed record GlobalWikiFavoritesResult(
    IReadOnlyList<GlobalWikiFavoriteItem> Items,
    IReadOnlyList<GlobalWikiFavoriteIssue> Issues);

public sealed record WikiPackageImportResponse(
    WikiBookSnapshot WikiBook,
    WikiPackageInspection Inspection,
    bool ReplacedExistingData,
    bool MigratedLegacyAnchors);

public sealed record WikiPackageExportResponse(
    string PackagePath,
    long PackageBytes,
    int MetadataBytes,
    long PayloadBytes);
