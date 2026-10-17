using System.ClientModel;
using System.Text;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace Terraria_Wiki.Agent;

public sealed class TerrariaWikiAgent : IAsyncDisposable
{
    internal const string InsufficientMarkdown = "本地 Wiki 证据不足，无法可靠回答这个问题。";
    private readonly WikiAgentProvider _provider;
    private readonly McpClient _mcp;
    private readonly HttpClient _mcpHttp;
    private readonly IList<AITool> _tools;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _sync = new();
    private TaskCompletionSource? _drained;
    private Task? _disposeTask;
    private bool _aborted;
    private bool _closed;

    private TerrariaWikiAgent(WikiAgentProvider provider, McpClient mcp, HttpClient mcpHttp, IList<AITool> tools)
    { _provider = provider; _mcp = mcp; _mcpHttp = mcpHttp; _tools = tools; }

    public static Task<TerrariaWikiAgent> CreateAsync(WikiAgentOptions options, WikiToolConnection connection,
        string apiKey, CancellationToken cancellationToken = default) =>
        CreateCoreAsync(options, connection, apiKey, null, cancellationToken);

    internal static async Task<TerrariaWikiAgent> CreateCoreAsync(WikiAgentOptions options, WikiToolConnection connection,
        string apiKey, HttpMessageHandler? providerHandler, CancellationToken cancellationToken)
    {
        WikiAgentProvider? provider = null;
        McpClient? mcp = null;
        var http = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false })
        { Timeout = Timeout.InfiniteTimeSpan };
        try
        {
            ArgumentNullException.ThrowIfNull(connection);
            provider = new(options, apiKey, providerHandler);
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = connection.Endpoint,
                TransportMode = HttpTransportMode.StreamableHttp,
                EnableStandaloneGetStream = false,
                MaxReconnectionAttempts = 0,
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + connection.BearerToken },
            }, http, ownsHttpClient: false);
            mcp = await McpClient.CreateAsync(transport, new McpClientOptions
            {
                ProtocolVersion = "2025-11-25", InitializationTimeout = TimeSpan.FromSeconds(15),
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
            var tools = await mcp.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            if (tools.Count != 2 || !tools.Select(t => t.Name).Order().SequenceEqual(new[] { "read_wiki", "search_wiki" }))
                throw new WikiAgentException("wiki_tool_unavailable");
            return new(provider, mcp, http, tools.Cast<AITool>().ToList());
        }
        catch (OperationCanceledException) { provider?.Dispose(); http.Dispose(); if (mcp is not null) await mcp.DisposeAsync().ConfigureAwait(false); throw; }
        catch
        {
            provider?.Dispose(); http.Dispose();
            if (mcp is not null) await mcp.DisposeAsync().ConfigureAwait(false);
            throw new WikiAgentException("wiki_tool_unavailable");
        }
    }

    public async Task<WikiAgentAnswer> AnswerAsync(string question, IReadOnlyList<WikiConversationTurn> history,
        Func<WikiAgentEvent, ValueTask>? onEvent = null, string answerStyle = "detailed", CancellationToken cancellationToken = default)
    {
        if (!ValidText(question) || history is null || history.Count > 4
            || history.Any(turn => turn is null || !ValidText(turn.User) || !ValidText(turn.Assistant))
            || answerStyle is not ("detailed" or "concise")) throw new WikiAgentException("invalid_input");
        TaskCompletionSource drained;
        lock (_sync)
        {
            if (_disposeTask is not null || _lifetime.IsCancellationRequested) throw new WikiAgentException("wiki_tool_unavailable");
            if (_drained is not null) throw new WikiAgentException("runtime_busy");
            _drained = drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        using var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        run.CancelAfter(TimeSpan.FromSeconds(_provider.Options.TimeoutSeconds));
        using var eventGate = new SemaphoreSlim(1);
        var trace = new List<WikiAgentToolCall>();
        async ValueTask Emit(WikiAgentEvent value)
        {
            await eventGate.WaitAsync(run.Token).ConfigureAwait(false);
            try
            {
                run.Token.ThrowIfCancellationRequested();
                if (value.Kind == WikiAgentEventKind.ToolFinished) trace.Add(new(value.Name!, value.Status!));
                if (onEvent is not null)
                {
                    try { await onEvent(value).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (run.IsCancellationRequested) { throw; }
                    catch { throw new WikiAgentException("unknown"); }
                }
            }
            finally { eventGate.Release(); }
        }
        try
        {
            var evidence = new WikiAgentEvidence();
            // The provider client is owned by this Agent. Disposing a per-answer loop
            // must not dispose the reusable provider after cancellation or completion.
            using var loop = new WikiAgentToolLoop(new BorrowedChatClient(_provider.Client), _provider.Options.ToolCallsLimit,
                Emit, async (context, token) =>
                {
                    try
                    {
                        WikiAgentArguments.Validate(context.CallContent.Name, context.Arguments);
                        var result = await _mcp.CallToolAsync(context.CallContent.Name,
                            context.Arguments, cancellationToken: token).ConfigureAwait(false);
                        return evidence.Process(context.CallContent.Name, context.Arguments, result, context.Iteration);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (WikiToolArgumentsException) { throw; }
                    catch (WikiAgentException) { throw; }
                    catch { throw new WikiAgentException("wiki_tool_unavailable"); }
                });
            loop.State.OnFinalizing = () => run.CancelAfter(TimeSpan.FromSeconds(_provider.Options.TimeoutSeconds));
            await Emit(new(WikiAgentEventKind.RunStarted)).ConfigureAwait(false);
            var messages = new List<ChatMessage> { new(ChatRole.System, Instructions(answerStyle)) };
            foreach (var turn in history)
            { messages.Add(new(ChatRole.User, turn.User)); messages.Add(new(ChatRole.Assistant, turn.Assistant)); }
            messages.Add(new(ChatRole.User, question.Trim()));
            var response = await loop.RunAsync(messages, _provider.CreateChatOptions(_tools), run.Token).ConfigureAwait(false);
            var markdown = loop.Observer.FinalMarkdown;
            if (evidence.Sources.Count == 0 && markdown != InsufficientMarkdown)
            {
                await Emit(new(WikiAgentEventKind.AnswerReclassified)).ConfigureAwait(false);
                markdown = InsufficientMarkdown;
                await Emit(new(WikiAgentEventKind.AnswerDelta, Delta: markdown)).ConfigureAwait(false);
            }
            if (!ValidText(markdown)) throw new WikiAgentException("model_unavailable");
            var usage = response.Usage;
            var input = usage?.InputTokenCount ?? 0;
            var output = usage?.OutputTokenCount ?? 0;
            var cached = usage?.CachedInputTokenCount ?? 0;
            if (input < 0 || output < 0 || cached < 0 || cached > input) throw new WikiAgentException("model_unavailable");
            return new(markdown == InsufficientMarkdown ? "insufficient_evidence" : "answered", markdown,
                evidence.Sources.ToArray(), new(loop.Observer.Requests, loop.State.Attempts, input, cached, output, checked(input + output)), trace.ToArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _lifetime.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { throw new WikiAgentException("timeout"); }
        catch (Exception) when (cancellationToken.IsCancellationRequested || _lifetime.IsCancellationRequested)
        { throw new OperationCanceledException(cancellationToken); }
        catch (Exception) when (run.IsCancellationRequested) { throw new WikiAgentException("timeout"); }
        catch (WikiAgentException) { throw; }
        catch (ClientResultException error)
        {
            // Read only recognized context codes; never embed provider bodies/exceptions.
            var context = error.Status == 400 && (error.Message.Contains("context_length_exceeded", StringComparison.Ordinal)
                || error.Message.Contains("context_window_exceeded", StringComparison.Ordinal));
            throw new WikiAgentException(error.Status is 401 or 403 ? "model_authentication" : context ? "context_limit" : "model_unavailable");
        }
        catch { throw new WikiAgentException("model_unavailable"); }
        finally
        {
            lock (_sync) { _drained = null; drained.TrySetResult(); }
        }
    }

    private static bool ValidText(string? value) => !string.IsNullOrWhiteSpace(value) && value.EnumerateRunes().Count() <= 32768;

    private static string Instructions(string style)
    {
        using var stream = typeof(TerrariaWikiAgent).Assembly.GetManifestResourceStream("Terraria_Wiki.Agent.WikiAgentInstructions.txt")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd() + "\n\n" + (style == "concise"
            ? "Default to a concise answer: give the direct conclusion and essential steps or conditions."
            : "Default to a detailed answer: explain supported steps, prerequisites, quantities, locations and caveats without repetition.")
            + " The user's explicit length and format requirements take priority.";
    }

    public void Abort()
    {
        lock (_sync)
        {
            if (_aborted || _closed) return;
            _aborted = true;
            _lifetime.Cancel();
            _provider.Dispose();
            _mcpHttp.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_sync) return new(_disposeTask ??= DisposeCoreAsync(_drained?.Task ?? Task.CompletedTask));
    }

    private async Task DisposeCoreAsync(Task drained)
    {
        _lifetime.Cancel();
        await drained.ConfigureAwait(false);
        await _mcp.DisposeAsync().ConfigureAwait(false);
        lock (_sync)
        {
            _closed = true;
            _mcpHttp.Dispose(); _provider.Dispose(); _lifetime.Dispose();
        }
    }

    private sealed class BorrowedChatClient(IChatClient inner) : DelegatingChatClient(inner)
    { protected override void Dispose(bool disposing) { } }
}
