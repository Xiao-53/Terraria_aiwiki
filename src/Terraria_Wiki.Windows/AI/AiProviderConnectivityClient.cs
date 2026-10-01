using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Terraria_Wiki.Windows.AI;

internal sealed class AiProviderConnectivityClient : IDisposable
{
    internal const int MaximumResponseBytes = 1024 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private const string TestPrompt = "Reply with OK.";
    private readonly HttpClient _httpClient;

    public AiProviderConnectivityClient(HttpMessageHandler? handler = null)
    {
        handler ??= new HttpClientHandler { AllowAutoRedirect = false };
        _httpClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = RequestTimeout,
        };
    }

    public async Task<IReadOnlyList<string>> DiscoverModelsAsync(
        AiProviderConfiguration provider,
        string apiKey,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            var endpoint = BuildEndpoint(provider.BaseUrl, "models");
            using var request = CreateRequest(HttpMethod.Get, endpoint, apiKey);
            using var response = await SendAsync(request, timeout.Token).ConfigureAwait(false);
            var payload = await ReadBoundedAsync(response, timeout.Token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16,
            });
            if (!document.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Array)
            {
                throw InvalidResponse();
            }

            return data.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("id", out var id)
                    && id.ValueKind == JsonValueKind.String
                        ? id.GetString()?.Trim()
                        : null)
                .Where(id => !string.IsNullOrWhiteSpace(id) && id!.Length <= 256 && !id.Any(char.IsControl))
                .Select(id => id!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
        }
        catch (JsonException)
        {
            throw InvalidResponse();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw TimeoutError();
        }
        catch (IOException exception) when (exception is not AiProviderConnectionException)
        {
            throw NetworkError();
        }
    }

    public async Task TestConfigurationAsync(
        AiProviderConfiguration provider,
        string apiKey,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            provider = AiRuntimeConfiguration.ValidateProvider(provider);
            var route = provider.Api == "responses" ? "responses" : "chat/completions";
            var payload = provider.Api == "responses"
                ? BuildResponsesPayload(provider)
                : BuildChatCompletionsPayload(provider);
            using var request = CreateRequest(HttpMethod.Post, BuildEndpoint(provider.BaseUrl, route), apiKey);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await SendAsync(request, timeout.Token).ConfigureAwait(false);
            _ = await ReadBoundedAsync(response, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw TimeoutError();
        }
        catch (IOException exception) when (exception is not AiProviderConnectionException)
        {
            throw NetworkError();
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var exception = MapStatus(response.StatusCode);
                response.Dispose();
                throw exception;
            }
            return response;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw TimeoutError();
        }
        catch (HttpRequestException)
        {
            throw NetworkError();
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
            throw ResponseTooLarge();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            if (buffer.Length + read > MaximumResponseBytes)
                throw ResponseTooLarge();
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, Uri endpoint, string apiKey)
    {
        var request = new HttpRequestMessage(method, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            AiRuntimeConfiguration.ValidateApiKey(apiKey));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static Uri BuildEndpoint(string baseUrl, string route)
    {
        var baseUri = AiRuntimeConfiguration.ValidateProviderBaseUrl(baseUrl);
        return new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/" + route, UriKind.Absolute);
    }

    private static string BuildResponsesPayload(AiProviderConfiguration provider)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = provider.Model,
            ["input"] = TestPrompt,
            ["max_output_tokens"] = 1,
        };
        if (provider.ReasoningEffort is not null)
            payload["reasoning"] = new Dictionary<string, string> { ["effort"] = provider.ReasoningEffort };
        return JsonSerializer.Serialize(payload);
    }

    private static string BuildChatCompletionsPayload(AiProviderConfiguration provider)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = provider.Model,
            ["messages"] = new[] { new Dictionary<string, string> { ["role"] = "user", ["content"] = TestPrompt } },
            ["max_tokens"] = 1,
        };
        if (provider.ReasoningEffort is not null)
            payload["reasoning_effort"] = provider.ReasoningEffort;
        return JsonSerializer.Serialize(payload);
    }

    private static AiProviderConnectionException MapStatus(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            new("provider-authentication", "模型服务拒绝了凭据，请检查 API Key。"),
        HttpStatusCode.NotFound =>
            new("provider-not-found", "模型服务不支持此接口；你仍可手动填写 model id。"),
        HttpStatusCode.TooManyRequests =>
            new("provider-rate-limited", "模型服务请求过于频繁，请稍后重试。"),
        _ => new("provider-http-error", "模型服务返回错误，请检查配置后重试。"),
    };

    private static AiProviderConnectionException InvalidResponse() =>
        new("provider-response-invalid", "模型服务返回了无法识别的数据。");

    private static AiProviderConnectionException ResponseTooLarge() =>
        new("provider-response-too-large", "模型服务响应超过 1 MiB 安全上限。");

    private static AiProviderConnectionException TimeoutError() =>
        new("provider-timeout", "连接模型服务超时，请稍后重试。");

    private static AiProviderConnectionException NetworkError() =>
        new("provider-network", "无法连接模型服务，请检查地址和网络。");
}

internal sealed class AiProviderConnectionException : IOException
{
    public AiProviderConnectionException(string code, string message)
        : base(message) => Code = code;

    public string Code { get; }
}
