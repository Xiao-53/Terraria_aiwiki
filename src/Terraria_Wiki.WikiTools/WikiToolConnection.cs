using System.Diagnostics;

namespace Terraria_Wiki.WikiTools;

[DebuggerDisplay("{Endpoint}")]
public sealed class WikiToolConnection
{
    internal WikiToolConnection(Uri endpoint, string bearerToken)
    {
        Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        BearerToken = !string.IsNullOrWhiteSpace(bearerToken)
            ? bearerToken
            : throw new ArgumentException("A bearer token is required.", nameof(bearerToken));
    }

    public Uri Endpoint { get; }
    public string BearerToken { get; }

    public override string ToString() => Endpoint.AbsoluteUri;
}
