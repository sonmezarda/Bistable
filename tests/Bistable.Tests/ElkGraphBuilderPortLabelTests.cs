using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;
using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Design.Schematic;

namespace Bistable.Tests;

/// <summary>
/// Phase 2 P2-5/P2-7: each primitive node carries a port-label glyph (D / > / R / Q /
/// 0 / 1 / S / G / Y) that the renderer paints inside the symbol body.
/// These tests lock the glyph contract so symbol renderers can rely on it.
/// </summary>
public sealed class ElkGraphBuilderPortLabelTests
{
    private static HierarchyScopePortViewModel In(string name, int width = 8) =>
        new(name, SignalDirection.Input, width, false);

    private static HierarchyScopePortViewModel Out(string name, int width = 8) =>
        new(name, SignalDirection.Output, width, false);

    // ── FlipFlop ─────────────────────────────────────────────────────────

    [Fact]
    public void FlipFlop_Ports_LabeledD_Clock_Q()
    {
        FlipFlopPrimitive ff = new(
            "ff_q_0", "q", "clk", EdgeKind.Rising, null, null, "d_in", 8);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("clk", 1), In("d_in")], [], [], [], Primitives: [ff]),
            compactLayout: true);

        ElkNode node = Assert.Single(result.Graph.Children, n => ElkNodeIds.IsFlipFlop(n.Id));
        ElkPort dPort   = Assert.Single(node.Ports!, p => p.Id.EndsWith(".d"));
        ElkPort clkPort = Assert.Single(node.Ports!, p => p.Id.EndsWith(".clk"));
        ElkPort qPort   = Assert.Single(node.Ports!, p => p.Id.EndsWith(".q"));

        Assert.Equal("D", Assert.Single(dPort.Labels!).Text);
        Assert.Equal(">", Assert.Single(clkPort.Labels!).Text);   // edge-trigger glyph
        Assert.Equal("Q", Assert.Single(qPort.Labels!).Text);
    }

    [Fact]
    public void FlipFlop_AsyncReset_PortLabeledR()
    {
        FlipFlopPrimitive ff = new(
            "ff_q_0", "q", "clk", EdgeKind.Rising, "rst_n", EdgeKind.Falling, "d_in", 8);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("clk", 1), In("rst_n", 1), In("d_in")], [], [], [], Primitives: [ff]),
            compactLayout: true);

        ElkNode node = Assert.Single(result.Graph.Children, n => ElkNodeIds.IsFlipFlop(n.Id));
        ElkPort rstPort = Assert.Single(node.Ports!, p => p.Id.EndsWith(".rst"));
        Assert.Equal("R", Assert.Single(rstPort.Labels!).Text);
    }

    // ── Mux ──────────────────────────────────────────────────────────────

    [Fact]
    public void Mux_BranchInputs_LabeledFromDecoder()
    {
        // Branch labels "1" and "0" come from the decoder's MuxInput.Label
        MuxPrimitive mux = new(
            "mux_y_0", "y",
            SelectSignals: ["sel"],
            Inputs: [
                new("1", new MuxSignalSource("a")),
                new("0", new MuxSignalSource("b")),
            ],
            Width: 8);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("a"), In("b"), In("sel", 1), Out("y")], [], [], [], Primitives: [mux]),
            compactLayout: true);

        ElkNode node = Assert.Single(result.Graph.Children, n => ElkNodeIds.IsMux(n.Id));
        ElkPort in0 = Assert.Single(node.Ports!, p => p.Id.EndsWith(".in.0"));
        ElkPort in1 = Assert.Single(node.Ports!, p => p.Id.EndsWith(".in.1"));
        Assert.Equal("1", Assert.Single(in0.Labels!).Text);
        Assert.Equal("0", Assert.Single(in1.Labels!).Text);
    }

    [Fact]
    public void Mux_SingleSelector_LabeledS()
    {
        MuxPrimitive mux = new(
            "mux_y_0", "y",
            SelectSignals: ["sel"],
            Inputs: [new("1", new MuxSignalSource("a")), new("0", new MuxSignalSource("b"))],
            Width: 8);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("a"), In("b"), In("sel", 1)], [], [], [], Primitives: [mux]),
            compactLayout: true);

        ElkPort sel = Assert.Single(result.Graph.Children.Single(n => ElkNodeIds.IsMux(n.Id)).Ports!,
            p => p.Id.EndsWith(".sel.0"));
        Assert.Equal("S", Assert.Single(sel.Labels!).Text);
    }

    [Fact]
    public void Mux_MultipleSelectors_LabeledS0AndS1()
    {
        MuxPrimitive mux = new(
            "mux_y_0", "y",
            SelectSignals: ["s1", "s0"],
            Inputs: [
                new("1", new MuxSignalSource("a")),
                new("1", new MuxSignalSource("b")),
                new("0", new MuxSignalSource("c")),
            ],
            Width: 8);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("a"), In("b"), In("c"), In("s0", 1), In("s1", 1)], [], [], [], Primitives: [mux]),
            compactLayout: true);

        var node = result.Graph.Children.Single(n => ElkNodeIds.IsMux(n.Id));
        ElkPort sel0 = Assert.Single(node.Ports!, p => p.Id.EndsWith(".sel.0"));
        ElkPort sel1 = Assert.Single(node.Ports!, p => p.Id.EndsWith(".sel.1"));
        Assert.Equal("S0", Assert.Single(sel0.Labels!).Text);
        Assert.Equal("S1", Assert.Single(sel1.Labels!).Text);
    }

    [Fact]
    public void Mux_Output_LabeledY()
    {
        MuxPrimitive mux = new(
            "mux_y_0", "y",
            SelectSignals: ["sel"],
            Inputs: [new("1", new MuxSignalSource("a")), new("0", new MuxSignalSource("b"))],
            Width: 8);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("a"), In("b"), In("sel", 1)], [], [], [], Primitives: [mux]),
            compactLayout: true);

        ElkPort outPort = Assert.Single(result.Graph.Children.Single(n => ElkNodeIds.IsMux(n.Id)).Ports!,
            p => p.Id.EndsWith(".out"));
        Assert.Equal("Y", Assert.Single(outPort.Labels!).Text);
    }

    // ── Latch ────────────────────────────────────────────────────────────

    [Fact]
    public void Latch_Ports_LabeledD_G_Q()
    {
        LatchPrimitive latch = new("latch_q_0", "q", "g", "d", 8);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("d"), In("g", 1)], [], [], [], Primitives: [latch]),
            compactLayout: true);

        ElkNode node = Assert.Single(result.Graph.Children, n => ElkNodeIds.IsLatch(n.Id));
        ElkPort dPort = Assert.Single(node.Ports!, p => p.Id.EndsWith(".d"));
        ElkPort gPort = Assert.Single(node.Ports!, p => p.Id.EndsWith(".g"));
        ElkPort qPort = Assert.Single(node.Ports!, p => p.Id.EndsWith(".q"));
        Assert.Equal("D", Assert.Single(dPort.Labels!).Text);
        Assert.Equal("G", Assert.Single(gPort.Labels!).Text);
        Assert.Equal("Q", Assert.Single(qPort.Labels!).Text);
    }

    // ── Latch has no clock-edge marker on G ──────────────────────────────
    // (this is a contract test: ensure ">" never appears on a Latch port; that's
    //  reserved for the FF clock pin only)
    [Fact]
    public void Latch_GatePort_DoesNotUseEdgeTriggerGlyph()
    {
        LatchPrimitive latch = new("latch_q_0", "q", "g", "d", 8);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("d"), In("g", 1)], [], [], [], Primitives: [latch]),
            compactLayout: true);

        ElkNode node = Assert.Single(result.Graph.Children, n => ElkNodeIds.IsLatch(n.Id));
        Assert.DoesNotContain(node.Ports!, p =>
            p.Labels is { Count: > 0 } && p.Labels[0].Text == ">");
    }

    // ── No port labels leak across primitive types ───────────────────────

    [Fact]
    public void EachPrimitive_PortLabels_DoNotOverlapAcrossTypes()
    {
        // FF uses {D, >, R, Q}; Latch uses {D, G, Q}; Mux uses {<branchLabel>, S/S0/S1, Y}.
        // No "D" should bleed onto Mux ports, no "0/1" should bleed onto FF, etc.
        FlipFlopPrimitive ff = new("ff_q_0", "q", "clk", EdgeKind.Rising, null, null, "d_in", 8);
        MuxPrimitive mux = new(
            "mux_y_0", "y",
            SelectSignals: ["sel"],
            Inputs: [new("1", new MuxSignalSource("a")), new("0", new MuxSignalSource("b"))],
            Width: 8);
        LatchPrimitive latch = new("latch_z_0", "z", "g", "d", 8);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                [In("clk", 1), In("d_in"), In("a"), In("b"), In("sel", 1), In("d"), In("g", 1)],
                [], [], [],
                Primitives: [ff, mux, latch]),
            compactLayout: true);

        var ffNode    = result.Graph.Children.Single(n => ElkNodeIds.IsFlipFlop(n.Id));
        var muxNode   = result.Graph.Children.Single(n => ElkNodeIds.IsMux(n.Id));
        var latchNode = result.Graph.Children.Single(n => ElkNodeIds.IsLatch(n.Id));

        var ffLabels    = ffNode.Ports!.Select(p => p.Labels![0].Text).ToHashSet();
        var muxLabels   = muxNode.Ports!.Select(p => p.Labels![0].Text).ToHashSet();
        var latchLabels = latchNode.Ports!.Select(p => p.Labels![0].Text).ToHashSet();

        Assert.Equal(new[] { "D", ">", "Q" }.ToHashSet(), ffLabels);
        Assert.Equal(new[] { "1", "0", "S", "Y" }.ToHashSet(), muxLabels);
        Assert.Equal(new[] { "D", "G", "Q" }.ToHashSet(), latchLabels);

        // FF and Latch share "D" and "Q" (legitimate — both have data/output pins) but
        // not the discriminating glyphs:
        Assert.Contains(">", ffLabels);   // edge-trigger glyph only on FF
        Assert.DoesNotContain(">", latchLabels);
        Assert.Contains("G", latchLabels); // gate label only on Latch
        Assert.DoesNotContain("G", ffLabels);
    }
}
