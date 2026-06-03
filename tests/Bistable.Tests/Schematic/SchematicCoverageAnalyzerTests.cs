using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Design.Schematic;

namespace Bistable.Tests.Schematic;

public sealed class SchematicCoverageAnalyzerTests
{
    [Fact]
    public void Decode_UnsupportedContAssign_EmitsCoverageEvent()
    {
        ModuleAst module = Module(contAssigns:
        [
            new ContAssignAst(
                new VarRefLValue("y"),
                new FunctionCallExpr("user_func", [new SignalRef("a")]))
        ]);

        SchematicPrimitiveList primitives = SchematicDecoder.Decode(module);

        SchematicDecoderCoverageEvent coverageEvent = Assert.Single(primitives.CoverageEvents!);
        Assert.Equal("contassign:0:y", coverageEvent.EndpointId);
        Assert.Equal(EndpointCoverageStatus.Unsupported, coverageEvent.Status);
        Assert.Equal("ContAssign", coverageEvent.UnsupportedConstructKind);
    }

    [Fact]
    public void Decode_RoutedSequentialBlock_EmitsCoverageEvent()
    {
        ModuleAst module = Module(sequentialBlocks:
        [
            new SequentialBlockAst(
                Triggers: [new EdgeTrigger(EdgeKind.Rising, "clk")],
                Body: new AssignAst(new VarRefLValue("q"), new SignalRef("d"), IsNonBlocking: true),
                HasAsynchronousReset: false)
        ]);

        SchematicPrimitiveList primitives = SchematicDecoder.Decode(module);

        SchematicDecoderCoverageEvent coverageEvent = Assert.Single(primitives.CoverageEvents!);
        Assert.Equal("sequential:0:q", coverageEvent.EndpointId);
        Assert.Equal(EndpointCoverageStatus.Routed, coverageEvent.Status);
        Assert.Null(coverageEvent.UnsupportedConstructKind);
    }

    [Fact]
    public void Analyze_BufferContAssign_ReportsRoutedEndpointWithoutUnsupported()
    {
        ModuleAst module = Module(contAssigns:
        [
            new ContAssignAst(new VarRefLValue("y"), new SignalRef("a"))
        ]);

        SchematicCoverageReport report = SchematicCoverageAnalyzer.Analyze(module);

        Assert.Equal(0, report.SilentMissCount);
        Assert.Equal(0, report.UnsupportedCount);
        Assert.Contains(report.Modules.Single().Endpoints,
            endpoint => endpoint.EndpointId.StartsWith("contassign:0:y", StringComparison.Ordinal)
                     && endpoint.Status == EndpointCoverageStatus.Routed);
    }

    [Fact]
    public void Analyze_UnsupportedContAssign_ReportsExplicitDiagnosticNotSilentMiss()
    {
        ModuleAst module = Module(contAssigns:
        [
            new ContAssignAst(
                new VarRefLValue("y"),
                new FunctionCallExpr("user_func", [new SignalRef("a")]))
        ]);

        SchematicCoverageReport report = SchematicCoverageAnalyzer.Analyze(module);

        Assert.Equal(0, report.SilentMissCount);
        UnsupportedConstructDiagnostic diagnostic = Assert.Single(report.UnsupportedConstructs);
        Assert.Equal("ContAssign", diagnostic.ConstructKind);
        Assert.Contains("FunctionCallExpr", diagnostic.Reason);
        Assert.Contains(report.Modules.Single().Endpoints,
            endpoint => endpoint.SignalName == "y"
                     && endpoint.Status == EndpointCoverageStatus.Unsupported);
    }

    [Fact]
    public void Analyze_PrimitiveInputWithUnknownSignal_ReportsUnsupportedDiagnostic()
    {
        ModuleAst module = Module(contAssigns:
        [
            new ContAssignAst(
                new VarRefLValue("y"),
                new UnaryExpr(UnaryOp.Not, new FunctionCallExpr("user_func", [])))
        ]);

        SchematicCoverageReport report = SchematicCoverageAnalyzer.Analyze(module);

        Assert.Equal(0, report.SilentMissCount);
        UnsupportedConstructDiagnostic diagnostic = Assert.Single(
            report.UnsupportedConstructs,
            static d => d.ConstructKind == "PrimitiveEndpoint");
        Assert.Contains("could not be resolved", diagnostic.Reason);
        Assert.Contains(report.Modules.Single().Endpoints,
            endpoint => endpoint.Status == EndpointCoverageStatus.Unsupported
                     && endpoint.SignalName == "?"
                     && endpoint.Kind == EndpointKind.PrimitiveInput);
    }

    [Fact]
    public void Analyze_VerilatorInternalContAssign_IsIntentionalOmission()
    {
        ModuleAst module = Module(contAssigns:
        [
            new ContAssignAst(new VarRefLValue("__VdfgTmp_0"), new SignalRef("a"))
        ]);

        SchematicCoverageReport report = SchematicCoverageAnalyzer.Analyze(module);

        Assert.Equal(0, report.SilentMissCount);
        Assert.Equal(0, report.UnsupportedCount);
        Assert.Contains(report.Modules.Single().Endpoints,
            endpoint => endpoint.SignalName == "__VdfgTmp_0"
                     && endpoint.Status == EndpointCoverageStatus.IntentionalOmission);
    }

    [Fact]
    public void Analyze_UnsupportedSequentialBlock_ReportsExplicitDiagnostic()
    {
        ModuleAst module = Module(sequentialBlocks:
        [
            new SequentialBlockAst(
                Triggers: [],
                Body: new AssignAst(new VarRefLValue("q"), new SignalRef("d"), IsNonBlocking: true),
                HasAsynchronousReset: false)
        ]);

        SchematicCoverageReport report = SchematicCoverageAnalyzer.Analyze(module);

        Assert.Equal(0, report.SilentMissCount);
        UnsupportedConstructDiagnostic diagnostic = Assert.Single(report.UnsupportedConstructs);
        Assert.Equal("SequentialBlock", diagnostic.ConstructKind);
        Assert.Contains(report.Modules.Single().Endpoints,
            endpoint => endpoint.SignalName == "q"
                     && endpoint.Kind == EndpointKind.SequentialTarget
                     && endpoint.Status == EndpointCoverageStatus.Unsupported);
    }

    [Fact]
    public void Analyze_UnmaterializableMuxInputRenderedAsX_ReportsUnsupportedDiagnostic()
    {
        ModuleAst module = Module(contAssigns:
        [
            new ContAssignAst(
                new VarRefLValue("y"),
                new CondExpr(
                    new SignalRef("sel"),
                    new FunctionCallExpr("user_func", [new SignalRef("a")]),
                    new SignalRef("c")))
        ]);

        SchematicCoverageReport report = SchematicCoverageAnalyzer.Analyze(module);

        Assert.Equal(0, report.SilentMissCount);
        UnsupportedConstructDiagnostic diagnostic = Assert.Single(
            report.UnsupportedConstructs,
            static d => d.ConstructKind == "MuxInput");
        Assert.Contains("rendered as X", diagnostic.Reason);
        Assert.Contains(report.Modules.Single().Endpoints,
            endpoint => endpoint.SignalName == "X"
                     && endpoint.Kind == EndpointKind.PrimitiveInput
                     && endpoint.Status == EndpointCoverageStatus.Unsupported);
    }

    [Fact]
    public void Analyze_PrimitiveInputDrivenByLiteralConstant_IsIntentionalOmission()
    {
        SchematicPrimitiveList primitives = new(
            ModuleName: "top",
            Ports: [],
            Signals: [],
            Instances: [],
            Logic:
            [
                new BufferPrimitive("buf_y_0", "y", "1'b1", Width: 1)
            ]);
        ModuleAst module = Module();

        SchematicCoverageReport report = SchematicCoverageAnalyzer.Analyze(module, primitives);

        Assert.Equal(0, report.SilentMissCount);
        Assert.Equal(0, report.UnsupportedCount);
        Assert.Contains(report.Modules.Single().Endpoints,
            endpoint => endpoint.SignalName == "1'b1"
                     && endpoint.Kind == EndpointKind.PrimitiveInput
                     && endpoint.Status == EndpointCoverageStatus.IntentionalOmission);
    }

    [Fact]
    public void Analyze_ArraySelectMemoryRead_IsRouted()
    {
        ModuleAst module = new(
            Name: "top",
            IsTop: true,
            Ports:
            [
                new PortDecl("addr", SignalDirection.Input, 4, false, 0),
                new PortDecl("data", SignalDirection.Output, 8, false, 1)
            ],
            Parameters: [],
            LocalSignals: [new SignalDecl("mem", 8, false, [new BitRange(15, 0)])],
            Instances: [],
            ContAssigns:
            [
                new ContAssignAst(
                    new VarRefLValue("data"),
                    new ArraySelectExpr(new SignalRef("mem"), new SignalRef("addr")))
            ],
            SequentialBlocks: [],
            CombinationalBlocks: []);

        SchematicCoverageReport report = SchematicCoverageAnalyzer.Analyze(module);

        Assert.Equal(0, report.SilentMissCount);
        Assert.Equal(0, report.UnsupportedCount);
        Assert.Contains(report.Modules.Single().Endpoints,
            endpoint => endpoint.SignalName == "data"
                     && endpoint.Kind == EndpointKind.ContAssignTarget
                     && endpoint.Status == EndpointCoverageStatus.Routed);
    }

    [Fact]
    public void Analyze_ReplicateExpression_IsRouted()
    {
        ModuleAst module = Module(
            ports:
            [
                new PortDecl("a", SignalDirection.Input, 1, false, 0),
                new PortDecl("y", SignalDirection.Output, 4, false, 1)
            ],
            contAssigns:
            [
                new ContAssignAst(
                    new VarRefLValue("y"),
                    new ReplicateExpr(4, new SignalRef("a")))
            ]);

        SchematicCoverageReport report = SchematicCoverageAnalyzer.Analyze(module);

        Assert.Equal(0, report.SilentMissCount);
        Assert.Equal(0, report.UnsupportedCount);
        Assert.Contains(report.Modules.Single().Endpoints,
            endpoint => endpoint.SignalName == "y"
                     && endpoint.Kind == EndpointKind.ContAssignTarget
                     && endpoint.Status == EndpointCoverageStatus.Routed);
    }

    private static ModuleAst Module(
        IReadOnlyList<PortDecl>? ports = null,
        IReadOnlyList<SignalDecl>? locals = null,
        IReadOnlyList<ContAssignAst>? contAssigns = null,
        IReadOnlyList<SequentialBlockAst>? sequentialBlocks = null) =>
        new(
            Name: "top",
            IsTop: true,
            Ports: ports ?? [],
            Parameters: [],
            LocalSignals: locals ?? [],
            Instances: [],
            ContAssigns: contAssigns ?? [],
            SequentialBlocks: sequentialBlocks ?? [],
            CombinationalBlocks: []);
}
