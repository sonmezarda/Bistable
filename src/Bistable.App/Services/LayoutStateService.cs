using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bistable.App.Services;

public sealed class LayoutStateService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    static LayoutStateService()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    public string LayoutPath { get; }

    public LayoutStateService()
    {
        string root = FindRepositoryRoot();
        LayoutPath = Path.Combine(root, ".bistable", "layout.json");
    }

    public async Task<LayoutState> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(LayoutPath))
        {
            return LayoutState.Default;
        }

        await using FileStream stream = File.OpenRead(LayoutPath);
        LayoutState? state = await JsonSerializer.DeserializeAsync<LayoutState>(stream, JsonOptions, cancellationToken);
        return state ?? LayoutState.Default;
    }

    public async Task SaveAsync(LayoutState state, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LayoutPath) ?? ".");
        await using FileStream stream = File.Create(LayoutPath);
        await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Bistable.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? Directory.GetCurrentDirectory();
    }
}
