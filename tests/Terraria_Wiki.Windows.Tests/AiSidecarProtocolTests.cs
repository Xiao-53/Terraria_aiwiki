using System.Text;
using System.Text.Json;
using Terraria_Wiki.Windows.AI;

namespace Terraria_Wiki.Windows.Tests;

public sealed class AiSidecarProtocolTests
{
    [Fact]
    public void AnswerCommandCarriesOnlyBoundedPlainTextHistory()
    {
        var line = AiSidecarProtocol.CreateAnswer(
            "answer-1",
            "当前问题",
            [new AiConversationHistoryTurn("上一问", "上一答")]);
        using var document = JsonDocument.Parse(line.AsMemory(0, line.Length - 1));

        Assert.Equal(2, document.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("当前问题", document.RootElement.GetProperty("question").GetString());
        var turn = Assert.Single(document.RootElement.GetProperty("history").EnumerateArray());
        Assert.Equal(["assistant", "user"], turn.EnumerateObject().Select(item => item.Name).Order());
        Assert.Equal("上一问", turn.GetProperty("user").GetString());
        Assert.Equal("上一答", turn.GetProperty("assistant").GetString());
        Assert.DoesNotContain("tool", Encoding.UTF8.GetString(line), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParsesCompletedTypedAnswerAndRejectsUnknownFields()
    {
        const string json = """
            {"version":2,"type":"completed","requestId":"answer-1","answer":{"status":"answered","markdown":"answer","sources":[{"requested_title":"Alias","resolved_title":"Page","anchor":"Part","last_modified":"2026-08-27T00:00:00Z"}],"usage":{"requests":3,"tool_calls":2,"input_tokens":10,"cached_input_tokens":4,"output_tokens":4,"total_tokens":14,"cost_usd":"0.001"},"tool_trace":[{"name":"search_wiki","status":"succeeded"},{"name":"read_wiki","status":"succeeded"}]}}
            """;

        var message = AiSidecarProtocol.Parse(json);

        Assert.Equal(AiSidecarEventKind.Completed, message.Kind);
        Assert.Equal("answer", message.Answer!.Markdown);
        Assert.Equal("Page", message.Answer.Sources.Single().ResolvedTitle);
        Assert.Equal(4, message.Answer.Usage.CachedInputTokens);
        Assert.Equal(0.001m, message.Answer.Usage.CostUsd);

        var unknown = json.Replace("\"answer\":{", "\"unknown\":true,\"answer\":{");
        var error = Assert.Throws<AiRuntimeException>(() => AiSidecarProtocol.Parse(unknown));
        Assert.Equal("protocol_violation", error.Code);
    }

    [Theory]
    [InlineData("{\"version\":2,\"type\":\"tool_started\",\"requestId\":\"x\",\"name\":\"shell\"}")]
    [InlineData("{\"version\":2,\"type\":\"answer_delta\",\"requestId\":\"x\",\"delta\":\"\"}")]
    [InlineData("{\"version\":1,\"type\":\"run_started\",\"requestId\":\"x\"}")]
    [InlineData("{\"version\":2,\"type\":\"unknown\",\"requestId\":\"x\"}")]
    public void RejectsInvalidEvents(string json)
    {
        var error = Assert.Throws<AiRuntimeException>(() => AiSidecarProtocol.Parse(json));
        Assert.Equal("protocol_violation", error.Code);
    }

    [Fact]
    public void RuntimeLimitMessageDoesNotDescribeAChargeBudget()
    {
        Assert.Equal(
            "本次回答达到运行限制，请缩短问题后重试。",
            AiRuntimeException.SafeMessage("budget_exceeded"));
    }

    [Fact]
    public void ProcessArgumentsContainOnlyIsolatedRuntimeFlagsAndLauncher()
    {
        var configuration = new AiRuntimeConfiguration(
            @"C:\runtime\python.exe",
            new AiProviderConfiguration("https://api.example.test", "secret-looking-model", "responses", null),
            "test-only-key",
            @"C:\config\ai-runtime.json");
        var start = ProcessAiSidecarConnectionFactory.CreateStartInfo(
            configuration,
            @"C:\app\AgentRuntime\sidecar_launcher.py");

        Assert.Equal(configuration.PythonExecutable, start.FileName);
        Assert.Equal(
            ["-I", "-B", "-u", "-X", "utf8", @"C:\app\AgentRuntime\sidecar_launcher.py"],
            start.ArgumentList);
        var arguments = string.Join(' ', start.ArgumentList);
        Assert.DoesNotContain(configuration.Provider.BaseUrl, arguments, StringComparison.Ordinal);
        Assert.DoesNotContain(configuration.Provider.Model, arguments, StringComparison.Ordinal);
        Assert.DoesNotContain("TERRARIA_AI_API_KEY", arguments, StringComparison.Ordinal);
        Assert.DoesNotContain(configuration.ApiKey, arguments, StringComparison.Ordinal);
        Assert.Equal(configuration.ApiKey, start.Environment["TERRARIA_AI_API_KEY"]);
    }

    [Fact]
    public void RejectsLinesAboveProtocolLimit()
    {
        var line = new string('x', AiSidecarProtocol.MaxLineBytes);
        var error = Assert.Throws<AiRuntimeException>(() => AiSidecarProtocol.Parse(line));
        Assert.Equal("protocol_violation", error.Code);
    }
}
