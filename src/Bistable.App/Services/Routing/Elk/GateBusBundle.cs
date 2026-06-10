namespace Bistable.App.Services.Routing.Elk;

// Phase 6.5 follow-up: non-destructive bus presentation metadata. The graph
// still contains one ELK port and one ELK edge per bit so per-bit selection,
// simulation cross-probe, and routing remain bit-accurate. A bundle records
// "these N edges form one logical bus from this driver port group to that
// receiver port group" so overview/compact LOD can draw one trunk + fan-out
// without throwing the bit-level connectivity away.
public sealed record GateBusBundle(
    string Id,
    string LogicalName,
    int Msb,
    int Lsb,
    string SourceNodeId,
    string SourceBaseName,
    string TargetNodeId,
    string TargetBaseName,
    IReadOnlyList<GateBusBundleMember> Members);

public sealed record GateBusBundleMember(
    int BitIndex,
    int NetId,
    string SourcePortId,
    string TargetPortId,
    string EdgeId);

internal static class GateBusBundleKeys
{
    // Layout option key written onto each member edge so the canvas/hit-test
    // can recover the bundle for a clicked edge without reverse-mapping by id.
    public const string BundleIdLayoutOption = "bistable.bundleId";
}

// Surfaced to UI listeners when the user clicks any wire that belongs to a
// bus bundle. The full bundle record is included so a properties panel can
// show the logical bus name, range, and member count without re-reading the
// build result.
public sealed record GateBusBundleSelection(GateBusBundle Bundle);
