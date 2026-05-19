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

    public LayoutStateService(string? layoutPath = null)
    {
        LayoutPath = layoutPath ?? Path.Combine(FindRepositoryRoot(), ".bistable", "layout.json");
    }

    public async Task<LayoutState> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(LayoutPath))
        {
            return LayoutState.Default;
        }

        string json = await File.ReadAllTextAsync(LayoutPath, cancellationToken);
        LayoutState? state = JsonSerializer.Deserialize<LayoutState>(json, JsonOptions);
        if (state is null)
        {
            return LayoutState.Default;
        }

        if (!json.Contains("\"schematicDockZone\"", StringComparison.Ordinal))
        {
            state = state with { SchematicDockZone = LayoutState.Default.SchematicDockZone };
        }

        return state;
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
