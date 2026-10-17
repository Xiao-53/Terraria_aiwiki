using System.Net;

namespace Terraria_Wiki.Agent;

public sealed record WikiAgentOptions(
    string BaseUrl,
    string Model,
    string Api,
    string? ReasoningEffort = null,
    int ToolCallsLimit = 15,
    double TimeoutSeconds = 120)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl) || BaseUrl != BaseUrl.Trim()
            || !Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || uri.Port is < 1 or > 65535
            || uri.Scheme == "http" && (!IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address)
                || !IPAddress.IsLoopback(address))
            || string.IsNullOrWhiteSpace(Model) || Model != Model.Trim()
            || Model.Length > 256 || Model.Any(char.IsControl)
            || Api is not ("responses" or "chat_completions")
            || ReasoningEffort is not (null or "none" or "low" or "medium" or "high" or "xhigh" or "max")
            || ToolCallsLimit is < 1 or > 15
            || !double.IsFinite(TimeoutSeconds) || TimeoutSeconds is <= 0 or > 120)
        {
            throw new ArgumentException("AI provider options are invalid.");
        }
    }
}
