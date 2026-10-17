using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Threading.Channels;

namespace Terraria_Wiki.Windows.Hosting;

// Opt-in investigation only. Uses the existing bounded product log backend.
internal enum StartupInvestigationPhase
{
    ManagedEntry, Velopack, SingleInstanceActivation, AvaloniaBuilder,
    FrameworkInitializationStarted, ApplicationXaml, FrameworkInitialized,
    MainWindowConstructor, MainWindowXaml, ShellViewConstructor,
    ChatViewConstructor, ReaderViewConstructor, ProductDiagnosticsInitialization,
    ConversationStore, HostSettingsStore, ShellViewModel, MarkdownPipeline,
    ShellDataContext, WindowOpened, QuickHostInitialization, QuickPreferences,
    TrayInitialization, HotkeyInitialization, RuntimeCreation, ThemeApplication,
    RuntimeAttachment, ActiveWiki, AiDispatch, AiAttempt, AiGateWait,
    AiConfiguration, AiConfigurationDocument, AiCredentialProbe, AiCredentialRead,
    AiPreviousRuntimeDispose, WikiTools, AgentInitialization,
    ReadyValidated,
    HandshakeTimeout, AiStarting, AiReady, AiDisabled, AiFailed, AiStopped,
    AiUiStarting, AiUiReady, AiUiDisabled, AiUiFailed, AiUiStopped,

    ClientLifetimeEnded,
}

internal sealed record StartupInvestigationEntry(
    DateTimeOffset TimestampUtc, string Event, int DiagnosticVersion, string RunId, int ProcessId,
    string BuildVersion, string DotnetVersion, long ManagedEntryMilliseconds,
    string Stage, string Kind, string Clock, long ElapsedMilliseconds,
    long DurationMilliseconds, int AiAttemptId, long OperationId);

internal static class WindowsStartupInvestigation
{
    private const int MaximumEntries = 512;
    private static readonly AsyncLocal<int> AiAttempt = new();
    private static Channel<StartupInvestigationEntry>? _entries;
    private static Stopwatch? _clock;
    private static ProductDiagnostics? _diagnostics;
    private static Task? _writer;
    private static string _runId = string.Empty;
    private static string _buildVersion = string.Empty;
    private static int _attemptCount;
    private static int _entryCount;
    private static long _operationCount;

    internal static bool Enabled => _entries is not null;
    internal static int CurrentAiAttempt => AiAttempt.Value;

    internal static void Configure(string[] arguments)
    {
        if (!arguments.Contains("--startup-diagnostics", StringComparer.Ordinal))
            return;
        _entryCount = 0;
        _attemptCount = 0;
        _operationCount = 0;
        _writer = null;
        _diagnostics = null;
        AiAttempt.Value = 0;
        _clock = Stopwatch.StartNew();
        _runId = Guid.NewGuid().ToString("N");
        _buildVersion = typeof(WindowsStartupInvestigation).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        _entries = Channel.CreateBounded<StartupInvestigationEntry>(new BoundedChannelOptions(MaximumEntries)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropWrite,
        });
        Checkpoint(StartupInvestigationPhase.ManagedEntry);
    }

    internal static void Attach(ProductDiagnostics diagnostics) => _diagnostics = diagnostics;

    internal static void StartPersistence()
    {
        if (_entries is not { } entries || _diagnostics is not { } diagnostics || _writer is not null)
            return;
        _writer = Task.Run(async () =>
        {
            try
            {
                await foreach (var entry in entries.Reader.ReadAllAsync())
                    diagnostics.WriteInvestigation(entry);
            }
            catch { /* Investigation must not affect the client. */ }
        });
    }

    internal static Measurement Measure(StartupInvestigationPhase phase)
    {
        if (!Enabled) return default;
        var operation = Interlocked.Increment(ref _operationCount);
        var attempt = CurrentAiAttempt;
        Record(phase.ToString(), "Started", "HostMonotonic", 0, attempt, operation);
        return new Measurement(phase, Stopwatch.GetTimestamp(), attempt, operation);
    }

    internal static Attempt BeginAiAttempt()
    {
        if (!Enabled) return default;
        var previous = AiAttempt.Value;
        AiAttempt.Value = Interlocked.Increment(ref _attemptCount);
        return new Attempt(previous, true);
    }

    internal static void Checkpoint(StartupInvestigationPhase phase, int? attempt = null)
    {
        if (!Enabled) return;
        Record(phase.ToString(), "Checkpoint", "HostMonotonic", 0, attempt ?? CurrentAiAttempt, 0);
    }

    internal static void ExistingStage(WindowsStartupStage stage, long elapsed, long duration)
    {
        if (!Enabled) return;
        Record(WindowsStartupClock.Name(stage), "ExistingStage", "ProcessWall", duration, CurrentAiAttempt, 0, elapsed);
    }

    internal static void Complete()
    {
        try { CompleteAsync().Wait(TimeSpan.FromMilliseconds(500)); } catch { }
    }

    internal static Task CompleteAsync()
    {
        if (_entries is not { } entries) return Task.CompletedTask;
        Checkpoint(StartupInvestigationPhase.ClientLifetimeEnded);
        StartPersistence();
        entries.Writer.TryComplete();
        _entries = null;
        return _writer ?? Task.CompletedTask;
    }

    private static void Record(string stage, string kind, string clock, long duration, int attempt, long operation, long? elapsed = null)
    {
        if (_entries is not { } entries || Interlocked.Increment(ref _entryCount) > MaximumEntries)
            return;
        entries.Writer.TryWrite(new StartupInvestigationEntry(
            DateTimeOffset.UtcNow, "StartupInvestigation", 1, _runId, Environment.ProcessId, _buildVersion,
            Environment.Version.ToString(), WindowsStartupClock.ManagedEntryMilliseconds,
            stage, kind, clock, elapsed ?? _clock!.ElapsedMilliseconds,
            Math.Max(0, duration), attempt, operation));
    }

    internal readonly struct Measurement(
        StartupInvestigationPhase phase, long timestamp, int attempt, long operation) : IDisposable
    {
        public void Dispose()
        {
            if (operation == 0) return;
            Record(phase.ToString(), "Finished", "HostMonotonic",
                (long)Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds, attempt, operation);
        }
    }

    internal readonly struct Attempt(int previous, bool active) : IDisposable
    {
        public void Dispose()
        {
            if (active) AiAttempt.Value = previous;
        }
    }
}
