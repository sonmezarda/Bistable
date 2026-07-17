using Bistable.App.Services;
using Bistable.Core.Projects;

namespace Bistable.Tests;

public sealed class ProjectFileWatcherServiceTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task FileWrite_TrackedSource_PublishesDebouncedChange()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"bistable-watcher-write-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string projectPath = Path.Combine(directory, "design.bistable.json");
        string sourcePath = Path.Combine(directory, "top.sv");
        await File.WriteAllTextAsync(projectPath, "{}");
        await File.WriteAllTextAsync(sourcePath, "module top; endmodule");

        try
        {
            using ProjectFileWatcherService watcher = new();
            TaskCompletionSource<ProjectFilesChangedEventArgs> changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            watcher.FilesChanged += (_, e) => changed.TrySetResult(e);
            watcher.Start(
                new ProjectConfiguration { TopModule = "top", Sources = ["top.sv"] },
                projectPath,
                directory,
                debounceMs: 100);

            await File.WriteAllTextAsync(sourcePath, "module top; logic value; endmodule");

            ProjectFilesChangedEventArgs result = await changed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Contains(sourcePath, result.Paths, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task NotifyPathChanged_SaveStorm_CoalescesRelevantFilesOnce()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"bistable-watcher-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string projectPath = Path.Combine(directory, "design.bistable.json");
        string sourcePath = Path.Combine(directory, "top.sv");
        await File.WriteAllTextAsync(projectPath, "{}");
        await File.WriteAllTextAsync(sourcePath, "module top; endmodule");
        ProjectConfiguration project = new() { TopModule = "top", Sources = ["top.sv"] };

        try
        {
            using ProjectFileWatcherService watcher = new();
            TaskCompletionSource<ProjectFilesChangedEventArgs> changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            watcher.FilesChanged += (_, e) => changed.TrySetResult(e);
            watcher.Start(project, projectPath, directory, debounceMs: 100);

            watcher.NotifyPathChanged(sourcePath);
            watcher.NotifyPathChanged(sourcePath);
            watcher.NotifyPathChanged(projectPath);

            ProjectFilesChangedEventArgs result = await changed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(2, result.Paths.Count);
            Assert.Contains(sourcePath, result.Paths, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(projectPath, result.Paths, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task NotifyPathChanged_UntrackedNonHdlFile_IsIgnored()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"bistable-watcher-ignore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string projectPath = Path.Combine(directory, "design.bistable.json");
        string sourcePath = Path.Combine(directory, "top.sv");
        await File.WriteAllTextAsync(projectPath, "{}");
        await File.WriteAllTextAsync(sourcePath, "module top; endmodule");
        try
        {
            using ProjectFileWatcherService watcher = new();
            int events = 0;
            watcher.FilesChanged += (_, _) => Interlocked.Increment(ref events);
            watcher.Start(new ProjectConfiguration { TopModule = "top", Sources = ["top.sv"] }, projectPath, directory, 100);
            watcher.NotifyPathChanged(Path.Combine(directory, "notes.txt"));
            await Task.Delay(180);
            Assert.Equal(0, Volatile.Read(ref events));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
