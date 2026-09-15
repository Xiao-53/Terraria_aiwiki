using Terraria_Wiki.Application;
using Terraria_Wiki.Core.Operations;
using Terraria_Wiki.Core.Packages;
using Terraria_Wiki.Windows.Presentation.Management;
using Terraria_Wiki.Windows.Presentation.Settings;

namespace Terraria_Wiki.Windows.Tests;

public sealed class Phase09TakeoverTests
{
    [Fact]
    public async Task UpdateReportsActualDownloadsSkippedDeletedAndIncompleteFailures()
    {
        var active = Wiki(1, "同步测试");
        using var viewModel = new WikiManagementViewModel();
        viewModel.Attach(Actions(
            list: _ => Task.FromResult<IReadOnlyList<WikiBookSnapshot>>([active]),
            active: _ => Task.FromResult(active)) with
        {
            Update = (_, _, _) => Task.FromResult(new WikiOperationSummary(
                Pages: 3, Failed: 1, Changed: true, Checked: 10000, Skipped: 9996, Deleted: 1))
        });
        await viewModel.RefreshAsync();
        await viewModel.UpdateAsync();
        Assert.Contains("未完成", viewModel.StatusMessage);
        Assert.Contains("下载页面 3", viewModel.StatusMessage);
        Assert.Contains("跳过 9,996", viewModel.StatusMessage);
        Assert.Contains("删除 1", viewModel.StatusMessage);
        Assert.Contains("失败 1", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ManagementSwitchesOnlyAfterConfirmationAndRequestsViewerRebind()
    {
        var first = Wiki(1, "泰拉瑞亚中文 Wiki");
        var second = Wiki(2, "Terraria Wiki", folder: "Terraria_Wiki_en");
        var active = first;
        using var viewModel = new WikiManagementViewModel();
        viewModel.Attach(Actions(
            list: _ => Task.FromResult<IReadOnlyList<WikiBookSnapshot>>([first, second]),
            active: _ => Task.FromResult(active),
            switchWiki: (id, _) =>
            {
                active = id == second.Id ? second : first;
                return Task.FromResult(active);
            }));
        WikiDataChangedEventArgs? changed = null;
        viewModel.DataChanged += (_, args) => changed = args;

        await viewModel.RefreshAsync();
        viewModel.RequestSwitch(second);

        Assert.True(viewModel.IsConfirmationVisible);
        Assert.Equal(first.Id, viewModel.ActiveWiki!.Id);

        await viewModel.ConfirmAsync();

        Assert.Equal(second.Id, viewModel.ActiveWiki!.Id);
        Assert.NotNull(changed);
        Assert.True(changed!.RequiresViewerRebind);
        Assert.Contains("已切换", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HostCoordinatedManagementSwitchDoesNotRequestASecondViewerRebind()
    {
        var first = Wiki(1, "泰拉瑞亚中文 Wiki");
        var second = Wiki(2, "Terraria Wiki", folder: "Terraria_Wiki_en");
        var active = first;
        using var viewModel = new WikiManagementViewModel();
        viewModel.Attach(Actions(
            list: _ => Task.FromResult<IReadOnlyList<WikiBookSnapshot>>([first, second]),
            active: _ => Task.FromResult(active),
            switchWiki: (id, _) =>
            {
                active = id == second.Id ? second : first;
                return Task.FromResult(active);
            }) with { SwitchIncludesViewerRebind = true });
        WikiDataChangedEventArgs? changed = null;
        viewModel.DataChanged += (_, args) => changed = args;
        await viewModel.RefreshAsync();

        viewModel.RequestSwitch(second);
        await viewModel.ConfirmAsync();

        Assert.NotNull(changed);
        Assert.False(changed!.RequiresViewerRebind);
    }

    [Fact]
    public async Task DatabaseImportInspectsBeforeConfirmationAndPassesInspectionToken()
    {
        var active = Wiki(1, "泰拉瑞亚中文 Wiki");
        var inspection = new WikiDatabaseInspection(
            @"C:\backups\terraria.db",
            4096,
            "inspection-token");
        string? importedToken = null;
        WikiDataChangedEventArgs? changed = null;
        using var viewModel = new WikiManagementViewModel();
        var actions = Actions(
            list: _ => Task.FromResult<IReadOnlyList<WikiBookSnapshot>>([active]),
            active: _ => Task.FromResult(active)) with
        {
            PickImportDatabase = _ => Task.FromResult<string?>(inspection.DatabasePath),
            InspectDatabase = (path, _) =>
            {
                Assert.Equal(inspection.DatabasePath, path);
                return Task.FromResult(inspection);
            },
            ImportDatabase = (_, token, _) =>
            {
                importedToken = token;
                return Task.FromResult(new WikiDatabaseImportResponse(active, inspection, true));
            },
        };
        viewModel.Attach(actions);
        viewModel.DataChanged += (_, args) => changed = args;
        await viewModel.RefreshAsync();

        await viewModel.BeginImportAsync();

        Assert.True(viewModel.IsConfirmationVisible);
        Assert.Null(importedToken);
        Assert.Contains(active.Title, viewModel.ConfirmationMessage, StringComparison.Ordinal);
        Assert.Contains("替换", viewModel.ConfirmationMessage, StringComparison.Ordinal);

        await viewModel.ConfirmAsync();

        Assert.Equal(inspection.InspectionToken, importedToken);
        Assert.NotNull(changed);
        Assert.True(changed!.RequiresViewerRebind);
        Assert.Contains("已导入", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyWikiOffersDownloadAndPublishesRebindAfterContentArrives()
    {
        var active = Wiki(1, "泰拉瑞亚中文 Wiki", downloaded: false);
        WikiDataChangedEventArgs? changed = null;
        using var viewModel = new WikiManagementViewModel();
        var actions = Actions(
            list: _ => Task.FromResult<IReadOnlyList<WikiBookSnapshot>>([active]),
            active: _ => Task.FromResult(active)) with
        {
            Download = (_, _, _) =>
            {
                active = Wiki(1, "泰拉瑞亚中文 Wiki", downloaded: true);
                return Task.FromResult(new WikiOperationSummary { Pages = 100, Resources = 5 });
            },
        };
        viewModel.Attach(actions);
        viewModel.DataChanged += (_, args) => changed = args;

        await viewModel.RefreshAsync();

        Assert.True(viewModel.NeedsPageData);
        Assert.True(viewModel.DownloadAllCommand.CanExecute(null));
        Assert.Contains("首次使用", viewModel.StatusMessage, StringComparison.Ordinal);

        await viewModel.DownloadAsync(includeResources: true);

        Assert.True(viewModel.HasPageData);
        Assert.False(viewModel.NeedsPageData);
        Assert.NotNull(changed);
        Assert.True(changed!.RequiresViewerRebind);
        Assert.Contains("页面与资源下载完成", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LongDataOperationCanBeCancelledWithoutReportingSuccess()
    {
        var active = Wiki(1, "泰拉瑞亚中文 Wiki", downloaded: false);
        using var viewModel = new WikiManagementViewModel();
        var actions = Actions(
            list: _ => Task.FromResult<IReadOnlyList<WikiBookSnapshot>>([active]),
            active: _ => Task.FromResult(active)) with
        {
            Download = async (_, _, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new WikiOperationSummary();
            },
        };
        viewModel.Attach(actions);
        await viewModel.RefreshAsync();

        var operation = viewModel.DownloadAsync(includeResources: false);
        await WaitUntilAsync(() => viewModel.IsBusy);
        viewModel.CancelOperationCommand.Execute(null);
        await operation;

        Assert.False(viewModel.IsBusy);
        Assert.Contains("已取消", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("页面下载完成：", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(90)]
    public async Task SettingsSaveAllValidatedFieldsAndApplyThemToCurrentSession(int? fullScanDays)
    {
        var original = new ApplicationPreferences { WikiFullScanIntervalDays = fullScanDays };
        ApplicationPreferences? saved = null;
        ApplicationPreferences? applied = null;
        using var viewModel = new SettingsViewModel(value =>
        {
            applied = value;
            return Task.CompletedTask;
        });
        viewModel.Attach(
            _ => Task.FromResult(original),
            (value, _) =>
            {
                saved = value;
                return Task.FromResult(value);
            });
        await viewModel.RefreshAsync();

        viewModel.LanguageIndex = 1;
        viewModel.AppThemeIndex = 2;
        viewModel.ContentThemeIndex = 1;
        viewModel.IsSideButtonBackEnabled = false;
        viewModel.SearchLimit = 75;
        viewModel.PageConcurrency = 3;
        viewModel.ResourceConcurrency = 15;
        viewModel.RetryAttempts = 10;
        Assert.True(viewModel.CanSave);

        await viewModel.SaveAsync();

        Assert.NotNull(saved);
        Assert.Equal("en-us", saved!.Language);
        Assert.Equal("dark", saved.AppTheme);
        Assert.Equal("original", saved.ContentTheme);
        Assert.False(saved.IsSideButtonBackEnabled);
        Assert.Equal(75, saved.SearchLimit);
        Assert.Equal(3, saved.PageConcurrency);
        Assert.Equal(15, saved.ResourceConcurrency);
        Assert.Equal(10, saved.RetryAttempts);
        Assert.Equal(fullScanDays, saved.WikiFullScanIntervalDays);
        Assert.Equal(saved, applied);
        Assert.False(viewModel.IsDirty);
    }

    [Fact]
    public void CompletedExportReplacesExistingTargetOnlyAfterTemporaryFileExists()
    {
        using var directory = new TestDirectory();
        var target = Path.Combine(directory.Path, "wiki.db");
        File.WriteAllText(target, "old");
        var temporary = MainWindow.CreateTemporaryExportPath(target);
        File.WriteAllText(temporary, "new-complete-package");

        MainWindow.CompleteExportTarget(temporary, target);

        Assert.Equal("new-complete-package", File.ReadAllText(target));
        Assert.False(File.Exists(temporary));
    }

    [Fact]
    public void PhaseNinePresentationUsesApplicationBoundaryAndKeepsSingleViewer()
    {
        var root = FindSourceRoot();
        var presentation = string.Join("\n", Directory.GetFiles(
                Path.Combine(root, "Presentation"),
                "*",
                SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText));
        var allXaml = string.Join("\n", Directory.GetFiles(root, "*.axaml", SearchOption.AllDirectories)
            .Select(File.ReadAllText));
        var management = File.ReadAllText(Path.Combine(root, "Presentation", "Management", "WikiManagementViewModel.cs"));
        var managementXaml = File.ReadAllText(Path.Combine(root, "Presentation", "Management", "WikiManagementView.axaml"));
        var shell = File.ReadAllText(Path.Combine(root, "Presentation", "Shell", "ShellViewModel.cs"));

        Assert.Contains("application.ActiveWiki", management, StringComparison.Ordinal);
        Assert.Contains("application.Data", management, StringComparison.Ordinal);
        Assert.Contains("runtime.Application.Settings", shell, StringComparison.Ordinal);
        Assert.Equal(1, Count(shell, "Settings.RefreshAsync()"));
        Assert.Matches("Content=\"检查并更新\"[\\s\\S]*?IsVisible=\"\\{Binding HasPageData\\}\"", managementXaml);
        Assert.Matches("Content=\"下载缺失资源\"[\\s\\S]*?IsVisible=\"\\{Binding NeedsResources\\}\"", managementXaml);
        Assert.DoesNotContain("SelectDataRoot", managementXaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ImportDatabase\"", managementXaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ExportDatabase\"", managementXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("using SQLite", presentation, StringComparison.Ordinal);
        Assert.DoesNotContain("DatabaseService", presentation, StringComparison.Ordinal);
        Assert.Equal(1, Count(allXaml, "<NativeWebView"));
    }

    private static WikiManagementActions Actions(
        Func<CancellationToken, Task<IReadOnlyList<WikiBookSnapshot>>> list,
        Func<CancellationToken, Task<WikiBookSnapshot>> active,
        Func<int, CancellationToken, Task<WikiBookSnapshot>>? switchWiki = null) => new()
    {
        ListWikis = list,
        GetActiveWiki = active,
        GetStatistics = active,
        HasFailures = _ => Task.FromResult(false),
        SwitchWiki = switchWiki ?? ((_, _) => active(CancellationToken.None)),
    };

    private static WikiBookSnapshot Wiki(
        int id,
        string title,
        string folder = "Terraria_Wiki_zh",
        bool downloaded = true) => new(
        id,
        title,
        "本地 Wiki",
        downloaded,
        downloaded,
        downloaded ? 100 : 0,
        downloaded ? 50 : 0,
        downloaded ? 5 : 0,
        downloaded ? 1024 * 1024 : 0,
        downloaded ? new DateTime(2026, 9, 2, 10, 0, 0) : default,
        folder,
        "首页");

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition() && DateTime.UtcNow < timeout)
            await Task.Delay(10);
        Assert.True(condition());
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
