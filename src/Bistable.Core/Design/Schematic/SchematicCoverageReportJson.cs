using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bistable.Core.Design.Schematic;

/// <summary>
/// Stable JSON artifact format for schematic coverage reports.
/// Used by tests today and by future CI/UI export paths.
/// </summary>
public static class SchematicCoverageReportJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static string Serialize(SchematicCoverageReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, Options);
    }

    public static SchematicCoverageReport Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<SchematicCoverageReport>(json, Options)
            ?? throw new InvalidDataException("Schematic coverage report JSON was empty.");
    }

    public static void Write(string path, SchematicCoverageReport report)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(report);

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, Serialize(report));
    }

    public static async Task WriteAsync(
        string path,
        SchematicCoverageReport report,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(report);

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using FileStream stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, report, Options, cancellationToken);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
