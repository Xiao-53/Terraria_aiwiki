using Terraria_Wiki.Windows.Presentation.Settings;
using Terraria_Wiki.Windows.Updates;

namespace Terraria_Wiki.Windows.Tests;

public sealed class ProductUpdateViewModelTests
{
    [Theory]
    [InlineData("https://github.com/owner/repository", true)]
    [InlineData("https://github.com/owner/repository/", true)]
    [InlineData("http://github.com/owner/repository", false)]
    [InlineData("https://example.com/owner/repository", false)]
    [InlineData("https://github.com/owner/repository/extra", false)]
    [InlineData("https://github.com/owner/repository?token=secret", false)]
    public void RepositoryUrlAllowsOnlyPublicGitHubRepositoryShape(string value, bool expected) =>
        Assert.Equal(expected, ProductMetadata.IsGitHubRepositoryUrl(value));

    [Fact]
    public async Task CheckDownloadAndRestartRequireExplicitUserSteps()
    {
        var service = new FakeUpdateService
        {
            CheckResult = new ProductUpdateAvailable("1.2.3"),
        };
        var restartCount = 0;
        using var viewModel = new ProductUpdateViewModel(
            service,
            () =>
            {
                restartCount++;
                return Task.CompletedTask;
            },
            _ => Task.CompletedTask);

        Assert.Equal(ProductUpdateState.Idle, viewModel.State);
        Assert.Equal(0, service.CheckCount);
        Assert.Equal(0, service.DownloadCount);

        await viewModel.CheckAsync();
        Assert.Equal(ProductUpdateState.Available, viewModel.State);
        Assert.Equal("1.2.3", viewModel.AvailableVersion);
        Assert.Equal(1, service.CheckCount);
        Assert.Equal(0, service.DownloadCount);

        await viewModel.DownloadAsync();
        Assert.Equal(ProductUpdateState.ReadyToRestart, viewModel.State);
        Assert.Equal(100, viewModel.DownloadProgress);
        Assert.Equal(1, service.DownloadCount);
        Assert.Equal(0, restartCount);

        viewModel.RestartCommand.Execute(null);
        await WaitUntilAsync(() => restartCount == 1);
    }

    [Fact]
    public async Task NetworkFailureAndCancellationKeepCurrentVersionUsable()
    {
        var failure = new FakeUpdateService { CheckError = new HttpRequestException("private detail") };
        using var failedViewModel = new ProductUpdateViewModel(
            failure,
            () => Task.CompletedTask,
            _ => Task.CompletedTask);

        await failedViewModel.CheckAsync();

        Assert.Equal(ProductUpdateState.Failed, failedViewModel.State);
        Assert.DoesNotContain("private detail", failedViewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("当前版本不受影响", failedViewModel.StatusMessage, StringComparison.Ordinal);

        var cancellation = new FakeUpdateService { WaitForCancellation = true };
        using var cancelledViewModel = new ProductUpdateViewModel(
            cancellation,
            () => Task.CompletedTask,
            _ => Task.CompletedTask);
        var check = cancelledViewModel.CheckAsync();
        await WaitUntilAsync(() => cancelledViewModel.State == ProductUpdateState.Checking);
        cancelledViewModel.CancelCommand.Execute(null);
        await check;

        Assert.Equal(ProductUpdateState.Cancelled, cancelledViewModel.State);
    }

    [Fact]
    public void UnconfiguredBuildDisablesNetworkCommands()
    {
        var service = new FakeUpdateService(canUpdate: false);
        using var viewModel = new ProductUpdateViewModel(
            service,
            () => Task.CompletedTask,
            _ => Task.CompletedTask);

        Assert.Equal(ProductUpdateState.Disabled, viewModel.State);
        Assert.False(viewModel.CanCheck);
        Assert.False(viewModel.CheckCommand.CanExecute(null));
        Assert.Equal(0, service.CheckCount);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.True(condition());
    }

    private sealed class FakeUpdateService(bool canUpdate = true) : IProductUpdateService
    {
        public ProductUpdateAvailable? CheckResult { get; init; }
        public Exception? CheckError { get; init; }
        public bool WaitForCancellation { get; init; }
        public int CheckCount { get; private set; }
        public int DownloadCount { get; private set; }
        public ProductUpdateCapabilities Capabilities { get; } = new(
            canUpdate,
            false,
            "1.0.0",
            canUpdate ? new Uri("https://github.com/owner/repository/releases") : null,
            canUpdate ? "可以更新。" : "未配置更新源。");

        public async Task<ProductUpdateAvailable?> CheckAsync(
            CancellationToken cancellationToken = default)
        {
            CheckCount++;
            if (WaitForCancellation)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (CheckError is not null)
                throw CheckError;
            return CheckResult;
        }

        public Task DownloadAsync(
            IProgress<int> progress,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DownloadCount++;
            progress.Report(63);
            return Task.CompletedTask;
        }

        public void ApplyAndRestart() => throw new NotSupportedException();
    }
}
