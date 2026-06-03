using System.Text.Json.Serialization;

namespace Bistable.Core.Design.Schematic;

/// <summary>
/// Result of <c>SchematicDecoder.Decode</c>. Layout-agnostic primitive set for one module scope.
/// </summary>
public sealed record SchematicPrimitiveList(
    string ModuleName,
    IReadOnlyList<PortPrimitive> Ports,
    IReadOnlyList<SignalPrimitive> Signals,
    IReadOnlyList<InstancePrimitive> Instances,
    IReadOnlyList<SchematicPrimitive> Logic,
    IReadOnlyList<MultiDriverDiagnostic>? Diagnostics = null,
    [property: JsonIgnore]
    IReadOnlyList<SchematicDecoderCoverageEvent>? CoverageEvents = null);

/// <summary>
/// P2.6-5: a signal whose value is computed by more than one driver in the
/// module. Usually a bug (intentional OR-tied tri-state buses are an exception);
/// flagged so the schematic can paint a warning triangle near the convergence
/// point.
/// </summary>
/// <param name="SignalName">The over-driven net.</param>
/// <param name="DriverDescriptions">One short description per driver (assign/always/instance.out).</param>
public sealed record MultiDriverDiagnostic(
    string SignalName,
    IReadOnlyList<string> DriverDescriptions);

/// <summary>
/// Decoder-side coverage event for a source construct that was routed,
/// intentionally omitted, or could not be materialized.
/// </summary>
public sealed record SchematicDecoderCoverageEvent(
    string ModuleName,
    string EndpointId,
    string SignalName,
    EndpointKind EndpointKind,
    EndpointCoverageStatus Status,
    string Reason,
    string? UnsupportedConstructKind = null);
