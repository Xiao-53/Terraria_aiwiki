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
    public async Task DeepSeekPresetSavesOfficialResponsesProviderAndRestartsRuntime()
    {
        using var directory = new TestDirectory();
        var python = Path.Combine(directory.Path, "python.exe");
        await File.WriteAllBytesAsync(python, [0]);
        var path = Path.Combine(directory.Path, "ai-runtime.json");
        var initial = new AiRuntimeConfigurationDraft(
            python,
            new AiProviderConfiguration("https://custom.example/v1", "custom-model", "chat_completions", null),
            "test-key",
            path,
            Exists: true);
        AiRuntimeConfigurationDraft? requested = null;
        var restartCount = 0;
        using var viewModel = new AiSettingsViewModel(
            _ => Task.FromResult(initial),
            (draft, _) =>
            {
                requested = draft;
                return Task.FromResult(AiRuntimeConfiguration.Create(
                    path,
                    draft.PythonExecutable,
                    draft.Provider,
                    draft.ApiKey));
            },
            _ =>
            {
                restartCount++;
                return Task.FromResult(new WindowsAiRuntimeStatus(
                    WindowsAiRuntimeState.Ready,
                    "AI Runtime 已就绪。"));
            },
            _ => Task.FromResult<string?>(null));
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
        Assert.Equal(1, restartCount);
        Assert.False(viewModel.IsDirty);
        Assert.Contains("已保存并应用", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CustomProviderPreservesCompatibleApiAndMapsReasoningChoice()
    {
        using var directory = new TestDirectory();
        var python = Path.Combine(directory.Path, "python.exe");
        await File.WriteAllBytesAsync(python, [0]);
        var path = Path.Combine(directory.Path, "ai-runtime.json");
        var initial = new AiRuntimeConfigurationDraft(
            python,
            new AiProviderConfiguration("https://api.deepseek.com", "deepseek-v4-flash", "responses", null),
            "test-key",
            path,
            Exists: true);
        AiRuntimeConfigurationDraft? requested = null;
        using var viewModel = new AiSettingsViewModel(
            _ => Task.FromResult(initial),
            (draft, _) =>
            {
                requested = draft;
                return Task.FromResult(AiRuntimeConfiguration.Create(
                    path,
                    draft.PythonExecutable,
                    draft.Provider,
                    draft.ApiKey));
            },
            _ => Task.FromResult(new WindowsAiRuntimeStatus(
                WindowsAiRuntimeState.Disabled,
                "自定义服务暂未通过运行时验证。")),
            _ => Task.FromResult<string?>(null));
        viewModel.Attach();
        await viewModel.RefreshAsync();

        Assert.True(viewModel.IsDeepSeek);
        viewModel.ProviderIndex = 1;
        viewModel.CustomBaseUrl = "https://compatible.example/v1";
        viewModel.CustomModel = "compatible-model";
        viewModel.CustomApiIndex = 1;
        viewModel.ReasoningEffortIndex = 2;
        await viewModel.SaveAsync();

        Assert.NotNull(requested);
        Assert.Equal("https://compatible.example/v1", requested!.Provider.BaseUrl);
        Assert.Equal("compatible-model", requested.Provider.Model);
        Assert.Equal("chat_completions", requested.Provider.Api);
        Assert.Equal("low", requested.Provider.ReasoningEffort);
        Assert.Contains("已保存", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("暂未通过", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PythonPickerMarksDraftDirtyWithoutSaving()
    {
        using var directory = new TestDirectory();
        var selected = Path.Combine(directory.Path, "python.exe");
        var path = Path.Combine(directory.Path, "ai-runtime.json");
        using var viewModel = new AiSettingsViewModel(
            _ => Task.FromResult(new AiRuntimeConfigurationDraft(
                string.Empty,
                new AiProviderConfiguration("https://api.deepseek.com", "deepseek-v4-flash", "responses", null),
                string.Empty,
                path,
                Exists: false)),
            (_, _) => throw new InvalidOperationException(),
            _ => throw new InvalidOperationException(),
            _ => Task.FromResult<string?>(selected));
        viewModel.Attach();
        await viewModel.RefreshAsync();

        viewModel.BrowsePythonCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsBusy && viewModel.PythonExecutable == selected);

        Assert.True(viewModel.IsDirty);
        Assert.True(viewModel.CanSave);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.True(condition());
    }
}
