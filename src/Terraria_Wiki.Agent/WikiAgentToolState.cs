using Microsoft.Extensions.AI;

namespace Terraria_Wiki.Agent;

// One instance per answer. Reservations happen on receipt of the complete call,
// before framework binding, ordering checks, retries or MCP invocation.
internal sealed class WikiAgentToolState(int limit)
{
    private readonly Dictionary<FunctionCallContent, bool> _reservations = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<FunctionCallContent> _malformed = new(ReferenceEqualityComparer.Instance);
    private bool _batchCanRead;
    internal int Attempts { get; private set; }
    internal bool HasSearch { get; set; }
    internal bool HasRead { get; set; }
    internal bool Exhausted => Attempts >= limit;
    internal bool Finalizing { get; private set; }
    internal Action? OnFinalizing { get; set; }

    internal ChatOptions? BeginResponse(ChatOptions? options)
    {
        _batchCanRead = HasSearch;
        if (!Exhausted) return options;
        if (Finalizing) throw new WikiAgentException("model_unavailable");
        Finalizing = true;
        OnFinalizing?.Invoke();
        var final = options?.Clone() ?? new ChatOptions();
        final.Tools = null;
        final.ToolMode = null;
        return final;
    }

    internal void Reserve(FunctionCallContent call)
    {
        if (Finalizing) throw new WikiAgentException("model_unavailable");
        if (_reservations.ContainsKey(call)) return;
        var allowed = Attempts < limit;
        if (allowed) Attempts++;
        _reservations.Add(call, allowed);
        if (call.Exception is not null || call.Arguments is null)
        {
            _malformed.Add(call);
            call.Exception = null;
            call.Arguments = new Dictionary<string, object?>();
        }
    }

    internal bool IsAllowed(FunctionCallContent call) => _reservations.TryGetValue(call, out var allowed) && allowed;
    internal bool CanRead => _batchCanRead;
    internal bool IsMalformed(FunctionCallContent call) => _malformed.Contains(call);
}
