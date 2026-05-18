using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Bistable.App.Services;

public sealed class ProjectDialogService
{
    public async Task<string?> PickProjectFileAsync(Window owner)
    {
        IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Bistable project",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Bistable project")
                {
                    Patterns = ["*.bistable.json", "*.json"]
                }
            ]
        });

        return files.Count == 0 ? null : files[0].Path.LocalPath;
    }
}
