using System.Numerics;
using Bistable.Core.Design.Ast;

namespace Bistable.Core.Design.Schematic;

/// <summary>
/// Translates a <see cref="ModuleAst"/> into a layout-agnostic <see cref="SchematicPrimitiveList"/>.
/// Pure function — no ELK, no rendering. Backend-agnostic: a future Yosys reader would feed the
/// same AST into this decoder.
/// </summary>
public static class SchematicDecoder
{
    public static SchematicPrimitiveList Decode(ModuleAst module)
    {
        ArgumentNullException.ThrowIfNull(module);

        List<PortPrimitive> ports = module.Ports
            .Select(p => new PortPrimitive($"port_{p.Name}", p.Name, p.Direction, p.Width))
            .ToList();

        // P2.5-4: Verilator's DFG-based optimisations emit auto-generated signals
        // named like "__VdfgTmp_h1814ef32__0" or "__Vlvbound_h1234__1" that don't
        // correspond to user source code. Hiding them at the decoder layer keeps
        // the schematic readable. Level-3 (substitution into consumers) is
        // deferred to P2.6-1; this is the level-1 "just hide them" fix.
        List<SignalPrimitive> signals = module.LocalSignals
            .Where(static s => s.ArrayDims.Count == 0)
            .Where(static s => !IsVerilatorInternalSignal(s.Name))
            .Select(s => new SignalPrimitive($"sig_{s.Name}", s.Name, s.Width, s.IsRegistered))
            .ToList();
        Dictionary<string, SignalDecl> memorySignals = module.LocalSignals
            .Where(static s => s.ArrayDims.Count > 0)
            .ToDictionary(static s => s.Name, StringComparer.OrdinalIgnoreCase);

        List<InstancePrimitive> instances = module.Instances
            .Select(DecodeInstance)
            .ToList();

        List<SchematicPrimitive> logic = [];
        List<SchematicDecoderCoverageEvent> coverageEvents = [];

        // Memories (skip if the array signal name is internal — defensive, unusual)
        foreach (SignalDecl s in module.LocalSignals
                     .Where(static x => x.ArrayDims.Count > 0)
                     .Where(static x => !IsVerilatorInternalSignal(x.Name)))
        {
            BitRange dim = s.ArrayDims[0];
            logic.Add(new MemoryPrimitive($"mem_{s.Name}", s.Name, s.Width, dim.Hi, dim.Lo));
        }

        // Sequential blocks → FF / latch (skip when target is an internal tmp)
        int seqIndex = 0;
        foreach (SequentialBlockAst block in module.SequentialBlocks)
        {
            int currentIndex = seqIndex++;
            SchematicPrimitive? primitive = DecodeSequentialBlock(block, currentIndex, logic, memorySignals);
            SchematicPrimitive? materializedPrimitive = primitive is not null && !IsPrimitiveOnInternalSignal(primitive)
                ? primitive
                : null;
            if (materializedPrimitive is not null)
            {
                logic.Add(materializedPrimitive);
            }
            AddSequentialCoverageEvents(module.Name, block, currentIndex, materializedPrimitive, coverageEvents);
        }

        // P2-11: scan struct-typed signals for field accesses, group into fan-out
        // primitives. The set of "suppressed" contassign targets / instance signals
        // is computed up-front so the regular DecodeContAssign / DecodeInstance loops
        // can skip per-field consumers (the fan-out node owns them now).
        List<StructFanOutPrimitive> fanOuts = BuildStructFanOuts(module);
        HashSet<string> fanOutSplitterTargets = CollectFanOutContAssignTargets(module, fanOuts);
        logic.AddRange(fanOuts);

        // Continuous assignments → combinational primitives. Skip:
        //   • targets owned by struct fan-outs (handled by fan-out leg)
        //   • targets that are Verilator internal tmps (P2.5-4)
        int caIndex = 0;
        foreach (ContAssignAst ca in module.ContAssigns)
        {
            int currentIndex = caIndex++;
            string target = LValueName(ca.Target);
            if (string.IsNullOrWhiteSpace(target))
            {
                AddCoverageEvent(
                    coverageEvents,
                    module.Name,
                    $"contassign:{currentIndex}:?",
                    "?",
                    EndpointKind.ContAssignTarget,
                    EndpointCoverageStatus.Unsupported,
                    "ContAssign target could not be resolved.",
                    "ContAssign");
                continue;
            }
            if (LValueContainsUnknownSegment(ca.Target))
            {
                string reason = $"ContAssign l-value contains an unresolved segment ({ca.Target.GetType().Name}).";
                AddCoverageEvent(
                    coverageEvents,
                    module.Name,
                    $"contassign:{currentIndex}:{target}",
                    target,
                    EndpointKind.ContAssignTarget,
                    EndpointCoverageStatus.Unsupported,
                    reason,
                    "ContAssignLValue");
                continue;
            }
            if (fanOutSplitterTargets.Contains(target))
            {
                AddCoverageEvent(
                    coverageEvents,
                    module.Name,
                    $"contassign:{currentIndex}:{target}",
                    target,
                    EndpointKind.ContAssignTarget,
                    EndpointCoverageStatus.Routed,
                    "ContAssign target is owned by a struct fan-out primitive.");
                continue;
            }
            if (IsVerilatorInternalSignal(target))
            {
                AddCoverageEvent(
                    coverageEvents,
                    module.Name,
                    $"contassign:{currentIndex}:{target}",
                    target,
                    EndpointKind.ContAssignTarget,
                    EndpointCoverageStatus.IntentionalOmission,
                    "Verilator internal target intentionally hidden.");
                continue;
            }
            SchematicPrimitive? primitive = DecodeContAssign(ca, currentIndex, logic, memorySignals);
            if (primitive is not null)
            {
                logic.Add(primitive);
                AddCoverageEvent(
                    coverageEvents,
                    module.Name,
                    $"contassign:{currentIndex}:{target}",
                    target,
                    EndpointKind.ContAssignTarget,
                    EndpointCoverageStatus.Routed,
                    "ContAssign target is owned by a schematic primitive.");
            }
            else
            {
                string reason = $"Unsupported contassign source expression '{ca.Source.GetType().Name}'.";
                AddCoverageEvent(
                    coverageEvents,
                    module.Name,
                    $"contassign:{currentIndex}:{target}",
                    target,
                    EndpointKind.ContAssignTarget,
                    EndpointCoverageStatus.Unsupported,
                    reason,
                    "ContAssign");
            }
        }

        IReadOnlyList<MultiDriverDiagnostic> diagnostics = DetectMultiDriver(module);
        return new SchematicPrimitiveList(module.Name, ports, signals, instances, logic, diagnostics, coverageEvents);
    }

    /// <summary>
    /// P2.6-5: scan every assignment site in the module and flag signals with
    /// more than one driver. Sources counted:
    ///   • each <c>assign x = ...;</c> targeting the signal
    ///   • each non-blocking / blocking assignment inside a sequential block
    ///   • each instance output port wired to the signal
    /// Verilator-internal tmps are excluded from the scan (they're hidden).
    /// </summary>
    private static IReadOnlyList<MultiDriverDiagnostic> DetectMultiDriver(ModuleAst module)
    {
        Dictionary<string, List<string>> drivers = new(StringComparer.OrdinalIgnoreCase);

        void AddDriver(string signalName, string description)
        {
            if (string.IsNullOrEmpty(signalName) || IsVerilatorInternalSignal(signalName)) return;
            if (!drivers.TryGetValue(signalName, out List<string>? list))
            {
                list = new List<string>(2);
                drivers[signalName] = list;
            }
            list.Add(description);
        }

        foreach (ContAssignAst ca in module.ContAssigns)
        {
            string target = LValueName(ca.Target);
            AddDriver(target, "assign");
        }

        int seqIndex = 0;
        foreach (SequentialBlockAst block in module.SequentialBlocks)
        {
            AddDriversFromStatement(block.Body, $"always[{seqIndex}]", AddDriver);
            seqIndex++;
        }

        foreach (InstanceDecl inst in module.Instances)
        {
            foreach (PortConnectionDecl conn in inst.PortConnections)
            {
                if (string.Equals(conn.Direction, "out", StringComparison.OrdinalIgnoreCase))
                {
                    AddDriver(conn.SignalName, $"{inst.InstanceName}.{conn.PortName}");
                }
            }
        }

        List<MultiDriverDiagnostic> result = [];
        foreach ((string name, List<string> sources) in drivers)
        {
            if (sources.Count > 1)
            {
                result.Add(new MultiDriverDiagnostic(name, sources));
            }
        }
        return result;
    }

    private static void AddDriversFromStatement(StatementAst stmt, string blockTag, Action<string, string> addDriver)
    {
        switch (stmt)
        {
            case AssignAst asg:
                addDriver(LValueName(asg.Target), blockTag);
                break;
            case BeginAst bk:
                foreach (StatementAst s in bk.Statements) AddDriversFromStatement(s, blockTag, addDriver);
                break;
            case IfAst ifs:
                AddDriversFromStatement(ifs.Then, blockTag, addDriver);
                if (ifs.Else is not null) AddDriversFromStatement(ifs.Else, blockTag, addDriver);
                break;
            case CaseAst cs:
                foreach (CaseArm arm in cs.Arms) AddDriversFromStatement(arm.Body, blockTag, addDriver);
                if (cs.Default is not null) AddDriversFromStatement(cs.Default, blockTag, addDriver);
                break;
        }
    }

    /// <summary>
    /// True when the signal name matches Verilator's auto-generated tmp pattern
    /// (any name starting with "__V"). Examples seen in arnicomp:
    /// <c>__VdfgTmp_h1814ef32__0</c>, <c>__Vlvbound_h1234__1</c>,
    /// <c>__Vfunc_*_*</c>. These are compiler-internal CSE / DFG temporaries
    /// — never user-meaningful and never worth rendering as separate nodes.
    /// </summary>
    public static bool IsVerilatorInternalSignal(string signalName) =>
        !string.IsNullOrEmpty(signalName) && signalName.StartsWith("__V", StringComparison.Ordinal);

    private static bool IsPrimitiveOnInternalSignal(SchematicPrimitive primitive) => primitive switch
    {
        FlipFlopPrimitive ff   => IsVerilatorInternalSignal(ff.QSignal),
        LatchPrimitive lt      => IsVerilatorInternalSignal(lt.QSignal),
        MuxPrimitive mux       => IsVerilatorInternalSignal(mux.OutputSignal),
        BufferPrimitive buf    => IsVerilatorInternalSignal(buf.OutputSignal),
        InverterPrimitive inv  => IsVerilatorInternalSignal(inv.OutputSignal),
        GatePrimitive gate     => IsVerilatorInternalSignal(gate.OutputSignal),
        ArithPrimitive arith   => IsVerilatorInternalSignal(arith.OutputSignal),
        SplitterPrimitive spl  => IsVerilatorInternalSignal(spl.OutputSignal),
        JoinerPrimitive join   => IsVerilatorInternalSignal(join.OutputSignal),
        MemoryPrimitive mem    => IsVerilatorInternalSignal(mem.SignalName),
        MemoryReadPrimitive rd => IsVerilatorInternalSignal(rd.OutputSignal),
        _ => false
    };

    private static void AddSequentialCoverageEvents(
        string moduleName,
        SequentialBlockAst block,
        int index,
        SchematicPrimitive? materializedPrimitive,
        List<SchematicDecoderCoverageEvent> coverageEvents)
    {
        HashSet<string> routedTargets = materializedPrimitive is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : PrimitiveOutputTargets(materializedPrimitive).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string target in AssignedTargets(block.Body).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string endpointId = $"sequential:{index}:{target}";
            if (IsVerilatorInternalSignal(target))
            {
                AddCoverageEvent(
                    coverageEvents,
                    moduleName,
                    endpointId,
                    target,
                    EndpointKind.SequentialTarget,
                    EndpointCoverageStatus.IntentionalOmission,
                    "Verilator internal sequential target intentionally hidden.");
            }
            else if (routedTargets.Contains(target))
            {
                AddCoverageEvent(
                    coverageEvents,
                    moduleName,
                    endpointId,
                    target,
                    EndpointKind.SequentialTarget,
                    EndpointCoverageStatus.Routed,
                    "Sequential target is owned by a schematic primitive.");
            }
            else
            {
                string reason = "Sequential assignment was not decoded into a supported FF/latch primitive.";
                AddCoverageEvent(
                    coverageEvents,
                    moduleName,
                    endpointId,
                    target,
                    EndpointKind.SequentialTarget,
                    EndpointCoverageStatus.Unsupported,
                    reason,
                    "SequentialBlock");
            }
        }
    }

    private static IEnumerable<string> PrimitiveOutputTargets(SchematicPrimitive primitive)
    {
        switch (primitive)
        {
            case FlipFlopPrimitive ff:
                yield return ff.QSignal;
                break;
            case LatchPrimitive latch:
                yield return latch.QSignal;
                break;
            case MuxPrimitive mux:
                yield return mux.OutputSignal;
                break;
            case BufferPrimitive buffer:
                yield return buffer.OutputSignal;
                break;
            case ConstantTiePrimitive tie:
                yield return tie.OutputSignal;
                break;
            case TriStatePrimitive triState:
                yield return triState.OutputSignal;
                break;
            case InverterPrimitive inverter:
                yield return inverter.OutputSignal;
                break;
            case GatePrimitive gate:
                yield return gate.OutputSignal;
                break;
            case ArithPrimitive arith:
                yield return arith.OutputSignal;
                break;
            case SplitterPrimitive splitter:
                yield return splitter.OutputSignal;
                break;
            case JoinerPrimitive joiner:
                yield return joiner.OutputSignal;
                break;
            case MemoryPrimitive memory:
                yield return memory.SignalName;
                break;
            case MemoryReadPrimitive read:
                yield return read.OutputSignal;
                break;
            case StructFanOutPrimitive fanOut:
                yield return fanOut.StructSignal;
                foreach (StructFanOutLeg leg in fanOut.Legs)
                {
                    foreach (string consumer in leg.Consumers)
                    {
                        yield return consumer;
                    }
                }
                break;
        }
    }

    private static void AddCoverageEvent(
        List<SchematicDecoderCoverageEvent> coverageEvents,
        string moduleName,
        string endpointId,
        string signalName,
        EndpointKind endpointKind,
        EndpointCoverageStatus status,
        string reason,
        string? unsupportedConstructKind = null) =>
        coverageEvents.Add(new SchematicDecoderCoverageEvent(
            moduleName,
            endpointId,
            signalName,
            endpointKind,
            status,
            reason,
            unsupportedConstructKind));

    // ── Struct fan-out (P2-11) ───────────────────────────────────────────────

    private static List<StructFanOutPrimitive> BuildStructFanOuts(ModuleAst module)
    {
        // Identify struct-typed local signals (port struct typing is a follow-up).
        Dictionary<string, StructTypeDecl> structSignals = module.LocalSignals
            .Where(static s => s.StructType is not null)
            .ToDictionary(static s => s.Name, static s => s.StructType!, StringComparer.OrdinalIgnoreCase);

        if (structSignals.Count == 0) return [];

        Dictionary<string, Dictionary<BitRange, List<string>>> rangeConsumers = new(StringComparer.OrdinalIgnoreCase);
        foreach (string structName in structSignals.Keys)
            rangeConsumers[structName] = [];

        CollectContAssignFieldConsumers(module, rangeConsumers);
        CollectInstancePinFieldConsumers(module, rangeConsumers);

        return MaterialiseFanOuts(rangeConsumers, structSignals);
    }

    // Walks every contassign and, when the RHS is a slice on a known struct signal,
    // records the consumer (the LHS target) under the (struct, range) bucket.
    private static void CollectContAssignFieldConsumers(
        ModuleAst module,
        Dictionary<string, Dictionary<BitRange, List<string>>> rangeConsumers)
    {
        foreach (ContAssignAst ca in module.ContAssigns)
        {
            if (ca.Source is not BitSelectExpr bs) continue;
            if (bs.Base is not SignalRef baseRef) continue;
            if (!rangeConsumers.TryGetValue(baseRef.Name, out Dictionary<BitRange, List<string>>? legMap)) continue;
            string consumer = LValueName(ca.Target);
            if (!string.IsNullOrEmpty(consumer))
                AppendConsumer(legMap, bs.Range, consumer);
        }
    }

    // Walks every instance port connection. When the connection is wrapped in a
    // sel on a known struct signal, the pin reads struct[hi:lo].
    private static void CollectInstancePinFieldConsumers(
        ModuleAst module,
        Dictionary<string, Dictionary<BitRange, List<string>>> rangeConsumers)
    {
        foreach (InstanceDecl inst in module.Instances)
        {
            foreach (PortConnectionDecl pin in inst.PortConnections)
            {
                if (pin.SignalRange is null) continue;
                if (!rangeConsumers.TryGetValue(pin.SignalName, out Dictionary<BitRange, List<string>>? legMap)) continue;
                AppendConsumer(legMap, pin.SignalRange.Value, $"{inst.InstanceName}.{pin.PortName}");
            }
        }
    }

    private static void AppendConsumer(
        Dictionary<BitRange, List<string>> legMap, BitRange range, string consumer)
    {
        if (!legMap.TryGetValue(range, out List<string>? list))
        {
            list = [];
            legMap[range] = list;
        }
        list.Add(consumer);
    }

    private static List<StructFanOutPrimitive> MaterialiseFanOuts(
        Dictionary<string, Dictionary<BitRange, List<string>>> rangeConsumers,
        Dictionary<string, StructTypeDecl> structSignals)
    {
        // Skip struct signals with zero field accesses (they would render as a
        // regular wire). Each consumed range becomes one leg; the field name is
        // recovered from the struct definition by (lo, width) match.
        List<StructFanOutPrimitive> result = [];
        int index = 0;
        foreach ((string structName, Dictionary<BitRange, List<string>> legMap) in rangeConsumers)
        {
            if (legMap.Count == 0) continue;
            StructTypeDecl structType = structSignals[structName];
            List<StructFanOutLeg> legs = legMap
                .OrderByDescending(static kv => kv.Key.Hi)
                .Select(kv => new StructFanOutLeg(
                    FieldName: ResolveFieldName(structType, kv.Key),
                    Range: kv.Key,
                    Consumers: kv.Value))
                .ToList();

            result.Add(new StructFanOutPrimitive(
                Id: $"fanout_{structName}_{index++}",
                StructSignal: structName,
                StructTypeName: structType.Name,
                StructWidth: structType.TotalWidth,
                Legs: legs));
        }
        return result;
    }

    private static string ResolveFieldName(StructTypeDecl structType, BitRange range)
    {
        StructFieldDecl? match = structType.Fields.FirstOrDefault(f => f.Lo == range.Lo && f.Width == range.Width);
        return match?.FieldName ?? range.ToString();
    }

    private static HashSet<string> CollectFanOutContAssignTargets(ModuleAst module, List<StructFanOutPrimitive> fanOuts)
    {
        // Targets to suppress in the regular contassign loop. These are the LHS
        // signals of `assign target = struct[hi:lo];` — the fan-out leg drives
        // the consumer directly via the renderer, so the redundant SplitterPrimitive
        // would render the same wire twice.
        HashSet<string> structBases = new(StringComparer.OrdinalIgnoreCase);
        foreach (StructFanOutPrimitive f in fanOuts)
            structBases.Add(f.StructSignal);

        HashSet<string> suppressed = new(StringComparer.OrdinalIgnoreCase);
        foreach (ContAssignAst ca in module.ContAssigns)
        {
            if (ca.Source is not BitSelectExpr bs) continue;
            if (bs.Base is not SignalRef baseRef) continue;
            if (!structBases.Contains(baseRef.Name)) continue;
            string target = LValueName(ca.Target);
            if (!string.IsNullOrEmpty(target))
                suppressed.Add(target);
        }
        return suppressed;
    }

    // ── Instance ─────────────────────────────────────────────────────────────

    private static InstancePrimitive DecodeInstance(InstanceDecl inst)
    {
        List<InstancePinBinding> pins = inst.PortConnections
            .Select(p => new InstancePinBinding(p.PortName, p.SignalName, p.Direction, p.PortIndex))
            .ToList();
        return new InstancePrimitive($"inst_{inst.InstanceName}", inst.InstanceName, inst.ModuleName, pins);
    }

    // ── Sequential block decoding ───────────────────────────────────────────

    private static SchematicPrimitive? DecodeSequentialBlock(
        SequentialBlockAst block,
        int index,
        List<SchematicPrimitive> logic,
        IReadOnlyDictionary<string, SignalDecl> memorySignals)
    {
        // Identify clock and (optional) async reset triggers
        EdgeTrigger? clock = block.Triggers.FirstOrDefault(static t => t.Edge == EdgeKind.Rising);
        EdgeTrigger? asyncReset = block.Triggers.FirstOrDefault(static t => t.Edge == EdgeKind.Falling);

        // Latches have no edge triggers
        if (clock is null && asyncReset is null && block.Triggers.Count > 0)
        {
            return DecodeLatch(block, index);
        }

        if (clock is null)
            return null; // Unrecognized — let the caller (or higher-level logic) handle it

        // Walk to the single AssignAst (commonly nested under BeginAst or IfAst)
        AssignAst? assign = FindPrimaryAssign(block.Body);
        if (assign is null) return null;

        string qSignal = LValueName(assign.Target);
        if (string.IsNullOrEmpty(qSignal)) return null;
        if (IsVerilatorInternalSignal(qSignal))
        {
            return new FlipFlopPrimitive(
                Id: $"ff_{qSignal}_{index}",
                QSignal: qSignal,
                ClockSignal: clock.SignalName,
                ClockEdge: clock.Edge,
                AsyncResetSignal: null,
                AsyncResetEdge: null,
                DSignal: "?",
                Width: 1);
        }

        // Determine D signal (RHS) — peel off async reset mux if present
        ExpressionAst source = assign.Source;
        string? asyncResetSignal = null;
        if (asyncReset is not null && source is CondExpr cond && cond.Condition is SignalRef condRef &&
            string.Equals(condRef.Name, asyncReset.SignalName, StringComparison.OrdinalIgnoreCase))
        {
            // Async-reset pattern recognized — peel the reset ternary so the FF's
            // D port carries only the active-mode data signal.
            asyncResetSignal = asyncReset.SignalName;
            source = cond.IfTrue;
        }

        ExpressionMaterializationContext context = new(qSignal, index, logic, memorySignals);
        string dSignal = MaterializeExpression(source, context, "ff_d") ?? "?";
        int width = 1; // Width is not in the AST at this level; resolved by signal lookup in the renderer.

        return new FlipFlopPrimitive(
            Id: $"ff_{qSignal}_{index}",
            QSignal: qSignal,
            ClockSignal: clock.SignalName,
            ClockEdge: clock.Edge,
            AsyncResetSignal: asyncResetSignal,
            AsyncResetEdge: asyncReset?.Edge,
            DSignal: dSignal,
            Width: width);
    }

    private static LatchPrimitive? DecodeLatch(SequentialBlockAst block, int index)
    {
        AssignAst? assign = FindPrimaryAssign(block.Body);
        if (assign is null) return null;
        string qSignal = LValueName(assign.Target);
        if (string.IsNullOrEmpty(qSignal)) return null;

        string dSignal = ExpressionToSignalName(assign.Source) ?? "?";
        string gate = block.Triggers.Count > 0 ? block.Triggers[0].SignalName : "?";
        return new LatchPrimitive($"latch_{qSignal}_{index}", qSignal, gate, dSignal, Width: 1);
    }

    private static AssignAst? FindPrimaryAssign(StatementAst body)
    {
        return body switch
        {
            AssignAst a => a,
            BeginAst b  => b.Statements.Select(FindPrimaryAssign).FirstOrDefault(s => s is not null),
            IfAst i     => FindPrimaryAssign(i.Then) ?? (i.Else is not null ? FindPrimaryAssign(i.Else) : null),
            _ => null
        };
    }

    // ── ContAssign decoding ─────────────────────────────────────────────────

    private static SchematicPrimitive? DecodeContAssign(
        ContAssignAst ca,
        int index,
        List<SchematicPrimitive> logic,
        IReadOnlyDictionary<string, SignalDecl> memorySignals)
    {
        string target = LValueName(ca.Target);
        if (string.IsNullOrEmpty(target)) return null;

        ExpressionMaterializationContext context = new(target, index, logic, memorySignals);
        return DecodeExpressionToPrimitive(target, ca.Source, index, context);
    }

    private static SchematicPrimitive? DecodeExpressionToPrimitive(
        string target,
        ExpressionAst source,
        int index,
        ExpressionMaterializationContext context)
    {
        return source switch
        {
            // P2.6-8: constant-only contassign → tie primitive instead of a
            // buffer with a dangling input. The literal carries its formatted
            // textual form so the symbol can render "8'h00" / "1'b1" verbatim.
            ConstExpr c             => new ConstantTiePrimitive($"tie_{target}_{index}", target, FormatConstLiteral(c), c.Width),
            SignalRef s             => new BufferPrimitive($"buf_{target}_{index}", target, s.Name, 1),
            BitSelectExpr bs        => DecodeSplitter(target, bs, index, context),
            ArraySelectExpr arr     => DecodeMemoryRead(target, arr, index, context),
            ConcatExpr cc           => DecodeJoiner(target, cc, index, context),
            ReplicateExpr rep       => DecodeReplicate(target, rep, index, context),
            CondExpr cond           => DecodeTriStateOrMux(target, cond, index, context),
            UnaryExpr u when u.Op == UnaryOp.Not => new InverterPrimitive(
                $"inv_{target}_{index}",
                target,
                MaterializeExpression(u.Operand, context, "not_in") ?? "?",
                1),
            UnaryExpr u             => DecodeUnaryGate(target, u, index, context),
            BinaryExpr b            => DecodeBinary(target, b, index, context),
            ExtendExpr ex           => new BufferPrimitive(
                $"buf_{target}_{index}",
                target,
                MaterializeExpression(ex.Inner, context, "extend_in") ?? "?",
                1),
            _ => null
        };
    }

    private static string FormatConstLiteral(ConstExpr c)
    {
        // Single-bit constants get IEEE-style 1'b0 / 1'b1 (or ground / Vdd symbol
        // semantics). Wider constants use hex. BigInteger's "X" format pads to
        // an even number of hex digits when positive — strip the leading zero
        // when present so the literal reads naturally (16'hCAFE not 16'h0CAFE).
        if (c.Width == 1) return $"1'b{(c.Value == 0 ? "0" : "1")}";
        string hex = c.Value.ToString("X");
        if (hex.Length > 1 && hex[0] == '0') hex = hex.TrimStart('0');
        if (hex.Length == 0) hex = "0";
        return $"{c.Width}'h{hex}";
    }

    private static SplitterPrimitive DecodeSplitter(
        string target,
        BitSelectExpr bs,
        int index,
        ExpressionMaterializationContext context)
    {
        string inputName = MaterializeExpression(bs.Base, context, "split_in") ?? "?";
        return new SplitterPrimitive(
            Id: $"split_{target}_{index}",
            OutputSignal: target,
            InputSignal: inputName,
            Range: bs.Range,
            InputWidth: 0,    // resolved by renderer from signal table
            OutputWidth: bs.Range.Width);
    }

    private static JoinerPrimitive DecodeJoiner(
        string target,
        ConcatExpr cc,
        int index,
        ExpressionMaterializationContext context)
    {
        List<string> inputs = cc.Parts
            .Select((p, i) => MaterializeExpression(p, context, $"join_in_{i}") ?? "?")
            .ToList();
        return new JoinerPrimitive(
            Id: $"join_{target}_{index}",
            OutputSignal: target,
            InputSignals: inputs,
            OutputWidth: 0);
    }

    private static MemoryReadPrimitive? DecodeMemoryRead(
        string target,
        ArraySelectExpr arr,
        int index,
        ExpressionMaterializationContext context)
    {
        string? memorySignal = ExpressionToSignalName(arr.Base);
        if (string.IsNullOrWhiteSpace(memorySignal))
            return null;

        string addressSignal = MaterializeExpression(arr.Index, context, "mem_addr") ?? "?";
        int cellWidth = context.TryGetMemory(memorySignal, out SignalDecl memory)
            ? memory.Width
            : 0;

        return new MemoryReadPrimitive(
            Id: $"memrd_{target}_{index}",
            MemorySignal: memorySignal,
            AddressSignal: addressSignal,
            OutputSignal: target,
            CellWidth: cellWidth);
    }

    private static SchematicPrimitive? DecodeReplicate(
        string target,
        ReplicateExpr rep,
        int index,
        ExpressionMaterializationContext context)
    {
        if (rep.Count <= 0)
            return null;

        if (rep.Pattern is ConstExpr c && !c.IsHighImpedance)
        {
            int patternWidth = Math.Max(1, c.Width);
            BigInteger mask = (BigInteger.One << patternWidth) - BigInteger.One;
            BigInteger patternValue = c.Value & mask;
            BigInteger value = BigInteger.Zero;
            for (int i = 0; i < rep.Count; i++)
            {
                value = (value << patternWidth) | patternValue;
            }

            return new ConstantTiePrimitive(
                $"tie_{target}_{index}",
                target,
                FormatConstLiteral(new ConstExpr(value, patternWidth * rep.Count, c.IsSigned)),
                patternWidth * rep.Count);
        }

        string? patternSignal = MaterializeExpression(rep.Pattern, context, "rep_pattern");
        if (string.IsNullOrWhiteSpace(patternSignal))
            return null;

        return new JoinerPrimitive(
            Id: $"join_{target}_{index}",
            OutputSignal: target,
            InputSignals: Enumerable.Repeat(patternSignal, rep.Count).ToList(),
            OutputWidth: 0);
    }

    /// <summary>
    /// Decodes a (possibly nested) <see cref="CondExpr"/> chain into an N-to-1 mux primitive.
    /// Pattern recognized: <c>sel1 ? a : sel0 ? b : c</c> → 3-input mux with selectors [sel1, sel0].
    ///
    /// Input-label semantics (P2.5-6):
    ///  • 2-input mux (single selector): keep classic "1"/"0" labels — most readable for
    ///    simple ternaries.
    ///  • N-input mux (chained ternaries): label input i by the SELECTOR signal name that
    ///    gates it (priority-encoder semantics). Final "else" branch labelled "else".
    ///    This makes it visually clear that the chain is `if-elif-elif-else` over different
    ///    signals — NOT a single multi-bit selector.
    /// </summary>
    /// <summary>
    /// P2.6-3: dispatch a CondExpr contassign. <c>en ? data : 'z</c> patterns
    /// (with high-impedance literal on one branch) become a TriStatePrimitive;
    /// everything else falls through to the regular mux decoder.
    /// </summary>
    private static SchematicPrimitive DecodeTriStateOrMux(
        string target,
        CondExpr root,
        int index,
        ExpressionMaterializationContext context)
    {
        if (TryExtractTriState(target, root, index, context) is { } tri) return tri;
        return DecodeMux(target, root, index, context);
    }

    private static TriStatePrimitive? TryExtractTriState(
        string target,
        CondExpr cond,
        int index,
        ExpressionMaterializationContext context)
    {
        // Pattern A: en ? data : 'z (active-high enable, data on the true side)
        if (cond.IfFalse is ConstExpr { IsHighImpedance: true } && cond.IfTrue is { } trueExpr
            && MaterializeExpression(trueExpr, context, "tristate_data") is { } dataA
            && MaterializeExpression(cond.Condition, context, "tristate_en") is { } enA)
        {
            int width = WidthOf(trueExpr) ?? 1;
            return new TriStatePrimitive($"tristate_{target}_{index}", target, dataA, enA, EnableActiveHigh: true, Width: width);
        }
        // Pattern B: en ? 'z : data (active-low enable, data on the false side)
        if (cond.IfTrue is ConstExpr { IsHighImpedance: true } && cond.IfFalse is { } falseExpr
            && MaterializeExpression(falseExpr, context, "tristate_data") is { } dataB
            && MaterializeExpression(cond.Condition, context, "tristate_en") is { } enB)
        {
            int width = WidthOf(falseExpr) ?? 1;
            return new TriStatePrimitive($"tristate_{target}_{index}", target, dataB, enB, EnableActiveHigh: false, Width: width);
        }
        return null;
    }

    private static int? WidthOf(ExpressionAst expr) => expr switch
    {
        ConstExpr c => c.Width,
        BitSelectExpr bs => bs.Range.Width,
        _ => null
    };

    private static MuxPrimitive DecodeMux(
        string target,
        CondExpr root,
        int index,
        ExpressionMaterializationContext context)
    {
        // Walk the chain iteratively to collect (selector, ifTrue) pairs + the final else.
        // We track BOTH the wire-up name and the display label separately:
        //   • wire-up name (bare signal, e.g. "control_pins") — used for endpoint
        //     registration in producers/consumers maps; must match the producer's
        //     real signal name or no edge will form.
        //   • display label (with bit detail, e.g. "control_pins[3:2]") — used as
        //     the port glyph so two selectors probing different bits of the same
        //     parent signal are visually distinguishable.
        List<string> selectors = [];        // wire-up names (used by builder for consumer keys)
        List<string> selectorLabels = [];   // display labels (used for port labels + chained input labels)
        List<ExpressionAst> branches = [];

        ExpressionAst current = root;
        while (current is CondExpr c)
        {
            selectors.Add(MaterializeExpression(c.Condition, context, $"mux_sel_{selectors.Count}") ?? "?");
            selectorLabels.Add(ExpressionToReadableLabel(c.Condition) ?? "?");
            branches.Add(c.IfTrue);
            current = c.IfFalse;
        }
        branches.Add(current); // final else branch

        // Choose labels based on chain depth
        List<MuxInput> inputs = [];
        bool simpleTernary = selectors.Count == 1;
        for (int i = 0; i < branches.Count; i++)
        {
            MuxSource source = ToMuxSource(branches[i], context, $"mux_in_{i}");
            string label;
            if (simpleTernary)
            {
                // sel ? a : b  →  input0="1" (when sel=1), input1="0" (when sel=0)
                label = i == 0 ? "1" : "0";
            }
            else
            {
                // Chained: input i is taken when selectors[i] is the first true
                // selector. We use the bit-AWARE label (selectorLabels[i]) for the
                // branch label so the user sees "control_pins[3:2]" not just
                // "control_pins" when sibling branches probe different bits of the
                // same parent signal. The last input is the default ("else").
                label = i < selectorLabels.Count ? selectorLabels[i] : "else";
            }
            // Suffix the label with the constant value (or "·X" for don't-care)
            // when the source has no wire. Without this, ports backed by constants
            // look identical to unconnected ports — the user can't tell at a glance
            // that the missing wire is intentional. P2.5-6 fix for Issue 4.
            if (source is MuxConstantSource constSrc)
                label = label + "·" + constSrc.Literal;
            inputs.Add(new MuxInput(label, source));
        }

        return new MuxPrimitive(
            Id: $"mux_{target}_{index}",
            OutputSignal: target,
            SelectSignals: selectors,
            Inputs: inputs,
            Width: 1,
            SelectorLabels: selectorLabels);
    }

    /// <summary>
    /// Converts an expression node into a <see cref="MuxSource"/>. Recognized cases:
    ///  • <see cref="SignalRef"/>      → wire-source (signal name)
    ///  • <see cref="ConstExpr"/>      → constant literal (e.g. "8'h0")
    ///  • <see cref="BitSelectExpr"/>  → wire-source via the base varref
    ///  • <see cref="ExtendExpr"/>     → wire-source via the inner expression
    ///  • Anything else (complex sub-expression: BinaryExpr, ConcatExpr, etc.) →
    ///    <see cref="MuxConstantSource"/> with literal "X" (don't-care).
    ///
    /// Verilator-internal signals (<c>__V*</c>) ALSO become don't-care: P2.5-4
    /// hides their defining contassigns, so a mux input referencing one would
    /// otherwise be an unconnected port with no producer. Marking it as X gives
    /// the user a clear "don't care from a folded tmp" instead of a phantom wire
    /// expectation. P2.6-1 (tmp fold) will replace this with the actual folded
    /// expression once it lands.
    /// </summary>
    private static MuxSource ToMuxSource(
        ExpressionAst expr,
        ExpressionMaterializationContext context,
        string role)
    {
        MuxSource result = expr switch
        {
            SignalRef s => new MuxSignalSource(s.Name),
            ConstExpr c => new MuxConstantSource(c.Value.ToString(), c.Width),
            _ => MaterializeExpression(expr, context, role) is { Length: > 0 } name
                    ? new MuxSignalSource(name)
                    : new MuxConstantSource("X", 1)
        };
        // Promote __V* internal-tmp references to don't-care so the renderer sees
        // a labelled empty port instead of a silently-unconnected one.
        if (result is MuxSignalSource sig && IsVerilatorInternalSignal(sig.SignalName))
            return new MuxConstantSource("X", 1);
        return result;
    }

    private static SchematicPrimitive? DecodeUnaryGate(
        string target,
        UnaryExpr u,
        int index,
        ExpressionMaterializationContext context)
    {
        GateKind? gateKind = u.Op switch
        {
            UnaryOp.ReduceAnd => GateKind.ReduceAnd,
            UnaryOp.ReduceOr  => GateKind.ReduceOr,
            UnaryOp.ReduceXor => GateKind.ReduceXor,
            _ => null
        };

        if (gateKind is null) return null;

        string input = MaterializeExpression(u.Operand, context, "unary_in") ?? "?";
        return new GatePrimitive($"op_{target}_{index}", target, gateKind.Value, [input], 1);
    }

    private static SchematicPrimitive? DecodeBinary(
        string target,
        BinaryExpr b,
        int index,
        ExpressionMaterializationContext context)
    {
        string left = MaterializeExpression(b.Left, context, "left") ?? "?";
        string right = MaterializeExpression(b.Right, context, "right") ?? "?";

        // Logic gates
        GateKind? gateKind = b.Op switch
        {
            BinaryOp.And => GateKind.And,
            BinaryOp.Or  => GateKind.Or,
            BinaryOp.Xor => GateKind.Xor,
            _ => null
        };

        if (gateKind is not null)
            return new GatePrimitive($"op_{target}_{index}", target, gateKind.Value, [left, right], 1);

        // Arithmetic / comparison
        ArithKind? arithKind = b.Op switch
        {
            BinaryOp.Add                  => ArithKind.Add,
            BinaryOp.Sub                  => ArithKind.Sub,
            BinaryOp.Mul                  => ArithKind.Mul,
            BinaryOp.Div                  => ArithKind.Div,
            BinaryOp.Mod                  => ArithKind.Mod,
            BinaryOp.ShiftLeft            => ArithKind.ShiftLeft,
            BinaryOp.ShiftRight           => ArithKind.ShiftRight,
            BinaryOp.ShiftRightArithmetic => ArithKind.ShiftRightArithmetic,
            BinaryOp.Equal                => ArithKind.Equal,
            BinaryOp.NotEqual             => ArithKind.NotEqual,
            BinaryOp.LessThan             => ArithKind.LessThan,
            BinaryOp.GreaterThan          => ArithKind.GreaterThan,
            BinaryOp.LessOrEqual          => ArithKind.LessOrEqual,
            BinaryOp.GreaterOrEqual       => ArithKind.GreaterOrEqual,
            _ => null
        };

        if (arithKind is not null)
            return new ArithPrimitive($"op_{target}_{index}", target, arithKind.Value, left, right, 1);

        // logand / logor — treat as logic gates for now
        return new GatePrimitive($"op_{target}_{index}", target,
            b.Op == BinaryOp.LogicAnd ? GateKind.And : GateKind.Or,
            [left, right], 1);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static string LValueName(LValueAst lval) => lval switch
    {
        VarRefLValue v       => v.Name,
        BitSelectLValue b    => b.SignalName,
        ArraySelectLValue a  => a.SignalName,
        StructFieldLValue sf => sf.SignalName,
        ConcatLValue c       => c.Parts.Count > 0 ? LValueName(c.Parts[0]) : string.Empty,
        _ => string.Empty
    };

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

    /// <summary>Best-effort name extraction for an expression. Returns null for non-signal expressions.</summary>
    private static string? ExpressionToSignalName(ExpressionAst expr) => expr switch
    {
        SignalRef s        => s.Name,
        BitSelectExpr bs   => ExpressionToSignalName(bs.Base),
        ExtendExpr ex      => ExpressionToSignalName(ex.Inner),
        _ => null
    };

    private static string? MaterializeExpression(
        ExpressionAst expr,
        ExpressionMaterializationContext context,
        string role)
    {
        if (ExpressionToSignalName(expr) is { Length: > 0 } name)
            return name;

        if (expr is FunctionCallExpr)
            return null;

        string signalName = context.CreateSignalName(role);
        int primitiveIndex = context.NextPrimitiveIndex();
        SchematicPrimitive? primitive = DecodeExpressionToPrimitive(signalName, expr, primitiveIndex, context);
        if (primitive is null)
            return null;

        context.Logic.Add(primitive);
        return signalName;
    }

    /// <summary>
    /// Like <see cref="ExpressionToSignalName"/> but preserves bit-select range info
    /// in the returned string (e.g. <c>"control_pins[3:2]"</c>) so chained-mux
    /// selectors that probe different bits of the same parent signal are visually
    /// distinguishable. Used only for DISPLAY labels — wire endpoint resolution
    /// still uses the bare signal name via <see cref="ExpressionToSignalName"/>.
    /// </summary>
    private static string? ExpressionToReadableLabel(ExpressionAst expr) => expr switch
    {
        SignalRef s        => s.Name,
        BitSelectExpr bs when ExpressionToSignalName(bs.Base) is { Length: > 0 } baseName
                           => $"{baseName}{bs.Range}",
        BitSelectExpr bs   => ExpressionToReadableLabel(bs.Base),
        ExtendExpr ex      => ExpressionToReadableLabel(ex.Inner),
        _ => null
    };

    private sealed class ExpressionMaterializationContext(
        string ownerSignal,
        int ownerIndex,
        List<SchematicPrimitive> logic,
        IReadOnlyDictionary<string, SignalDecl> memorySignals)
    {
        private int _signalIndex;
        private int _primitiveIndex;

        public List<SchematicPrimitive> Logic { get; } = logic;

        public bool TryGetMemory(string signalName, out SignalDecl memory)
        {
            if (memorySignals.TryGetValue(signalName, out SignalDecl? resolved))
            {
                memory = resolved;
                return true;
            }

            memory = null!;
            return false;
        }

        public string CreateSignalName(string role)
        {
            string safeRole = new(role.Select(static c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
            return $"__schematic_expr_{ownerSignal}_{ownerIndex}_{safeRole}_{_signalIndex++}";
        }

        public int NextPrimitiveIndex() => 10_000 + (ownerIndex * 100) + _primitiveIndex++;
    }
}
