using System.Text;

namespace Terraria_Wiki.Core.IO;

public static class WikiFileOperations
{
    public static bool HasMeaningfulContent(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return false;
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists || fileInfo.Length == 0) return false;
        if (fileInfo.Length > 1024) return true;

        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            int code;
            while ((code = reader.Read()) != -1)
            {
                var value = (char)code;
                if (!char.IsWhiteSpace(value) && value is not '\0' and not '\uFEFF') return true;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }

    public static async Task AppendFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath)) return;
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        await using var destination = new FileStream(destinationPath, FileMode.Append, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        await source.CopyToAsync(destination, cancellationToken);
    }

    public static int WriteDistinctLines(string inputPath, string outputPath)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var duplicateCount = 0;
        using var reader = new StreamReader(inputPath);
        using var writer = new StreamWriter(outputPath);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (seen.Add(line)) writer.WriteLine(line);
            else duplicateCount++;
        }
        return duplicateCount;
    }
}
