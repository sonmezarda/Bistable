using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;
using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Design.Schematic;

namespace Bistable.Tests;

public sealed class ElkGraphBuilderCoverageTests
{
    [Fact]
    public void Analyze_SimpleBuilderOutput_HasNoStructuralErrors()
    {
        HierarchyScopePortViewModel input = new("instruction", SignalDirection.Input, 32, isSigned: false);
        HierarchyScopeInstanceViewModel child = Child(
            "top.u_ctrl",
            "ctrl",
            [new HierarchyScopeInstancePortConnectionViewModel("opcode", "instruction", isInput: true, width: 7)]);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([input], [child], [], []),
            compactLayout: true);

        ElkGraphCoverageReport report = ElkGraphCoverageAnalyzer.Analyze(result);

        Assert.False(report.HasErrors);
        Assert.DoesNotContain(report.Diagnostics, static d => d.Severity == ElkGraphCoverageSeverity.Error);
        Assert.Equal(result.Graph.Edges.Count, report.EdgeCount);
        Assert.Equal(1, report.ProducerSignalCount);
        Assert.Equal(1, report.ConsumerSignalCount);
        Assert.Equal(0, report.DanglingConsumerSignalCount);
    }

    [Fact]
    public void Analyze_EdgeEndpointWithMissingPort_ReportsUnresolvedEndpoint()
    {
        ElkBuildResult result = BuildAliasedChildInput();
        result.Graph.Edges.Add(new ElkEdge
        {
            Id = "bad",
            Sources = ["boundary_in.a"],
            Targets = ["child_top_u_ctrl.in.missing"]
        });

        ElkGraphCoverageReport report = ElkGraphCoverageAnalyzer.Analyze(result);

        ElkGraphCoverageDiagnostic diagnostic = Assert.Single(
            report.Diagnostics,
            static d => d.Kind == ElkGraphCoverageDiagnosticKind.UnresolvedEdgeEndpoint);
        Assert.Equal(ElkGraphCoverageSeverity.Error, diagnostic.Severity);
        Assert.Equal("bad", diagnostic.SubjectId);
    }

    [Fact]
    public void Analyze_EdgeEndpointWithNodeId_ReportsNodeEndpoint()
    {
        ElkBuildResult result = BuildAliasedChildInput();
        result.Graph.Edges.Add(new ElkEdge
        {
            Id = "node_endpoint",
            Sources = ["boundary_in.a"],
            Targets = ["child_top_u_ctrl"]
        });

        ElkGraphCoverageReport report = ElkGraphCoverageAnalyzer.Analyze(result);

        ElkGraphCoverageDiagnostic diagnostic = Assert.Single(
            report.Diagnostics,
            static d => d.Kind == ElkGraphCoverageDiagnosticKind.EdgeEndpointIsNode);
        Assert.Equal(ElkGraphCoverageSeverity.Error, diagnostic.Severity);
        Assert.Equal("node_endpoint", diagnostic.SubjectId);
    }

    [Fact]
    public void Analyze_PortRefWithMissingGraphPort_ReportsMissingPortRefPort()
    {
        ElkBuildResult result = BuildAliasedChildInput();
        Dictionary<string, ElkPortRef> portRefs = new(result.PortRefs, StringComparer.OrdinalIgnoreCase)
        {
            ["bad.ref"] = new ElkPortRef("child_top_u_ctrl", "child_top_u_ctrl.in.missing", ElkPortRole.ChildInput, 1)
        };

        ElkGraphCoverageReport report = ElkGraphCoverageAnalyzer.Analyze(result.Graph, portRefs);

        ElkGraphCoverageDiagnostic diagnostic = Assert.Single(
            report.Diagnostics,
            static d => d.Kind == ElkGraphCoverageDiagnosticKind.MissingPortRefPort);
        Assert.Equal(ElkGraphCoverageSeverity.Error, diagnostic.Severity);
        Assert.Equal("bad.ref", diagnostic.SubjectId);
    }

    [Fact]
    public void Analyze_ExpandedCompoundWithNestedPorts_ResolvesRecursivePorts()
    {
        JoinerPrimitive joiner = new("join_bus_0", "bus", ["a", "b"], OutputWidth: 16);
        HierarchyScopeInstanceViewModel leaf = Child(
            "top.cmp_i.leaf_i",
            "leaf",
            [new HierarchyScopeInstancePortConnectionViewModel("bus", "bus", isInput: true, width: 16)]);
        HierarchyScopeInstanceViewModel compound = Child(
            "top.cmp_i",
            "cmp",
            [
                new HierarchyScopeInstancePortConnectionViewModel("a", "a", isInput: true, width: 8),
                new HierarchyScopeInstancePortConnectionViewModel("b", "b", isInput: true, width: 8)
            ],
            [leaf]);

        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "top.cmp_i" };
        var primitivesByModule = new Dictionary<string, IReadOnlyList<SchematicPrimitive>>(StringComparer.OrdinalIgnoreCase)
        {
            ["cmp"] = [joiner]
        };

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                BoundaryPorts: [],
                ChildScopes: [compound],
                LocalSignals: [],
                ContAssigns: [],
                ExpandedPaths: expanded,
                PrimitivesByModule: primitivesByModule),
            compactLayout: true);

        ElkGraphCoverageReport report = ElkGraphCoverageAnalyzer.Analyze(result);

        Assert.False(report.HasErrors);
        Assert.Contains(result.PortRefs.Values, static r => r.PortId == "child_top_cmp_i_leaf_i.in.bus");
        Assert.True(report.PortCount >= result.PortRefs.Values.Select(static r => r.PortId).Distinct().Count());
    }

    [Fact]
    public void Analyze_ChildInputWithoutProducer_ReportsDanglingConsumerWarning()
    {
        HierarchyScopeInstanceViewModel child = Child(
            "top.u_ctrl",
            "ctrl",
            [new HierarchyScopeInstancePortConnectionViewModel("enable", "missing_enable", isInput: true, width: 1)]);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(BoundaryPorts: [], ChildScopes: [child], LocalSignals: [], ContAssigns: []),
            compactLayout: true);

        ElkGraphCoverageReport report = ElkGraphCoverageAnalyzer.Analyze(result);

        Assert.False(report.HasErrors);
        Assert.Equal(1, report.DanglingConsumerSignalCount);
        ElkGraphCoverageDiagnostic diagnostic = Assert.Single(
            report.Diagnostics,
            static d => d.Kind == ElkGraphCoverageDiagnosticKind.DanglingConsumerSignal);
        Assert.Equal(ElkGraphCoverageSeverity.Warning, diagnostic.Severity);
        Assert.Equal("missing_enable", diagnostic.SubjectId);
    }

    [Fact]
    public void Analyze_PruneRemovesOrphanPrimitivePortRefsFromFinalBuildResult()
    {
        FlipFlopPrimitive orphan = new(
            Id: "ff_reg_q",
            QSignal: "reg_q",
            ClockSignal: "clk",
            ClockEdge: EdgeKind.Rising,
            AsyncResetSignal: null,
            AsyncResetEdge: EdgeKind.Falling,
            DSignal: "d",
            Width: 1);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                BoundaryPorts: [],
                ChildScopes: [],
                LocalSignals: [],
                ContAssigns: [],
                Primitives: [orphan]),
            compactLayout: true);

        ElkGraphCoverageReport report = ElkGraphCoverageAnalyzer.Analyze(result);

        Assert.False(report.HasErrors);
        Assert.DoesNotContain(result.Graph.Children, static n => n.Id.StartsWith("ff_", StringComparison.Ordinal));
        Assert.DoesNotContain(result.PortRefs.Values, static r => r.Role is ElkPortRole.FlipFlopD or ElkPortRole.FlipFlopQ);
        Assert.DoesNotContain(report.Diagnostics, static d => d.Kind == ElkGraphCoverageDiagnosticKind.MissingPortRefPort);
    }

    private static ElkBuildResult BuildAliasedChildInput()
    {
        HierarchyScopePortViewModel input = new("a", SignalDirection.Input, 1, isSigned: false);
        HierarchyScopeInstanceViewModel child = Child(
            "top.u_ctrl",
            "ctrl",
            [new HierarchyScopeInstancePortConnectionViewModel("a", "a", isInput: true, width: 1)]);

        return new ElkGraphBuilder().Build(
            new ElkScopeData([input], [child], [], []),
            compactLayout: true);
    }

    private static HierarchyScopeInstanceViewModel Child(
        string hierarchyPath,
        string moduleName,
        IReadOnlyList<HierarchyScopeInstancePortConnectionViewModel> ports,
        IReadOnlyList<HierarchyScopeInstanceViewModel>? grandchildren = null) =>
        new(
            hierarchyPath,
            hierarchyPath.Split('.')[^1],
            moduleName,
            ports.Count(static port => port.IsInput),
            ports.Count(static port => port.IsOutput),
            exactSignalCount: 0,
            descendantSignalCount: 0,
            ports,
            childInstances: grandchildren);
}
