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

        List<SignalPrimitive> signals = module.LocalSignals
            .Where(static s => s.ArrayDims.Count == 0)
            .Select(s => new SignalPrimitive($"sig_{s.Name}", s.Name, s.Width, s.IsRegistered))
            .ToList();

        List<InstancePrimitive> instances = module.Instances
            .Select(DecodeInstance)
            .ToList();

        List<SchematicPrimitive> logic = [];

        // Memories
        foreach (SignalDecl s in module.LocalSignals.Where(static x => x.ArrayDims.Count > 0))
        {
            BitRange dim = s.ArrayDims[0];
            logic.Add(new MemoryPrimitive($"mem_{s.Name}", s.Name, s.Width, dim.Hi, dim.Lo));
        }

        // Sequential blocks → FF / latch
        int seqIndex = 0;
        foreach (SequentialBlockAst block in module.SequentialBlocks)
        {
            SchematicPrimitive? primitive = DecodeSequentialBlock(block, seqIndex++);
            if (primitive is not null) logic.Add(primitive);
        }

        // Continuous assignments → combinational primitives
        int caIndex = 0;
        foreach (ContAssignAst ca in module.ContAssigns)
        {
            SchematicPrimitive? primitive = DecodeContAssign(ca, caIndex++);
            if (primitive is not null) logic.Add(primitive);
        }

        return new SchematicPrimitiveList(module.Name, ports, signals, instances, logic);
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

    private static SchematicPrimitive? DecodeSequentialBlock(SequentialBlockAst block, int index)
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

        // Determine D signal (RHS) — peel off async reset mux if present
        ExpressionAst source = assign.Source;
        string? asyncResetSignal = null;
        if (asyncReset is not null && source is CondExpr cond && cond.Condition is SignalRef condRef &&
            string.Equals(condRef.Name, asyncReset.SignalName, StringComparison.OrdinalIgnoreCase))
        {
            // Pattern: q <= rst ? data : reset_value;
            asyncResetSignal = asyncReset.SignalName;
            source = cond.IfTrue;
        }

        string dSignal = ExpressionToSignalName(source) ?? "?";
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

    private static SchematicPrimitive? DecodeContAssign(ContAssignAst ca, int index)
    {
        string target = LValueName(ca.Target);
        if (string.IsNullOrEmpty(target)) return null;

        return ca.Source switch
        {
            SignalRef s             => new BufferPrimitive($"buf_{target}_{index}", target, s.Name, 1),
            BitSelectExpr bs        => DecodeSplitter(target, bs, index),
            ConcatExpr cc           => DecodeJoiner(target, cc, index),
            CondExpr cond           => DecodeMux(target, cond, index),
            UnaryExpr u when u.Op == UnaryOp.Not => new InverterPrimitive($"inv_{target}_{index}", target, ExpressionToSignalName(u.Operand) ?? "?", 1),
            UnaryExpr u             => DecodeUnaryGate(target, u, index),
            BinaryExpr b            => DecodeBinary(target, b, index),
            ExtendExpr ex           => new BufferPrimitive($"buf_{target}_{index}", target, ExpressionToSignalName(ex.Inner) ?? "?", 1),
            _ => null
        };
    }

    private static SplitterPrimitive DecodeSplitter(string target, BitSelectExpr bs, int index)
    {
        string inputName = ExpressionToSignalName(bs.Base) ?? "?";
        return new SplitterPrimitive(
            Id: $"split_{target}_{index}",
            OutputSignal: target,
            InputSignal: inputName,
            Range: bs.Range,
            InputWidth: 0,    // resolved by renderer from signal table
            OutputWidth: bs.Range.Width);
    }

    private static JoinerPrimitive DecodeJoiner(string target, ConcatExpr cc, int index)
    {
        List<string> inputs = cc.Parts.Select(p => ExpressionToSignalName(p) ?? "?").ToList();
        return new JoinerPrimitive(
            Id: $"join_{target}_{index}",
            OutputSignal: target,
            InputSignals: inputs,
            OutputWidth: 0);
    }

    /// <summary>
    /// Decodes a (possibly nested) <see cref="CondExpr"/> chain into an N-to-1 mux primitive.
    /// Pattern recognized: <c>sel1 ? a : sel0 ? b : c</c> → 3-input mux with selectors [sel1, sel0].
    /// </summary>
    private static MuxPrimitive DecodeMux(string target, CondExpr root, int index)
    {
        List<string> selectors = [];
        List<MuxInput> inputs = [];

        FlattenCondChain(root, selectors, inputs);

        return new MuxPrimitive(
            Id: $"mux_{target}_{index}",
            OutputSignal: target,
            SelectSignals: selectors,
            Inputs: inputs,
            Width: 1);
    }

    private static void FlattenCondChain(ExpressionAst expr, List<string> selectors, List<MuxInput> inputs)
    {
        if (expr is CondExpr c)
        {
            string sel = ExpressionToSignalName(c.Condition) ?? "?";
            // First time we see a selector at this depth: add to list. Re-use existing entries on subsequent depths
            // to keep the mux compact when the same selector chain repeats.
            if (selectors.Count <= GetCurrentDepth(inputs))
                selectors.Add(sel);

            // "true" branch becomes the next input
            inputs.Add(new MuxInput("1", ToMuxSource(c.IfTrue)));

            // Recurse into the "false" branch
            FlattenCondChain(c.IfFalse, selectors, inputs);
        }
        else
        {
            // Terminal: the final "else" branch
            inputs.Add(new MuxInput("0", ToMuxSource(expr)));
        }
    }

    private static int GetCurrentDepth(List<MuxInput> inputs) => inputs.Count;

    private static MuxSource ToMuxSource(ExpressionAst expr) => expr switch
    {
        SignalRef s => new MuxSignalSource(s.Name),
        ConstExpr c => new MuxConstantSource(c.Value.ToString(), c.Width),
        _           => new MuxSignalSource(ExpressionToSignalName(expr) ?? "?")
    };

    private static SchematicPrimitive? DecodeUnaryGate(string target, UnaryExpr u, int index)
    {
        GateKind? gateKind = u.Op switch
        {
            UnaryOp.ReduceAnd => GateKind.ReduceAnd,
            UnaryOp.ReduceOr  => GateKind.ReduceOr,
            UnaryOp.ReduceXor => GateKind.ReduceXor,
            _ => null
        };

        if (gateKind is null) return null;

        string input = ExpressionToSignalName(u.Operand) ?? "?";
        return new GatePrimitive($"op_{target}_{index}", target, gateKind.Value, [input], 1);
    }

    private static SchematicPrimitive? DecodeBinary(string target, BinaryExpr b, int index)
    {
        string left = ExpressionToSignalName(b.Left) ?? "?";
        string right = ExpressionToSignalName(b.Right) ?? "?";

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

    /// <summary>Best-effort name extraction for an expression. Returns null for non-signal expressions.</summary>
    private static string? ExpressionToSignalName(ExpressionAst expr) => expr switch
    {
        SignalRef s        => s.Name,
        BitSelectExpr bs   => ExpressionToSignalName(bs.Base),
        ExtendExpr ex      => ExpressionToSignalName(ex.Inner),
        _ => null
    };
}
