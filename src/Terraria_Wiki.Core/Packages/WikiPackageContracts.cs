using System.Text.Json.Serialization;

namespace Terraria_Wiki.Core.Packages;

public sealed record WikiPackageDescriptor(
    int Id,
    string Title,
    bool IsPageDownloaded,
    bool IsResourceDownloaded,
    DateTime UpdateTime,
    string? AppVersion);

public sealed record WikiPackageSourceFile(string RelativePath, string SourcePath);

public sealed class WikiPackageOptions
{
    public int MaxMetadataBytes { get; init; } = 16 * 1024 * 1024;
    public int MaxFileCount { get; init; } = 100_000;
    public long MaxFileBytes { get; init; } = 8L * 1024 * 1024 * 1024;
    public long MaxTotalBytes { get; init; } = 16L * 1024 * 1024 * 1024;
    public int CopyBufferBytes { get; init; } = 1024 * 1024;
    public bool RejectTrailingBytes { get; init; } = true;

    internal WikiPackageOptions Validate()
    {
        if (MaxMetadataBytes < 0) throw new ArgumentOutOfRangeException(nameof(MaxMetadataBytes));
        if (MaxFileCount < 0) throw new ArgumentOutOfRangeException(nameof(MaxFileCount));
        if (MaxFileBytes < 0) throw new ArgumentOutOfRangeException(nameof(MaxFileBytes));
        if (MaxTotalBytes < 0) throw new ArgumentOutOfRangeException(nameof(MaxTotalBytes));
        if (CopyBufferBytes is < 1 or > 4 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(CopyBufferBytes));
        return this;
    }
}

public sealed class WikiPackageException : Exception
{
    public WikiPackageException(string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;

    public string Code { get; }
}

public sealed record WikiPackageMetadata
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("isPageDownloaded")]
    public bool IsPageDownloaded { get; init; }

    [JsonPropertyName("isResourceDownloaded")]
    public bool IsResourceDownloaded { get; init; }

    [JsonPropertyName("updateTime")]
    public DateTime UpdateTime { get; init; }

    [JsonPropertyName("appVersion")]
    public string? AppVersion { get; init; }

    [JsonPropertyName("files")]
    public List<WikiPackageFileMetadata>? Files { get; init; }
}

public sealed record WikiPackageFileMetadata
{
    [JsonPropertyName("relativePath")]
    public string? RelativePath { get; init; }

    [JsonPropertyName("size")]
    public long Size { get; init; }

    // The legacy camel-case policy serializes MD5 as mD5.
    [JsonPropertyName("mD5")]
    public string? Md5 { get; init; }
}

public sealed record WikiPackageInspection(
    string PackagePath,
    long PackageBytes,
    int MetadataBytes,
    long PayloadBytes,
    string InspectionToken,
    WikiPackageMetadata Metadata);

public sealed record WikiPackageExtractedFile(
    string RelativePath,
    string FullPath,
    long Size,
    string Md5);

public sealed record WikiPackageExtractionResult(
    WikiPackageInspection Inspection,
    string StagingDirectory,
    IReadOnlyList<WikiPackageExtractedFile> Files);

public sealed record WikiPackageWriteResult(
    string PackagePath,
    long PackageBytes,
    int MetadataBytes,
    long PayloadBytes,
    WikiPackageMetadata Metadata);
