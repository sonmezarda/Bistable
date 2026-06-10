using Bistable.App.Services.Routing.Elk;
using Bistable.Core.Synthesis;

namespace Bistable.Tests.Synthesis;

/// <summary>
/// Phase 6.5 follow-up: non-destructive bus bundle metadata produced by
/// <see cref="GateNetlistElkBuilder"/>. The graph still carries one ELK port
/// and one ELK edge per bit; bundles only describe which of those edges form
/// a logical bus so the renderer can draw a trunk overlay.
///
/// Tests use hand-built <see cref="GateNetlist"/> fixtures rather than Yosys
/// JSON so each scenario (reversed range, sparse bits, concatenation, etc.)
/// can be expressed precisely without depending on the synthesizer's choices.
/// </summary>
public sealed class GateBusBundleTests
{
    [Fact]
    public void Build_TopLevelBusFromInputToOutput_GroupsAllBitsIntoOneBundle()
    {
        // module wide_buf(input [3:0] d, output [3:0] q); assign q = d; endmodule
        // Boundary input "d" drives boundary output "q" bit-for-bit. No cells.
        // Expectation: one bundle named "d"→"q" with 4 members, msb=3 lsb=0.
        GateNetlist netlist = WideBufNetlist(width: 4);

        GateNetlistElkBuildResult result = GateNetlistElkBuilder.Build(netlist);

        Assert.NotEmpty(result.Bundles);
        GateBusBundle bundle = Assert.Single(result.Bundles);
        Assert.Equal("d", bundle.SourceBaseName);
        Assert.Equal("q", bundle.TargetBaseName);
        Assert.Equal(3, bundle.Msb);
        Assert.Equal(0, bundle.Lsb);
        Assert.Equal(4, bundle.Members.Count);
        // Members come back MSB → LSB.
        Assert.Equal([3, 2, 1, 0], bundle.Members.Select(m => m.BitIndex));
    }

    [Fact]
    public void Build_WideBus_EveryMemberEdgeIsTaggedWithBundleId()
    {
        // The canvas can recover bundle membership for a clicked edge by
        // reading ElkEdge.LayoutOptions[bistable.bundleId] — without that
        // hook the trunk-overlay/hit-test loop would have to reverse-map
        // every edge against the bundle list each frame.
        GateNetlist netlist = WideBufNetlist(width: 4);

        GateNetlistElkBuildResult result = GateNetlistElkBuilder.Build(netlist);

        GateBusBundle bundle = Assert.Single(result.Bundles);
        IReadOnlyDictionary<string, ElkEdge> edges = result.Graph.Edges!
            .ToDictionary(e => e.Id);
        foreach (GateBusBundleMember member in bundle.Members)
        {
            ElkEdge edge = edges[member.EdgeId];
            Assert.NotNull(edge.LayoutOptions);
            Assert.Equal(bundle.Id, edge.LayoutOptions![GateBusBundleKeys.BundleIdLayoutOption]);
        }
    }

    [Fact]
    public void Build_SingleBitScalar_DoesNotProduceBundle()
    {
        // Single-bit wires aren't buses — they must NOT show up as bundles
        // or the renderer would draw spurious trunks over normal scalar
        // signals.
        GateNetlist netlist = WideBufNetlist(width: 1);

        GateNetlistElkBuildResult result = GateNetlistElkBuilder.Build(netlist);

        Assert.Empty(result.Bundles);
        Assert.All(result.Graph.Edges!, e =>
        {
            if (e.LayoutOptions is not null)
            {
                Assert.False(e.LayoutOptions.ContainsKey(GateBusBundleKeys.BundleIdLayoutOption));
            }
        });
    }

    [Fact]
    public void Build_PerBitConnectivityIsPreservedAlongsideBundle()
    {
        // Critical guardrail from the handoff: bundles must NOT replace the
        // per-bit edges. Selection / simulation cross-probe / net highlight
        // all depend on one ELK edge per Yosys net id.
        GateNetlist netlist = WideBufNetlist(width: 4);

        GateNetlistElkBuildResult result = GateNetlistElkBuilder.Build(netlist);

        // 4 bits → 4 emitted edges, regardless of bundling.
        Assert.Equal(4, result.Graph.Edges!.Count);
        // Each edge still carries its net{id} label so canvas hit-testing
        // continues to resolve net ids.
        Assert.All(result.Graph.Edges!, e =>
        {
            string text = e.Labels!.Single().Text;
            Assert.StartsWith("net", text);
        });
    }

    [Fact]
    public void Build_BusSplitToDifferentTargets_DoesNotMerge()
    {
        // Boundary input "d[1:0]" drives two SEPARATE single-bit DFFs.
        // Bits go to different target nodes, so no bundle should form —
        // the heuristic must require both source AND target group equality.
        GateNetlist netlist = BusSplitToTwoDffsNetlist();

        GateNetlistElkBuildResult result = GateNetlistElkBuilder.Build(netlist);

        Assert.Empty(result.Bundles);
    }

    [Fact]
    public void Build_BusWithConstantBit_OnlyRoutableBitsAreBundleMembers()
    {
        // wire [3:0] q; assign q = {1'b0, d[2:0]};
        // Bit 3 is a literal — no producer port exists for it, so no edge,
        // so the bundle has 3 members (bits 0..2), not 4. Msb is the highest
        // *routed* bit, not the declared port width.
        GateNetlist netlist = WideBufWithConstHighBitNetlist();

        GateNetlistElkBuildResult result = GateNetlistElkBuilder.Build(netlist);

        GateBusBundle bundle = Assert.Single(result.Bundles);
        Assert.Equal(3, bundle.Members.Count);
        Assert.Equal(2, bundle.Msb);
        Assert.Equal(0, bundle.Lsb);
        Assert.Equal([2, 1, 0], bundle.Members.Select(m => m.BitIndex));
    }

    // ── Synthetic fixtures ────────────────────────────────────────────────

    private static GateNetlist WideBufNetlist(int width)
    {
        // Top-level pass-through: every bit of input d goes straight to the
        // matching bit of output q. No cells; the producer/consumer maps
        // bridge boundary in → boundary out via shared net ids.
        List<GateBit> dBits = new(width);
        List<GateBit> qBits = new(width);
        for (int i = 0; i < width; i++)
        {
            GateBit shared = GateBit.Net(2 + i);
            dBits.Add(shared);
            qBits.Add(shared);
        }
        GateModule top = new(
            Name: "top",
            Ports:
            [
                new GatePort("d", GatePortDirection.Input,  dBits),
                new GatePort("q", GatePortDirection.Output, qBits),
            ],
            Cells: [],
            Nets: []);
        return new GateNetlist("top", new Dictionary<string, GateModule> { ["top"] = top });
    }

    private static GateNetlist BusSplitToTwoDffsNetlist()
    {
        // d[0] → DFF0.D → q0; d[1] → DFF1.D → q1. Different cell instances,
        // so the two bits target different ELK nodes and cannot bundle.
        GateBit clk = GateBit.Net(2);
        GateBit d0 = GateBit.Net(3);
        GateBit d1 = GateBit.Net(4);
        GateBit q0 = GateBit.Net(5);
        GateBit q1 = GateBit.Net(6);

        GateCell ff0 = new(
            Name: "u_dff0",
            Type: "$_DFF_P_",
            Connections: new Dictionary<string, GateConnection>
            {
                ["D"] = new("D", [d0]),
                ["C"] = new("C", [clk]),
                ["Q"] = new("Q", [q0]),
            },
            PortDirections: new Dictionary<string, GatePortDirection>
            {
                ["D"] = GatePortDirection.Input,
                ["C"] = GatePortDirection.Input,
                ["Q"] = GatePortDirection.Output,
            },
            Parameters: new Dictionary<string, string>(),
            Attributes: new Dictionary<string, string>());
        GateCell ff1 = ff0 with
        {
            Name = "u_dff1",
            Connections = new Dictionary<string, GateConnection>
            {
                ["D"] = new("D", [d1]),
                ["C"] = new("C", [clk]),
                ["Q"] = new("Q", [q1]),
            },
        };

        GateModule top = new(
            Name: "top",
            Ports:
            [
                new GatePort("clk", GatePortDirection.Input, [clk]),
                new GatePort("d",   GatePortDirection.Input, [d0, d1]),
                new GatePort("q0",  GatePortDirection.Output, [q0]),
                new GatePort("q1",  GatePortDirection.Output, [q1]),
            ],
            Cells: [ff0, ff1],
            Nets: []);
        return new GateNetlist("top", new Dictionary<string, GateModule> { ["top"] = top });
    }

    private static GateNetlist WideBufWithConstHighBitNetlist()
    {
        // Output q is declared 4-bit, but q[3] is tied to a literal 0. Only
        // bits 0..2 have a routable producer (the matching d[0..2] inputs),
        // so only those form bundle members.
        List<GateBit> dBits = [GateBit.Net(2), GateBit.Net(3), GateBit.Net(4)];
        // q is declared 4-bit. q[3] is the literal 0; q[0..2] reuse the d
        // net ids so producer/consumer maps emit edges only for those bits.
        List<GateBit> qBits =
        [
            dBits[0], dBits[1], dBits[2], GateBit.ConstantZero,
        ];
        GateModule top = new(
            Name: "top",
            Ports:
            [
                new GatePort("d", GatePortDirection.Input,  dBits),
                new GatePort("q", GatePortDirection.Output, qBits),
            ],
            Cells: [],
            Nets: []);
        return new GateNetlist("top", new Dictionary<string, GateModule> { ["top"] = top });
    }
}
