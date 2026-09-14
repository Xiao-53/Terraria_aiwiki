using Terraria_Wiki.Core.Packages;
using Terraria_Wiki.Core.Query;
using Terraria_Wiki.Models;

namespace Terraria_Wiki.Application.Internal;

internal static class ApplicationMapper
{
    public static WikiBookSnapshot ToSnapshot(WikiBook book) => new(
        book.Id,
        book.Title,
        book.Description,
        book.IsPageDownloaded,
        book.IsResourceDownloaded,
        book.PageCount,
        book.ResourceCount,
        book.RedirectCount,
        book.DataSize,
        book.UpdateTime,
        book.DataFolder,
        book.DefaultPageTitle);

    public static WikiReadResponse ToResponse(WikiReadResult result) => new(
        result.RequestedTitle,
        result.RequestedPageTitle,
        result.Status,
        result.Page?.Title,
        result.Page?.Content,
        result.Page?.LastModified,
        result.Redirect?.FromTitle,
        result.Redirect?.TargetTitle,
        result.Redirect?.TargetAnchor,
        result.Anchor);

    public static WikiDatabaseImportResponse ToResponse(WikiDatabaseImportResult result) => new(
        ToSnapshot(result.WikiBook),
        result.Inspection,
        result.ReplacedExistingData);

    public static WikiDatabaseExportResponse ToResponse(WikiDatabaseExportResult result) => new(
        result.DatabasePath,
        result.DatabaseBytes);
}
