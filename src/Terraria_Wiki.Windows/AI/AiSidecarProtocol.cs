using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Terraria_Wiki.Windows.AI;

internal enum AiSidecarEventKind
{
    Ready,
    RunStarted,
    ToolStarted,
    ToolFinished,
    AnswerDelta,
    Completed,
    Cancelled,
    Failed,
    Fatal,
}

internal sealed record AiSidecarError(string Code, string Message);

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
    public required int InputTokens { get; init; }

    [JsonPropertyName("output_tokens")]
    public required int OutputTokens { get; init; }

    [JsonPropertyName("total_tokens")]
    public required int TotalTokens { get; init; }

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

internal sealed record AiSidecarEvent(
    AiSidecarEventKind Kind,
    string RequestId,
    string? Name = null,
    string? Status = null,
    string? Delta = null,
    AiAgentAnswer? Answer = null,
    AiSidecarError? Error = null,
    string? RuntimeVersion = null,
    string? PythonVersion = null,
    string? PydanticAiVersion = null);

internal static class AiSidecarProtocol
{
    public const int Version = 1;
    public const int MaxLineBytes = 256 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16,
    };

    public static byte[] CreateInitialize(
        string requestId,
        AiRuntimeConfiguration configuration,
        Uri endpoint,
        string bearerToken) => Serialize(new
        {
            version = Version,
            type = "initialize",
            requestId,
            provider = new
            {
                baseUrl = configuration.Provider.BaseUrl,
                model = configuration.Provider.Model,
                api = configuration.Provider.Api,
                reasoningEffort = configuration.Provider.ReasoningEffort,
            },
            wikiTools = new
            {
                endpoint = endpoint.AbsoluteUri,
                bearerToken,
            },
        });

    public static byte[] CreateAnswer(string requestId, string question) => Serialize(new
    {
        version = Version,
        type = "answer",
        requestId,
        question,
    });

    public static byte[] CreateCancel(string requestId, string targetRequestId) => Serialize(new
    {
        version = Version,
        type = "cancel",
        requestId,
        targetRequestId,
    });

    public static byte[] CreateShutdown(string requestId) => Serialize(new
    {
        version = Version,
        type = "shutdown",
        requestId,
    });

    public static AiSidecarEvent Parse(string line)
    {
        if (StrictUtf8.GetByteCount(line) + 1 > MaxLineBytes)
            throw new AiRuntimeException("protocol_violation");
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16,
            });
        }
        catch (JsonException)
        {
            throw new AiRuntimeException("protocol_violation");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("version", out var version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var value)
                || value != Version
                || !root.TryGetProperty("type", out var typeElement)
                || typeElement.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("requestId", out var requestElement)
                || requestElement.ValueKind != JsonValueKind.String)
            {
                throw new AiRuntimeException("protocol_violation");
            }

            var type = typeElement.GetString()!;
            var requestId = requestElement.GetString()!;
            if (!IsValidRequestId(requestId))
                throw new AiRuntimeException("protocol_violation");

            return type switch
            {
                "ready" => ParseReady(root, requestId),
                "run_started" => ParseSimple(root, requestId, AiSidecarEventKind.RunStarted),
                "tool_started" => ParseTool(root, requestId, finished: false),
                "tool_finished" => ParseTool(root, requestId, finished: true),
                "answer_delta" => ParseDelta(root, requestId),
                "completed" => ParseCompleted(root, requestId),
                "cancelled" => ParseSimple(root, requestId, AiSidecarEventKind.Cancelled),
                "failed" => ParseError(root, requestId, fatal: false),
                "fatal" => ParseError(root, requestId, fatal: true),
                _ => throw new AiRuntimeException("protocol_violation"),
            };
        }
    }

    private static AiSidecarEvent ParseReady(JsonElement root, string requestId)
    {
        RequireFields(root, "version", "type", "requestId", "runtimeVersion", "pythonVersion", "pydanticAiVersion");
        return new AiSidecarEvent(
            AiSidecarEventKind.Ready,
            requestId,
            RuntimeVersion: RequiredString(root, "runtimeVersion"),
            PythonVersion: RequiredString(root, "pythonVersion"),
            PydanticAiVersion: RequiredString(root, "pydanticAiVersion"));
    }

    private static AiSidecarEvent ParseSimple(
        JsonElement root,
        string requestId,
        AiSidecarEventKind kind)
    {
        RequireFields(root, "version", "type", "requestId");
        return new AiSidecarEvent(kind, requestId);
    }

    private static AiSidecarEvent ParseTool(JsonElement root, string requestId, bool finished)
    {
        RequireFields(root, finished
            ? ["version", "type", "requestId", "name", "status"]
            : ["version", "type", "requestId", "name"]);
        var name = RequiredString(root, "name");
        if (name is not ("search_wiki" or "read_wiki"))
            throw new AiRuntimeException("protocol_violation");
        if (!finished)
            return new AiSidecarEvent(AiSidecarEventKind.ToolStarted, requestId, Name: name);
        var status = RequiredString(root, "status");
        if (status is not ("succeeded" or "not_found" or "failed"))
            throw new AiRuntimeException("protocol_violation");
        return new AiSidecarEvent(AiSidecarEventKind.ToolFinished, requestId, name, status);
    }

    private static AiSidecarEvent ParseDelta(JsonElement root, string requestId)
    {
        RequireFields(root, "version", "type", "requestId", "delta");
        var delta = RequiredString(root, "delta");
        if (delta.Length == 0)
            throw new AiRuntimeException("protocol_violation");
        return new AiSidecarEvent(AiSidecarEventKind.AnswerDelta, requestId, Delta: delta);
    }

    private static AiSidecarEvent ParseCompleted(JsonElement root, string requestId)
    {
        RequireFields(root, "version", "type", "requestId", "answer");
        if (!root.TryGetProperty("answer", out var answerElement)
            || answerElement.ValueKind != JsonValueKind.Object)
        {
            throw new AiRuntimeException("protocol_violation");
        }
        try
        {
            var answer = answerElement.Deserialize<AiAgentAnswer>(StrictJson)
                ?? throw new JsonException();
            ValidateAnswer(answer);
            return new AiSidecarEvent(AiSidecarEventKind.Completed, requestId, Answer: answer);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw new AiRuntimeException("protocol_violation");
        }
    }

    private static AiSidecarEvent ParseError(JsonElement root, string requestId, bool fatal)
    {
        RequireFields(root, "version", "type", "requestId", "error");
        if (!root.TryGetProperty("error", out var error)
            || error.ValueKind != JsonValueKind.Object)
        {
            throw new AiRuntimeException("protocol_violation");
        }
        RequireFields(error, "code", "message");
        var code = RequiredString(error, "code");
        _ = RequiredString(error, "message");
        return new AiSidecarEvent(
            fatal ? AiSidecarEventKind.Fatal : AiSidecarEventKind.Failed,
            requestId,
            Error: new AiSidecarError(code, AiRuntimeException.SafeMessage(code)));
    }

    private static void ValidateAnswer(AiAgentAnswer answer)
    {
        if (answer.Status is not ("answered" or "insufficient_evidence")
            || string.IsNullOrWhiteSpace(answer.Markdown)
            || answer.Markdown.Length > 32_768
            || answer.Sources.Count > 8
            || answer.ToolTrace.Count > 8
            || answer.Usage.Requests < 0
            || answer.Usage.ToolCalls < 0
            || answer.Usage.InputTokens < 0
            || answer.Usage.OutputTokens < 0
            || answer.Usage.TotalTokens < 0
            || answer.Usage.TotalTokens != answer.Usage.InputTokens + answer.Usage.OutputTokens
            || answer.Usage.CostUsd < 0)
        {
            throw new InvalidOperationException();
        }
        if (answer.Sources.Any(source => string.IsNullOrWhiteSpace(source.RequestedTitle)
            || string.IsNullOrWhiteSpace(source.ResolvedTitle)))
        {
            throw new InvalidOperationException();
        }
        if (answer.ToolTrace.Any(call => call.Name is not ("search_wiki" or "read_wiki")
            || call.Status is not ("succeeded" or "not_found")))
        {
            throw new InvalidOperationException();
        }
        if (answer.Status == "answered" && answer.Sources.Count == 0)
            throw new InvalidOperationException();
    }

    private static byte[] Serialize<T>(T payload)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(payload, StrictJson);
        if (body.Length + 1 > MaxLineBytes)
            throw new AiRuntimeException("protocol_violation");
        var line = new byte[body.Length + 1];
        body.CopyTo(line, 0);
        line[^1] = (byte)'\n';
        return line;
    }

    private static bool IsValidRequestId(string value) => value.Length is > 0 and <= 128
        && value.All(character => char.IsAsciiLetterOrDigit(character)
            || character is '.' or '_' or ':' or '-');

    private static string RequiredString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new AiRuntimeException("protocol_violation");
        return value.GetString()!;
    }

    private static void RequireFields(JsonElement element, params string[] expected)
    {
        var names = element.EnumerateObject().Select(property => property.Name).ToArray();
        if (names.Length != expected.Length
            || names.Except(expected, StringComparer.Ordinal).Any())
        {
            throw new AiRuntimeException("protocol_violation");
        }
    }
}

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
            ["dependency_mismatch"] = "Python Runtime 依赖不兼容。",
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
