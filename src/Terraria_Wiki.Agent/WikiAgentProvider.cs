using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Responses;

namespace Terraria_Wiki.Agent;

// Locked SDK 2.12.0 marks Responses and raw reasoning options experimental.
// Keep this opt-in local to the provider adapter; wire fixtures are the adoption gate.
#pragma warning disable OPENAI001

// Provider details stay inside the Agent; Windows receives only typed business results.
internal sealed class WikiAgentProvider : IDisposable
{
    private readonly HttpClient _http;

    internal WikiAgentProvider(WikiAgentOptions options, string apiKey, HttpMessageHandler? handler = null)
    {
        options.Validate();
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey != apiKey.Trim()
            || apiKey.Length > 4096 || apiKey.Any(char.IsControl))
        {
            throw new ArgumentException("AI credential is invalid.");
        }

        _http = new HttpClient(handler ?? new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
        }) { Timeout = Timeout.InfiniteTimeSpan };
        try
        {
            var clientOptions = new OpenAIClientOptions
            {
                Endpoint = new Uri(options.BaseUrl.TrimEnd('/') + "/"),
                Transport = new HttpClientPipelineTransport(_http),
                RetryPolicy = new ClientRetryPolicy(0),
                NetworkTimeout = Timeout.InfiniteTimeSpan,
            };
            var sdk = new OpenAIClient(new ApiKeyCredential(apiKey), clientOptions);
            Client = options.Api == "responses"
                ? sdk.GetResponsesClient().AsIChatClient(options.Model)
                : sdk.GetChatClient(options.Model).AsIChatClient();
            Options = options;
        }
        catch
        {
            _http.Dispose();
            throw;
        }
    }

    internal IChatClient Client { get; }
    internal WikiAgentOptions Options { get; }

    internal ChatOptions CreateChatOptions(IList<AITool>? tools = null) => new()
    {
        ModelId = Options.Model,
        Tools = tools,
        RawRepresentationFactory = Options.Api == "responses"
            ? _ => new CreateResponseOptions
            {
                StoredOutputEnabled = false,
                ReasoningOptions = Options.ReasoningEffort is { } effort
                    ? new ResponseReasoningOptions { ReasoningEffortLevel = new(effort) }
                    : null,
            }
            : _ => new ChatCompletionOptions
            {
                ReasoningEffortLevel = Options.ReasoningEffort is { } effort ? new(effort) : null,
            },
    };

    public void Dispose()
    {
        Client.Dispose();
        _http.Dispose();
    }

    public override string ToString() => nameof(WikiAgentProvider);
}

#pragma warning restore OPENAI001
