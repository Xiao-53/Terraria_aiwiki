using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Terraria_Wiki.Windows.AI;
using Terraria_Wiki.Windows.Hosting;
using Terraria_Wiki.Windows.Presentation.Settings;

namespace Terraria_Wiki.Windows.Tests;

public sealed class AiSettingsViewModelTests
{
    [Fact]
    public void SecretTextBoxAutomationValueIsMasked()
    {
        var textBox = new SecretTextBox { Text = "secret-value", PasswordChar = '●' };
        var peer = Assert.IsType<SecretTextBoxAutomationPeer>(
            ControlAutomationPeer.CreatePeerForElement(textBox));
        var provider = Assert.IsAssignableFrom<IValueProvider>(peer);

        Assert.Equal(new string('●', "secret-value".Length), provider.Value);
        Assert.DoesNotContain("secret-value", provider.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeepSeekPresetSavesOfficialProviderWithoutEchoingStoredSecret()
    {
        using var directory = new TestDirectory();
        var python = Path.Combine(directory.Path, "python.exe");
        await File.WriteAllBytesAsync(python, [0]);
        var path = Path.Combine(directory.Path, "ai-runtime.json");
        var initial = new AiRuntimeConfigurationDraft(
            python,
            new AiProviderConfiguration("https://custom.example/v1", "custom-model", "chat_completions", null),
            string.Empty,
            path,
            Exists: true,
            HasStoredCredential: true);
        AiRuntimeConfigurationDraft? requested = null;
        var restartCount = 0;
        using var viewModel = new AiSettingsViewModel(
            _ => Task.FromResult(initial),
            (draft, _) =>
            {
                requested = draft;
                return Task.FromResult(AiRuntimeConfiguration.Create(
                    path,
                    python,
                    draft.Provider,
                    "stored-test-key"));
            },
            _ =>
            {
                restartCount++;
                return Task.FromResult(new WindowsAiRuntimeStatus(
                    WindowsAiRuntimeState.Ready,
                    "AI Runtime 已就绪。"));
            });
        viewModel.Attach();
        await viewModel.RefreshAsync();

        viewModel.ProviderIndex = 0;
        viewModel.DeepSeekModelIndex = 1;
        viewModel.ReasoningEffortIndex = 4;
        await viewModel.SaveAsync();

        Assert.NotNull(requested);
        Assert.Equal("https://api.deepseek.com", requested!.Provider.BaseUrl);
        Assert.Equal("deepseek-v4-pro", requested.Provider.Model);
        Assert.Equal("responses", requested.Provider.Api);
        Assert.Equal("max", requested.Provider.ReasoningEffort);
        Assert.Equal(string.Empty, requested.ApiKey);
        Assert.Equal(string.Empty, viewModel.ApiKey);
        Assert.True(viewModel.HasStoredCredential);
        Assert.Equal(1, restartCount);
        Assert.False(viewModel.IsDirty);
        Assert.Contains("已保存并应用", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CustomProviderAndReplacementCredentialAreForwarded()
    {
        using var directory = new TestDirectory();
        var python = Path.Combine(directory.Path, "python.exe");
        await File.WriteAllBytesAsync(python, [0]);
        var path = Path.Combine(directory.Path, "ai-runtime.json");
        var initial = new AiRuntimeConfigurationDraft(
            python,
            new AiProviderConfiguration("https://api.deepseek.com", "deepseek-v4-flash", "responses", null),
            string.Empty,
            path,
            Exists: true,
            HasStoredCredential: true);
        AiRuntimeConfigurationDraft? requested = null;
        using var viewModel = new AiSettingsViewModel(
            _ => Task.FromResult(initial),
            (draft, _) =>
            {
                requested = draft;
                return Task.FromResult(AiRuntimeConfiguration.Create(
                    path,
                    python,
                    draft.Provider,
                    draft.ApiKey));
            },
            _ => Task.FromResult(new WindowsAiRuntimeStatus(
                WindowsAiRuntimeState.Disabled,
                "自定义服务暂未通过运行时验证。")));
        viewModel.Attach();
        await viewModel.RefreshAsync();

        Assert.True(viewModel.IsDeepSeek);
        viewModel.ProviderIndex = 1;
        viewModel.CustomBaseUrl = "https://compatible.example/v1";
        viewModel.CustomModel = "compatible-model";
        viewModel.CustomApiIndex = 1;
        viewModel.ReasoningEffortIndex = 2;
        viewModel.ApiKey = "replacement-test-key";
        await viewModel.SaveAsync();

        Assert.NotNull(requested);
        Assert.Equal("https://compatible.example/v1", requested!.Provider.BaseUrl);
        Assert.Equal("compatible-model", requested.Provider.Model);
        Assert.Equal("chat_completions", requested.Provider.Api);
        Assert.Equal("low", requested.Provider.ReasoningEffort);
        Assert.Equal("replacement-test-key", requested.ApiKey);
        Assert.Contains("已保存", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("暂未通过", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BundledPythonIsReadOnlyAndCredentialStatusDoesNotExposeSecret()
    {
        const string bundledPython = @"C:\app\AgentRuntime\python\python.exe";
        using var viewModel = new AiSettingsViewModel(
            _ => Task.FromResult(new AiRuntimeConfigurationDraft(
                bundledPython,
                new AiProviderConfiguration("https://api.deepseek.com", "deepseek-v4-flash", "responses", null),
                string.Empty,
                @"C:\config\ai-runtime.json",
                Exists: true,
                HasStoredCredential: true)),
            (_, _) => throw new InvalidOperationException(),
            _ => throw new InvalidOperationException());
        viewModel.Attach();

        await viewModel.RefreshAsync();

        Assert.Equal(bundledPython, viewModel.PythonExecutable);
        Assert.True(viewModel.HasStoredCredential);
        Assert.Equal(string.Empty, viewModel.ApiKey);
        Assert.Contains("Windows Credential Manager", viewModel.CredentialStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClearingStoredCredentialDoesNotChangeProviderConfiguration()
    {
        const string bundledPython = @"C:\app\AgentRuntime\python\python.exe";
        var clearCount = 0;
        using var viewModel = new AiSettingsViewModel(
            _ => Task.FromResult(new AiRuntimeConfigurationDraft(
                bundledPython,
                new AiProviderConfiguration("https://api.deepseek.com", "deepseek-v4-flash", "responses", null),
                string.Empty,
                @"C:\config\ai-runtime.json",
                Exists: true,
                HasStoredCredential: true)),
            (_, _) => throw new InvalidOperationException(),
            _ => throw new InvalidOperationException(),
            _ =>
            {
                clearCount++;
                return Task.FromResult(new WindowsAiRuntimeStatus(
                    WindowsAiRuntimeState.Disabled,
                    "AI 已停用。",
                    "model_authentication"));
            });
        viewModel.Attach();
        await viewModel.RefreshAsync();

        await viewModel.ClearCredentialAsync();

        Assert.Equal(1, clearCount);
        Assert.False(viewModel.HasStoredCredential);
        Assert.False(viewModel.CanClearCredential);
        Assert.True(viewModel.IsDeepSeek);
        Assert.Equal(0, viewModel.DeepSeekModelIndex);
        Assert.Contains("已停用", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AiCredentialCanBeEnteredAndSavedWithoutDatabaseRuntime()
    {
        using var directory = new TestDirectory();
        var python = Path.Combine(directory.Path, "python.exe");
        await File.WriteAllBytesAsync(python, [0]);
        var path = Path.Combine(directory.Path, "ai-runtime.json");
        var initial = new AiRuntimeConfigurationDraft(
            python,
            new AiProviderConfiguration("https://api.deepseek.com", "deepseek-v4-flash", "responses", null),
            string.Empty,
            path,
            Exists: false,
            HasStoredCredential: false);
        AiRuntimeConfigurationDraft? requested = null;
        using var settings = new SettingsViewModel(
            loadAiConfiguration: _ => Task.FromResult(initial),
            saveAiConfiguration: (draft, _) =>
            {
                requested = draft;
                return Task.FromResult(AiRuntimeConfiguration.Create(
                    path,
                    python,
                    draft.Provider,
                    draft.ApiKey));
            },
            restartAi: _ => Task.FromResult(new WindowsAiRuntimeStatus(
                WindowsAiRuntimeState.Disabled,
                "选择有效的 Wiki 数据文件夹后即可启动 AI。",
                "data-root-required")));

        Assert.False(settings.IsAvailable);
        Assert.True(settings.Ai.IsAvailable);
        await settings.Ai.RefreshAsync();
        settings.Ai.ApiKey = "new-test-key";
        Assert.True(settings.Ai.CanSave);

        await settings.Ai.SaveAsync();
        settings.Detach();

        Assert.Equal("new-test-key", requested?.ApiKey);
        Assert.True(settings.Ai.IsAvailable);
        Assert.True(settings.Ai.HasStoredCredential);
        Assert.Contains("选择有效", settings.Ai.StatusMessage, StringComparison.Ordinal);
    }
}
