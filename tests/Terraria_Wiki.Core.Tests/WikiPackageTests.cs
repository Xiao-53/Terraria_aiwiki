using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Terraria_Wiki.Core.Packages;

namespace Terraria_Wiki.Core.Tests;

public sealed class WikiPackageTests
{
    [Fact]
    public async Task WriterProducesLegacyShapeAndReaderRoundTripsPayloadOrder()
    {
        using var temp = new TestDirectory();
        var first = temp.File("failed_pages.txt");
        var second = temp.File("nested.bin");
        await File.WriteAllTextAsync(first, "none\n");
        await File.WriteAllBytesAsync(second, Enumerable.Range(0, 32).Select(value => (byte)value).ToArray());
        var package = temp.File("output.pkg");

        var written = await WikiPackageWriter.WriteAsync(
            package,
            new WikiPackageDescriptor(1, "泰拉瑞亚中文百科", true, true, new DateTime(2026, 8, 24), "0.3.0.3"),
            [
                new WikiPackageSourceFile("failed_pages.txt", first),
                new WikiPackageSourceFile("nested/data.bin", second),
            ]);
        var inspection = await WikiPackageReader.InspectAsync(package);
        var extracted = await WikiPackageReader.ExtractAsync(package, temp.File("staging"), inspection.InspectionToken);

        Assert.Equal("WIKIDATA", Encoding.ASCII.GetString((await File.ReadAllBytesAsync(package))[..8]));
        Assert.Equal(written.PackageBytes, inspection.PackageBytes);
        Assert.Equal(["failed_pages.txt", "nested/data.bin"], inspection.Metadata.Files!.Select(file => file.RelativePath));
        Assert.Equal("none\n", await File.ReadAllTextAsync(extracted.Files[0].FullPath));
        Assert.Equal(await File.ReadAllBytesAsync(second), await File.ReadAllBytesAsync(extracted.Files[1].FullPath));

        var metadataJson = await ReadMetadataJsonAsync(package);
        Assert.Contains("\"mD5\"", metadataJson, StringComparison.Ordinal);
        Assert.Contains("泰拉瑞亚中文百科", metadataJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u6cf0", metadataJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InspectIsReadOnlyAndExtractionRejectsChangedInspectionToken()
    {
        using var temp = new TestDirectory();
        var package = temp.File("valid.pkg");
        await WriteRawPackageAsync(package, [("data.db", new byte[] { 1, 2, 3 })]);
        var before = await Sha256Async(package);

        var inspection = await WikiPackageReader.InspectAsync(package);
        var after = await Sha256Async(package);
        var exception = await Assert.ThrowsAsync<WikiPackageException>(() =>
            WikiPackageReader.ExtractAsync(package, temp.File("stage"), new string('0', 64)));

        Assert.Equal(before, after);
        Assert.Equal("package-changed", exception.Code);
        Assert.False(Directory.Exists(temp.File("stage")));
        Assert.Equal(3, inspection.PayloadBytes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/absolute.txt")]
    [InlineData("\\\\server\\share\\file.txt")]
    [InlineData("//server/share/file.txt")]
    [InlineData("C:/file.txt")]
    [InlineData("../escape.txt")]
    [InlineData("nested/../../escape.txt")]
    [InlineData("nested/./file.txt")]
    [InlineData("nested//file.txt")]
    public async Task RejectsUnsafePathsBeforeCreatingStaging(string relativePath)
    {
        using var temp = new TestDirectory();
        var package = temp.File("unsafe.pkg");
        await WriteRawPackageAsync(package, [(relativePath, new byte[] { 1 })]);

        var exception = await Assert.ThrowsAsync<WikiPackageException>(() => WikiPackageReader.InspectAsync(package));

        Assert.Contains(exception.Code, new[] { "empty-path", "absolute-path", "drive-path", "invalid-path", "path-escape" });
        Assert.False(File.Exists(temp.File("escape.txt")));
    }

    [Fact]
    public async Task RejectsCaseInsensitiveDuplicatesAndFileDirectoryConflicts()
    {
        using var temp = new TestDirectory();
        var duplicate = temp.File("duplicate.pkg");
        var conflict = temp.File("conflict.pkg");
        await WriteRawPackageAsync(duplicate, [("A.txt", new byte[] { 1 }), ("a.TXT", new byte[] { 2 })]);
        await WriteRawPackageAsync(conflict, [("a", new byte[] { 1 }), ("a/b", new byte[] { 2 })]);

        Assert.Equal("duplicate-path", (await Assert.ThrowsAsync<WikiPackageException>(() => WikiPackageReader.InspectAsync(duplicate))).Code);
        Assert.Equal("path-conflict", (await Assert.ThrowsAsync<WikiPackageException>(() => WikiPackageReader.InspectAsync(conflict))).Code);
    }

    [Fact]
    public async Task RejectsInvalidLengthsHashTruncationAndTrailingData()
    {
        using var temp = new TestDirectory();
        var negative = temp.File("negative.pkg");
        var badHash = temp.File("hash.pkg");
        var truncated = temp.File("truncated.pkg");
        var trailing = temp.File("trailing.pkg");
        await WriteRawPackageAsync(negative, [("x", new byte[] { 1 })], sizeOverride: -1);
        await WriteRawPackageAsync(badHash, [("x", new byte[] { 1 })], md5Override: new string('0', 32));
        await WriteRawPackageAsync(truncated, [("x", new byte[] { 1, 2, 3 })], truncatePayload: true);
        await WriteRawPackageAsync(trailing, [("x", new byte[] { 1 })], trailing: new byte[] { 9 });

        Assert.Equal("negative-file-size", (await Assert.ThrowsAsync<WikiPackageException>(() => WikiPackageReader.InspectAsync(negative))).Code);
        Assert.Equal("hash-mismatch", (await Assert.ThrowsAsync<WikiPackageException>(() => WikiPackageReader.ExtractAsync(badHash, temp.File("hash-stage")))).Code);
        Assert.Equal("truncated-payload", (await Assert.ThrowsAsync<WikiPackageException>(() => WikiPackageReader.InspectAsync(truncated))).Code);
        Assert.Equal("trailing-data", (await Assert.ThrowsAsync<WikiPackageException>(() => WikiPackageReader.InspectAsync(trailing))).Code);
    }

    [Fact]
    public async Task OptionalTrailingByteCompatibilityStopsExtractionAtDeclaredBoundary()
    {
        using var temp = new TestDirectory();
        var package = temp.File("trailing-compatible.pkg");
        await WriteRawPackageAsync(package, [("x", new byte[] { 1 })], trailing: new byte[] { 9 });
        var options = new WikiPackageOptions { RejectTrailingBytes = false };

        var result = await WikiPackageReader.ExtractAsync(package, temp.File("stage"), options: options);

        Assert.Equal(new byte[] { 1 }, await File.ReadAllBytesAsync(result.Files.Single().FullPath));
    }

    [Fact]
    public async Task EnforcesMetadataCountAndTotalLimitsBeforeExtraction()
    {
        using var temp = new TestDirectory();
        var package = temp.File("limits.pkg");
        await WriteRawPackageAsync(package, [("x", new byte[] { 1, 2, 3 })]);

        var count = await Assert.ThrowsAsync<WikiPackageException>(() =>
            WikiPackageReader.InspectAsync(package, new WikiPackageOptions { MaxFileCount = 0 }));
        var total = await Assert.ThrowsAsync<WikiPackageException>(() =>
            WikiPackageReader.InspectAsync(package, new WikiPackageOptions { MaxTotalBytes = 2 }));

        Assert.Equal("file-count-too-large", count.Code);
        Assert.Equal("total-size-too-large", total.Code);
    }

    [Fact]
    public async Task RejectsMalformedMetadataLengthsAndJson()
    {
        using var temp = new TestDirectory();
        var negative = temp.File("negative-metadata.pkg");
        var truncated = temp.File("truncated-metadata.pkg");
        var invalid = temp.File("invalid-json.pkg");
        await File.WriteAllBytesAsync(negative, HeaderWithLength(-1));
        await File.WriteAllBytesAsync(truncated, HeaderWithLength(17));
        await WritePackageBytesAsync(invalid, "{"u8.ToArray(), []);

        Assert.Equal("negative-metadata-length", (await Assert.ThrowsAsync<WikiPackageException>(() => WikiPackageReader.InspectAsync(negative))).Code);
        Assert.Equal("truncated-metadata", (await Assert.ThrowsAsync<WikiPackageException>(() => WikiPackageReader.InspectAsync(truncated))).Code);
        Assert.Equal("invalid-json", (await Assert.ThrowsAsync<WikiPackageException>(() => WikiPackageReader.InspectAsync(invalid))).Code);
    }

    [Fact]
    public async Task RefusesNonEmptyStagingAndCleansCancelledOwnedStaging()
    {
        using var temp = new TestDirectory();
        var package = temp.File("valid.pkg");
        await WriteRawPackageAsync(package, [("x", new byte[] { 1 })]);
        var nonEmpty = temp.File("non-empty");
        Directory.CreateDirectory(nonEmpty);
        await File.WriteAllTextAsync(Path.Combine(nonEmpty, "keep.txt"), "keep");

        var nonEmptyError = await Assert.ThrowsAsync<WikiPackageException>(() => WikiPackageReader.ExtractAsync(package, nonEmpty));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            WikiPackageReader.ExtractAsync(package, temp.File("cancelled-stage"), cancellationToken: cancellation.Token));

        Assert.Equal("staging-not-empty", nonEmptyError.Code);
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(nonEmpty, "keep.txt")));
        Assert.False(Directory.Exists(temp.File("cancelled-stage")));
    }

    [Fact]
    public async Task WriterRejectsDuplicatePathsAndExistingOutputWithoutChangingIt()
    {
        using var temp = new TestDirectory();
        var first = temp.File("first.bin");
        var second = temp.File("second.bin");
        await File.WriteAllBytesAsync(first, new byte[] { 1 });
        await File.WriteAllBytesAsync(second, new byte[] { 2 });
        var descriptor = new WikiPackageDescriptor(1, "Fixture", true, true, DateTime.UnixEpoch, "1.0");

        var duplicate = await Assert.ThrowsAsync<WikiPackageException>(() => WikiPackageWriter.WriteAsync(
            temp.File("duplicate-output.pkg"),
            descriptor,
            [new("A", first), new("a", second)]));
        var existing = temp.File("existing.pkg");
        await File.WriteAllTextAsync(existing, "keep");
        var existingError = await Assert.ThrowsAsync<WikiPackageException>(() => WikiPackageWriter.WriteAsync(
            existing,
            descriptor,
            [new("first", first)]));

        Assert.Equal("duplicate-path", duplicate.Code);
        Assert.Equal("output-exists", existingError.Code);
        Assert.Equal("keep", await File.ReadAllTextAsync(existing));
    }

    private static async Task<string> ReadMetadataJsonAsync(string packagePath)
    {
        await using var stream = File.OpenRead(packagePath);
        stream.Position = 8;
        var lengthBytes = new byte[4];
        await stream.ReadExactlyAsync(lengthBytes);
        var metadata = new byte[BinaryPrimitives.ReadInt32LittleEndian(lengthBytes)];
        await stream.ReadExactlyAsync(metadata);
        return Encoding.UTF8.GetString(metadata);
    }

    private static async Task WriteRawPackageAsync(
        string path,
        IReadOnlyList<(string RelativePath, byte[] Data)> files,
        long? sizeOverride = null,
        string? md5Override = null,
        bool truncatePayload = false,
        byte[]? trailing = null)
    {
        var entries = files.Select((file, index) => new
        {
            relativePath = file.RelativePath,
            size = index == 0 && sizeOverride.HasValue ? sizeOverride.Value : file.Data.LongLength,
            mD5 = index == 0 && md5Override is not null ? md5Override : Md5(file.Data),
        }).ToArray();
        var metadata = JsonSerializer.SerializeToUtf8Bytes(new
        {
            id = 1,
            title = "Synthetic",
            isPageDownloaded = true,
            isResourceDownloaded = true,
            updateTime = "2026-08-24T00:00:00Z",
            appVersion = "0.3.0.3",
            files = entries,
        });

        var payload = files.SelectMany(file => file.Data).ToArray();
        if (truncatePayload && payload.Length > 0) payload = payload[..^1];
        await WritePackageBytesAsync(path, metadata, payload, trailing);
    }

    private static async Task WritePackageBytesAsync(string path, byte[] metadata, byte[] payload, byte[]? trailing = null)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await stream.WriteAsync("WIKIDATA"u8.ToArray());
        var length = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, metadata.Length);
        await stream.WriteAsync(length);
        await stream.WriteAsync(metadata);
        await stream.WriteAsync(payload);
        if (trailing is not null) await stream.WriteAsync(trailing);
    }

    private static byte[] HeaderWithLength(int length)
    {
        var result = new byte[12];
        "WIKIDATA"u8.CopyTo(result);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(8), length);
        return result;
    }

    private static string Md5(byte[] bytes) =>
        Convert.ToHexString(System.Security.Cryptography.MD5.HashData(bytes)).ToLowerInvariant();

    private static async Task<string> Sha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
    }
}
