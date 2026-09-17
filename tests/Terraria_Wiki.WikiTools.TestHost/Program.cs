using System.Text;
using System.Text.Json;
using Terraria_Wiki.WikiTools.TestHost;

Console.InputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

try
{
    if (args.Length != 0 && (args.Length != 2 || args[0] != "--snapshot"))
        throw new ArgumentException("Invalid test host arguments.");
    await using var host = await WikiToolTestHost.StartAsync(snapshotPath: args.Length == 2 ? args[1] : null);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        endpoint = host.Connection.Endpoint.AbsoluteUri,
        bearer_token = host.Connection.BearerToken,
    }));
    await Console.Out.FlushAsync();
    await Console.In.ReadLineAsync();
}
catch (OperationCanceledException)
{
    Environment.ExitCode = 2;
}
catch
{
    Console.Error.WriteLine("wiki-test-host-failed");
    Environment.ExitCode = 1;
}
