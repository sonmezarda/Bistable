using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Design.Schematic;

namespace Bistable.Tests.Schematic;

/// <summary>
/// Phase 2.9 P2.9-7. The analyzer's load-bearing guarantee is that a construct
/// the renderer can't materialise must surface as an explicit
/// <see cref="UnsupportedConstructDiagnostic"/> (or
/// <see cref="EndpointCoverageStatus.Unsupported"/>) — never disappear silently.
/// Each test here pins one category of "we used to drop this" so future
/// decoder/builder rewrites can't regress visibility.
/// </summary>
public sealed class SchematicCoverageNegativeTests
{
    [Fact]
    public void Analyze_ContAssignWithConcatLValueContainingUnknownVar_ReportsUnsupported()
    {
        // `{__unknown__, y} = <expr>` — the reader's unknown-l-value fallback
        // emits __unknown__ as a placeholder. The analyzer must treat the
        // composite l-value as unsupported instead of just routing the y half.
        ModuleAst module = Module(contAssigns:
        [
            new ContAssignAst(
                new ConcatLValue([new VarRefLValue("__unknown__"), new VarRefLValue("y")]),
                new SignalRef("a"))
        ]);

        SchematicCoverageReport report = SchematicCoverageAnalyzer.Analyze(module);

        Assert.Equal(0, report.SilentMissCount);
        EndpointCoverage endpoint = Assert.Single(report.Modules.Single().Endpoints,
            e => e.Kind == EndpointKind.ContAssignTarget);
        Assert.Equal(EndpointCoverageStatus.Unsupported, endpoint.Status);
        Assert.Contains("unresolved segment", endpoint.Reason, StringComparison.OrdinalIgnoreCase);

        UnsupportedConstructDiagnostic diagnostic = Assert.Single(report.UnsupportedConstructs);
        Assert.Equal("ContAssignLValue", diagnostic.ConstructKind);
        Assert.Equal(endpoint.EndpointId, diagnostic.ConstructId);
    }

    [Fact]
    public void Analyze_CombinationalBlock_ProducesNoSilentEndpoints()
    {
        // CombinationalBlockAst isn't decoded into primitives yet. We don't
        // expect a Routed endpoint, but we MUST not silently swallow the
        // assignment target.
        ModuleAst module = Module(combinationalBlocks:
        [
            new CombinationalBlockAst(
                new AssignAst(new VarRefLValue("y"), new SignalRef("a"), IsNonBlocking: false))
        ]);

        SchematicCoverageReport report = SchematicCoverageAnalyzer.Analyze(module);

        Assert.Equal(0, report.SilentMissCount);
        // Either Unsupported diagnostic surfaces or no endpoint at all (decoder
        // routed the assign into a buffer). Reachable + "silent" is the only
        // disallowed outcome — Routed without a backing primitive is silent.
        bool foundRoutedWithoutPrimitive = report.Modules
            .Single()
            .Endpoints
            .Any(e => e.SignalName == "y"
                  && e.Status == EndpointCoverageStatus.Routed
                  && e.Kind == EndpointKind.ContAssignTarget);
        Assert.False(foundRoutedWithoutPrimitive,
            "CombinationalBlock targets must not be marked Routed without a backing primitive.");
    }

    [Fact]
    public void Analyze_MultiDriverFromContAssignAndSequentialBlock_BothEndpointsReported()
    {
        // Same signal driven from two places. The schematic can render only one
        // (typically the FF), but the other driver must still appear in the
        // coverage report — silently dropping it would let a real multi-driver
        // bug hide.
        ModuleAst module = Module(
            contAssigns: [new ContAssignAst(new VarRefLValue("q"), new SignalRef("d_async"))],
            sequentialBlocks: [
                new SequentialBlockAst(
                    Triggers: [new EdgeTrigger(EdgeKind.Rising, "clk")],
                    Body: new AssignAst(new VarRefLValue("q"), new SignalRef("d_sync"), IsNonBlocking: true),
                    HasAsynchronousReset: false)
            ]);

        SchematicCoverageReport report = SchematicCoverageAnalyzer.Analyze(module);

        Assert.Equal(0, report.SilentMissCount);
        var qEndpoints = report.Modules.Single().Endpoints
            .Where(e => e.SignalName == "q")
            .ToList();
        Assert.True(qEndpoints.Count >= 2,
            $"Expected both contassign and sequential targets to be reported, got {qEndpoints.Count}");
    }

    [Fact]
    public void Analyze_BoundaryPortAlwaysRouted_EvenWithoutInternalDriver()
    {
        // The phase 2.9 contract says a boundary port is always "Routed" — it's
        // driven externally so the renderer doesn't need an internal primitive
        // for it. This test pins that semantics so a future change can't
        // accidentally demote inputs to SilentMiss.
        ModuleAst module = Module(ports:
        [
            new PortDecl("clk",   SignalDirection.Input,  1, false, 0),
            new PortDecl("rst_n", SignalDirection.Input,  1, false, 1),
            new PortDecl("data",  SignalDirection.Output, 8, false, 2),
        ]);

        SchematicCoverageReport report = SchematicCoverageAnalyzer.Analyze(module);

        Assert.Equal(0, report.SilentMissCount);
        Assert.Equal(0, report.UnsupportedCount);
        Assert.All(
            report.Modules.Single().Endpoints.Where(e => e.Kind == EndpointKind.BoundaryPort),
            e => Assert.Equal(EndpointCoverageStatus.Routed, e.Status));
    }

    [Fact]
    public void Analyze_EmptyModule_ProducesEmptyReportWithoutSilentMisses()
    {
        // Trivial guard: an empty module must produce a clean report. If this
        // ever fails the analyzer has gained a false-positive baseline.
        ModuleAst module = Module();

        SchematicCoverageReport report = SchematicCoverageAnalyzer.Analyze(module);

        Assert.Equal(0, report.SilentMissCount);
        Assert.Equal(0, report.UnsupportedCount);
        Assert.Empty(report.Modules.Single().Endpoints);
    }

    [Fact]
    public void Analyze_SequentialBlockWritingArrayCell_ReportsEndpointForMemoryWrite()
    {
        // `regs[idx] <= d` — array-select l-value. Memory writes are a known
        // gap in the renderer; if it produces no primitive, the analyzer must
        // still note the target so the coverage report flags it.
        ModuleAst module = new(
            Name: "top",
            IsTop: true,
            Ports: [
                new PortDecl("clk", SignalDirection.Input, 1, false, 0),
                new PortDecl("idx", SignalDirection.Input, 4, false, 1),
                new PortDecl("d",   SignalDirection.Input, 8, false, 2),
            ],
            Parameters: [],
            LocalSignals: [new SignalDecl("regs", 8, false, [new BitRange(15, 0)])],
            Instances: [],
            ContAssigns: [],
            SequentialBlocks: [
                new SequentialBlockAst(
                    Triggers: [new EdgeTrigger(EdgeKind.Rising, "clk")],
                    Body: new AssignAst(
                        new ArraySelectLValue("regs", new SignalRef("idx")),
                        new SignalRef("d"),
                        IsNonBlocking: true),
                    HasAsynchronousReset: false)
            ],
            CombinationalBlocks: []);

        SchematicCoverageReport report = SchematicCoverageAnalyzer.Analyze(module);

        Assert.Equal(0, report.SilentMissCount);
        // Acceptable: either Routed (decoder produced a memory write primitive)
        // or Unsupported (didn't), but never absent or SilentMiss.
        var regsEndpoints = report.Modules.Single().Endpoints
            .Where(e => e.Kind == EndpointKind.SequentialTarget && e.SignalName == "regs")
            .ToList();
        Assert.NotEmpty(regsEndpoints);
    }

    private static ModuleAst Module(
        IReadOnlyList<PortDecl>? ports = null,
        IReadOnlyList<SignalDecl>? locals = null,
        IReadOnlyList<ContAssignAst>? contAssigns = null,
        IReadOnlyList<SequentialBlockAst>? sequentialBlocks = null,
        IReadOnlyList<CombinationalBlockAst>? combinationalBlocks = null) =>
        new(
            Name: "top",
            IsTop: true,
            Ports: ports ?? [],
            Parameters: [],
            LocalSignals: locals ?? [],
            Instances: [],
            ContAssigns: contAssigns ?? [],
            SequentialBlocks: sequentialBlocks ?? [],
            CombinationalBlocks: combinationalBlocks ?? []);
}
