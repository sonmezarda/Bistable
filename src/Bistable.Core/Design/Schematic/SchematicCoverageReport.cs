using Bistable.Core.Design.Ast;

namespace Bistable.Core.Design.Schematic;

/// <summary>
/// Machine-readable schematic completeness report. The first production rule is
/// that an endpoint may be unsupported, but it must never disappear silently.
/// </summary>
public sealed record SchematicCoverageReport(
    string TopModule,
    IReadOnlyList<ModuleCoverage> Modules,
    IReadOnlyList<UnsupportedConstructDiagnostic> UnsupportedConstructs)
{
    public int SilentMissCount => Modules.Sum(static m => m.SilentMissCount);
    public int UnsupportedCount => UnsupportedConstructs.Count;
}

public sealed record ModuleCoverage(
    string ModuleName,
    IReadOnlyList<EndpointCoverage> Endpoints)
{
    public int ExpectedEndpointCount => Endpoints.Count;
    public int RoutedEndpointCount => Endpoints.Count(static e => e.Status == EndpointCoverageStatus.Routed);
    public int IntentionalOmissionCount => Endpoints.Count(static e => e.Status == EndpointCoverageStatus.IntentionalOmission);
    public int UnsupportedEndpointCount => Endpoints.Count(static e => e.Status == EndpointCoverageStatus.Unsupported);
    public int SilentMissCount => Endpoints.Count(static e => e.Status == EndpointCoverageStatus.SilentMiss);
}

public sealed record EndpointCoverage(
    string ModuleName,
    string? HierarchyPath,
    string EndpointId,
    string SignalName,
    EndpointKind Kind,
    EndpointCoverageStatus Status,
    string Reason);

public sealed record UnsupportedConstructDiagnostic(
    string ModuleName,
    string ConstructId,
    string ConstructKind,
    string Reason);

public enum EndpointKind
{
    BoundaryPort,
    ContAssignTarget,
    SequentialTarget,
    PrimitiveInput,
    PrimitiveOutput,
    PrimitiveControl,
    Memory,
}

public enum EndpointCoverageStatus
{
    Routed,
    IntentionalOmission,
    Unsupported,
    SilentMiss,
}

public static class SchematicCoverageAnalyzer
{
    public static SchematicCoverageReport Analyze(DesignAst design)
    {
        ArgumentNullException.ThrowIfNull(design);

        List<ModuleCoverage> modules = [];
        List<UnsupportedConstructDiagnostic> unsupported = [];
        foreach (ModuleAst module in design.Modules)
        {
            SchematicCoverageReport moduleReport = Analyze(module);
            modules.AddRange(moduleReport.Modules);
            unsupported.AddRange(moduleReport.UnsupportedConstructs);
        }

        string topModuleName = design.TopModule?.Name
            ?? design.Modules.FirstOrDefault()?.Name
            ?? "<empty-design>";
        return new SchematicCoverageReport(topModuleName, modules, unsupported);
    }

    public static SchematicCoverageReport Analyze(ModuleAst module)
    {
        ArgumentNullException.ThrowIfNull(module);
        return Analyze(module, SchematicDecoder.Decode(module));
    }

    public static SchematicCoverageReport Analyze(ModuleAst module, SchematicPrimitiveList primitives)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(primitives);

        List<EndpointCoverage> endpoints = [];
        List<UnsupportedConstructDiagnostic> unsupported = [];
        HashSet<string> routedTargets = CollectRoutedTargets(primitives);

        foreach (PortDecl port in module.Ports)
        {
            endpoints.Add(new EndpointCoverage(
                module.Name,
                null,
                $"port:{port.Name}",
                port.Name,
                EndpointKind.BoundaryPort,
                EndpointCoverageStatus.Routed,
                "Boundary port decoded."));
        }

        AddPrimitiveEndpointCoverage(module.Name, primitives, endpoints, unsupported);
        if (primitives.CoverageEvents is { Count: > 0 } decoderEvents)
        {
            AddDecoderCoverageEvents(decoderEvents, endpoints, unsupported);
        }
        else
        {
            AddContAssignCoverage(module, routedTargets, endpoints, unsupported);
            AddSequentialCoverage(module, routedTargets, endpoints, unsupported);
        }

        ModuleCoverage moduleCoverage = new(module.Name, endpoints);
        return new SchematicCoverageReport(module.Name, [moduleCoverage], unsupported);
    }

    private static void AddDecoderCoverageEvents(
        IReadOnlyList<SchematicDecoderCoverageEvent> events,
        List<EndpointCoverage> endpoints,
        List<UnsupportedConstructDiagnostic> unsupported)
    {
        foreach (SchematicDecoderCoverageEvent e in events)
        {
            endpoints.Add(new EndpointCoverage(
                e.ModuleName,
                null,
                e.EndpointId,
                e.SignalName,
                e.EndpointKind,
                e.Status,
                e.Reason));

            if (e.Status == EndpointCoverageStatus.Unsupported
                && !string.IsNullOrWhiteSpace(e.UnsupportedConstructKind))
            {
                unsupported.Add(new UnsupportedConstructDiagnostic(
                    e.ModuleName,
                    e.EndpointId,
                    e.UnsupportedConstructKind,
                    e.Reason));
            }
        }
    }

    private static HashSet<string> CollectRoutedTargets(SchematicPrimitiveList primitives)
    {
        HashSet<string> targets = new(StringComparer.OrdinalIgnoreCase);
        foreach (SchematicPrimitive primitive in primitives.Logic)
        {
            switch (primitive)
            {
                case FlipFlopPrimitive ff:
                    Add(ff.QSignal);
                    break;
                case LatchPrimitive latch:
                    Add(latch.QSignal);
                    break;
                case MuxPrimitive mux:
                    Add(mux.OutputSignal);
                    break;
                case BufferPrimitive buffer:
                    Add(buffer.OutputSignal);
                    break;
                case ConstantTiePrimitive tie:
                    Add(tie.OutputSignal);
                    break;
                case TriStatePrimitive triState:
                    Add(triState.OutputSignal);
                    break;
                case InverterPrimitive inverter:
                    Add(inverter.OutputSignal);
                    break;
                case GatePrimitive gate:
                    Add(gate.OutputSignal);
                    break;
                case ArithPrimitive arith:
                    Add(arith.OutputSignal);
                    break;
                case SplitterPrimitive splitter:
                    Add(splitter.OutputSignal);
                    break;
                case JoinerPrimitive joiner:
                    Add(joiner.OutputSignal);
                    break;
                case MemoryPrimitive memory:
                    Add(memory.SignalName);
                    break;
                case MemoryReadPrimitive read:
                    Add(read.OutputSignal);
                    break;
                case StructFanOutPrimitive fanOut:
                    Add(fanOut.StructSignal);
                    foreach (StructFanOutLeg leg in fanOut.Legs)
                    {
                        foreach (string consumer in leg.Consumers)
                        {
                            Add(consumer);
                        }
                    }
                    break;
            }
        }
        return targets;

        void Add(string signal)
        {
            if (!string.IsNullOrWhiteSpace(signal))
            {
                targets.Add(signal);
            }
        }
    }

    private static void AddContAssignCoverage(
        ModuleAst module,
        IReadOnlySet<string> routedTargets,
        List<EndpointCoverage> endpoints,
        List<UnsupportedConstructDiagnostic> unsupported)
    {
        int index = 0;
        foreach (ContAssignAst assign in module.ContAssigns)
        {
            string target = LValueName(assign.Target);
            string endpointId = $"contassign:{index}:{target}";
            if (string.IsNullOrWhiteSpace(target))
            {
                endpoints.Add(UnsupportedEndpoint(module.Name, endpointId, "?", EndpointKind.ContAssignTarget, "ContAssign target could not be resolved."));
                unsupported.Add(new UnsupportedConstructDiagnostic(module.Name, endpointId, "ContAssign", "Target l-value could not be resolved."));
            }
            else if (LValueContainsUnknownSegment(assign.Target))
            {
                // P2.9-7: a concat l-value with at least one __unknown__
                // segment is a partial render — the schematic can show the
                // resolved half but the rest is dropped. Surface that so the
                // coverage report keeps the SilentMiss counter clean.
                string reason = $"ContAssign l-value contains an unresolved segment ({assign.Target.GetType().Name}).";
                endpoints.Add(UnsupportedEndpoint(module.Name, endpointId, target, EndpointKind.ContAssignTarget, reason));
                unsupported.Add(new UnsupportedConstructDiagnostic(module.Name, endpointId, "ContAssignLValue", reason));
            }
            else if (SchematicDecoder.IsVerilatorInternalSignal(target))
            {
                endpoints.Add(IntentionalEndpoint(module.Name, endpointId, target, EndpointKind.ContAssignTarget, "Verilator internal target intentionally hidden."));
            }
            else if (routedTargets.Contains(target))
            {
                endpoints.Add(RoutedEndpoint(module.Name, endpointId, target, EndpointKind.ContAssignTarget, "ContAssign target is owned by a schematic primitive."));
            }
            else
            {
                string reason = $"Unsupported contassign source expression '{assign.Source.GetType().Name}'.";
                endpoints.Add(UnsupportedEndpoint(module.Name, endpointId, target, EndpointKind.ContAssignTarget, reason));
                unsupported.Add(new UnsupportedConstructDiagnostic(module.Name, endpointId, "ContAssign", reason));
            }
            index++;
        }
    }

    private static void AddSequentialCoverage(
        ModuleAst module,
        IReadOnlySet<string> routedTargets,
        List<EndpointCoverage> endpoints,
        List<UnsupportedConstructDiagnostic> unsupported)
    {
        int index = 0;
        foreach (SequentialBlockAst block in module.SequentialBlocks)
        {
            foreach (string target in AssignedTargets(block.Body).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string endpointId = $"sequential:{index}:{target}";
                if (SchematicDecoder.IsVerilatorInternalSignal(target))
                {
                    endpoints.Add(IntentionalEndpoint(module.Name, endpointId, target, EndpointKind.SequentialTarget, "Verilator internal sequential target intentionally hidden."));
                }
                else if (routedTargets.Contains(target))
                {
                    endpoints.Add(RoutedEndpoint(module.Name, endpointId, target, EndpointKind.SequentialTarget, "Sequential target is owned by a schematic primitive."));
                }
                else
                {
                    string reason = "Sequential assignment was not decoded into a supported FF/latch primitive.";
                    endpoints.Add(UnsupportedEndpoint(module.Name, endpointId, target, EndpointKind.SequentialTarget, reason));
                    unsupported.Add(new UnsupportedConstructDiagnostic(module.Name, endpointId, "SequentialBlock", reason));
                }
            }
            index++;
        }
    }

    private static void AddPrimitiveEndpointCoverage(
        string moduleName,
        SchematicPrimitiveList primitives,
        List<EndpointCoverage> endpoints,
        List<UnsupportedConstructDiagnostic> unsupported)
    {
        foreach (SchematicPrimitive primitive in primitives.Logic)
        {
            switch (primitive)
            {
                case FlipFlopPrimitive ff:
                    AddInput(primitive.Id, "d", ff.DSignal, EndpointKind.PrimitiveInput);
                    AddInput(primitive.Id, "clk", ff.ClockSignal, EndpointKind.PrimitiveControl);
                    if (ff.AsyncResetSignal is { } rst) AddInput(primitive.Id, "rst", rst, EndpointKind.PrimitiveControl);
                    AddOutput(primitive.Id, "q", ff.QSignal);
                    break;
                case LatchPrimitive latch:
                    AddInput(primitive.Id, "d", latch.DSignal, EndpointKind.PrimitiveInput);
                    AddInput(primitive.Id, "gate", latch.GateSignal, EndpointKind.PrimitiveControl);
                    AddOutput(primitive.Id, "q", latch.QSignal);
                    break;
                case MuxPrimitive mux:
                    for (int i = 0; i < mux.Inputs.Count; i++)
                    {
                        if (mux.Inputs[i].Source is MuxSignalSource signal)
                        {
                            AddInput(primitive.Id, $"in.{i}", signal.SignalName, EndpointKind.PrimitiveInput);
                        }
                        else if (mux.Inputs[i].Source is MuxConstantSource { Literal: "X" })
                        {
                            string endpointId = $"primitive:{primitive.Id}:in.{i}";
                            string reason = "Mux input source expression could not be routed and is rendered as X.";
                            endpoints.Add(UnsupportedEndpoint(moduleName, endpointId, "X", EndpointKind.PrimitiveInput, reason));
                            unsupported.Add(new UnsupportedConstructDiagnostic(moduleName, endpointId, "MuxInput", reason));
                        }
                        else
                        {
                            AddIntentional(primitive.Id, $"in.{i}", "<const>", EndpointKind.PrimitiveInput, "Mux input is constant-backed.");
                        }
                    }
                    for (int i = 0; i < mux.SelectSignals.Count; i++) AddInput(primitive.Id, $"sel.{i}", mux.SelectSignals[i], EndpointKind.PrimitiveControl);
                    AddOutput(primitive.Id, "out", mux.OutputSignal);
                    break;
                case BufferPrimitive buffer:
                    AddInput(primitive.Id, "in", buffer.InputSignal, EndpointKind.PrimitiveInput);
                    AddOutput(primitive.Id, "out", buffer.OutputSignal);
                    break;
                case ConstantTiePrimitive tie:
                    AddOutput(primitive.Id, "out", tie.OutputSignal);
                    break;
                case TriStatePrimitive triState:
                    AddInput(primitive.Id, "data", triState.DataSignal, EndpointKind.PrimitiveInput);
                    AddInput(primitive.Id, "enable", triState.EnableSignal, EndpointKind.PrimitiveControl);
                    AddOutput(primitive.Id, "out", triState.OutputSignal);
                    break;
                case InverterPrimitive inverter:
                    AddInput(primitive.Id, "in", inverter.InputSignal, EndpointKind.PrimitiveInput);
                    AddOutput(primitive.Id, "out", inverter.OutputSignal);
                    break;
                case GatePrimitive gate:
                    for (int i = 0; i < gate.InputSignals.Count; i++) AddInput(primitive.Id, $"in.{i}", gate.InputSignals[i], EndpointKind.PrimitiveInput);
                    AddOutput(primitive.Id, "out", gate.OutputSignal);
                    break;
                case ArithPrimitive arith:
                    AddInput(primitive.Id, "left", arith.LeftSignal, EndpointKind.PrimitiveInput);
                    AddInput(primitive.Id, "right", arith.RightSignal, EndpointKind.PrimitiveInput);
                    AddOutput(primitive.Id, "out", arith.OutputSignal);
                    break;
                case SplitterPrimitive splitter:
                    AddInput(primitive.Id, "in", splitter.InputSignal, EndpointKind.PrimitiveInput);
                    AddOutput(primitive.Id, "out", splitter.OutputSignal);
                    break;
                case JoinerPrimitive joiner:
                    for (int i = 0; i < joiner.InputSignals.Count; i++) AddInput(primitive.Id, $"in.{i}", joiner.InputSignals[i], EndpointKind.PrimitiveInput);
                    AddOutput(primitive.Id, "out", joiner.OutputSignal);
                    break;
                case MemoryPrimitive memory:
                    endpoints.Add(ClassifySignal(moduleName, $"primitive:{primitive.Id}:mem", memory.SignalName, EndpointKind.Memory, unsupported));
                    break;
                case MemoryReadPrimitive read:
                    AddInput(primitive.Id, "addr", read.AddressSignal, EndpointKind.PrimitiveInput);
                    AddOutput(primitive.Id, "data", read.OutputSignal);
                    endpoints.Add(ClassifySignal(moduleName, $"primitive:{primitive.Id}:mem", read.MemorySignal, EndpointKind.Memory, unsupported));
                    break;
                case StructFanOutPrimitive fanOut:
                    AddInput(primitive.Id, "struct", fanOut.StructSignal, EndpointKind.PrimitiveInput);
                    foreach (StructFanOutLeg leg in fanOut.Legs)
                    {
                        foreach (string consumer in leg.Consumers)
                        {
                            AddOutput(primitive.Id, $"leg.{leg.FieldName}.{consumer}", consumer);
                        }
                    }
                    break;
            }
        }

        void AddInput(string primitiveId, string pin, string signal, EndpointKind kind)
        {
            string endpointId = $"primitive:{primitiveId}:{pin}";
            endpoints.Add(ClassifySignal(moduleName, endpointId, signal, kind, unsupported));
        }

        void AddOutput(string primitiveId, string pin, string signal)
        {
            string endpointId = $"primitive:{primitiveId}:{pin}";
            endpoints.Add(ClassifySignal(moduleName, endpointId, signal, EndpointKind.PrimitiveOutput, unsupported));
        }

        void AddIntentional(string primitiveId, string pin, string signal, EndpointKind kind, string reason)
        {
            endpoints.Add(IntentionalEndpoint(moduleName, $"primitive:{primitiveId}:{pin}", signal, kind, reason));
        }
    }

    private static EndpointCoverage ClassifySignal(
        string moduleName,
        string endpointId,
        string signal,
        EndpointKind kind,
        List<UnsupportedConstructDiagnostic>? unsupported = null)
    {
        if (string.IsNullOrWhiteSpace(signal) || signal == "?")
        {
            if (unsupported is not null)
            {
                string reason = "Primitive endpoint signal name could not be resolved.";
                unsupported.Add(new UnsupportedConstructDiagnostic(moduleName, endpointId, "PrimitiveEndpoint", reason));
                return UnsupportedEndpoint(moduleName, endpointId, signal, kind, reason);
            }

            return new EndpointCoverage(moduleName, null, endpointId, signal, kind, EndpointCoverageStatus.SilentMiss, "Signal name could not be resolved.");
        }

        if (SchematicDecoder.IsVerilatorInternalSignal(signal))
        {
            return IntentionalEndpoint(moduleName, endpointId, signal, kind, "Verilator internal signal intentionally hidden.");
        }

        if (IsConstantLiteral(signal))
        {
            return IntentionalEndpoint(moduleName, endpointId, signal, kind, "Endpoint is driven by a literal constant.");
        }

        return RoutedEndpoint(moduleName, endpointId, signal, kind, "Endpoint signal resolved.");
    }

    private static bool IsConstantLiteral(string signal)
    {
        string trimmed = signal.Trim();
        return trimmed is "0" or "1"
               || trimmed.StartsWith("'")
               || trimmed.Contains("'b", StringComparison.OrdinalIgnoreCase)
               || trimmed.Contains("'h", StringComparison.OrdinalIgnoreCase)
               || trimmed.Contains("'d", StringComparison.OrdinalIgnoreCase);
    }

    private static EndpointCoverage RoutedEndpoint(string moduleName, string endpointId, string signal, EndpointKind kind, string reason) =>
        new(moduleName, null, endpointId, signal, kind, EndpointCoverageStatus.Routed, reason);

    private static EndpointCoverage IntentionalEndpoint(string moduleName, string endpointId, string signal, EndpointKind kind, string reason) =>
        new(moduleName, null, endpointId, signal, kind, EndpointCoverageStatus.IntentionalOmission, reason);

    private static EndpointCoverage UnsupportedEndpoint(string moduleName, string endpointId, string signal, EndpointKind kind, string reason) =>
        new(moduleName, null, endpointId, signal, kind, EndpointCoverageStatus.Unsupported, reason);

    private static IEnumerable<string> AssignedTargets(StatementAst statement)
    {
        switch (statement)
        {
            case AssignAst assign:
                string name = LValueName(assign.Target);
                if (!string.IsNullOrWhiteSpace(name)) yield return name;
                break;
            case BeginAst begin:
                foreach (StatementAst child in begin.Statements)
                {
                    foreach (string target in AssignedTargets(child)) yield return target;
                }
                break;
            case IfAst branch:
                foreach (string target in AssignedTargets(branch.Then)) yield return target;
                if (branch.Else is not null)
                {
                    foreach (string target in AssignedTargets(branch.Else)) yield return target;
                }
                break;
            case CaseAst caseAst:
                foreach (CaseArm arm in caseAst.Arms)
                {
                    foreach (string target in AssignedTargets(arm.Body)) yield return target;
                }
                if (caseAst.Default is not null)
                {
                    foreach (string target in AssignedTargets(caseAst.Default)) yield return target;
                }
                break;
        }
    }

    private static string LValueName(LValueAst lval) => lval switch
    {
        VarRefLValue v => v.Name,
        BitSelectLValue b => b.SignalName,
        ArraySelectLValue a => a.SignalName,
        StructFieldLValue sf => sf.SignalName,
        ConcatLValue c => c.Parts.Count > 0 ? LValueName(c.Parts[0]) : string.Empty,
        _ => string.Empty
    };

    /// <summary>
    /// P2.9-7: a composite l-value (concat) might carry a `__unknown__`
    /// segment emitted by the XML reader's fallback when it encountered an
    /// element it didn't recognise. Any such segment must surface as Unsupported
    /// instead of letting the analyzer treat the whole assign as Routed based
    /// on the first segment alone.
    /// </summary>
    private const string UnknownLValueMarker = "__unknown__";

    private static bool LValueContainsUnknownSegment(LValueAst lval) => lval switch
    {
        VarRefLValue v       => string.Equals(v.Name,       UnknownLValueMarker, StringComparison.Ordinal),
        BitSelectLValue b    => string.Equals(b.SignalName, UnknownLValueMarker, StringComparison.Ordinal),
        ArraySelectLValue a  => string.Equals(a.SignalName, UnknownLValueMarker, StringComparison.Ordinal),
        StructFieldLValue sf => string.Equals(sf.SignalName, UnknownLValueMarker, StringComparison.Ordinal),
        ConcatLValue c       => c.Parts.Any(LValueContainsUnknownSegment),
        _                    => false,
    };
}
