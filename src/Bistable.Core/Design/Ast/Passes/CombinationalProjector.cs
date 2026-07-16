namespace Bistable.Core.Design.Ast.Passes;

/// <summary>
/// Lowers procedural combinational blocks into one continuous assignment per
/// fully-defined signal. Partial bit writes are tracked per bit and are emitted
/// only after every bit of the destination is defined, preserving selection
/// semantics without creating multiple cosmetic drivers for one bus.
/// </summary>
public static class CombinationalProjector
{
    public const int MaxExpressionDepth = 128;
    public const int MaxStatementDepth = 128;
    public const int MaxProjectedWidth = 4096;

    public static DesignAst Project(DesignAst design)
    {
        ArgumentNullException.ThrowIfNull(design);
        return design with { Modules = design.Modules.Select(Project).ToList() };
    }

    public static ModuleAst Project(ModuleAst module)
    {
        ArgumentNullException.ThrowIfNull(module);

        Dictionary<string, int> widths = BuildWidthMap(module);
        List<ContAssignAst> contAssigns = [.. module.ContAssigns];
        List<CombinationalBlockAst> blocks = new(module.CombinationalBlocks.Count);

        foreach (CombinationalBlockAst block in module.CombinationalBlocks)
        {
            if (block.ProjectionResults is not null)
            {
                blocks.Add(block);
                continue;
            }

            BlockProjection projection = ProjectBlock(block.Body, widths, contAssigns.Count);
            contAssigns.AddRange(projection.Assignments);
            blocks.Add(block with { ProjectionResults = projection.Results });
        }

        return module with
        {
            ContAssigns = contAssigns,
            CombinationalBlocks = blocks,
        };
    }

    private static BlockProjection ProjectBlock(
        StatementAst body,
        IReadOnlyDictionary<string, int> widths,
        int firstContAssignIndex)
    {
        TargetCatalog targets = new(widths);
        targets.Collect(body);
        Dictionary<string, SymbolicVector> finalState = Execute(
            body,
            new Dictionary<string, SymbolicVector>(StringComparer.Ordinal),
            targets,
            depth: 0);

        List<ContAssignAst> assignments = [];
        List<CombinationalProjectionTarget> results = new(targets.Ordered.Count);

        foreach (TargetDescriptor target in targets.Ordered)
        {
            finalState.TryGetValue(target.Key, out SymbolicVector? value);
            string? reason = target.UnsupportedReason ?? value?.UnsupportedReason;
            ExpressionAst? expression = null;

            if (target.Width > MaxProjectedWidth)
            {
                reason ??= $"Target width {target.Width} exceeds maximum projected width {MaxProjectedWidth}.";
            }
            else if (reason is null && value is not null && TryBuildExpression(value, out ExpressionAst built))
            {
                expression = built;
                if (GetExpressionDepth(expression) > MaxExpressionDepth)
                {
                    reason = $"Projected expression exceeds maximum depth {MaxExpressionDepth}.";
                    expression = null;
                }
            }
            else if (reason is null)
            {
                reason = "Target is not assigned on every control-flow path (latch risk).";
            }

            if (reason is null && expression is not null)
            {
                int contAssignIndex = firstContAssignIndex + assignments.Count;
                assignments.Add(new ContAssignAst(target.ProjectedTarget, expression));
                results.Add(new CombinationalProjectionTarget(
                    target.Index,
                    target.ProjectedTarget,
                    target.SignalName,
                    CombinationalProjectionStatus.Projected,
                    "Projected to a synthetic continuous assignment.",
                    CollectSignalRefs(expression),
                    contAssignIndex));
            }
            else
            {
                IReadOnlyList<string> reads = value is null
                    ? []
                    : value.ReadSignals.Order(StringComparer.Ordinal).ToList();
                results.Add(new CombinationalProjectionTarget(
                    target.Index,
                    target.ProjectedTarget,
                    target.SignalName,
                    CombinationalProjectionStatus.Unsupported,
                    reason ?? "Combinational target could not be projected.",
                    reads,
                    SyntheticContAssignIndex: null));
            }
        }

        return new BlockProjection(assignments, results);
    }

    private static Dictionary<string, SymbolicVector> Execute(
        StatementAst statement,
        Dictionary<string, SymbolicVector> state,
        TargetCatalog targets,
        int depth)
    {
        if (depth > MaxStatementDepth)
        {
            MarkSubtreeUnsupported(statement, state, targets,
                $"Combinational statement nesting exceeds maximum depth {MaxStatementDepth}.");
            return state;
        }

        switch (statement)
        {
            case BeginAst begin:
                foreach (StatementAst child in begin.Statements)
                {
                    state = Execute(child, state, targets, depth + 1);
                }
                return state;

            case AssignAst assign:
                ApplyAssignment(assign, state, targets);
                return state;

            case IfAst branch:
            {
                Dictionary<string, SymbolicVector> before = Clone(state);
                Dictionary<string, SymbolicVector> thenState = Execute(branch.Then, Clone(before), targets, depth + 1);
                Dictionary<string, SymbolicVector> elseState = branch.Else is null
                    ? Clone(before)
                    : Execute(branch.Else, Clone(before), targets, depth + 1);
                return MergeConditional(branch.Condition, thenState, elseState, targets);
            }

            case CaseAst caseStatement:
                return ExecuteCase(caseStatement, state, targets, depth + 1);

            default:
                MarkSubtreeUnsupported(statement, state, targets,
                    $"Unsupported combinational statement '{statement.GetType().Name}'.");
                return state;
        }
    }

    private static void ApplyAssignment(
        AssignAst assignment,
        Dictionary<string, SymbolicVector> state,
        TargetCatalog targets)
    {
        TargetDescriptor target = targets.GetOrAdd(assignment.Target);
        HashSet<string> reads = CollectSignalRefsSet(assignment.Source);
        AddLValueReads(assignment.Target, reads);

        if (target.UnsupportedReason is { } unsupportedTarget)
        {
            state[target.Key] = SymbolicVector.Unsupported(target.Width, unsupportedTarget, reads);
            return;
        }
        if (assignment.IsNonBlocking)
        {
            state[target.Key] = SymbolicVector.Unsupported(
                target.Width,
                "Non-blocking assignment is not supported in a combinational block.",
                reads);
            return;
        }

        switch (assignment.Target)
        {
            case VarRefLValue:
                state[target.Key] = SymbolicVector.FromWholeExpression(
                    target.Width,
                    assignment.Source,
                    reads);
                break;

            case BitSelectLValue bitSelect:
                if (bitSelect.Range.Lo < 0 || bitSelect.Range.Hi >= target.Width)
                {
                    state[target.Key] = SymbolicVector.Unsupported(
                        target.Width,
                        $"Bit-select target range {bitSelect.Range} is outside signal width {target.Width}.",
                        reads);
                    break;
                }

                SymbolicVector updated = state.TryGetValue(target.Key, out SymbolicVector? current)
                    ? current.Copy()
                    : SymbolicVector.Undefined(target.Width);
                if (updated.UnsupportedReason is not null)
                {
                    updated.ReadSignals.UnionWith(reads);
                    state[target.Key] = updated;
                    break;
                }

                for (int destinationBit = bitSelect.Range.Lo; destinationBit <= bitSelect.Range.Hi; destinationBit++)
                {
                    int sourceBit = destinationBit - bitSelect.Range.Lo;
                    updated.Bits[destinationBit] = new SymbolicLane(
                        assignment.Source,
                        sourceBit,
                        bitSelect.Range.Width);
                }
                updated.ReadSignals.UnionWith(reads);
                state[target.Key] = updated;
                break;
        }
    }

    private static Dictionary<string, SymbolicVector> ExecuteCase(
        CaseAst caseStatement,
        Dictionary<string, SymbolicVector> state,
        TargetCatalog targets,
        int depth)
    {
        Dictionary<string, SymbolicVector> before = Clone(state);
        Dictionary<string, SymbolicVector> result = caseStatement.Default is null
            ? Clone(before)
            : Execute(caseStatement.Default, Clone(before), targets, depth);

        for (int armIndex = caseStatement.Arms.Count - 1; armIndex >= 0; armIndex--)
        {
            CaseArm arm = caseStatement.Arms[armIndex];
            Dictionary<string, SymbolicVector> armState = Execute(arm.Body, Clone(before), targets, depth);
            if (arm.Label is not ConstExpr)
            {
                HashSet<string> affectedTargets = [];
                CollectTargetKeys(arm.Body, targets, affectedTargets, depth: 0);
                HashSet<string> reads = CollectSignalRefsSet(caseStatement.Subject);
                reads.UnionWith(CollectSignalRefsSet(arm.Label));

                foreach (string key in affectedTargets)
                {
                    TargetDescriptor target = targets.ByKey(key);
                    HashSet<string> targetReads = new(reads, StringComparer.Ordinal);
                    if (armState.TryGetValue(key, out SymbolicVector? armValue))
                        targetReads.UnionWith(armValue.ReadSignals);
                    if (result.TryGetValue(key, out SymbolicVector? fallbackValue))
                        targetReads.UnionWith(fallbackValue.ReadSignals);
                    result[key] = SymbolicVector.Unsupported(
                        target.Width,
                        "Case arm label is not a constant expression.",
                        targetReads);
                }
                continue;
            }

            ExpressionAst condition = new BinaryExpr(BinaryOp.Equal, caseStatement.Subject, arm.Label);
            result = MergeConditional(condition, armState, result, targets);
        }

        return result;
    }

    private static Dictionary<string, SymbolicVector> MergeConditional(
        ExpressionAst condition,
        IReadOnlyDictionary<string, SymbolicVector> trueState,
        IReadOnlyDictionary<string, SymbolicVector> falseState,
        TargetCatalog targets)
    {
        Dictionary<string, SymbolicVector> merged = new(StringComparer.Ordinal);
        HashSet<string> keys = trueState.Keys.Concat(falseState.Keys).ToHashSet(StringComparer.Ordinal);
        HashSet<string> conditionReads = CollectSignalRefsSet(condition);

        foreach (string key in keys)
        {
            TargetDescriptor target = targets.ByKey(key);
            SymbolicVector trueValue = trueState.TryGetValue(key, out SymbolicVector? resolvedTrue)
                ? resolvedTrue
                : SymbolicVector.Undefined(target.Width);
            SymbolicVector falseValue = falseState.TryGetValue(key, out SymbolicVector? resolvedFalse)
                ? resolvedFalse
                : SymbolicVector.Undefined(target.Width);
            HashSet<string> reads = new(conditionReads, StringComparer.Ordinal);
            reads.UnionWith(trueValue.ReadSignals);
            reads.UnionWith(falseValue.ReadSignals);

            if (trueValue.UnsupportedReason is not null || falseValue.UnsupportedReason is not null)
            {
                merged[key] = SymbolicVector.Unsupported(
                    target.Width,
                    JoinReasons(trueValue.UnsupportedReason, falseValue.UnsupportedReason),
                    reads);
                continue;
            }

            if (TryBuildExpression(trueValue, out ExpressionAst trueExpression)
                && TryBuildExpression(falseValue, out ExpressionAst falseExpression))
            {
                ExpressionAst expression = Equals(trueExpression, falseExpression)
                    ? trueExpression
                    : new CondExpr(condition, trueExpression, falseExpression);
                merged[key] = SymbolicVector.FromWholeExpression(target.Width, expression, CollectSignalRefsSet(expression));
                continue;
            }

            SymbolicVector bitwise = SymbolicVector.Undefined(target.Width);
            bitwise.ReadSignals.UnionWith(reads);
            for (int bit = 0; bit < target.Width; bit++)
            {
                SymbolicLane? whenTrue = trueValue.Bits[bit];
                SymbolicLane? whenFalse = falseValue.Bits[bit];
                if (whenTrue is null || whenFalse is null) continue;
                if (Equals(whenTrue, whenFalse))
                {
                    bitwise.Bits[bit] = whenTrue;
                    continue;
                }

                ExpressionAst bitExpression = new CondExpr(
                    condition,
                    LaneExpression(whenTrue),
                    LaneExpression(whenFalse));
                bitwise.Bits[bit] = new SymbolicLane(bitExpression, SourceBit: 0, SourceWidth: 1);
            }
            merged[key] = bitwise;
        }

        return merged;
    }

    private static bool TryBuildExpression(SymbolicVector value, out ExpressionAst expression)
    {
        expression = null!;
        if (value.UnsupportedReason is not null || value.Bits.Any(static bit => bit is null)) return false;

        int width = value.Bits.Length;
        SymbolicLane first = value.Bits[0]!;
        bool wholeExpression = first.SourceWidth == width && first.SourceBit == 0;
        for (int bit = 1; wholeExpression && bit < width; bit++)
        {
            SymbolicLane lane = value.Bits[bit]!;
            wholeExpression = lane.SourceWidth == width
                              && lane.SourceBit == bit
                              && Equals(lane.Source, first.Source);
        }
        if (wholeExpression)
        {
            expression = first.Source;
            return true;
        }

        List<ExpressionAst> parts = [];
        for (int high = width - 1; high >= 0;)
        {
            SymbolicLane highLane = value.Bits[high]!;
            int low = high;
            while (low > 0)
            {
                SymbolicLane candidate = value.Bits[low - 1]!;
                SymbolicLane previous = value.Bits[low]!;
                if (!Equals(candidate.Source, highLane.Source)
                    || candidate.SourceWidth != highLane.SourceWidth
                    || candidate.SourceBit != previous.SourceBit - 1)
                {
                    break;
                }
                low--;
            }

            SymbolicLane lowLane = value.Bits[low]!;
            int partWidth = high - low + 1;
            bool entireSource = lowLane.SourceBit == 0
                                && highLane.SourceBit == highLane.SourceWidth - 1
                                && partWidth == highLane.SourceWidth;
            parts.Add(entireSource || highLane.SourceWidth == 1
                ? highLane.Source
                : new BitSelectExpr(highLane.Source, new BitRange(highLane.SourceBit, lowLane.SourceBit)));
            high = low - 1;
        }

        expression = parts.Count == 1 ? parts[0] : new ConcatExpr(parts);
        return true;
    }

    private static ExpressionAst LaneExpression(SymbolicLane lane) =>
        lane.SourceWidth == 1
            ? lane.Source
            : new BitSelectExpr(lane.Source, new BitRange(lane.SourceBit, lane.SourceBit));

    private static string JoinReasons(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first)) return second ?? "Combinational projection failed.";
        if (string.IsNullOrWhiteSpace(second) || string.Equals(first, second, StringComparison.Ordinal)) return first;
        return $"{first} {second}";
    }

    private static void MarkSubtreeUnsupported(
        StatementAst statement,
        Dictionary<string, SymbolicVector> state,
        TargetCatalog targets,
        string reason)
    {
        HashSet<string> keys = [];
        CollectTargetKeys(statement, targets, keys, depth: 0);
        HashSet<string> reads = CollectStatementReads(statement, depth: 0);
        foreach (string key in keys)
        {
            TargetDescriptor target = targets.ByKey(key);
            state[key] = SymbolicVector.Unsupported(target.Width, reason, reads);
        }
    }

    private static void CollectTargetKeys(
        StatementAst statement,
        TargetCatalog targets,
        HashSet<string> keys,
        int depth)
    {
        if (depth > MaxStatementDepth + 1) return;
        switch (statement)
        {
            case AssignAst assign:
                keys.Add(targets.GetOrAdd(assign.Target).Key);
                break;
            case BeginAst begin:
                foreach (StatementAst child in begin.Statements)
                    CollectTargetKeys(child, targets, keys, depth + 1);
                break;
            case IfAst branch:
                CollectTargetKeys(branch.Then, targets, keys, depth + 1);
                if (branch.Else is not null) CollectTargetKeys(branch.Else, targets, keys, depth + 1);
                break;
            case CaseAst caseStatement:
                foreach (CaseArm arm in caseStatement.Arms)
                    CollectTargetKeys(arm.Body, targets, keys, depth + 1);
                if (caseStatement.Default is not null)
                    CollectTargetKeys(caseStatement.Default, targets, keys, depth + 1);
                break;
        }
    }

    private static HashSet<string> CollectStatementReads(StatementAst statement, int depth)
    {
        HashSet<string> reads = new(StringComparer.Ordinal);
        if (depth > MaxStatementDepth + 1) return reads;
        switch (statement)
        {
            case AssignAst assign:
                reads.UnionWith(CollectSignalRefsSet(assign.Source));
                AddLValueReads(assign.Target, reads);
                break;
            case BeginAst begin:
                foreach (StatementAst child in begin.Statements)
                    reads.UnionWith(CollectStatementReads(child, depth + 1));
                break;
            case IfAst branch:
                reads.UnionWith(CollectSignalRefsSet(branch.Condition));
                reads.UnionWith(CollectStatementReads(branch.Then, depth + 1));
                if (branch.Else is not null)
                    reads.UnionWith(CollectStatementReads(branch.Else, depth + 1));
                break;
            case CaseAst caseStatement:
                reads.UnionWith(CollectSignalRefsSet(caseStatement.Subject));
                foreach (CaseArm arm in caseStatement.Arms)
                {
                    reads.UnionWith(CollectSignalRefsSet(arm.Label));
                    reads.UnionWith(CollectStatementReads(arm.Body, depth + 1));
                }
                if (caseStatement.Default is not null)
                    reads.UnionWith(CollectStatementReads(caseStatement.Default, depth + 1));
                break;
        }
        return reads;
    }

    private static void AddLValueReads(LValueAst target, HashSet<string> reads)
    {
        switch (target)
        {
            case ArraySelectLValue array:
                reads.UnionWith(CollectSignalRefsSet(array.Index));
                break;
            case ConcatLValue concat:
                foreach (LValueAst part in concat.Parts) AddLValueReads(part, reads);
                break;
        }
    }

    private static IReadOnlyList<string> CollectSignalRefs(ExpressionAst expression) =>
        CollectSignalRefsSet(expression).Order(StringComparer.Ordinal).ToList();

    private static HashSet<string> CollectSignalRefsSet(ExpressionAst expression)
    {
        HashSet<string> names = new(StringComparer.Ordinal);
        Stack<ExpressionAst> pending = new();
        pending.Push(expression);
        while (pending.Count > 0)
        {
            switch (pending.Pop())
            {
                case SignalRef signal:
                    names.Add(signal.Name);
                    break;
                case BitSelectExpr bitSelect:
                    pending.Push(bitSelect.Base);
                    break;
                case ArraySelectExpr arraySelect:
                    pending.Push(arraySelect.Base);
                    pending.Push(arraySelect.Index);
                    break;
                case ConcatExpr concat:
                    for (int i = concat.Parts.Count - 1; i >= 0; i--) pending.Push(concat.Parts[i]);
                    break;
                case ReplicateExpr replicate:
                    pending.Push(replicate.Pattern);
                    break;
                case ExtendExpr extend:
                    pending.Push(extend.Inner);
                    break;
                case BinaryExpr binary:
                    pending.Push(binary.Right);
                    pending.Push(binary.Left);
                    break;
                case UnaryExpr unary:
                    pending.Push(unary.Operand);
                    break;
                case CondExpr conditional:
                    pending.Push(conditional.IfFalse);
                    pending.Push(conditional.IfTrue);
                    pending.Push(conditional.Condition);
                    break;
                case FunctionCallExpr function:
                    for (int i = function.Args.Count - 1; i >= 0; i--) pending.Push(function.Args[i]);
                    break;
            }
        }
        return names;
    }

    private static int GetExpressionDepth(ExpressionAst expression)
    {
        Stack<(ExpressionAst Expression, int Depth)> pending = new();
        pending.Push((expression, 1));
        int maxDepth = 0;
        while (pending.Count > 0)
        {
            (ExpressionAst current, int depth) = pending.Pop();
            maxDepth = Math.Max(maxDepth, depth);
            if (maxDepth > MaxExpressionDepth) return maxDepth;
            switch (current)
            {
                case BitSelectExpr bitSelect:
                    pending.Push((bitSelect.Base, depth + 1));
                    break;
                case ArraySelectExpr arraySelect:
                    pending.Push((arraySelect.Base, depth + 1));
                    pending.Push((arraySelect.Index, depth + 1));
                    break;
                case ConcatExpr concat:
                    foreach (ExpressionAst part in concat.Parts) pending.Push((part, depth + 1));
                    break;
                case ReplicateExpr replicate:
                    pending.Push((replicate.Pattern, depth + 1));
                    break;
                case ExtendExpr extend:
                    pending.Push((extend.Inner, depth + 1));
                    break;
                case BinaryExpr binary:
                    pending.Push((binary.Left, depth + 1));
                    pending.Push((binary.Right, depth + 1));
                    break;
                case UnaryExpr unary:
                    pending.Push((unary.Operand, depth + 1));
                    break;
                case CondExpr conditional:
                    pending.Push((conditional.Condition, depth + 1));
                    pending.Push((conditional.IfTrue, depth + 1));
                    pending.Push((conditional.IfFalse, depth + 1));
                    break;
                case FunctionCallExpr function:
                    foreach (ExpressionAst argument in function.Args) pending.Push((argument, depth + 1));
                    break;
            }
        }
        return maxDepth;
    }

    private static Dictionary<string, SymbolicVector> Clone(
        IReadOnlyDictionary<string, SymbolicVector> state) =>
        state.ToDictionary(static pair => pair.Key, static pair => pair.Value.Copy(), StringComparer.Ordinal);

    private static Dictionary<string, int> BuildWidthMap(ModuleAst module)
    {
        Dictionary<string, int> widths = new(StringComparer.Ordinal);
        foreach (PortDecl port in module.Ports) widths[port.Name] = Math.Max(1, port.Width);
        foreach (SignalDecl signal in module.LocalSignals) widths[signal.Name] = Math.Max(1, signal.Width);
        return widths;
    }

    private sealed record BlockProjection(
        IReadOnlyList<ContAssignAst> Assignments,
        IReadOnlyList<CombinationalProjectionTarget> Results);

    private sealed record SymbolicLane(ExpressionAst Source, int SourceBit, int SourceWidth);

    private sealed class SymbolicVector
    {
        private SymbolicVector(
            SymbolicLane?[] bits,
            string? unsupportedReason,
            HashSet<string> readSignals)
        {
            Bits = bits;
            UnsupportedReason = unsupportedReason;
            ReadSignals = readSignals;
        }

        public SymbolicLane?[] Bits { get; }
        public string? UnsupportedReason { get; }
        public HashSet<string> ReadSignals { get; }

        public static SymbolicVector Undefined(int width) =>
            new(new SymbolicLane?[Math.Max(1, width)], null, new HashSet<string>(StringComparer.Ordinal));

        public static SymbolicVector FromWholeExpression(
            int width,
            ExpressionAst expression,
            IEnumerable<string> reads)
        {
            int resolvedWidth = Math.Max(1, width);
            SymbolicLane?[] bits = new SymbolicLane?[resolvedWidth];
            for (int bit = 0; bit < resolvedWidth; bit++)
                bits[bit] = new SymbolicLane(expression, bit, resolvedWidth);
            return new SymbolicVector(bits, null, reads.ToHashSet(StringComparer.Ordinal));
        }

        public static SymbolicVector Unsupported(int width, string reason, IEnumerable<string> reads) =>
            new(
                new SymbolicLane?[Math.Max(1, width)],
                reason,
                reads.ToHashSet(StringComparer.Ordinal));

        public SymbolicVector Copy() =>
            new([.. Bits], UnsupportedReason, new HashSet<string>(ReadSignals, StringComparer.Ordinal));
    }

    private sealed class TargetCatalog(IReadOnlyDictionary<string, int> widths)
    {
        private readonly Dictionary<string, TargetDescriptor> _byKey = new(StringComparer.Ordinal);
        private readonly List<TargetDescriptor> _ordered = [];

        public IReadOnlyList<TargetDescriptor> Ordered => _ordered;

        public void Collect(StatementAst statement) => Collect(statement, depth: 0);

        public TargetDescriptor ByKey(string key) => _byKey[key];

        public TargetDescriptor GetOrAdd(LValueAst target)
        {
            (string key, string signalName, LValueAst projectedTarget, string? unsupportedReason) = target switch
            {
                VarRefLValue variable => ($"signal:{variable.Name}", variable.Name, variable, null),
                BitSelectLValue bit => ($"signal:{bit.SignalName}", bit.SignalName, new VarRefLValue(bit.SignalName), null),
                ArraySelectLValue array => ($"array:{array.SignalName}", array.SignalName, array,
                    "Array-select combinational assignment targets are not supported."),
                StructFieldLValue field => ($"field:{field.SignalName}:{field.FieldName}", field.SignalName, field,
                    "Struct-field combinational assignment targets are not supported."),
                ConcatLValue concat => ($"concat:{_ordered.Count}", LValueName(concat), concat,
                    "Concatenated combinational assignment targets are not supported."),
                _ => ($"unknown:{_ordered.Count}", "?", target,
                    $"Target l-value '{target.GetType().Name}' is not supported."),
            };

            if (_byKey.TryGetValue(key, out TargetDescriptor? existing))
            {
                int minimumWidth = target is BitSelectLValue selected ? selected.Range.Hi + 1 : existing.Width;
                return Widen(existing, minimumWidth);
            }
            int width = widths.TryGetValue(signalName, out int resolvedWidth) ? Math.Max(1, resolvedWidth) : 1;
            if (target is BitSelectLValue selectedTarget)
                width = Math.Max(width, selectedTarget.Range.Hi + 1);
            TargetDescriptor created = new(
                _ordered.Count,
                key,
                projectedTarget,
                signalName,
                width,
                unsupportedReason);
            _byKey.Add(key, created);
            _ordered.Add(created);
            return created;
        }

        private TargetDescriptor ObserveAssignment(AssignAst assignment)
        {
            TargetDescriptor target = GetOrAdd(assignment.Target);
            int inferredSourceWidth = InferWidth(assignment.Source);
            return assignment.Target is VarRefLValue && inferredSourceWidth > target.Width
                ? Widen(target, inferredSourceWidth)
                : target;
        }

        private TargetDescriptor Widen(TargetDescriptor target, int minimumWidth)
        {
            if (minimumWidth <= target.Width) return target;
            TargetDescriptor widened = target with { Width = minimumWidth };
            _byKey[target.Key] = widened;
            _ordered[target.Index] = widened;
            return widened;
        }

        private int InferWidth(ExpressionAst expression) => expression switch
        {
            SignalRef signal => widths.GetValueOrDefault(signal.Name, 1),
            ConstExpr constant => Math.Max(1, constant.Width),
            BitSelectExpr bitSelect => bitSelect.Range.Width,
            ArraySelectExpr arraySelect => InferWidth(arraySelect.Base),
            ConcatExpr concat => concat.Parts.Sum(InferWidth),
            ReplicateExpr replicate => replicate.Count * InferWidth(replicate.Pattern),
            ExtendExpr extend => Math.Max(1, extend.TargetWidth),
            UnaryExpr unary => InferWidth(unary.Operand),
            BinaryExpr binary when binary.Op is BinaryOp.Equal or BinaryOp.NotEqual
                or BinaryOp.LessThan or BinaryOp.GreaterThan
                or BinaryOp.LessOrEqual or BinaryOp.GreaterOrEqual
                or BinaryOp.LogicAnd or BinaryOp.LogicOr => 1,
            BinaryExpr binary => Math.Max(InferWidth(binary.Left), InferWidth(binary.Right)),
            CondExpr conditional => Math.Max(InferWidth(conditional.IfTrue), InferWidth(conditional.IfFalse)),
            _ => 1,
        };

        private void Collect(StatementAst statement, int depth)
        {
            if (depth > MaxStatementDepth + 1) return;
            switch (statement)
            {
                case AssignAst assign:
                    ObserveAssignment(assign);
                    break;
                case BeginAst begin:
                    foreach (StatementAst child in begin.Statements) Collect(child, depth + 1);
                    break;
                case IfAst branch:
                    Collect(branch.Then, depth + 1);
                    if (branch.Else is not null) Collect(branch.Else, depth + 1);
                    break;
                case CaseAst caseStatement:
                    foreach (CaseArm arm in caseStatement.Arms) Collect(arm.Body, depth + 1);
                    if (caseStatement.Default is not null) Collect(caseStatement.Default, depth + 1);
                    break;
            }
        }

        private static string LValueName(LValueAst target) => target switch
        {
            VarRefLValue variable => variable.Name,
            BitSelectLValue bit => bit.SignalName,
            ArraySelectLValue array => array.SignalName,
            StructFieldLValue field => field.SignalName,
            ConcatLValue concat when concat.Parts.Count > 0 => LValueName(concat.Parts[0]),
            _ => "?",
        };
    }

    private sealed record TargetDescriptor(
        int Index,
        string Key,
        LValueAst ProjectedTarget,
        string SignalName,
        int Width,
        string? UnsupportedReason);
}
