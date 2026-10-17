using System.Text.Json.Serialization;

namespace Terraria_Wiki.Windows.AI;

internal sealed record AiAgentSource
{
    [JsonPropertyName("requested_title")]
    public required string RequestedTitle { get; init; }

    [JsonPropertyName("resolved_title")]
    public required string ResolvedTitle { get; init; }

    [JsonPropertyName("anchor")]
    public string? Anchor { get; init; }

    [JsonPropertyName("last_modified")]
    public DateTimeOffset? LastModified { get; init; }
}

internal sealed record AiAgentToolCall
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }
}

internal sealed record AiAgentUsage
{
    [JsonPropertyName("requests")]
    public required int Requests { get; init; }

    [JsonPropertyName("tool_calls")]
    public required int ToolCalls { get; init; }

    [JsonPropertyName("input_tokens")]
    public required long InputTokens { get; init; }

    [JsonPropertyName("cached_input_tokens")]
    public required long CachedInputTokens { get; init; }

    [JsonPropertyName("output_tokens")]
    public required long OutputTokens { get; init; }

    [JsonPropertyName("total_tokens")]
    public required long TotalTokens { get; init; }

    [JsonPropertyName("cost_usd")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? CostUsd { get; init; }
}

internal sealed record AiAgentAnswer
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("markdown")]
    public required string Markdown { get; init; }

    [JsonPropertyName("sources")]
    public required IReadOnlyList<AiAgentSource> Sources { get; init; }

    [JsonPropertyName("usage")]
    public required AiAgentUsage Usage { get; init; }

    [JsonPropertyName("tool_trace")]
    public required IReadOnlyList<AiAgentToolCall> ToolTrace { get; init; }
}

internal enum AiRunEventKind
{
    RunStarted,
    ToolStarted,
    ToolFinished,
    AnswerDelta,
    AnswerReclassified,
    ProcessDelta,
}

internal sealed record AiRunEvent(
    AiRunEventKind Kind,
    string? Name = null,
    string? Status = null,
    string? Delta = null);

internal sealed record AiConversationHistoryTurn(string User, string Assistant);

internal sealed class AiRuntimeException : IOException
{
    private static readonly IReadOnlyDictionary<string, string> SafeMessages =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["protocol_violation"] = "AI Runtime 协议无效。",
            ["runtime_busy"] = "AI 正在回答另一个问题。",
            ["request_not_active"] = "没有可停止的 AI 回答。",
            ["invalid_input"] = "问题无效。",
            ["invalid_connection"] = "本地 Wiki 工具连接无效。",
            ["wiki_tool_unavailable"] = "本地 Wiki 工具不可用。",
            ["model_authentication"] = "模型密钥无效或缺失。",
            ["model_unavailable"] = "模型服务不可用。",
            ["context_limit"] = "Wiki 证据超过模型上下文上限。",
            ["budget_exceeded"] = "本次回答达到运行限制，请缩短问题后重试。",
            ["timeout"] = "AI 回答超时。",
            ["cancelled"] = "AI 回答已取消。",
            ["unknown"] = "AI 回答失败。",
            ["dependency_mismatch"] = "AI Runtime 依赖不兼容。",
            ["invalid_initialize"] = "AI Runtime 初始化无效。",
            ["unexpected_eof"] = "AI Runtime 意外退出。",
            ["unexpected_stdout"] = "AI Runtime 输出了非法内容。",
            ["runtime_crashed"] = "AI Runtime 意外停止。",
        };

    public AiRuntimeException(string code, Exception? innerException = null)
        : base(SafeMessage(code), innerException) => Code = SafeMessages.ContainsKey(code) ? code : "unknown";

    public string Code { get; }

    public static string SafeMessage(string code) => SafeMessages.TryGetValue(code, out var message)
        ? message
        : SafeMessages["unknown"];
}
