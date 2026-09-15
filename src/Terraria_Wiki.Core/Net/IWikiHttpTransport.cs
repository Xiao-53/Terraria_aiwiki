using System.Net;
using Terraria_Wiki.Models;

namespace Terraria_Wiki.Core.Net;

public interface IWikiHttpTransport
{
    Task<string> GetStringAsync(WikiBook book, Uri uri, CancellationToken cancellationToken = default);

    Task<WikiHttpContentResponse> GetContentAsync(
        WikiBook book,
        Uri uri,
        DateTimeOffset? ifModifiedSince = null,
        CancellationToken cancellationToken = default);
}

public sealed record WikiHttpContentResponse(
    HttpStatusCode StatusCode,
    byte[] Data,
    string ContentType,
    DateTimeOffset? LastModified)
{
    public bool IsNotModified => StatusCode == HttpStatusCode.NotModified;
}

public sealed class HttpClientWikiHttpTransport : IWikiHttpTransport
{
    internal const string DefaultUserAgent = "TerrariaAIWiki/1.0";
    private readonly HttpClient _httpClient;

    public HttpClientWikiHttpTransport(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(DefaultUserAgent);
    }

    public async Task<string> GetStringAsync(WikiBook book, Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        ThrowIfThrottled(response);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<WikiHttpContentResponse> GetContentAsync(
        WikiBook book,
        Uri uri,
        DateTimeOffset? ifModifiedSince = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.IfModifiedSince = ifModifiedSince;
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        ThrowIfThrottled(response);
        if (response.StatusCode != HttpStatusCode.NotModified)
            response.EnsureSuccessStatusCode();

        var data = response.StatusCode == HttpStatusCode.NotModified
            ? Array.Empty<byte>()
            : await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return new WikiHttpContentResponse(
            response.StatusCode,
            data,
            response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream",
            response.Content.Headers.LastModified);
    }

    private static void ThrowIfThrottled(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.TooManyRequests &&
            !(response.StatusCode == HttpStatusCode.ServiceUnavailable && response.Headers.RetryAfter is not null)) return;
        var delay = response.Headers.RetryAfter?.Delta ??
            (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(30));
        throw new WikiHttpThrottleException(response.StatusCode, delay);
    }
}

internal sealed class WikiHttpThrottleException(HttpStatusCode status, TimeSpan retryAfter)
    : HttpRequestException("The Wiki server asked the client to retry later.", null, status)
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}

internal static class WikiHttpRetry
{
    public static async Task DelayAsync(Exception exception, int attempt, CancellationToken token)
    {
        var delay = exception is WikiHttpThrottleException throttled ? throttled.RetryAfter :
            exception is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable }
                ? TimeSpan.FromSeconds(Math.Min(60, 10 * Math.Pow(2, attempt - 1))) : TimeSpan.FromSeconds(1);
        // A long Retry-After ends this round; never retry sooner than the server permits.
        if (delay > TimeSpan.FromMinutes(2)) throw exception;
        if (delay < TimeSpan.FromSeconds(1)) delay = TimeSpan.FromSeconds(1);
        await Task.Delay(delay, token);
    }
}
