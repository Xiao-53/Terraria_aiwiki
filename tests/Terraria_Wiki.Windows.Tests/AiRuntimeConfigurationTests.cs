using System.Text.Json;
using Terraria_Wiki.Windows.AI;

namespace Terraria_Wiki.Windows.Tests;

public sealed class AiRuntimeConfigurationTests
{
    [Fact]
    public async Task LoadsStrictVersionTwoConfigurationFromAbsoluteOverride()
    {
        using var directory = new TestDirectory();
        var python = Path.Combine(directory.Path, "python.exe");
        await File.WriteAllBytesAsync(python, [0]);
        var path = await WriteConfigurationAsync(directory.Path, python);

        var configuration = await AiRuntimeConfiguration.LoadAsync(["--ai-config", path]);

        Assert.Equal(Path.GetFullPath(python), configuration.PythonExecutable);
        Assert.Equal("https://api.example.test/v1", configuration.Provider.BaseUrl);
        Assert.Equal("provider-model", configuration.Provider.Model);
        Assert.Equal("responses", configuration.Provider.Api);
        Assert.Equal("low", configuration.Provider.ReasoningEffort);
        Assert.Equal("test-only-key", configuration.ApiKey);
        Assert.DoesNotContain(configuration.ApiKey, configuration.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("relative.json", "ai-config-argument-invalid")]
    [InlineData("", "ai-config-argument-missing")]
    public void RejectsInvalidOverride(string path, string code)
    {
        var arguments = path.Length == 0 ? new[] { "--ai-config" } : ["--ai-config", path];
        var error = Assert.Throws<AiConfigurationException>(() =>
            AiRuntimeConfiguration.ParseConfigurationPath(arguments));
        Assert.Equal(code, error.Code);
    }

    [Fact]
    public async Task RejectsUnknownFieldsInvalidKeysAndNonPythonExecutablesWithoutLeakingValues()
    {
        using var directory = new TestDirectory();
        var python = Path.Combine(directory.Path, "python.exe");
        await File.WriteAllBytesAsync(python, [0]);
        var path = await WriteConfigurationAsync(directory.Path, python, includeUnknown: true);

        var unknown = await Assert.ThrowsAsync<AiConfigurationException>(() =>
            AiRuntimeConfiguration.LoadAsync(["--ai-config", path]));
        Assert.Equal("ai-config-invalid", unknown.Code);
        Assert.DoesNotContain("not-a-schema-field", unknown.ToString(), StringComparison.Ordinal);

        path = await WriteConfigurationAsync(directory.Path, python, apiKey: " ");
        var invalidKey = await Assert.ThrowsAsync<AiConfigurationException>(() =>
            AiRuntimeConfiguration.LoadAsync(["--ai-config", path]));
        Assert.Equal("model_authentication", invalidKey.Code);

        var wrongExecutable = Path.Combine(directory.Path, "python3.exe");
        await File.WriteAllBytesAsync(wrongExecutable, [0]);
        path = await WriteConfigurationAsync(directory.Path, wrongExecutable);
        var missing = await Assert.ThrowsAsync<AiConfigurationException>(() =>
            AiRuntimeConfiguration.LoadAsync(["--ai-config", path]));
        Assert.Equal("python-missing", missing.Code);
    }

    [Fact]
    public async Task MissingDraftUsesDeepSeekOfficialDefaultsWithoutCreatingAFile()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "nested", "ai-runtime.json");
        var store = new AiRuntimeConfigurationStore(path);

        var draft = await store.LoadDraftAsync();

        Assert.False(draft.Exists);
        Assert.Equal(string.Empty, draft.PythonExecutable);
        Assert.Equal(string.Empty, draft.ApiKey);
        Assert.Equal("https://api.deepseek.com", draft.Provider.BaseUrl);
        Assert.Equal("deepseek-v4-flash", draft.Provider.Model);
        Assert.Equal("responses", draft.Provider.Api);
        Assert.Null(draft.Provider.ReasoningEffort);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task SavesVersionTwoConfigurationAtomicallyAndRedactsDraftSecret()
    {
        using var directory = new TestDirectory();
        var python = Path.Combine(directory.Path, "python.exe");
        await File.WriteAllBytesAsync(python, [0]);
        var path = Path.Combine(directory.Path, "settings", "ai-runtime.json");
        var store = new AiRuntimeConfigurationStore(path);
        var draft = new AiRuntimeConfigurationDraft(
            python,
            new AiProviderConfiguration(
                "https://api.deepseek.com",
                "deepseek-v4-pro",
                "responses",
                "max"),
            "secret-that-must-not-leak",
            path,
            Exists: false);

        var saved = await store.SaveAsync(draft);
        var loaded = await AiRuntimeConfiguration.LoadAsync(["--ai-config", path]);

        Assert.Equal("deepseek-v4-pro", saved.Provider.Model);
        Assert.Equal("max", loaded.Provider.ReasoningEffort);
        Assert.Equal("secret-that-must-not-leak", loaded.ApiKey);
        Assert.DoesNotContain("secret-that-must-not-leak", draft.ToString(), StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.Equal(2, document.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task InvalidSaveLeavesPreviousConfigurationUntouched()
    {
        using var directory = new TestDirectory();
        var python = Path.Combine(directory.Path, "python.exe");
        await File.WriteAllBytesAsync(python, [0]);
        var path = await WriteConfigurationAsync(directory.Path, python);
        var original = await File.ReadAllBytesAsync(path);
        var store = new AiRuntimeConfigurationStore(path);
        var invalid = new AiRuntimeConfigurationDraft(
            python,
            new AiProviderConfiguration("http://example.com", "model", "responses", null),
            "new-secret",
            path,
            Exists: true);

        var error = await Assert.ThrowsAsync<AiConfigurationException>(() => store.SaveAsync(invalid));

        Assert.Equal("provider-invalid", error.Code);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    private static async Task<string> WriteConfigurationAsync(
        string directory,
        string python,
        bool includeUnknown = false,
        string apiKey = "test-only-key")
    {
        var path = Path.Combine(directory, "ai-runtime.json");
        var provider = new Dictionary<string, object?>
        {
            ["baseUrl"] = "https://api.example.test/v1",
            ["model"] = "provider-model",
            ["api"] = "responses",
            ["reasoningEffort"] = "low",
        };
        var document = new Dictionary<string, object?>
        {
            ["version"] = 2,
            ["pythonExecutable"] = python,
            ["apiKey"] = apiKey,
            ["provider"] = provider,
        };
        if (includeUnknown)
            document["unexpected"] = "not-a-schema-field";
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document));
        return path;
    }
}
