using Bistable.App.Services.Routing.Elk;

namespace Bistable.Tests.Routing;

public sealed class SchematicRoutingLimitDiagnosticTests
{
    private static readonly SchematicGraphMetrics Metrics = new(5572, 20196, 14056);

    [Fact]
    public void BuildMessage_ExpandedScope_DirectsUserToCollapseModules()
    {
        string message = SchematicRoutingLimitDiagnostic.BuildMessage(
            "riscv_single_cycle_top",
            Metrics,
            expandedInstancePaths: ["u_registers"],
            hasHierarchicalChildren: true,
            isTopScope: true);

        Assert.Contains("'u_registers'", message, StringComparison.Ordinal);
        Assert.Contains("Collapse", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Re-synthesize", message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMessage_LargeLeaf_ExplainsThatResynthesisWillNotHelp()
    {
        string message = SchematicRoutingLimitDiagnostic.BuildMessage(
            "riscv_register_file",
            Metrics,
            expandedInstancePaths: [],
            hasHierarchicalChildren: false,
            isTopScope: false);

        Assert.Contains("Leaf scope 'riscv_register_file'", message, StringComparison.Ordinal);
        Assert.Contains("Re-synthesis will not reduce", message, StringComparison.Ordinal);
        Assert.Contains("Up or the breadcrumb", message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMessage_HierarchicalScope_DirectsUserToSmallerChild()
    {
        string message = SchematicRoutingLimitDiagnostic.BuildMessage(
            "cpu_cluster",
            Metrics,
            expandedInstancePaths: [],
            hasHierarchicalChildren: true,
            isTopScope: false);

        Assert.Contains("hierarchy retained", message, StringComparison.Ordinal);
        Assert.Contains("smaller child module", message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMessage_MonolithicTop_RecommendsDisablingFlatten()
    {
        string message = SchematicRoutingLimitDiagnostic.BuildMessage(
            "monolithic_top",
            Metrics,
            expandedInstancePaths: [],
            hasHierarchicalChildren: false,
            isTopScope: true);

        Assert.Contains("Flatten disabled", message, StringComparison.Ordinal);
        Assert.Contains("logic-cone or macro view", message, StringComparison.Ordinal);
    }
}
