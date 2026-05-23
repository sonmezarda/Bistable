namespace Bistable.Core.Design.Schematic;

/// <summary>
/// Result of <c>SchematicDecoder.Decode</c>. Layout-agnostic primitive set for one module scope.
/// </summary>
public sealed record SchematicPrimitiveList(
    string ModuleName,
    IReadOnlyList<PortPrimitive> Ports,
    IReadOnlyList<SignalPrimitive> Signals,
    IReadOnlyList<InstancePrimitive> Instances,
    IReadOnlyList<SchematicPrimitive> Logic);
