using Terraria_Wiki.Models;

namespace Terraria_Wiki.Core.Packages;

public sealed class WikiDatabaseTransferException : Exception
{
    public WikiDatabaseTransferException(string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;

    public string Code { get; }
}

public sealed record WikiDatabaseInspection(
    string DatabasePath,
    long DatabaseBytes,
    string InspectionToken);

public sealed record WikiDatabaseExportResult(
    string DatabasePath,
    long DatabaseBytes);

public sealed record WikiDatabaseImportRequest(
    string DatabasePath,
    string DataRoot,
    int WikiId,
    string InspectionToken);

public sealed record WikiDatabaseImportResult(
    WikiBook WikiBook,
    WikiDatabaseInspection Inspection,
    bool ReplacedExistingData);
