using System.Text.Json.Serialization;

namespace Bistable.Core.Design;

public sealed record ModuleMetadata(
    string Name,
    IReadOnlyList<SignalPort> Ports,
    IReadOnlyList<DesignParameter> Parameters,
    string? OriginalName = null)
{
    [JsonIgnore]
    public string SourceName => string.IsNullOrWhiteSpace(OriginalName) ? Name : OriginalName;

    public IReadOnlyList<SignalPort> Inputs => Ports.Where(static p => p.Direction == SignalDirection.Input).ToArray();

    public IReadOnlyList<SignalPort> Outputs => Ports.Where(static p => p.Direction == SignalDirection.Output).ToArray();
}
