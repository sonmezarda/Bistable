using System.Diagnostics;

namespace Bistable.Core.Design.Ast;

/// <summary>
/// Folds single-consumer Verilator internal temporaries back into their consumers.
/// This undoes CSE artifacts such as <c>__VdfgTmp_*</c> when they do not represent
/// a real sharing win, so downstream schematic decoding sees the original expression.
/// </summary>
public static class TempFolder
{
    private const int MaxIterations = 10;

    public static DesignAst Fold(DesignAst ast)
    {
        ArgumentNullException.ThrowIfNull(ast);
        return ast with { Modules = ast.Modules.Select(FoldModule).ToList() };
    }

    private static ModuleAst FoldModule(ModuleAst module)
    {
        ModuleAst current = module;
        for (int iteration = 0; iteration < MaxIterations; iteration++)
        {
            Dictionary<string, TempDef> tmps = BuildTempRegistry(current);
            if (tmps.Count == 0) break;

            Dictionary<string, int> consumerCounts = CountConsumers(current, tmps.Keys);
            HashSet<string> foldable = tmps
                .Where(kv => consumerCounts.GetValueOrDefault(kv.Key) == 1 || IsCheapAlias(kv.Value.Expression))
                .Where(kv => !DependsOnTempCycle(kv.Key, tmps))
                .Where(kv => WidthMatches(kv.Key, kv.Value, current))
                .Select(static kv => kv.Key)
                .ToHashSet(StringComparer.Ordinal);

            if (foldable.Count == 0) break;

            foreach (string skipped in tmps.Keys.Where(name => consumerCounts.GetValueOrDefault(name) == 1 && DependsOnTempCycle(name, tmps)))
                Trace.TraceWarning($"Skipping Verilator tmp fold for '{skipped}' because its definition is cyclic.");

            current = FoldOnce(current, tmps, foldable);
        }

        return current;
    }

    private static Dictionary<string, TempDef> BuildTempRegistry(ModuleAst module)
    {
        Dictionary<string, TempDef> tmps = new(StringComparer.Ordinal);
        foreach (ContAssignAst ca in module.ContAssigns)
        {
            string target = LValueName(ca.Target);
            if (IsVerilatorInternalSignal(target))
                tmps[target] = new TempDef(ca.Source);
        }
        return tmps;
    }

    private static Dictionary<string, int> CountConsumers(ModuleAst module, IEnumerable<string> tmpNames)
    {
        HashSet<string> tmpSet = tmpNames.ToHashSet(StringComparer.Ordinal);
        Dictionary<string, int> counts = tmpSet.ToDictionary(static n => n, static _ => 0, StringComparer.Ordinal);

        foreach (ContAssignAst ca in module.ContAssigns)
            CountRefs(ca.Source, tmpSet, counts);
        foreach (SequentialBlockAst block in module.SequentialBlocks)
            CountRefs(block.Body, tmpSet, counts);
        foreach (CombinationalBlockAst block in module.CombinationalBlocks)
            CountRefs(block.Body, tmpSet, counts);

        return counts;
    }

    private static ModuleAst FoldOnce(ModuleAst module, Dictionary<string, TempDef> tmps, HashSet<string> foldable)
    {
        Dictionary<string, ExpressionAst> replacements = foldable.ToDictionary(
            static name => name,
            name => ResolveReplacement(name, tmps, foldable),
            StringComparer.Ordinal);

        List<ContAssignAst> contAssigns = module.ContAssigns
            .Where(ca => !foldable.Contains(LValueName(ca.Target)))
            .Select(ca => ca with { Source = SubstituteMany(ca.Source, replacements) })
            .ToList();

        List<SequentialBlockAst> sequential = module.SequentialBlocks
            .Select(block => block with { Body = SubstituteMany(block.Body, replacements) })
            .ToList();

        List<CombinationalBlockAst> combinational = module.CombinationalBlocks
            .Select(block => block with { Body = SubstituteMany(block.Body, replacements) })
            .ToList();

        List<SignalDecl> locals = module.LocalSignals
            .Where(s => !foldable.Contains(s.Name))
            .ToList();

        return module with
        {
            LocalSignals = locals,
            ContAssigns = contAssigns,
            SequentialBlocks = sequential,
            CombinationalBlocks = combinational
        };
    }

    private static ExpressionAst ResolveReplacement(string tmpName, Dictionary<string, TempDef> tmps, HashSet<string> foldable)
    {
        ExpressionAst replacement = tmps[tmpName].Expression;
        for (int i = 0; i < MaxIterations; i++)
        {
            ExpressionAst before = replacement;
            foreach (string nested in foldable.Where(n => !string.Equals(n, tmpName, StringComparison.Ordinal)))
                replacement = Substitute(replacement, nested, tmps[nested].Expression);
            if (Equals(before, replacement)) break;
        }
        return replacement;
    }

    private static ExpressionAst SubstituteMany(ExpressionAst expr, Dictionary<string, ExpressionAst> replacements)
    {
        ExpressionAst current = expr;
        foreach ((string tmpName, ExpressionAst replacement) in replacements)
            current = Substitute(current, tmpName, replacement);
        return current;
    }

    private static StatementAst SubstituteMany(StatementAst stmt, Dictionary<string, ExpressionAst> replacements)
    {
        StatementAst current = stmt;
        foreach ((string tmpName, ExpressionAst replacement) in replacements)
            current = SubstituteInStatement(current, tmpName, replacement);
        return current;
    }

    private static ExpressionAst Substitute(ExpressionAst expr, string tmpName, ExpressionAst replacement)
    {
        return expr switch
        {
            SignalRef s when string.Equals(s.Name, tmpName, StringComparison.Ordinal) => replacement,
            SignalRef or ConstExpr => expr,
            BitSelectExpr bs => bs with { Base = Substitute(bs.Base, tmpName, replacement) },
            ArraySelectExpr arr => arr with
            {
                Base = Substitute(arr.Base, tmpName, replacement),
                Index = Substitute(arr.Index, tmpName, replacement)
            },
            ConcatExpr concat => concat with { Parts = concat.Parts.Select(p => Substitute(p, tmpName, replacement)).ToList() },
            ReplicateExpr rep => rep with { Pattern = Substitute(rep.Pattern, tmpName, replacement) },
            ExtendExpr ext => ext with { Inner = Substitute(ext.Inner, tmpName, replacement) },
            BinaryExpr bin => bin with
            {
                Left = Substitute(bin.Left, tmpName, replacement),
                Right = Substitute(bin.Right, tmpName, replacement)
            },
            UnaryExpr un => un with { Operand = Substitute(un.Operand, tmpName, replacement) },
            CondExpr cond => cond with
            {
                Condition = Substitute(cond.Condition, tmpName, replacement),
                IfTrue = Substitute(cond.IfTrue, tmpName, replacement),
                IfFalse = Substitute(cond.IfFalse, tmpName, replacement)
            },
            FunctionCallExpr fn => fn with { Args = fn.Args.Select(a => Substitute(a, tmpName, replacement)).ToList() },
            _ => expr
        };
    }

    private static StatementAst SubstituteInStatement(StatementAst stmt, string tmpName, ExpressionAst replacement)
    {
        return stmt switch
        {
            BeginAst begin => begin with
            {
                Statements = begin.Statements.Select(s => SubstituteInStatement(s, tmpName, replacement)).ToList()
            },
            IfAst ifAst => ifAst with
            {
                Condition = Substitute(ifAst.Condition, tmpName, replacement),
                Then = SubstituteInStatement(ifAst.Then, tmpName, replacement),
                Else = ifAst.Else is null ? null : SubstituteInStatement(ifAst.Else, tmpName, replacement)
            },
            CaseAst caseAst => caseAst with
            {
                Subject = Substitute(caseAst.Subject, tmpName, replacement),
                Arms = caseAst.Arms
                    .Select(a => a with
                    {
                        Label = Substitute(a.Label, tmpName, replacement),
                        Body = SubstituteInStatement(a.Body, tmpName, replacement)
                    })
                    .ToList(),
                Default = caseAst.Default is null ? null : SubstituteInStatement(caseAst.Default, tmpName, replacement)
            },
            AssignAst assign => assign with
            {
                Target = SubstituteInLValue(assign.Target, tmpName, replacement),
                Source = Substitute(assign.Source, tmpName, replacement)
            },
            _ => stmt
        };
    }

    private static LValueAst SubstituteInLValue(LValueAst lval, string tmpName, ExpressionAst replacement)
    {
        return lval switch
        {
            ArraySelectLValue arr => arr with { Index = Substitute(arr.Index, tmpName, replacement) },
            ConcatLValue concat => concat with { Parts = concat.Parts.Select(p => SubstituteInLValue(p, tmpName, replacement)).ToList() },
            _ => lval
        };
    }

    private static void CountRefs(StatementAst stmt, HashSet<string> tmpSet, Dictionary<string, int> counts)
    {
        switch (stmt)
        {
            case BeginAst begin:
                foreach (StatementAst child in begin.Statements) CountRefs(child, tmpSet, counts);
                break;
            case IfAst ifAst:
                CountRefs(ifAst.Condition, tmpSet, counts);
                CountRefs(ifAst.Then, tmpSet, counts);
                if (ifAst.Else is not null) CountRefs(ifAst.Else, tmpSet, counts);
                break;
            case CaseAst caseAst:
                CountRefs(caseAst.Subject, tmpSet, counts);
                foreach (CaseArm arm in caseAst.Arms)
                {
                    CountRefs(arm.Label, tmpSet, counts);
                    CountRefs(arm.Body, tmpSet, counts);
                }
                if (caseAst.Default is not null) CountRefs(caseAst.Default, tmpSet, counts);
                break;
            case AssignAst assign:
                CountRefs(assign.Target, tmpSet, counts);
                CountRefs(assign.Source, tmpSet, counts);
                break;
        }
    }

    private static void CountRefs(LValueAst lval, HashSet<string> tmpSet, Dictionary<string, int> counts)
    {
        switch (lval)
        {
            case ArraySelectLValue arr:
                CountRefs(arr.Index, tmpSet, counts);
                break;
            case ConcatLValue concat:
                foreach (LValueAst part in concat.Parts) CountRefs(part, tmpSet, counts);
                break;
        }
    }

    private static void CountRefs(ExpressionAst expr, HashSet<string> tmpSet, Dictionary<string, int> counts)
    {
        foreach (string name in CollectSignalRefs(expr))
        {
            if (tmpSet.Contains(name))
                counts[name] = counts.GetValueOrDefault(name) + 1;
        }
    }

    private static IEnumerable<string> CollectSignalRefs(ExpressionAst expr)
    {
        switch (expr)
        {
            case SignalRef s:
                yield return s.Name;
                break;
            case BitSelectExpr bs:
                foreach (string name in CollectSignalRefs(bs.Base)) yield return name;
                break;
            case ArraySelectExpr arr:
                foreach (string name in CollectSignalRefs(arr.Base)) yield return name;
                foreach (string name in CollectSignalRefs(arr.Index)) yield return name;
                break;
            case ConcatExpr concat:
                foreach (ExpressionAst part in concat.Parts)
                foreach (string name in CollectSignalRefs(part))
                    yield return name;
                break;
            case ReplicateExpr rep:
                foreach (string name in CollectSignalRefs(rep.Pattern)) yield return name;
                break;
            case ExtendExpr ext:
                foreach (string name in CollectSignalRefs(ext.Inner)) yield return name;
                break;
            case BinaryExpr bin:
                foreach (string name in CollectSignalRefs(bin.Left)) yield return name;
                foreach (string name in CollectSignalRefs(bin.Right)) yield return name;
                break;
            case UnaryExpr un:
                foreach (string name in CollectSignalRefs(un.Operand)) yield return name;
                break;
            case CondExpr cond:
                foreach (string name in CollectSignalRefs(cond.Condition)) yield return name;
                foreach (string name in CollectSignalRefs(cond.IfTrue)) yield return name;
                foreach (string name in CollectSignalRefs(cond.IfFalse)) yield return name;
                break;
            case FunctionCallExpr fn:
                foreach (ExpressionAst arg in fn.Args)
                foreach (string name in CollectSignalRefs(arg))
                    yield return name;
                break;
        }
    }

    private static bool DependsOnTempCycle(string tmpName, Dictionary<string, TempDef> tmps)
    {
        HashSet<string> visiting = new(StringComparer.Ordinal);
        HashSet<string> visited = new(StringComparer.Ordinal);
        return Visit(tmpName);

        bool Visit(string name)
        {
            if (!tmps.TryGetValue(name, out TempDef? tmp)) return false;
            if (!visiting.Add(name)) return true;
            foreach (string dep in CollectSignalRefs(tmp.Expression).Where(tmps.ContainsKey))
            {
                if (string.Equals(dep, name, StringComparison.Ordinal) || (!visited.Contains(dep) && Visit(dep)))
                    return true;
            }
            visiting.Remove(name);
            visited.Add(name);
            return false;
        }
    }

    private static bool WidthMatches(string tmpName, TempDef tmp, ModuleAst module)
    {
        Dictionary<string, int> widths = BuildWidthMap(module);
        if (!widths.TryGetValue(tmpName, out int tmpWidth))
            return true;

        int? sourceWidth = InferWidth(tmp.Expression, widths);
        if (sourceWidth is null || sourceWidth == tmpWidth)
            return true;

        Trace.TraceWarning(
            $"Skipping Verilator tmp fold for '{tmpName}' because source width {sourceWidth.Value} does not match tmp width {tmpWidth}.");
        return false;
    }

    private static bool IsCheapAlias(ExpressionAst expr) => expr switch
    {
        SignalRef => true,
        BitSelectExpr bs => IsCheapAlias(bs.Base),
        ExtendExpr ext => IsCheapAlias(ext.Inner),
        _ => false
    };

    private static Dictionary<string, int> BuildWidthMap(ModuleAst module)
    {
        Dictionary<string, int> widths = new(StringComparer.Ordinal);
        foreach (PortDecl port in module.Ports)
            widths[port.Name] = port.Width;
        foreach (SignalDecl signal in module.LocalSignals)
            widths[signal.Name] = signal.Width;
        return widths;
    }

    private static int? InferWidth(ExpressionAst expr, IReadOnlyDictionary<string, int> widths)
    {
        return expr switch
        {
            SignalRef s => widths.TryGetValue(s.Name, out int w) ? w : null,
            ConstExpr c => c.Width,
            BitSelectExpr bs => bs.Range.Width,
            ArraySelectExpr arr => InferWidth(arr.Base, widths),
            ConcatExpr concat => SumWidths(concat.Parts, widths),
            ReplicateExpr rep => InferWidth(rep.Pattern, widths) is { } w ? rep.Count * w : null,
            ExtendExpr ext => ext.TargetWidth > 0 ? ext.TargetWidth : InferWidth(ext.Inner, widths),
            BinaryExpr bin => InferBinaryWidth(bin, widths),
            UnaryExpr un => InferWidth(un.Operand, widths),
            CondExpr cond => MaxKnownWidth([cond.IfTrue, cond.IfFalse], widths),
            FunctionCallExpr => null,
            _ => null
        };
    }

    private static int? InferBinaryWidth(BinaryExpr bin, IReadOnlyDictionary<string, int> widths)
    {
        return bin.Op is BinaryOp.Equal or BinaryOp.NotEqual or BinaryOp.LessThan or BinaryOp.GreaterThan
            or BinaryOp.LessOrEqual or BinaryOp.GreaterOrEqual or BinaryOp.LogicAnd or BinaryOp.LogicOr
            ? 1
            : MaxKnownWidth([bin.Left, bin.Right], widths);
    }

    private static int? SumWidths(IReadOnlyList<ExpressionAst> expressions, IReadOnlyDictionary<string, int> widths)
    {
        int sum = 0;
        foreach (ExpressionAst expr in expressions)
        {
            int? width = InferWidth(expr, widths);
            if (width is null) return null;
            sum += width.Value;
        }
        return sum;
    }

    private static int? MaxKnownWidth(IReadOnlyList<ExpressionAst> expressions, IReadOnlyDictionary<string, int> widths)
    {
        int? max = null;
        foreach (ExpressionAst expr in expressions)
        {
            int? width = InferWidth(expr, widths);
            if (width is null) return null;
            max = Math.Max(max ?? width.Value, width.Value);
        }
        return max;
    }

    private static bool IsVerilatorInternalSignal(string signalName) =>
        !string.IsNullOrEmpty(signalName) && signalName.StartsWith("__V", StringComparison.Ordinal);

    private static string LValueName(LValueAst lval) => lval switch
    {
        VarRefLValue v => v.Name,
        BitSelectLValue b => b.SignalName,
        ArraySelectLValue a => a.SignalName,
        StructFieldLValue sf => sf.SignalName,
        ConcatLValue c => c.Parts.Count > 0 ? LValueName(c.Parts[0]) : string.Empty,
        _ => string.Empty
    };

    private sealed record TempDef(ExpressionAst Expression);
}
