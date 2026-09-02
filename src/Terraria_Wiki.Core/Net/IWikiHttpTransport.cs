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
    private readonly HttpClient _httpClient;

    public HttpClientWikiHttpTransport(HttpClient httpClient) =>
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public async Task<string> GetStringAsync(WikiBook book, Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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
}
