using Bistable.Core.Design;
using Bistable.Core.Design.Ast;

namespace Bistable.Verilator;

/// <summary>
/// Converts a <see cref="DesignAst"/> into the existing flat <see cref="ElaboratedDesign"/> model
/// so that <c>ElkGraphBuilder</c> and all Phase 0 tests keep working unchanged during Phase 1.
/// Sequential and combinational blocks have no flat representation and are silently discarded here;
/// Phase 2 will consume them directly from the AST.
/// </summary>
public static class LegacyDesignFlattener
{
    public static ElaboratedDesign Flatten(DesignAst ast, string fallbackTopName = "unknown")
    {
        Dictionary<string, ModuleMetadata> catalog = ast.Modules
            .Select(FlattenModuleMetadata)
            .ToDictionary(static m => m.Name, StringComparer.OrdinalIgnoreCase);

        Dictionary<string, DesignModuleDefinition> definitions = ast.Modules
            .Select(m => FlattenModuleDefinition(m, catalog))
            .ToDictionary(static d => d.Metadata.Name, StringComparer.OrdinalIgnoreCase);

        ModuleAst? topAst = ast.TopModule;
        ModuleMetadata topModule = topAst is not null
            ? catalog.TryGetValue(topAst.Name, out ModuleMetadata? m) ? m : FlattenModuleMetadata(topAst)
            : new ModuleMetadata(fallbackTopName, [], []);

        // Build hierarchy from the top module's instance tree
        DesignHierarchyNode hierarchyRoot = BuildHierarchy(ast, topModule.Name);

        return new ElaboratedDesign(topModule, hierarchyRoot, catalog, definitions);
    }

    // ── Module ────────────────────────────────────────────────────────────

    private static ModuleMetadata FlattenModuleMetadata(ModuleAst m)
    {
        List<SignalPort> ports = m.Ports
            .Select(static p => new SignalPort(p.Name, p.Direction, p.Width, p.IsSigned, p.PinIndex))
            .ToList();
        return new ModuleMetadata(m.Name, ports, m.Parameters);
    }

    private static DesignModuleDefinition FlattenModuleDefinition(
        ModuleAst m,
        Dictionary<string, ModuleMetadata> catalog)
    {
        ModuleMetadata metadata = catalog.TryGetValue(m.Name, out ModuleMetadata? meta)
            ? meta
            : FlattenModuleMetadata(m);

        List<DesignLocalSignal> locals = m.LocalSignals
            .Select(static s => new DesignLocalSignal(s.Name, s.Width, s.IsSigned))
            .ToList();

        List<DesignInstanceDefinition> instances = m.Instances
            .Select(FlattenInstance)
            .ToList();

        List<DesignContAssign> contAssigns = m.ContAssigns
            .Select(FlattenContAssign)
            .OfType<DesignContAssign>()
            .ToList();

        return new DesignModuleDefinition(metadata, locals, instances, contAssigns);
    }

    private static DesignInstanceDefinition FlattenInstance(InstanceDecl inst)
    {
        List<DesignInstancePortConnection> ports = inst.PortConnections
            .Select(static p => new DesignInstancePortConnection(p.PortName, p.SignalName, p.Direction, p.PortIndex))
            .OrderBy(static p => p.PortIndex)
            .ToList();
        return new DesignInstanceDefinition(inst.InstanceName, inst.ModuleName, ports);
    }

    // ── ContAssign flattening ─────────────────────────────────────────────

    private static DesignContAssign? FlattenContAssign(ContAssignAst ca)
    {
        string targetName = LValueToName(ca.Target);
        if (string.IsNullOrWhiteSpace(targetName))
            return null;

        (string? opSymbol, DesignBitRange? sourceRange) = InspectTopLevelExpr(ca.Source);

        List<string> sourceNames = CollectSignalRefs(ca.Source)
            .Where(n => !string.Equals(n, targetName, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (sourceNames.Count == 0 && sourceRange is null)
            return null;

        return new DesignContAssign(targetName, sourceNames, opSymbol, sourceRange);
    }

    private static (string? opSymbol, DesignBitRange? range) InspectTopLevelExpr(ExpressionAst expr)
        => expr switch
        {
            SignalRef                                       => (null, null),
            ConstExpr                                      => (null, null),
            BitSelectExpr { Base: SignalRef, Range: var r } => (null, new DesignBitRange(r.Hi, r.Lo)),
            BitSelectExpr                                   => (null, null),
            ConcatExpr                                      => ("{}", null),
            CondExpr                                        => ("?:", null),
            ExtendExpr                                      => (null, null),
            BinaryExpr { Op: var op }                       => (BinaryOpSymbol(op), null),
            UnaryExpr  { Op: var op }                       => (UnaryOpSymbol(op), null),
            _                                               => (null, null)
        };

    private static string? BinaryOpSymbol(BinaryOp op) => op switch
    {
        BinaryOp.Add                  => "+",
        BinaryOp.Sub                  => "-",
        BinaryOp.Mul                  => "*",
        BinaryOp.Div                  => "/",
        BinaryOp.Mod                  => "%",
        BinaryOp.And                  => "&",
        BinaryOp.Or                   => "|",
        BinaryOp.Xor                  => "^",
        BinaryOp.LogicAnd             => "&&",
        BinaryOp.LogicOr              => "||",
        BinaryOp.Equal                => "=",
        BinaryOp.NotEqual             => "≠",
        BinaryOp.LessThan             => "<",
        BinaryOp.GreaterThan          => ">",
        BinaryOp.LessOrEqual          => "≤",
        BinaryOp.GreaterOrEqual       => "≥",
        BinaryOp.ShiftLeft            => "<<",
        BinaryOp.ShiftRight           => ">>",
        BinaryOp.ShiftRightArithmetic => ">>>",
        _                             => null
    };

    private static string? UnaryOpSymbol(UnaryOp op) => op switch
    {
        UnaryOp.Not      => "~",
        UnaryOp.LogicNot => "!",
        UnaryOp.Negate   => "-",
        UnaryOp.ReduceAnd => "&",
        UnaryOp.ReduceOr  => "|",
        UnaryOp.ReduceXor => "^",
        _                 => null
    };

    // ── Signal ref collection (DFS) ───────────────────────────────────────

    private static IEnumerable<string> CollectSignalRefs(ExpressionAst expr)
    {
        return expr switch
        {
            SignalRef s => [s.Name],
            ConstExpr   => [],
            BitSelectExpr b  => CollectSignalRefs(b.Base),
            ArraySelectExpr a => CollectSignalRefs(a.Base).Concat(CollectSignalRefs(a.Index)),
            ConcatExpr c      => c.Parts.SelectMany(CollectSignalRefs),
            ReplicateExpr r   => CollectSignalRefs(r.Pattern),
            ExtendExpr e      => CollectSignalRefs(e.Inner),
            BinaryExpr b      => CollectSignalRefs(b.Left).Concat(CollectSignalRefs(b.Right)),
            UnaryExpr u       => CollectSignalRefs(u.Operand),
            CondExpr c        => CollectSignalRefs(c.Condition)
                                    .Concat(CollectSignalRefs(c.IfTrue))
                                    .Concat(CollectSignalRefs(c.IfFalse)),
            FunctionCallExpr f => f.Args.SelectMany(CollectSignalRefs),
            _                  => []
        };
    }

    // ── LValue helpers ────────────────────────────────────────────────────

    private static string LValueToName(LValueAst lval) => lval switch
    {
        VarRefLValue v       => v.Name,
        BitSelectLValue b    => b.SignalName,
        ArraySelectLValue a  => a.SignalName,
        StructFieldLValue sf => sf.SignalName,
        ConcatLValue c       => c.Parts.Count > 0 ? LValueToName(c.Parts[0]) : string.Empty,
        _                    => string.Empty
    };

    // ── Hierarchy reconstruction ──────────────────────────────────────────

    private static DesignHierarchyNode BuildHierarchy(DesignAst ast, string topModuleName)
    {
        // Build a module-name → instances map for recursive descent
        Dictionary<string, ModuleAst> moduleMap = ast.Modules
            .ToDictionary(static m => m.Name, StringComparer.OrdinalIgnoreCase);

        if (!moduleMap.TryGetValue(topModuleName, out ModuleAst? topModule))
            return new DesignHierarchyNode(topModuleName, topModuleName, topModuleName, []);

        return BuildNode(topModule, topModuleName, topModuleName, moduleMap, depth: 0);
    }

    private static DesignHierarchyNode BuildNode(
        ModuleAst module,
        string instanceName,
        string hierarchyPath,
        Dictionary<string, ModuleAst> moduleMap,
        int depth)
    {
        if (depth > 64)
            return new DesignHierarchyNode(instanceName, module.Name, hierarchyPath, []);

        DesignHierarchyNode[] children = module.Instances
            .Select(inst =>
            {
                string childPath = hierarchyPath == instanceName
                    ? $"{hierarchyPath}.{inst.InstanceName}"
                    : $"{hierarchyPath}.{inst.InstanceName}";
                if (moduleMap.TryGetValue(inst.ModuleName, out ModuleAst? childModule))
                    return BuildNode(childModule, inst.InstanceName, childPath, moduleMap, depth + 1);
                return new DesignHierarchyNode(inst.InstanceName, inst.ModuleName, childPath, []);
            })
            .ToArray();

        return new DesignHierarchyNode(instanceName, module.Name, hierarchyPath, children);
    }
}
