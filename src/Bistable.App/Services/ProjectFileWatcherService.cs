using Bistable.Core.Projects;

namespace Bistable.App.Services;

/// <summary>
/// Watches project sources, include trees, and the project file. Editor save
/// storms are coalesced into one change set after a configurable debounce.
/// </summary>
public sealed class ProjectFileWatcherService : IDisposable
{
    private static readonly HashSet<string> HdlExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".sv", ".svh", ".v", ".vh" };

    private readonly object _gate = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly HashSet<string> _sourceFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _includeRoots = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingPaths = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _debounceCancellation;
    private string? _projectFilePath;
    private int _debounceMs = 400;
    private bool _disposed;

    public event EventHandler<ProjectFilesChangedEventArgs>? FilesChanged;

    public void Start(
        ProjectConfiguration project,
        string projectFilePath,
        string projectDirectory,
        int debounceMs)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ObjectDisposedException.ThrowIf(_disposed, this);

        Stop();
        _debounceMs = Math.Clamp(debounceMs, 100, 5000);
        _projectFilePath = Path.GetFullPath(projectFilePath);
        foreach (string source in project.Sources)
        {
            _sourceFiles.Add(ResolvePath(projectDirectory, source));
        }
        foreach (string includeDir in project.IncludeDirs)
        {
            string root = ResolvePath(projectDirectory, includeDir);
            if (Directory.Exists(root)) _includeRoots.Add(root);
        }

        Dictionary<string, bool> directories = new(StringComparer.OrdinalIgnoreCase);
        AddWatchDirectory(directories, Path.GetDirectoryName(_projectFilePath), recursive: false);
        foreach (string source in _sourceFiles)
        {
            AddWatchDirectory(directories, Path.GetDirectoryName(source), recursive: false);
        }
        foreach (string includeRoot in _includeRoots)
        {
            AddWatchDirectory(directories, includeRoot, recursive: true);
        }

        foreach ((string directory, bool recursive) in directories)
        {
            FileSystemWatcher watcher = new(directory)
            {
                IncludeSubdirectories = recursive,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;
            watcher.Deleted += OnChanged;
            watcher.Renamed += OnRenamed;
            _watchers.Add(watcher);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _debounceCancellation?.Cancel();
            _debounceCancellation?.Dispose();
            _debounceCancellation = null;
            _pendingPaths.Clear();
        }
        foreach (FileSystemWatcher watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Changed -= OnChanged;
            watcher.Created -= OnChanged;
            watcher.Deleted -= OnChanged;
            watcher.Renamed -= OnRenamed;
            watcher.Dispose();
        }
        _watchers.Clear();
        _sourceFiles.Clear();
        _includeRoots.Clear();
        _projectFilePath = null;
    }

    private void OnChanged(object sender, FileSystemEventArgs e) => NotifyPathChanged(e.FullPath);

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        NotifyPathChanged(e.OldFullPath);
        NotifyPathChanged(e.FullPath);
    }

    internal void NotifyPathChanged(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (!IsRelevant(fullPath)) return;

        CancellationTokenSource cancellation;
        lock (_gate)
        {
            if (_disposed) return;
            _pendingPaths.Add(fullPath);
            _debounceCancellation?.Cancel();
            _debounceCancellation?.Dispose();
            cancellation = new CancellationTokenSource();
            _debounceCancellation = cancellation;
        }
        _ = PublishAfterDebounceAsync(cancellation);
    }

    private async Task PublishAfterDebounceAsync(CancellationTokenSource owner)
    {
        try
        {
            await Task.Delay(_debounceMs, owner.Token).ConfigureAwait(false);
            string[] paths;
            lock (_gate)
            {
                if (!ReferenceEquals(_debounceCancellation, owner)) return;
                paths = _pendingPaths.Order(StringComparer.OrdinalIgnoreCase).ToArray();
                _pendingPaths.Clear();
                _debounceCancellation = null;
            }
            owner.Dispose();
            if (paths.Length > 0)
            {
                FilesChanged?.Invoke(this, new ProjectFilesChangedEventArgs(paths));
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private bool IsRelevant(string path)
    {
        if (string.Equals(path, _projectFilePath, StringComparison.OrdinalIgnoreCase)) return true;
        if (_sourceFiles.Contains(path)) return true;
        if (!HdlExtensions.Contains(Path.GetExtension(path))) return false;
        return _includeRoots.Any(root => IsUnderRoot(path, root));
    }

    private static bool IsUnderRoot(string path, string root)
    {
        string relative = Path.GetRelativePath(root, path);
        return !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }

    private static void AddWatchDirectory(Dictionary<string, bool> directories, string? directory, bool recursive)
    {
        if (directory is null || !Directory.Exists(directory)) return;
        string fullPath = Path.GetFullPath(directory);
        directories[fullPath] = recursive || directories.GetValueOrDefault(fullPath);
    }

    private static string ResolvePath(string root, string path) =>
        Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(path, root);

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }
}

public sealed class ProjectFilesChangedEventArgs(IReadOnlyList<string> paths) : EventArgs
{
    public IReadOnlyList<string> Paths { get; } = paths;
}
