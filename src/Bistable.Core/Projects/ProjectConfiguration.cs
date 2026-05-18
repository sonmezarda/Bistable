using System.Text.Json;

namespace Bistable.Core.Projects;

public sealed record ProjectConfiguration
{
    public required string TopModule { get; init; }

    public IReadOnlyList<string> Sources { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> IncludeDirs { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, string> Defines { get; init; } = new Dictionary<string, string>();

    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();

    public IReadOnlyList<string> VerilatorOptions { get; init; } = Array.Empty<string>();

    public IReadOnlyList<ClockHint> Clocks { get; init; } = Array.Empty<ClockHint>();

    public IReadOnlyList<ResetHint> Resets { get; init; } = Array.Empty<ResetHint>();

    public TraceConfiguration Trace { get; init; } = new();

    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    public static async Task<ProjectConfiguration> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using FileStream stream = File.OpenRead(path);
        ProjectConfiguration? configuration = await JsonSerializer.DeserializeAsync<ProjectConfiguration>(
            stream,
            JsonOptions,
            cancellationToken);

        return configuration is null
            ? throw new InvalidDataException($"Project file '{path}' is empty or invalid.")
            : configuration;
    }
}
