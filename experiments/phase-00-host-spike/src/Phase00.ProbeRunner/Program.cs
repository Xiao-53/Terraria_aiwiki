using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Phase00.Probes;

return await Phase00Runner.RunAsync(args);

internal static class Phase00Runner
{
    private static readonly JsonSerializerOptions OutputJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintUsage();
            return args.Length == 0 ? 2 : 0;
        }

        var command = args[0].ToLowerInvariant();
        var values = ParseOptions(args[1..]);
        if (!values.TryGetValue("output", out var outputPath) || string.IsNullOrWhiteSpace(outputPath))
        {
            Console.Error.WriteLine("Missing required --output <path>.");
            PrintUsage();
            return 2;
        }

        var fullOutput = Path.GetFullPath(outputPath);
        try
        {
            ValidateOutputTarget(command, values, fullOutput);
        }
        catch (Exception ex)
        {
            // An unsafe output target must be rejected before the normal
            // failure-evidence path, otherwise writing that evidence could
            // itself overwrite the protected input or a SQLite sidecar.
            Console.Error.WriteLine($"Unsafe output target: {ex.Message}");
            return 2;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullOutput)!);
            var keepTemp = values.TryGetValue("keep-temp", out var keepText)
                && bool.TryParse(keepText, out var parsedKeep)
                && parsedKeep;
            if (command is "fixture")
                return await RunFixtureAsync(fullOutput, keepTemp);
            if (command is "package")
            {
                if (!values.TryGetValue("package", out var packagePath)) throw new ArgumentException("Missing required --package <path>.");
                return await RunPackageAsync(packagePath, fullOutput, keepTemp);
            }
            if (command is "database")
            {
                if (!values.TryGetValue("database", out var databasePath)) throw new ArgumentException("Missing required --database <path>.");
                return await RunDatabaseAsync(databasePath, fullOutput, keepTemp);
            }

            throw new ArgumentException($"Unknown command '{command}'.");
        }
        catch (Exception ex)
        {
            var failure = new RunnerEvidence(command, DateTimeOffset.UtcNow, false, null, null, null, new RunnerError(ex.GetType().Name, ex.Message, ex.StackTrace));
            await WriteJsonAsync(fullOutput, failure);
            Console.Error.WriteLine($"Phase 00 runner failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RunFixtureAsync(string outputPath, bool keepTemp)
    {
        var root = CreateTemporaryRoot("fixture");
        try
        {
            var databasePath = Path.Combine(root, "fixture.db");
            await SqliteProbe.CreateFixtureAsync(databasePath);
            var packagePath = Path.Combine(root, "fixture.pkg");
            await CreateFixturePackageAsync(packagePath);
            var staticRoot = await PrepareViewerStaticRootAsync(root);

            var sqliteWork = Path.Combine(root, "sqlite-probe");
            var sqlite = await SqliteProbe.ProbeAsync(databasePath, sqliteWork);
            var packageWork = Path.Combine(root, "package-staging");
            var package = await PackageOracle.ValidateAsync(packagePath, packageWork);
            var viewer = await ProbeViewerAsync(staticRoot, databasePath);
            var bridge = await ProbeBridgeAsync(databasePath);

            var evidence = new RunnerEvidence(
                "fixture",
                DateTimeOffset.UtcNow,
                sqlite.Success && package.SourceUnchanged && viewer.Success && bridge.Success,
                ToPackageEvidence(package),
                sqlite,
                viewer with { Bridge = bridge },
                null);
            await WriteJsonAsync(outputPath, evidence);
            Console.WriteLine($"Fixture evidence written to {outputPath}");
            return evidence.Success ? 0 : 1;
        }
        finally
        {
            if (!keepTemp) TryDeleteDirectory(root);
        }
    }

    private static async Task<int> RunPackageAsync(string packagePath, string outputPath, bool keepTemp)
    {
        var packageFull = Path.GetFullPath(packagePath);
        EnsureDifferentOutput(packageFull, outputPath);
        var root = CreateTemporaryRoot("package");
        try
        {
            var package = await PackageOracle.ValidateAsync(packageFull, Path.Combine(root, "staging"));
            var evidence = new RunnerEvidence("package", DateTimeOffset.UtcNow, package.SourceUnchanged, ToPackageEvidence(package), null, null, null);
            await WriteJsonAsync(outputPath, evidence);
            Console.WriteLine($"Package evidence written to {outputPath}");
            return 0;
        }
        finally
        {
            if (!keepTemp) TryDeleteDirectory(root);
        }
    }

    private static async Task<int> RunDatabaseAsync(string databasePath, string outputPath, bool keepTemp)
    {
        var databaseFull = Path.GetFullPath(databasePath);
        EnsureDatabaseOutputSafe(databaseFull, outputPath);
        var root = CreateTemporaryRoot("database");
        try
        {
            var sqlite = await SqliteProbe.ProbeAsync(databaseFull, Path.Combine(root, "sqlite-probe"));
            var evidence = new RunnerEvidence("database", DateTimeOffset.UtcNow, sqlite.Success, null, sqlite, null, null);
            await WriteJsonAsync(outputPath, evidence);
            Console.WriteLine($"Database evidence written to {outputPath}");
            return evidence.Success ? 0 : 1;
        }
        finally
        {
            if (!keepTemp) TryDeleteDirectory(root);
        }
    }

    private static async Task<ViewerEvidence> ProbeViewerAsync(string staticRoot, string databasePath)
    {
        var dataSource = new SqliteViewerDataSource(databasePath);
        await using var server = new ViewerServer(new ViewerServerOptions { StaticRoot = staticRoot, DataSource = dataSource });
        var started = await server.StartAsync();
        var second = await server.StartAsync();
        var samePort = started.Port == second.Port && second.Running;

        using var client = new HttpClient { BaseAddress = new Uri(started.Origin), Timeout = TimeSpan.FromSeconds(10) };
        var health = await GetStatusAsync(client, "/health");
        var staticPage = await GetStatusAsync(client, "/wiki/Terraria_Wiki_zh/index.html");
        var bridge = await GetStatusAsync(client, "/_common/iframe-bridge.js");
        var asset = await GetStatusAsync(client, "/src/fixture.png");
        var missing = await GetStatusAsync(client, "/not-found");
        var head = await HeadStatusAsync(client, "/health");
        var post = await MethodStatusAsync(client, HttpMethod.Post, "/health");
        var badHost = await MethodStatusAsync(client, HttpMethod.Get, "/health", "evil.example");
        var traversal = await GetStatusAsync(client, "/src/%2e%2e/secret");

        var conflictObserved = false;
        await using (var conflict = new ViewerServer(new ViewerServerOptions { StaticRoot = staticRoot, Port = started.Port }))
        {
            try { await conflict.StartAsync(); }
            catch (ViewerServerStartException) { conflictObserved = true; }
        }

        await server.StopAsync();
        var stopped = !server.IsRunning;
        var restarted = await server.StartAsync();
        await server.StopAsync();

        var success = samePort && conflictObserved && stopped && restarted.Running
            && health == 200 && staticPage == 200 && bridge == 200 && asset == 200
            && missing == 404 && head == 200 && post == 405 && badHost == 400 && traversal is 400 or 404;
        return new ViewerEvidence(success, started, second, health, staticPage, bridge, asset, missing, head, post, badHost, traversal, conflictObserved, stopped, restarted, null);
    }

    private static async Task<BridgeEvidence> ProbeBridgeAsync(string databasePath)
    {
        var bridge = new SqliteWikiViewerBridge(databasePath);
        var page = await bridge.PageRedirectAsync("Alias");
        var titleAnchor = await bridge.GetRedirectedTitleAndAnchorAsync("Alias");
        await bridge.SaveToTabHistoryAsync("{\"title\":\"Guide\",\"position\":12}");
        bridge.ChangeTheme("True");
        var success = page?.Title == "Redirected" && page.Content.Contains("fixture.png", StringComparison.Ordinal)
            && titleAnchor.Title == "Redirected" && titleAnchor.Anchor == "Section"
            && bridge.History.Count == 1 && bridge.History[0].Position == 12 && bridge.IsDarkTheme;
        return new BridgeEvidence(success, page, titleAnchor, bridge.History.ToArray(), bridge.IsDarkTheme);
    }

    private static async Task CreateFixturePackageAsync(string packagePath)
    {
        var payload = new SyntheticPackageFile[]
        {
            new("failed_pages.txt", Encoding.UTF8.GetBytes("none\n")),
            new("res.txt", Encoding.UTF8.GetBytes("fixture resource\n")),
            new("nested/data.bin", Enumerable.Range(0, 128).Select(i => (byte)i).ToArray())
        };
        await SyntheticPackageWriter.WriteAsync(packagePath, payload);
    }

    private static async Task<string> PrepareViewerStaticRootAsync(string tempRoot)
    {
        var source = FindRepositoryPath("terraria-wiki-offline/Terraria_Wiki/Resources/Raw/Web");
        var bridge = FindRepositoryPath("terraria-wiki-offline/Terraria_Wiki/wwwroot/js/iframe-bridge.js");
        if (source is null || bridge is null)
            throw new DirectoryNotFoundException("Existing Viewer assets could not be located.");
        var root = Path.Combine(tempRoot, "viewer");
        Directory.CreateDirectory(Path.Combine(root, "Web"));
        CopyDirectory(source, Path.Combine(root, "Web"));
        Directory.CreateDirectory(Path.Combine(root, "_common"));
        File.Copy(bridge, Path.Combine(root, "_common", "iframe-bridge.js"));
        var outer = FindRepositoryPath("experiments/phase-00-host-spike/assets/index.html")
            ?? throw new FileNotFoundException("PoC outer Viewer page not found.");
        File.Copy(outer, Path.Combine(root, "index.html"));
        return root;
    }

    private static string? FindRepositoryPath(string relativePath)
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate) || Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Unexpected argument: {args[i]}");
            var key = args[i][2..];
            if (string.IsNullOrWhiteSpace(key) || i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Option --{key} requires a value.");
            result[key] = args[++i];
        }
        return result;
    }

    private static async Task<int> GetStatusAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        return (int)response.StatusCode;
    }

    private static async Task<int> HeadStatusAsync(HttpClient client, string path)
    {
        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, path));
        return (int)response.StatusCode;
    }

    private static async Task<int> MethodStatusAsync(HttpClient client, HttpMethod method, string path, string? host = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (host is not null) request.Headers.Host = host;
        using var response = await client.SendAsync(request);
        return (int)response.StatusCode;
    }

    private static PackageEvidence ToPackageEvidence(PackageValidationResult package) => new(
        package.PackagePath,
        package.PackageBytes,
        package.SourceSha256Before,
        package.SourceSha256After,
        package.SourceUnchanged,
        package.MetadataBytes,
        package.Metadata.Id,
        package.Metadata.Title,
        package.Metadata.AppVersion,
        package.Metadata.Files?.Count ?? 0,
        package.PayloadBytes,
        package.Files.Select(x => new PackageFileEvidence(x.RelativePath, x.Size, x.Md5)).ToArray());

    private static string CreateTemporaryRoot(string name)
    {
        var root = Path.Combine(Path.GetTempPath(), $"phase00-host-spike-{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void EnsureDifferentOutput(string input, string output)
    {
        if (string.Equals(input, Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Input and output paths must differ.");
    }

    private static void EnsureDatabaseOutputSafe(string databasePath, string output)
    {
        var outputFull = Path.GetFullPath(output);
        foreach (var protectedPath in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
        {
            if (string.Equals(protectedPath, outputFull, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Output must not overwrite the source database or its WAL/SHM sidecars.");
        }
    }

    private static void ValidateOutputTarget(string command, IReadOnlyDictionary<string, string> values, string output)
    {
        if (command == "package" && values.TryGetValue("package", out var packagePath))
        {
            EnsureDifferentOutput(Path.GetFullPath(packagePath), output);
        }
        else if (command == "database" && values.TryGetValue("database", out var databasePath))
        {
            EnsureDatabaseOutputSafe(Path.GetFullPath(databasePath), output);
        }
    }

    private static async Task WriteJsonAsync(string outputPath, RunnerEvidence evidence)
    {
        var full = Path.GetFullPath(outputPath);
        var temp = full + $".phase00-{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(evidence, OutputJson), new UTF8Encoding(false));
        File.Move(temp, full, overwrite: true);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var output = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.Copy(file, output, overwrite: true);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { /* report is already written */ }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: Phase00.ProbeRunner <fixture|package|database> --output <json> [--package <pkg>] [--database <db>] [--keep-temp true]");
        Console.WriteLine("fixture  creates disposable SQLite/package fixtures and runs SQLite, package, Viewer and bridge probes");
        Console.WriteLine("package  streams and validates an explicit .pkg into a disposable temporary staging directory");
        Console.WriteLine("database opens an explicit database read-only, then exercises only a temporary copy");
    }
}

internal sealed record RunnerEvidence(string Mode, DateTimeOffset CompletedUtc, bool Success, PackageEvidence? Package, SqliteProbeResult? Sqlite, ViewerEvidence? Viewer, RunnerError? Error);
internal sealed record RunnerError(string Type, string Message, string? StackTrace);
internal sealed record PackageEvidence(string PackagePath, long PackageBytes, string SourceSha256Before, string SourceSha256After, bool SourceUnchanged, int MetadataBytes, int Id, string? Title, string? AppVersion, int FileCount, long PayloadBytes, IReadOnlyList<PackageFileEvidence> Files);
internal sealed record PackageFileEvidence(string RelativePath, long Size, string Md5);
internal sealed record ViewerEvidence(bool Success, ViewerServerStatus Started, ViewerServerStatus IdempotentStart, int HealthStatus, int StaticPageStatus, int BridgeStatus, int AssetStatus, int MissingStatus, int HeadStatus, int PostStatus, int BadHostStatus, int TraversalStatus, bool PortConflictObserved, bool Stopped, ViewerServerStatus Restarted, BridgeEvidence? Bridge);
internal sealed record BridgeEvidence(bool Success, ViewerPage? PageRedirect, ViewerTitleAnchor TitleAnchor, IReadOnlyList<ViewerHistoryEntry> History, bool DarkTheme);
