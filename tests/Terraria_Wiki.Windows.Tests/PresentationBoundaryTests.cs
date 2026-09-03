namespace Terraria_Wiki.Windows.Tests;

public sealed class PresentationBoundaryTests
{
    [Fact]
    public void PresentationPinsSafeMarkdownAndLinksExistingBrandAssetsAndNativeIcon()
    {
        var root = FindSourceRoot();
        var project = File.ReadAllText(Path.Combine(root, "Terraria_Wiki.Windows.csproj"));
        var parser = File.ReadAllText(Path.Combine(root, "Presentation", "Markdown", "SafeMarkdownParser.cs"));

        Assert.Contains("Markdig\" Version=\"1.3.2", project, StringComparison.Ordinal);
        Assert.Contains("assets/windows/AppIcon/logo.svg", project, StringComparison.Ordinal);
        Assert.Contains("assets/windows/AppIcon/appicon.ico", project, StringComparison.Ordinal);
        Assert.Contains("<ApplicationIcon>../../assets/windows/AppIcon/appicon.ico</ApplicationIcon>", project, StringComparison.Ordinal);
        Assert.Contains("DisableHtml()", parser, StringComparison.Ordinal);
        Assert.Contains("UseCjkFriendlyEmphasis()", parser, StringComparison.Ordinal);
        Assert.Contains("UsePipeTables()", parser, StringComparison.Ordinal);
        Assert.DoesNotContain("ToHtml", parser, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellCreatesOnlyOneNativeViewerAndExposesCompletePhaseNineNavigation()
    {
        var root = FindSourceRoot();
        var sourceFiles = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                           || path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var combined = string.Join("\n", sourceFiles.Select(File.ReadAllText));
        var xaml = string.Join("\n", sourceFiles
            .Where(path => path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText));
        var shell = File.ReadAllText(Path.Combine(root, "Presentation", "Shell", "ShellView.axaml"));
        var library = File.ReadAllText(Path.Combine(root, "Presentation", "Library", "WikiLibraryView.axaml"));
        var management = File.ReadAllText(Path.Combine(root, "Presentation", "Management", "WikiManagementView.axaml"));
        var settings = File.ReadAllText(Path.Combine(root, "Presentation", "Settings", "SettingsView.axaml"));

        Assert.Equal(1, Count(xaml, "<NativeWebView"));
        Assert.Equal(1, Count(combined, "new ViewerWebViewSession("));
        Assert.Contains("AI 问答导航", shell, StringComparison.Ordinal);
        Assert.Contains("收藏导航", shell, StringComparison.Ordinal);
        Assert.Contains("Wiki 资料库导航", shell, StringComparison.Ordinal);
        Assert.Contains("数据管理导航", shell, StringComparison.Ordinal);
        Assert.Contains("设置导航", shell, StringComparison.Ordinal);
        Assert.Contains("打开 Wiki 预览", shell, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding OpenWikiReaderCommand}\"", shell, StringComparison.Ordinal);
        Assert.Contains("重新选择 Wiki 数据文件夹", shell, StringComparison.Ordinal);
        Assert.Contains("查看全部页面", library, StringComparison.Ordinal);
        Assert.Contains("查看历史记录", library, StringComparison.Ordinal);
        Assert.Contains("确认清空浏览历史", library, StringComparison.Ordinal);
        Assert.Contains("继续加载 Wiki 资料库", library, StringComparison.Ordinal);
        Assert.Contains("选择并检查 Wiki 数据包", management, StringComparison.Ordinal);
        Assert.Contains("确认危险数据操作", management, StringComparison.Ordinal);
        Assert.Contains("保存应用设置", settings, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"GeneralSettingsTab\"", settings, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"AiSettingsTab\"", settings, StringComparison.Ordinal);
        Assert.Equal(1, Count(settings, "<ComboBoxItem Content=\"DeepSeek 官方 API\" />"));
        Assert.Equal(1, Count(settings, "<ComboBoxItem Content=\"自定义 OpenAI-compatible\" />"));
        Assert.Contains("<settings:SecretTextBox", settings, StringComparison.Ordinal);
        Assert.Contains("PasswordChar=\"●\"", settings, StringComparison.Ordinal);
        Assert.Contains("AI 思考强度", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("Wiki 阅读导航", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void KeyboardReaderAndFocusTargetsHaveAutomationNames()
    {
        var root = FindSourceRoot();
        var chat = File.ReadAllText(Path.Combine(root, "Presentation", "Chat", "ChatView.axaml"));
        var chatCode = File.ReadAllText(Path.Combine(root, "Presentation", "Chat", "ChatView.axaml.cs"));
        var reader = File.ReadAllText(Path.Combine(root, "Presentation", "Reader", "ReaderView.axaml"));
        var chatViewModel = File.ReadAllText(Path.Combine(root, "Presentation", "Chat", "ChatViewModel.cs"));
        var app = File.ReadAllText(Path.Combine(root, "App.axaml"));
        var controls = File.ReadAllText(Path.Combine(root, "Presentation", "Styles", "Controls.axaml"));

        Assert.DoesNotContain("KeyDown=\"OnQuestionKeyDown\"", chat, StringComparison.Ordinal);
        Assert.Contains("RoutingStrategies.Tunnel", chatCode, StringComparison.Ordinal);
        Assert.Contains("handledEventsToo: true", chatCode, StringComparison.Ordinal);
        Assert.Contains("TextPresenter", chatCode, StringComparison.Ordinal);
        Assert.Contains("PreeditText", chatCode, StringComparison.Ordinal);
        Assert.Contains("Enter 发送 · Shift+Enter 换行", chat, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"AI 问题输入\"", chat, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"发送问题\"", chat, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"停止回答\"", chat, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Reader 返回\"", reader, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"关闭 Reader\"", reader, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"本地 Wiki Reader\"", reader, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"收藏当前 Wiki 页面\"", reader, StringComparison.Ordinal);
        Assert.Contains("FocusAdorner=\"{x:Null}\"", chat, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"收藏当前对话\"", chat, StringComparison.Ordinal);
        Assert.Contains("Text=\"Token 花费\"", chat, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"AI Token 花费\"", chat, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"Usage\"", chat, StringComparison.Ordinal);
        Assert.DoesNotContain("Classes=\"question-card\"", chat, StringComparison.Ordinal);
        Assert.DoesNotContain("Border.question-card", app, StringComparison.Ordinal);
        Assert.Contains("Text=\"AI 结论\"", chat, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"AI 回答\"", chat, StringComparison.Ordinal);
        Assert.Contains("Classes=\"sources-card\"", chat, StringComparison.Ordinal);
        Assert.Contains("Classes=\"source-row\"", chat, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Stretch\" HorizontalContentAlignment=\"Stretch\"", chat, StringComparison.Ordinal);
        Assert.Contains("Classes=\"answer-meta-card\"", chat, StringComparison.Ordinal);
        Assert.Contains("Classes=\"answer-status\"", chat, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"1\" Classes=\"icon-button\"", chat, StringComparison.Ordinal);
        Assert.Contains("<ScrollViewer Grid.Column=\"0\"", chat, StringComparison.Ordinal);
        Assert.Contains("Width=\"12\" Height=\"12\" CornerRadius=\"2\"", chat, StringComparison.Ordinal);
        Assert.Contains("DensityStyle=\"Compact\"", app, StringComparison.Ordinal);
        Assert.Contains("HorizontalContentAlignment\" Value=\"Center\"", controls, StringComparison.Ordinal);
        Assert.Contains("VerticalContentAlignment\" Value=\"Center\"", controls, StringComparison.Ordinal);
        Assert.Contains("TextBox.composer-input:focus /template/ Border#PART_BorderElement", controls, StringComparison.Ordinal);
        Assert.Contains("Border.thinking-card", controls, StringComparison.Ordinal);
        Assert.Contains("Border.answer-card", controls, StringComparison.Ordinal);
        Assert.DoesNotContain("UseExampleCommand", chat, StringComparison.Ordinal);
        Assert.DoesNotContain("UseExampleCommand", chatViewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("天顶剑怎么合成？", chat, StringComparison.Ordinal);
    }

    [Fact]
    public void FluentTerrariaThemeCentralizesColorAndDefinesCompleteInteractionStates()
    {
        var root = FindSourceRoot();
        var themePath = Path.Combine(root, "Presentation", "Styles", "ThemeResources.axaml");
        var controlsPath = Path.Combine(root, "Presentation", "Styles", "Controls.axaml");
        var theme = File.ReadAllText(themePath);
        var controls = File.ReadAllText(controlsPath);
        var favorites = File.ReadAllText(Path.Combine(root, "Presentation", "Favorites", "FavoritesView.axaml"));
        var library = File.ReadAllText(Path.Combine(root, "Presentation", "Library", "WikiLibraryView.axaml"));
        var presentationXaml = Directory.GetFiles(root, "*.axaml", SearchOption.AllDirectories)
            .Where(path => !string.Equals(path, themePath, StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText)
            .ToArray();
        var viewModel = File.ReadAllText(Path.Combine(root, "Presentation", "Shell", "ShellViewModel.cs"));

        Assert.Contains("x:Key=\"Light\"", theme, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"Dark\"", theme, StringComparison.Ordinal);
        Assert.Contains("Color=\"#5F7F39\"", theme, StringComparison.Ordinal);
        Assert.Contains("Color=\"#FF9BC76A\"", theme, StringComparison.Ordinal);
        Assert.All(presentationXaml, xaml => Assert.DoesNotMatch("#[0-9A-Fa-f]{6,8}", xaml));
        Assert.DoesNotContain("TerrariaBlue", string.Join("\n", presentationXaml), StringComparison.Ordinal);
        Assert.Contains(":pointerover", controls, StringComparison.Ordinal);
        Assert.Contains(":pressed", controls, StringComparison.Ordinal);
        Assert.Contains(":focus-visible", controls, StringComparison.Ordinal);
        Assert.Contains(":disabled", controls, StringComparison.Ordinal);
        Assert.Contains("Button.wiki-preview-button:pressed", controls, StringComparison.Ordinal);
        Assert.Contains("Button.segment-button:pressed", controls, StringComparison.Ordinal);
        Assert.Contains("Button.source-row:pressed", controls, StringComparison.Ordinal);
        Assert.Contains("Button.stop-button:pressed", controls, StringComparison.Ordinal);
        Assert.Contains("Button.toolbar-action:pointerover", controls, StringComparison.Ordinal);
        Assert.Contains("Button.toolbar-action:pressed", controls, StringComparison.Ordinal);
        Assert.Contains("Button.toolbar-action:focus-visible", controls, StringComparison.Ordinal);
        Assert.Contains("Button.toolbar-action:disabled", controls, StringComparison.Ordinal);
        Assert.Contains("Classes=\"toolbar-action\"", favorites, StringComparison.Ordinal);
        Assert.Contains("Classes=\"toolbar-action\"", library, StringComparison.Ordinal);
        Assert.Contains("Button.list-row:disabled", controls, StringComparison.Ordinal);
        Assert.DoesNotContain("TogglePaneCommand", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("NavigationPaneWidth", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("IsPaneOpen", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void WideReaderDividerIsKeyboardNamedAndResizable()
    {
        var root = FindSourceRoot();
        var shell = File.ReadAllText(Path.Combine(root, "Presentation", "Shell", "ShellView.axaml"));

        Assert.Equal(1, Count(shell, "<GridSplitter"));
        Assert.Contains("ResizeBehavior=\"PreviousAndNext\"", shell, StringComparison.Ordinal);
        Assert.Contains("KeyboardIncrement=\"16\"", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("DragCompleted=", shell, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"调整对话与 Wiki 宽度\"", shell, StringComparison.Ordinal);
    }

    private static int Count(string value, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }

    private static string FindSourceRoot([System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "src", "Terraria_Wiki.Windows"));
}
