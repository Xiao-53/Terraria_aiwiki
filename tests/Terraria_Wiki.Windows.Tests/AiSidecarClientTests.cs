using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Terraria_Wiki.Windows.AI;

namespace Terraria_Wiki.Windows.Tests;

[Collection("AI environment")]
public sealed class AiSidecarClientTests
{
    [Fact]
    public async Task HandshakeFatalPreservesSafeDependencyCodeAndCleansProcess()
    {
        await WithApiKeyAsync(async () =>
        {
            var factory = new ScriptedConnectionFactory((message, connection) =>
            {
                var requestId = message.RootElement.GetProperty("requestId").GetString()!;
                connection.Enqueue(Event(
                    "fatal",
                    requestId,
                    "\"error\":{\"code\":\"dependency_mismatch\",\"message\":\"private-path\"}"));
            });

            var error = await Assert.ThrowsAsync<AiRuntimeException>(() => StartAsync(factory));

            Assert.Equal("dependency_mismatch", error.Code);
            Assert.DoesNotContain("private-path", error.ToString(), StringComparison.Ordinal);
            Assert.True(factory.Connection.Killed);
        });
    }

    [Fact]
    public async Task HandshakeStreamsOnlySafeEventsAndReturnsTypedCompletion()
    {
        await WithApiKeyAsync(async () =>
        {
            var factory = new ScriptedConnectionFactory((message, connection) =>
            {
                var type = message.RootElement.GetProperty("type").GetString();
                var requestId = message.RootElement.GetProperty("requestId").GetString()!;
                if (type == "initialize")
                    connection.Enqueue(Ready(requestId));
                else if (type == "answer")
                {
                    connection.Enqueue(Event("run_started", requestId));
                    connection.Enqueue(Event("tool_started", requestId, "\"name\":\"search_wiki\""));
                    connection.Enqueue(Event("tool_finished", requestId, "\"name\":\"search_wiki\",\"status\":\"succeeded\""));
                    connection.Enqueue(Event("tool_started", requestId, "\"name\":\"read_wiki\""));
                    connection.Enqueue(Event("tool_finished", requestId, "\"name\":\"read_wiki\",\"status\":\"succeeded\""));
                    connection.Enqueue(Event("answer_delta", requestId, "\"delta\":\"证据\""));
                    connection.Enqueue(Event("answer_delta", requestId, "\"delta\":\"回答\""));
                    connection.Enqueue(Completed(requestId, "证据回答"));
                }
                else if (type == "shutdown")
                    connection.Exit();
            });
            await using var client = await StartAsync(factory);
            var events = new List<AiRunEvent>();

            var answer = await client.AnswerAsync("问题", value =>
            {
                events.Add(value);
                return ValueTask.CompletedTask;
            });

            Assert.Equal("证据回答", answer.Markdown);
            Assert.Equal("Page", answer.Sources.Single().ResolvedTitle);
            Assert.Equal(7, events.Count);
            Assert.Equal(2, events.Count(value => value.Kind == AiRunEventKind.AnswerDelta));
            Assert.DoesNotContain(events, value => value.Delta?.Contains("http", StringComparison.OrdinalIgnoreCase) == true);
        });
    }

    [Fact]
    public async Task CallerCancellationSendsTargetedCancelAndAllowsAnotherQuestion()
    {
        await WithApiKeyAsync(async () =>
        {
            var answerCount = 0;
            var factory = new ScriptedConnectionFactory((message, connection) =>
            {
                var root = message.RootElement;
                var type = root.GetProperty("type").GetString();
                var requestId = root.GetProperty("requestId").GetString()!;
                if (type == "initialize")
                    connection.Enqueue(Ready(requestId));
                else if (type == "answer" && ++answerCount == 1)
                    connection.Enqueue(Event("run_started", requestId));
                else if (type == "cancel")
                    connection.Enqueue(Event("cancelled", root.GetProperty("targetRequestId").GetString()!));
                else if (type == "answer")
                {
                    connection.Enqueue(Event("run_started", requestId));
                    connection.Enqueue(Event("answer_delta", requestId, "\"delta\":\"再次回答\""));
                    connection.Enqueue(Completed(requestId, "再次回答"));
                }
                else if (type == "shutdown")
                    connection.Exit();
            });
            await using var client = await StartAsync(factory);
            using var cancellation = new CancellationTokenSource();

            var first = client.AnswerAsync("第一个问题", value =>
            {
                if (value.Kind == AiRunEventKind.RunStarted)
                    cancellation.Cancel();
                return ValueTask.CompletedTask;
            }, cancellation.Token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

            var second = await client.AnswerAsync("第二个问题");
            Assert.Equal("再次回答", second.Markdown);
        });
    }

    [Fact]
    public async Task InvalidEventOrderFaultsAndKillsOnlyTheOwnedProcess()
    {
        await WithApiKeyAsync(async () =>
        {
            var factory = new ScriptedConnectionFactory((message, connection) =>
            {
                var type = message.RootElement.GetProperty("type").GetString();
                var requestId = message.RootElement.GetProperty("requestId").GetString()!;
                if (type == "initialize")
                    connection.Enqueue(Ready(requestId));
                else if (type == "answer")
                {
                    connection.Enqueue(Event("run_started", requestId));
                    connection.Enqueue(Event("answer_delta", requestId, "\"delta\":\"text\""));
                    connection.Enqueue(Event("tool_started", requestId, "\"name\":\"search_wiki\""));
                }
            });
            await using var client = await StartAsync(factory);

            var error = await Assert.ThrowsAsync<AiRuntimeException>(() => client.AnswerAsync("问题"));

            Assert.Equal("protocol_violation", error.Code);
            Assert.True(factory.Connection.Killed);
            Assert.True(client.IsFaulted);
        });
    }

    [Fact]
    public async Task CompletionRacingCallerCancellationDrainsControlFailureBeforeReuse()
    {
        await WithApiKeyAsync(async () =>
        {
            var answerCount = 0;
            var factory = new ScriptedConnectionFactory((message, connection) =>
            {
                var root = message.RootElement;
                var type = root.GetProperty("type").GetString();
                var requestId = root.GetProperty("requestId").GetString()!;
                if (type == "initialize")
                    connection.Enqueue(Ready(requestId));
                else if (type == "answer" && ++answerCount == 1)
                {
                    connection.Enqueue(Event("run_started", requestId));
                    connection.Enqueue(Event("answer_delta", requestId, "\"delta\":\"已经完成\""));
                    connection.Enqueue(Completed(requestId, "已经完成"));
                }
                else if (type == "cancel")
                    connection.Enqueue(Event(
                        "failed",
                        requestId,
                        "\"error\":{\"code\":\"request_not_active\",\"message\":\"private\"}"));
                else if (type == "answer")
                {
                    connection.Enqueue(Event("run_started", requestId));
                    connection.Enqueue(Event("answer_delta", requestId, "\"delta\":\"可复用\""));
                    connection.Enqueue(Completed(requestId, "可复用"));
                }
                else if (type == "shutdown")
                    connection.Exit();
            });
            await using var client = await StartAsync(factory);
            using var cancellation = new CancellationTokenSource();

            var first = client.AnswerAsync("竞态问题", value =>
            {
                if (value.Kind == AiRunEventKind.AnswerDelta)
                    cancellation.Cancel();
                return ValueTask.CompletedTask;
            }, cancellation.Token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

            var second = await client.AnswerAsync("复用问题");
            Assert.Equal("可复用", second.Markdown);
        });
    }

    [Fact]
    public async Task FatalEventImmediatelyFaultsAndKillsTheSidecar()
    {
        await WithApiKeyAsync(async () =>
        {
            var factory = new ScriptedConnectionFactory((message, connection) =>
            {
                var type = message.RootElement.GetProperty("type").GetString();
                var requestId = message.RootElement.GetProperty("requestId").GetString()!;
                if (type == "initialize")
                    connection.Enqueue(Ready(requestId));
                else if (type == "answer")
                    connection.Enqueue(Event(
                        "fatal",
                        requestId,
                        "\"error\":{\"code\":\"unexpected_stdout\",\"message\":\"private-details\"}"));
            });
            await using var client = await StartAsync(factory);

            var error = await Assert.ThrowsAsync<AiRuntimeException>(() => client.AnswerAsync("问题"));

            Assert.Equal("unexpected_stdout", error.Code);
            Assert.DoesNotContain("private-details", error.ToString(), StringComparison.Ordinal);
            Assert.True(client.IsFaulted);
            Assert.True(factory.Connection.Killed);
        });
    }

    [Fact]
    public async Task ShutdownUsesOneGracePeriodThenKillsAnUnresponsiveActiveSidecar()
    {
        await WithApiKeyAsync(async () =>
        {
            var runStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var factory = new ScriptedConnectionFactory((message, connection) =>
            {
                var type = message.RootElement.GetProperty("type").GetString();
                var requestId = message.RootElement.GetProperty("requestId").GetString()!;
                if (type == "initialize")
                    connection.Enqueue(Ready(requestId));
                else if (type == "answer")
                {
                    connection.Enqueue(Event("run_started", requestId));
                    runStarted.TrySetResult();
                }
                // Deliberately ignore cancel and shutdown.
            });
            var client = await StartAsync(factory, new AiSidecarShutdownOptions(
                TimeSpan.FromMilliseconds(75),
                TimeSpan.FromMilliseconds(75),
                TimeSpan.FromMilliseconds(75)));
            var answer = client.AnswerAsync("不会结束的问题");
            await runStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

            var timer = System.Diagnostics.Stopwatch.StartNew();
            await client.DisposeAsync();
            timer.Stop();

            Assert.True(factory.Connection.Killed);
            Assert.True(timer.Elapsed < TimeSpan.FromMilliseconds(500), timer.Elapsed.ToString());
            await Assert.ThrowsAnyAsync<Exception>(() => answer);
        });
    }

    private static Task<AiSidecarClient> StartAsync(
        ScriptedConnectionFactory factory,
        AiSidecarShutdownOptions? shutdownOptions = null)
    {
        var configuration = new AiRuntimeConfiguration(
            @"C:\runtime\python.exe",
            new AiProviderConfiguration("https://api.example.test", "model", "responses", null),
            "test-only-key",
            @"C:\config\ai-runtime.json");
        return AiSidecarClient.StartAsync(
            configuration,
            new Uri("http://127.0.0.1:1234/mcp"),
            "private-bearer",
            factory,
            shutdownOptions: shutdownOptions);
    }

    private static async Task WithApiKeyAsync(Func<Task> action)
    {
        var previous = Environment.GetEnvironmentVariable("TERRARIA_AI_API_KEY");
        Environment.SetEnvironmentVariable("TERRARIA_AI_API_KEY", "test-only-key");
        try { await action(); }
        finally { Environment.SetEnvironmentVariable("TERRARIA_AI_API_KEY", previous); }
    }

    private static string Ready(string requestId) =>
        Event("ready", requestId, "\"runtimeVersion\":\"1\",\"pythonVersion\":\"3.12.3\",\"pydanticAiVersion\":\"2.31.0\"");

    private static string Completed(string requestId, string markdown) => JsonSerializer.Serialize(
        new Dictionary<string, object?>
        {
            ["version"] = 1,
            ["type"] = "completed",
            ["requestId"] = requestId,
            ["answer"] = new Dictionary<string, object?>
            {
                ["status"] = "answered",
                ["markdown"] = markdown,
                ["sources"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["requested_title"] = "Alias",
                        ["resolved_title"] = "Page",
                        ["anchor"] = null,
                        ["last_modified"] = null,
                    },
                },
                ["usage"] = new Dictionary<string, object?>
                {
                    ["requests"] = 3,
                    ["tool_calls"] = 2,
                    ["input_tokens"] = 10,
                    ["output_tokens"] = 4,
                    ["total_tokens"] = 14,
                    ["cost_usd"] = null,
                },
                ["tool_trace"] = new[]
                {
                    new Dictionary<string, object?> { ["name"] = "search_wiki", ["status"] = "succeeded" },
                    new Dictionary<string, object?> { ["name"] = "read_wiki", ["status"] = "succeeded" },
                },
            },
        });

    private static string Event(string type, string requestId, string? fields = null) =>
        $"{{\"version\":1,\"type\":\"{type}\",\"requestId\":\"{requestId}\"{(fields is null ? string.Empty : "," + fields)}}}";

    private sealed class ScriptedConnectionFactory(
        Action<JsonDocument, ScriptedConnection> onWrite) : IAiSidecarConnectionFactory
    {
        public ScriptedConnection Connection { get; } = new(onWrite);

        public Task<IAiSidecarConnection> StartAsync(
            AiRuntimeConfiguration configuration,
            string launcherPath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IAiSidecarConnection>(Connection);
        }
    }

    private sealed class ScriptedConnection(
        Action<JsonDocument, ScriptedConnection> onWrite) : IAiSidecarConnection
    {
        private readonly Channel<string> _messages = Channel.CreateUnbounded<string>();
        private readonly TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Killed { get; private set; }
        public bool HasExited => _exit.Task.IsCompleted;
        public int? ExitCode => HasExited ? 0 : null;

        public ValueTask WriteAsync(byte[] line, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var document = JsonDocument.Parse(new ReadOnlyMemory<byte>(line, 0, line.Length - 1));
            onWrite(document, this);
            return ValueTask.CompletedTask;
        }

        public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default) =>
            await _messages.Reader.ReadAsync(cancellationToken);

        public Task WaitForExitAsync(CancellationToken cancellationToken = default) =>
            _exit.Task.WaitAsync(cancellationToken);

        public void KillProcessTree()
        {
            Killed = true;
            Exit();
        }

        public void Enqueue(string message) => _messages.Writer.TryWrite(message);

        public void Exit()
        {
            _messages.Writer.TryComplete();
            _exit.TrySetResult();
        }

        public ValueTask DisposeAsync()
        {
            Exit();
            return ValueTask.CompletedTask;
        }
    }
}

[CollectionDefinition("AI environment", DisableParallelization = true)]
public sealed class AiEnvironmentCollection;
