namespace Terraria_Wiki.Agent;

public sealed class WikiAgentException(string code) : Exception(MessageFor(code))
{
    public string Code { get; } = code;

    private static string MessageFor(string code) => code switch
    {
        "invalid_input" => "问题或对话上下文无效。",
        "invalid_connection" => "Wiki 工具连接无效。",
        "wiki_tool_unavailable" => "本地 Wiki 工具不可用，请重试 AI。",
        "model_authentication" => "模型服务认证失败，请检查 AI 配置。",
        "model_unavailable" => "模型服务暂时不可用，请稍后重试。",
        "context_limit" => "模型上下文已超出服务限制，请新建对话或缩小问题。",
        "timeout" => "AI 请求超时，请稍后重试。",
        _ => "AI 请求未能完成，请稍后重试。",
    };
}
