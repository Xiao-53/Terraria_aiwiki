using System.Text.Json;
using Terraria_Wiki.Windows.AI;

namespace Terraria_Wiki.Windows.Tests;

public sealed class AiRuntimeConfigurationTests
{
    [Fact]
    public async Task MigratesStrictVersionTwoConfigurationToCredentialManagerAndVersionThree()
    {
        using var directory = new TestDirectory();
        var python = await CreatePythonAsync(directory.Path);
        var path = await WriteVersionTwoAsync(directory.Path, python);
        var credentials = new MemoryCredentialStore();
        var store = new AiRuntimeConfigurationStore(path, credentials, () => python);

        var configuration = await store.LoadRequiredAsync();

        Assert.Equal(Path.GetFullPath(python), configuration.PythonExecutable);
        Assert.Equal("https://api.example.test/v1", configuration.Provider.BaseUrl);
        Assert.Equal("provider-model", configuration.Provider.Model);
        Assert.Equal("responses", configuration.Provider.Api);
        Assert.Equal("low", configuration.Provider.ReasoningEffort);
        Assert.Equal("test-only-key", configuration.ApiKey);
        Assert.Equal("test-only-key", credentials.Value);
        Assert.DoesNotContain(configuration.ApiKey, configuration.ToString(), StringComparison.Ordinal);
        using var migrated = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.Equal(3, migrated.RootElement.GetProperty("version").GetInt32());
        Assert.False(migrated.RootElement.TryGetProperty("apiKey", out _));
        Assert.False(migrated.RootElement.TryGetProperty("pythonExecutable", out _));
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
    public async Task RejectsUnknownFieldsAndInvalidKeysWithoutLeakingValues()
    {
        using var directory = new TestDirectory();
        var python = await CreatePythonAsync(directory.Path);
        var path = await WriteVersionTwoAsync(directory.Path, python, includeUnknown: true);
        var credentials = new MemoryCredentialStore();
        var store = new AiRuntimeConfigurationStore(path, credentials, () => python);

        var unknown = await Assert.ThrowsAsync<AiConfigurationException>(() => store.LoadRequiredAsync());
        Assert.Equal("ai-config-invalid", unknown.Code);
        Assert.DoesNotContain("not-a-schema-field", unknown.ToString(), StringComparison.Ordinal);
        Assert.Null(credentials.Value);

        path = await WriteVersionTwoAsync(directory.Path, python, apiKey: " ");
        store = new AiRuntimeConfigurationStore(path, credentials, () => python);
        var invalidKey = await Assert.ThrowsAsync<AiConfigurationException>(() => store.LoadRequiredAsync());
        Assert.Equal("model_authentication", invalidKey.Code);
        Assert.Null(credentials.Value);
    }

    [Fact]
    public async Task MissingDraftUsesBundledPythonAndDeepSeekDefaultsWithoutCreatingAFile()
    {
        using var directory = new TestDirectory();
        var python = Path.Combine(directory.Path, "bundle", "python.exe");
        var path = Path.Combine(directory.Path, "nested", "ai-runtime.json");
        var store = new AiRuntimeConfigurationStore(path, new MemoryCredentialStore(), () => python);

        var draft = await store.LoadDraftAsync();

        Assert.False(draft.Exists);
        Assert.False(draft.HasStoredCredential);
        Assert.Equal(python, draft.PythonExecutable);
        Assert.Equal(string.Empty, draft.ApiKey);
        Assert.Equal("https://api.deepseek.com", draft.Provider.BaseUrl);
        Assert.Equal("deepseek-v4-flash", draft.Provider.Model);
        Assert.Equal("responses", draft.Provider.Api);
        Assert.Null(draft.Provider.ReasoningEffort);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task SavesVersionThreeWithoutPlaintextAndDoesNotReadSecretIntoDraft()
    {
        using var directory = new TestDirectory();
        var python = await CreatePythonAsync(directory.Path);
        var path = Path.Combine(directory.Path, "settings", "ai-runtime.json");
        var credentials = new MemoryCredentialStore();
        var store = new AiRuntimeConfigurationStore(path, credentials, () => python);
        var draft = new AiRuntimeConfigurationDraft(
            python,
            new AiProviderConfiguration(
                "https://api.deepseek.com",
                "deepseek-v4-pro",
                "responses",
                "max"),
            "secret-that-must-not-leak",
            path,
            Exists: false,
            HasStoredCredential: false);

        var saved = await store.SaveAsync(draft);
        var loaded = await store.LoadRequiredAsync();
        var reloadedDraft = await store.LoadDraftAsync();

        Assert.Equal("deepseek-v4-pro", saved.Provider.Model);
        Assert.Equal("max", loaded.Provider.ReasoningEffort);
        Assert.Equal("secret-that-must-not-leak", loaded.ApiKey);
        Assert.Equal(string.Empty, reloadedDraft.ApiKey);
        Assert.True(reloadedDraft.HasStoredCredential);
        Assert.DoesNotContain("secret-that-must-not-leak", draft.ToString(), StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
        var json = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("secret-that-must-not-leak", json, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(3, document.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task EmptyEditorValuePreservesExistingCredential()
    {
        using var directory = new TestDirectory();
        var python = await CreatePythonAsync(directory.Path);
        var path = Path.Combine(directory.Path, "ai-runtime.json");
        var credentials = new MemoryCredentialStore { Value = "existing-secret" };
        var store = new AiRuntimeConfigurationStore(path, credentials, () => python);
        var draft = new AiRuntimeConfigurationDraft(
            python,
            new AiProviderConfiguration("https://api.example.test", "model", "responses", null),
            string.Empty,
            path,
            Exists: false,
            HasStoredCredential: true);

        var configuration = await store.SaveAsync(draft);

        Assert.Equal("existing-secret", configuration.ApiKey);
        Assert.Equal("existing-secret", credentials.Value);
    }

    [Fact]
    public async Task InvalidSaveLeavesPreviousConfigurationAndCredentialUntouched()
    {
        using var directory = new TestDirectory();
        var python = await CreatePythonAsync(directory.Path);
        var path = Path.Combine(directory.Path, "ai-runtime.json");
        var credentials = new MemoryCredentialStore { Value = "existing-secret" };
        var store = new AiRuntimeConfigurationStore(path, credentials, () => python);
        await store.SaveAsync(new AiRuntimeConfigurationDraft(
            python,
            new AiProviderConfiguration("https://api.example.test", "model", "responses", null),
            string.Empty,
            path,
            Exists: false,
            HasStoredCredential: true));
        var original = await File.ReadAllBytesAsync(path);
        var invalid = new AiRuntimeConfigurationDraft(
            python,
            new AiProviderConfiguration("http://example.com", "model", "responses", null),
            "new-secret",
            path,
            Exists: true,
            HasStoredCredential: true);

        var error = await Assert.ThrowsAsync<AiConfigurationException>(() => store.SaveAsync(invalid));

        Assert.Equal("provider-invalid", error.Code);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Equal("existing-secret", credentials.Value);
    }

    [Fact]
    public void WindowsCredentialManagerRoundTripsAndDeletesRandomizedTarget()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var target = $"TerrariaAIWiki.Windows.Tests/{Guid.NewGuid():N}";
        var store = new WindowsAiCredentialStore(target);
        try
        {
            store.Write("test-only-credential");
            Assert.Equal("test-only-credential", store.Read());
            store.Delete();
            Assert.Null(store.Read());
        }
        finally
        {
            store.Delete();
        }
    }

    private static async Task<string> CreatePythonAsync(string directory)
    {
        var path = Path.Combine(directory, "python.exe");
        await File.WriteAllBytesAsync(path, [0]);
        return path;
    }

    private static async Task<string> WriteVersionTwoAsync(
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

    private sealed class MemoryCredentialStore : IAiCredentialStore
    {
        public string? Value { get; set; }
        public string? Read() => Value;
        public void Write(string secret) => Value = secret;
        public void Delete() => Value = null;
    }
}
