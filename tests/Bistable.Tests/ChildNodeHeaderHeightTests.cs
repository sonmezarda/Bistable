using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;
using Bistable.Core.Design;

namespace Bistable.Tests;

/// <summary>
/// Phase 2.5 P2.5-2: sub-instance child boxes must reserve enough vertical space
/// in their header so the title never collides with the first port row.
/// Pre-fix bug: ModuleHeaderHeight=36 + 13pt title + 14px port label = visible
/// overlap on arnicomp's jump_decoder (title "jump_decoder" ↔ first port
/// "jmp_cond[3b]").
/// Fix: bumped ModuleHeaderHeight to 48 + title baseline pushed down to y+8.
/// </summary>
public sealed class ChildNodeHeaderHeightTests
{
    private static HierarchyScopeInstanceViewModel MakeChild(
        params HierarchyScopeInstancePortConnectionViewModel[] ports) =>
        new(
            hierarchyPath: "top.jd",
            instanceName: "jd",
            moduleName: "jump_decoder",
            inputCount: ports.Count(p => p.IsInput),
            outputCount: ports.Count(p => p.IsOutput),
            exactSignalCount: 0, descendantSignalCount: 0,
            ports);

    // ── Header height ─────────────────────────────────────────────────────

    [Fact]
    public void ChildNode_Height_AccommodatesHeaderPlusPortsPlusFooter()
    {
        // 4 input ports + 1 output → max 4 rows. With ModuleHeaderHeight=48,
        // PortRowHeight=22, ModuleFooterHeight=16: 48 + 4*22 + 16 = 152.
        var ports = new[]
        {
            new HierarchyScopeInstancePortConnectionViewModel("jmp_cond", "x", isInput: true, width: 3),
            new HierarchyScopeInstancePortConnectionViewModel("carry_flag", "x", isInput: true, width: 1),
            new HierarchyScopeInstancePortConnectionViewModel("zero_flag", "x", isInput: true, width: 1),
            new HierarchyScopeInstancePortConnectionViewModel("jgt", "x", isInput: true, width: 1),
            new HierarchyScopeInstancePortConnectionViewModel("jmp_taken", "x", isInput: false, width: 1),
        };
        var child = MakeChild(ports);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([], [child], [], []),
            compactLayout: true);

        var childNode = Assert.Single(result.Graph.Children, n => n.Id == "child_top_jd");
        // Min height of 80 OR computed (48+4*22+16=152) — whichever is bigger
        Assert.True(childNode.Height >= 152,
            $"Child node height ({childNode.Height}) should accommodate header (48) + 4*portRow (88) + footer (16) = 152+");
    }

    [Fact]
    public void ChildNode_HeaderHeight_GreaterThanPreFixValue()
    {
        // Regression guard: ModuleHeaderHeight must not fall back below 48.
        // A 1-port child's height = max(80, headerHeight + 1*22 + 16).
        //   Post-fix (48): max(80, 86) = 86
        //   Pre-fix  (36): max(80, 74) = 80
        // So if the header drops below 42, this 1-port height test fires.
        var child1 = MakeChild(
            new HierarchyScopeInstancePortConnectionViewModel("p", "x", isInput: true, width: 1));
        ElkBuildResult result1 = new ElkGraphBuilder().Build(
            new ElkScopeData([], [child1], [], []),
            compactLayout: true);
        var node1 = Assert.Single(result1.Graph.Children, n => n.Id == "child_top_jd");
        Assert.True(node1.Height >= 86,
            $"1-port child height ({node1.Height}) should be at least 86 (post-fix). Pre-fix would be 80.");
    }

    // ── Title rendering uses ellipsis when too long ───────────────────────

    [Fact]
    public void ChildNode_Title_StoredAsFirstLabel()
    {
        var child = MakeChild(
            new HierarchyScopeInstancePortConnectionViewModel("p", "x", isInput: true, width: 1));
        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([], [child], [], []),
            compactLayout: true);

        var node = Assert.Single(result.Graph.Children, n => n.Id == "child_top_jd");
        Assert.NotNull(node.Labels);
        Assert.Equal("jd", node.Labels![0].Text);   // instance name is the rendered title
    }

    [Fact]
    public void ChildNode_LongPortLabel_PortStillPresent_WidthAccommodatesOrEllipsizes()
    {
        // A child with a very long port name. Width should expand to fit OR the
        // renderer ellipsizes (visual concern, but the BUILDER must at least
        // include the port). Either way, the port must exist.
        var ports = new[]
        {
            new HierarchyScopeInstancePortConnectionViewModel(
                "very_long_port_name_that_should_not_break_layout", "x", isInput: true, width: 8),
        };
        var child = MakeChild(ports);
        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([], [child], [], []),
            compactLayout: true);

        var childNode = Assert.Single(result.Graph.Children, n => n.Id == "child_top_jd");
        ElkPort onlyPort = Assert.Single(
            childNode.Ports!,
            static port => !ElkGraphBuilder.IsHeaderSpacerPort(port.Id));
        // The builder doesn't ellipsize labels (that's the renderer's job).
        // Verify the port label IS the full original name.
        Assert.Contains("very_long_port_name", onlyPort.Labels![0].Text);
    }
}
