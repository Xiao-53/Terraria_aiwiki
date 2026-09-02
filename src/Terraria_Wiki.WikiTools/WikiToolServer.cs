using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using ModelContextProtocol.Server;
using Terraria_Wiki.Application;

namespace Terraria_Wiki.WikiTools;

public sealed class WikiToolServer : IAsyncDisposable
{
    private readonly object _stopSync = new();
    private readonly WebApplication _application;
    private readonly WikiToolConsumerCoordinator _coordinator;
    private readonly long _coordinatorEpoch;
    private Task? _stopTask;

    private WikiToolServer(
        WebApplication application,
        WikiToolConsumerCoordinator coordinator,
        long coordinatorEpoch,
        WikiToolConnection connection)
    {
        _application = application;
        _coordinator = coordinator;
        _coordinatorEpoch = coordinatorEpoch;
        Connection = connection;
    }

    public WikiToolConnection Connection { get; }

    public static async Task<WikiToolServer> StartAsync(
        WikiQueryUseCases query,
        WikiToolConsumerCoordinator coordinator,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(coordinator);
        cancellationToken.ThrowIfCancellationRequested();

        var token = CreateBearerToken();
        var expectedTokenBytes = Encoding.ASCII.GetBytes(token);
        var options = new WebApplicationOptions
        {
            Args = [],
            ApplicationName = typeof(WikiToolServer).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = Environments.Production,
        };
        var builder = WebApplication.CreateSlimBuilder(options);
        builder.Logging.ClearProviders();
        builder.WebHost.UseSetting(WebHostDefaults.PreventHostingStartupKey, "true");
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Listen(IPAddress.Loopback, 0);
        });
        builder.Services.AddSingleton(query);
        builder.Services.AddSingleton(coordinator);
        builder.Services
            .AddMcpServer()
            .WithHttpTransport(http => http.Stateless = true)
            .WithTools<WikiMcpTools>();

        var application = builder.Build();
        application.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!IsExpectedHost(context))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            if (!HasExpectedBearerToken(context, expectedTokenBytes))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers[HeaderNames.WWWAuthenticate] = "Bearer";
                return;
            }
            await next(context).ConfigureAwait(false);
        });
        application.MapMcp("/mcp");

        long coordinatorEpoch;
        try
        {
            coordinatorEpoch = coordinator.AttachServer();
        }
        catch
        {
            await application.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        try
        {
            await application.StartAsync(cancellationToken).ConfigureAwait(false);
            var endpoint = GetEndpoint(application);
            return new WikiToolServer(
                application,
                coordinator,
                coordinatorEpoch,
                new WikiToolConnection(endpoint, token));
        }
        catch
        {
            await coordinator.DetachServerAsync(coordinatorEpoch).ConfigureAwait(false);
            await application.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Task StopAsync()
    {
        lock (_stopSync)
            return _stopTask ??= StopCoreAsync();
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task StopCoreAsync()
    {
        try
        {
            await _coordinator.DetachServerAsync(_coordinatorEpoch).ConfigureAwait(false);
            await _application.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            await _application.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static bool IsExpectedHost(HttpContext context) =>
        string.Equals(context.Request.Host.Host, "127.0.0.1", StringComparison.Ordinal)
        && context.Request.Host.Port == context.Connection.LocalPort;

    private static bool HasExpectedBearerToken(HttpContext context, byte[] expectedToken)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var supplied = authorization[prefix.Length..];
        if (supplied.Length != expectedToken.Length || !supplied.All(character => character <= 0x7f))
            return false;
        var suppliedBytes = Encoding.ASCII.GetBytes(supplied);
        return CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedToken);
    }

    private static Uri GetEndpoint(WebApplication application)
    {
        var server = application.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses
            ?? throw new InvalidOperationException("The Wiki Tool server did not publish an address.");
        var address = addresses.Single(value => value.StartsWith("http://127.0.0.1:", StringComparison.Ordinal));
        var baseUri = new Uri(address.EndsWith('/') ? address : address + '/', UriKind.Absolute);
        return new Uri(baseUri, "mcp");
    }

    private static string CreateBearerToken()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        return token.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
