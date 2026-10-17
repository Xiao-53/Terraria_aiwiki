using Microsoft.Extensions.AI;

namespace Terraria_Wiki.Agent;

internal sealed class WikiAgentToolLoop : IDisposable
{
    private readonly FunctionInvokingChatClient _loop;
    private readonly Func<WikiAgentEvent, ValueTask> _emit;
    private readonly Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> _invoke;
    private int _corrections;
    internal WikiAgentStreamObserver Observer { get; }
    internal WikiAgentToolState State { get; }

    internal WikiAgentToolLoop(IChatClient provider, int limit, Func<WikiAgentEvent, ValueTask> emit,
        Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> invoke,
        bool concurrent = false)
    {
        State = new(limit);
        _emit = emit;
        _invoke = invoke;
        Observer = new(provider, emit, State);
        _loop = new(Observer)
        {
            MaximumIterationsPerRequest = int.MaxValue,
            MaximumConsecutiveErrorsPerRequest = 0,
            IncludeDetailedErrors = false,
            AllowConcurrentInvocation = concurrent,
            FunctionInvoker = InvokeAsync,
        };
    }

    internal async Task<ChatResponse> RunAsync(IEnumerable<ChatMessage> messages, ChatOptions options, CancellationToken cancellationToken)
    {
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in _loop.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
            updates.Add(update);
        return updates.ToChatResponse();
    }

    private async ValueTask<object?> InvokeAsync(FunctionInvocationContext context, CancellationToken cancellationToken)
    {
        if (!State.IsAllowed(context.CallContent))
            return "Tool limit reached; this call was not executed. Finish using existing evidence and identify unchecked parts.";
        var name = context.CallContent.Name;
        if (name is not ("search_wiki" or "read_wiki")) throw new WikiAgentException("model_unavailable");
        await _emit(new(WikiAgentEventKind.ToolStarted, Name: name)).ConfigureAwait(false);
        try
        {
            if (State.IsMalformed(context.CallContent))
                throw new WikiToolArgumentsException("Invalid tool arguments. Provide a valid JSON object with the declared parameters and try again.");
            if (name == "read_wiki" && !State.CanRead)
                throw new WikiToolArgumentsException("Call search_wiki successfully in an earlier response before read_wiki.");
            var result = await _invoke(context, cancellationToken).ConfigureAwait(false);
            var status = result is WikiAgentToolResult typed ? typed.Status : "succeeded";
            if (name == "search_wiki") State.HasSearch = true;
            if (name == "read_wiki" && status == "succeeded") State.HasRead = true;
            await _emit(new(WikiAgentEventKind.ToolFinished, Name: name, Status: status)).ConfigureAwait(false);
            return result is WikiAgentToolResult value ? value.ModelContent : result;
        }
        catch (WikiToolArgumentsException error)
        {
            await _emit(new(WikiAgentEventKind.ToolFinished, Name: name, Status: "failed")).ConfigureAwait(false);
            if (Interlocked.Increment(ref _corrections) > 1 && !State.Exhausted)
                throw new WikiAgentException("model_unavailable");
            return error.Message;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            await _emit(new(WikiAgentEventKind.ToolFinished, Name: name, Status: "failed")).ConfigureAwait(false);
            throw;
        }
    }

    public void Dispose() => _loop.Dispose();
}

internal sealed class WikiToolArgumentsException(string safeMessage) : Exception(safeMessage);
internal sealed record WikiAgentToolResult(object ModelContent, string Status);
