using Terraria_Wiki.Agent;
using Terraria_Wiki.Application;
using Terraria_Wiki.WikiTools;
using Terraria_Wiki.Windows.Hosting;
using AgentConnection = Terraria_Wiki.Agent.WikiToolConnection;

namespace Terraria_Wiki.Windows.AI;

internal enum WindowsAiRuntimeState { Disabled, Starting, Ready, Answering, Failed, Stopped }
internal sealed record WindowsAiRuntimeStatus(WindowsAiRuntimeState State, string Message, string? ErrorCode = null);
internal delegate Task<TerrariaWikiAgent> WindowsAiAgentFactory(WikiAgentOptions options, AgentConnection connection, string key, CancellationToken token);

internal sealed class WindowsAiRuntime : IAsyncDisposable
{
    private readonly WikiToolServer _wikiTools;
    private readonly TerrariaWikiAgent _agent;
    private readonly object _sync = new();
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _activeCancellation;
    private Task? _active;
    private Task? _stop;
    private Task? _lateCleanup;
    private int _disposed;

    private WindowsAiRuntime(WikiToolServer wikiTools, TerrariaWikiAgent agent)
    { _wikiTools = wikiTools; _agent = agent; Status = new(WindowsAiRuntimeState.Ready, "AI Runtime 已就绪。"); }

    public event EventHandler<WindowsAiRuntimeStatus>? StatusChanged;
    public WindowsAiRuntimeStatus Status { get; private set; }

    public static async Task<WindowsAiRuntime> StartAsync(WikiQueryUseCases query, WikiToolConsumerCoordinator coordinator,
        AiRuntimeConfiguration configuration, WindowsAiAgentFactory? agentFactory = null, CancellationToken cancellationToken = default,
        TimeSpan? initializationTimeout = null)
    {
        WikiToolServer? server = null;
        Task<TerrariaWikiAgent>? creation = null;
        TerrariaWikiAgent? agent = null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(initializationTimeout ?? TimeSpan.FromSeconds(15));
        try
        {
            using (WindowsStartupInvestigation.Measure(StartupInvestigationPhase.WikiTools))
                server = await WikiToolServer.StartAsync(query, coordinator, deadline.Token).ConfigureAwait(false);
            using (WindowsStartupInvestigation.Measure(StartupInvestigationPhase.AgentInitialization))
            {
                var options = new WikiAgentOptions(configuration.Provider.BaseUrl, configuration.Provider.Model,
                    configuration.Provider.Api, configuration.Provider.ReasoningEffort);
                creation = (agentFactory ?? TerrariaWikiAgent.CreateAsync)(options,
                    new AgentConnection(server.Connection.Endpoint.AbsoluteUri, server.Connection.BearerToken), configuration.ApiKey, deadline.Token);
                agent = await creation.WaitAsync(deadline.Token).ConfigureAwait(false);
            }
            deadline.Token.ThrowIfCancellationRequested();
            return new(server, agent);
        }
        catch
        {
            if (agent is not null) { agent.Abort(); await agent.DisposeAsync().ConfigureAwait(false); }
            else if (creation is not null) _ = CloseLateInitializationAsync(creation);
            if (server is not null) await server.StopAsync().ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            if (deadline.IsCancellationRequested) throw new AiRuntimeException("timeout");
            throw new AiRuntimeException("wiki_tool_unavailable");
        }
    }

    private static async Task CloseLateInitializationAsync(Task<TerrariaWikiAgent> creation)
    {
        try { var agent = await creation.ConfigureAwait(false); agent.Abort(); await agent.DisposeAsync().ConfigureAwait(false); }
        catch { /* This generation is rejected; late attachment is impossible. */ }
    }

    public Task<AiAgentAnswer> AnswerAsync(string question, IReadOnlyList<AiConversationHistoryTurn> history,
        Func<AiRunEvent, ValueTask>? onEvent = null, CancellationToken cancellationToken = default, string answerStyle = "detailed")
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (Status.State != WindowsAiRuntimeState.Ready || _active is not null)
                throw new AiRuntimeException(Status.State == WindowsAiRuntimeState.Answering ? "runtime_busy" : Status.ErrorCode ?? "runtime_crashed");
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            var completion = new TaskCompletionSource<AiAgentAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
            _activeCancellation = cancellation;
            _active = completion.Task;
            SetStatus(new(WindowsAiRuntimeState.Answering, "AI 正在查询本地 Wiki…"));
            _ = AnswerCoreAsync(question, history, onEvent, answerStyle, cancellation, completion);
            return completion.Task;
        }
    }

    private async Task AnswerCoreAsync(string question, IReadOnlyList<AiConversationHistoryTurn> history,
        Func<AiRunEvent, ValueTask>? onEvent, string style, CancellationTokenSource cancellation, TaskCompletionSource<AiAgentAnswer> completion)
    {
        AiAgentAnswer? result = null;
        Exception? failure = null;
        var fatal = false;
        try
        {
            var answer = await _agent.AnswerAsync(question, history.Select(t => new WikiConversationTurn(t.User, t.Assistant)).ToArray(),
                async value =>
                {
                    if (_disposed != 0 || cancellation.IsCancellationRequested || onEvent is null) return;
                    var kind = value.Kind switch
                    {
                        WikiAgentEventKind.RunStarted => AiRunEventKind.RunStarted,
                        WikiAgentEventKind.ToolStarted => AiRunEventKind.ToolStarted,
                        WikiAgentEventKind.ToolFinished => AiRunEventKind.ToolFinished,
                        WikiAgentEventKind.AnswerDelta => AiRunEventKind.AnswerDelta,
                        WikiAgentEventKind.AnswerReclassified => AiRunEventKind.AnswerReclassified,
                        WikiAgentEventKind.ProcessDelta => AiRunEventKind.ProcessDelta,
                        _ => throw new AiRuntimeException("unknown"),
                    };
                    await onEvent(new(kind, value.Name, value.Status, value.Delta)).ConfigureAwait(false);
                }, style, cancellation.Token).ConfigureAwait(false);
            result = new()
            {
                Status = answer.Status, Markdown = answer.Markdown,
                Sources = answer.Sources.Select(s => new AiAgentSource { RequestedTitle = s.RequestedTitle, ResolvedTitle = s.ResolvedTitle, Anchor = s.Anchor, LastModified = s.LastModified }).ToArray(),
                ToolTrace = answer.ToolTrace.Select(t => new AiAgentToolCall { Name = t.Name, Status = t.Status }).ToArray(),
                Usage = new() { Requests = answer.Usage.Requests, ToolCalls = answer.Usage.ToolCalls, InputTokens = answer.Usage.InputTokens,
                    CachedInputTokens = answer.Usage.CachedInputTokens, OutputTokens = answer.Usage.OutputTokens, TotalTokens = answer.Usage.TotalTokens, CostUsd = answer.Usage.CostUsd },
            };
        }
        catch (OperationCanceledException error) { failure = error; }
        catch (WikiAgentException error)
        {
            failure = new AiRuntimeException(error.Code);
            fatal = error.Code == "wiki_tool_unavailable";
            if (fatal)
            {
                try { await _wikiTools.StopAsync().ConfigureAwait(false); } catch { }
            }
        }
        catch { failure = new AiRuntimeException("unknown"); }
        finally
        {
            lock (_sync)
            {
                _active = null; _activeCancellation = null;
                if (_disposed == 0)
                    SetStatus(fatal ? new(WindowsAiRuntimeState.Failed, "本地 Wiki 工具不可用。", "wiki_tool_unavailable")
                        : new(WindowsAiRuntimeState.Ready, "AI Runtime 已就绪。"));
            }
            cancellation.Dispose();
        }
        if (failure is OperationCanceledException) completion.TrySetCanceled();
        else if (failure is not null) completion.TrySetException(failure);
        else completion.TrySetResult(result!);
    }

    public Task<AiAgentAnswer> AnswerAsync(string question, Func<AiRunEvent, ValueTask>? onEvent = null, CancellationToken cancellationToken = default) =>
        AnswerAsync(question, [], onEvent, cancellationToken);

    public async Task CancelAsync()
    {
        Task? active;
        lock (_sync) { _activeCancellation?.Cancel(); active = _active; }
        if (active is not null) try { await active.ConfigureAwait(false); } catch { }
    }

    public ValueTask DisposeAsync()
    {
        lock (_sync) return new(_stop ??= StopCoreAsync());
    }

    private async Task StopCoreAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        _lifetime.Cancel();
        var active = _active ?? Task.CompletedTask;
        try { await active.WaitAsync(TimeSpan.FromMilliseconds(1500)).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            _agent.Abort();
            try { await active.WaitAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false); } catch { }
        }
        catch { }
        // Revoke this server generation, cancel real tool leases and await their
        // drain before the host can release Application.
        await _wikiTools.StopAsync().ConfigureAwait(false);
        var cleanup = FinishAgentAsync(active);
        try { await cleanup.WaitAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false); }
        catch (TimeoutException) { _agent.Abort(); _lateCleanup = cleanup; }
        SetStatus(new(WindowsAiRuntimeState.Stopped, "AI Runtime 已停止。"));
    }

    private async Task FinishAgentAsync(Task active)
    {
        try { await active.ConfigureAwait(false); } catch { }
        try { await _agent.DisposeAsync().ConfigureAwait(false); } catch { }
        _lifetime.Dispose();
    }

    private void SetStatus(WindowsAiRuntimeStatus status)
    {
        Status = status;
        try { StatusChanged?.Invoke(this, status); } catch { }
    }
}
