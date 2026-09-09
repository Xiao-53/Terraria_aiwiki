using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using LuYao.TlsClient;

return await TlsProbeProgram.RunAsync(args);

internal static class TlsProbeProgram
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task<int> RunAsync(string[] args)
    {
        var values = ParseOptions(args);
        if (!values.TryGetValue("url", out var urlText)
            || !Uri.TryCreate(urlText, UriKind.Absolute, out var url)
            || url.Scheme != Uri.UriSchemeHttps)
        {
            Console.Error.WriteLine("Required: --url <https-url>");
            return 2;
        }

        if (!values.TryGetValue("output", out var outputText) || string.IsNullOrWhiteSpace(outputText))
        {
            Console.Error.WriteLine("Required: --output <json-path>");
            return 2;
        }

        var outputPath = Path.GetFullPath(outputText);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var started = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = new TlsClient
            {
                TLSClientIdentifier = ClientIdentifiers.Chrome_131,
                FollowRedirect = true,
                Timeout = TimeSpan.FromSeconds(30)
            };
            var request = client.CreateRequest();
            request.RequestUrl = url.AbsoluteUri;
            request.RequestMethod = "GET";
            request.Headers["User-Agent"] = "TerrariaWikiScraper/1.0 (Phase 00 compatibility probe)";
            request.Headers["Accept"] = "text/html,application/json;q=0.9,*/*;q=0.8";

            var response = await Task.Run(() => client.Request(request));
            stopwatch.Stop();
            var bodyBytes = Encoding.UTF8.GetByteCount(response.Body ?? string.Empty);
            var success = response.Status is >= 200 and < 400 && bodyBytes > 0;
            var evidence = new TlsProbeEvidence(
                success,
                started,
                DateTimeOffset.UtcNow,
                stopwatch.ElapsedMilliseconds,
                Redact(url),
                response.Status,
                bodyBytes,
                typeof(TlsClient).Assembly.GetName().Version?.ToString(),
                Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
                null);
            await WriteEvidenceAsync(outputPath, evidence);
            Console.WriteLine($"TLS probe status={response.Status}, bodyBytes={bodyBytes}, output={outputPath}");
            return success ? 0 : 1;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var evidence = new TlsProbeEvidence(
                false,
                started,
                DateTimeOffset.UtcNow,
                stopwatch.ElapsedMilliseconds,
                Redact(url),
                null,
                0,
                typeof(TlsClient).Assembly.GetName().Version?.ToString(),
                Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
                new TlsProbeError(ex.GetType().Name, ex.Message));
            await WriteEvidenceAsync(outputPath, evidence);
            Console.Error.WriteLine($"TLS probe failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task WriteEvidenceAsync(string path, TlsProbeEvidence evidence) =>
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(evidence, JsonOptions));

    private static string Redact(Uri uri) => $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal)
                || index + 1 >= args.Length
                || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Invalid option near '{args[index]}'.");
            }
            result[args[index][2..]] = args[++index];
        }
        return result;
    }
}

internal sealed record TlsProbeEvidence(
    bool Success,
    DateTimeOffset StartedUtc,
    DateTimeOffset CompletedUtc,
    long ElapsedMilliseconds,
    string Url,
    int? Status,
    int BodyBytes,
    string? TlsClientAssemblyVersion,
    string? ProbeAssemblyVersion,
    TlsProbeError? Error);

internal sealed record TlsProbeError(string Type, string Message);
