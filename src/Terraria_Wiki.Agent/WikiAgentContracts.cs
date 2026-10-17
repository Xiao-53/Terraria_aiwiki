namespace Terraria_Wiki.Agent;

public sealed record WikiConversationTurn(string User, string Assistant);
public sealed record WikiAgentSource(string RequestedTitle, string ResolvedTitle, string? Anchor, DateTimeOffset? LastModified);
public sealed record WikiAgentToolCall(string Name, string Status);
public sealed record WikiAgentUsage(int Requests, int ToolCalls, long InputTokens, long CachedInputTokens, long OutputTokens, long TotalTokens, decimal? CostUsd = null);
public sealed record WikiAgentAnswer(string Status, string Markdown, IReadOnlyList<WikiAgentSource> Sources,
    WikiAgentUsage Usage, IReadOnlyList<WikiAgentToolCall> ToolTrace);

public sealed class WikiToolConnection
{
    internal string BearerToken { get; }
    public Uri Endpoint { get; }

    public WikiToolConnection(string endpoint, string bearerToken)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || uri.Port is < 1 or > 65535 || endpoint != $"http://127.0.0.1:{uri.Port}/mcp"
            || string.IsNullOrEmpty(bearerToken) || bearerToken.Length > 4096
            || bearerToken.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new WikiAgentException("invalid_connection");
        Endpoint = uri;
        BearerToken = bearerToken;
    }

    public override string ToString() => $"WikiToolConnection {{ Endpoint = {Endpoint} }}";
}
