using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Terraria_Wiki.Windows.Hosting;

internal sealed class AvaloniaDataRootPicker
{
    private readonly TopLevel _owner;

    public AvaloniaDataRootPicker(TopLevel owner) =>
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    public async Task<string?> PickAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var folders = await _owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = "选择包含 Manager.db 的 Terraria Wiki 数据文件夹",
        });
        cancellationToken.ThrowIfCancellationRequested();
        return folders.FirstOrDefault()?.Path.LocalPath;
    }
}
