using System.Globalization;
using Bistable.App.ViewModels;
using Bistable.Core.Design;

namespace Bistable.App.Services.Routing.Elk;

/// <summary>
/// Converts the in-memory scope view models (boundary ports, child instances,
/// local signals) into an <see cref="ElkGraph"/> suitable for elkjs layered routing.
/// </summary>
internal sealed class ElkGraphBuilder
{
    private const double ModuleHeaderHeight = 36;
    private const double ModuleFooterHeight = 16;
    private const double PortRowHeight = 22;
    private const double PortLabelCharWidth = 6.4;
    private const double ModuleMinWidth = 180;
    private const double ModuleSidePadding = 24;
    private const double BoundaryNodeWidth = 88;
    private const double OperatorNodeSize = 40;

    private const string ElkPortSideKey = "elk.port.side";
    private const string ElkPortIndexKey = "elk.port.index";
    private const string ElkPortConstraintsKey = "elk.portConstraints";
    private const string PortSideEast = "EAST";
    private const string PortSideWest = "WEST";
    private const string PortConstraintsFixedOrder = "FIXED_ORDER";

    public ElkBuildResult Build(ElkScopeData scope, bool compactLayout)
    {
        ElkGraph graph = new()
        {
            Id = "root",
            LayoutOptions = BuildRootOptions(compactLayout)
        };

        Dictionary<string, ElkPortRef> portRefs = new(StringComparer.OrdinalIgnoreCase);
        AddBoundaryInputNode(graph, scope, portRefs);
        AddBoundaryOutputNode(graph, scope, portRefs);

        foreach (HierarchyScopeInstanceViewModel child in scope.ChildScopes)
        {
            AddChildNode(graph, child, portRefs, scope.ExpandedPaths);
        }

        foreach (DesignContAssign assign in scope.ContAssigns.Where(a => a.SourceNames.Count >= 2))
        {
            if (IsConcatAssign(assign))
            {
                AddJoinerNode(graph, scope, assign, portRefs);
            }
            else
            {
                AddOperatorNode(graph, scope, assign, portRefs);
            }
        }

        foreach (IGrouping<string, DesignContAssign> group in scope.ContAssigns
                     .Where(static a => a.SourceRange.HasValue && a.SourceNames.Count == 1)
                     .GroupBy(static a => a.SourceNames[0], StringComparer.OrdinalIgnoreCase))
        {
            AddSplitterNode(graph, scope, group.Key, [.. group.OrderByDescending(static a => a.SourceRange!.Value.Hi)], portRefs);
        }

        AddEdges(graph, scope, portRefs);
        return new ElkBuildResult(graph, portRefs);
    }

    private static void AddOperatorNode(
        ElkGraph graph,
        ElkScopeData scope,
        DesignContAssign assign,
        Dictionary<string, ElkPortRef> portRefs)
    {
        string nodeId = ElkNodeIds.ForOperator(assign.TargetName);
        int targetWidth = ResolveSignalWidth(scope, assign.TargetName);
        string symbol = assign.OperatorSymbol ?? "?";

        ElkNode node = new()
        {
            Id = nodeId,
            Width = OperatorNodeSize,
            Height = OperatorNodeSize,
            LayoutOptions = FixedOrderPortConstraints(),
            Labels = [new ElkLabel { Text = symbol }],
            Ports = []
        };

        for (int i = 0; i < assign.SourceNames.Count; i++)
        {
            string portId = $"{nodeId}.in.{i}";
            int sourceWidth = ResolveSignalWidth(scope, assign.SourceNames[i]);
            node.Ports!.Add(new ElkPort { Id = portId, LayoutOptions = PortLayout(PortSideWest, i) });
            portRefs[ElkSignalKey.OpInput(assign.TargetName, i)] =
                new ElkPortRef(nodeId, portId, ElkPortRole.OperatorInput, sourceWidth);
        }

        string outPortId = $"{nodeId}.out";
        node.Ports!.Add(new ElkPort { Id = outPortId, LayoutOptions = PortLayout(PortSideEast, 0) });
        portRefs[ElkSignalKey.OpOutput(assign.TargetName)] =
            new ElkPortRef(nodeId, outPortId, ElkPortRole.OperatorOutput, targetWidth);

        graph.Children.Add(node);
    }

    private static bool IsConcatAssign(DesignContAssign assign) =>
        string.Equals(assign.OperatorSymbol, "{}", StringComparison.Ordinal);

    // Mirror of the splitter: multiple WEST inputs feed a single EAST output that
    // represents the concatenated bus. Visually rendered as a left-flat / right-apex wedge.
    private static void AddJoinerNode(
        ElkGraph graph,
        ElkScopeData scope,
        DesignContAssign assign,
        Dictionary<string, ElkPortRef> portRefs)
    {
        const double portRowHeight = 24;
        const double nodeWidth = 40;

        string nodeId = ElkNodeIds.ForJoiner(assign.TargetName);
        int targetWidth = ResolveSignalWidth(scope, assign.TargetName);
        double height = Math.Max(OperatorNodeSize, assign.SourceNames.Count * portRowHeight + 8);

        ElkNode node = new()
        {
            Id = nodeId,
            Width = nodeWidth,
            Height = height,
            LayoutOptions = FixedOrderPortConstraints(),
            Labels = [],
            Ports = []
        };

        // One WEST input per concat operand, in declaration order (MSB-first by Verilog
        // convention — Verilator preserves this in the XML source list).
        for (int i = 0; i < assign.SourceNames.Count; i++)
        {
            string portId = $"{nodeId}.in.{i}";
            int sourceWidth = ResolveSignalWidth(scope, assign.SourceNames[i]);
            node.Ports!.Add(new ElkPort { Id = portId, LayoutOptions = PortLayout(PortSideWest, i) });
            portRefs[ElkSignalKey.JoinerInput(assign.TargetName, i)] =
                new ElkPortRef(nodeId, portId, ElkPortRole.JoinerInput, sourceWidth);
        }

        string outPortId = $"{nodeId}.out";
        node.Ports!.Add(new ElkPort { Id = outPortId, LayoutOptions = PortLayout(PortSideEast, 0) });
        portRefs[ElkSignalKey.JoinerOutput(assign.TargetName)] =
            new ElkPortRef(nodeId, outPortId, ElkPortRole.JoinerOutput, targetWidth);

        graph.Children.Add(node);
    }

    private static void AddSplitterNode(
        ElkGraph graph,
        ElkScopeData scope,
        string sourceName,
        IReadOnlyList<DesignContAssign> slices,
        Dictionary<string, ElkPortRef> portRefs)
    {
        const double portRowHeight = 24;
        const double nodeWidth = 40;

        string nodeId = ElkNodeIds.ForSplitter(sourceName);
        int busWidth = ResolveSignalWidth(scope, sourceName);
        double height = Math.Max(OperatorNodeSize, slices.Count * portRowHeight + 8);

        ElkNode node = new()
        {
            Id = nodeId,
            Width = nodeWidth,
            Height = height,
            LayoutOptions = FixedOrderPortConstraints(),
            Labels = [],
            Ports = []
        };

        // Single WEST input port — the full bus
        string inPortId = $"{nodeId}.in";
        node.Ports!.Add(new ElkPort { Id = inPortId, LayoutOptions = PortLayout(PortSideWest, 0) });
        portRefs[ElkSignalKey.SplitterInput(sourceName)] =
            new ElkPortRef(nodeId, inPortId, ElkPortRole.SplitterInput, busWidth);

        // One EAST output port per slice — ordered MSB-first (slices already sorted by caller)
        for (int i = 0; i < slices.Count; i++)
        {
            DesignContAssign slice = slices[i];
            string targetName = slice.TargetName;
            DesignBitRange range = slice.SourceRange!.Value;
            string outPortId = $"{nodeId}.out.{i}";
            node.Ports!.Add(new ElkPort
            {
                Id = outPortId,
                LayoutOptions = PortLayout(PortSideEast, i),
                Labels = [new ElkLabel { Text = range.ToString() }]
            });
            portRefs[ElkSignalKey.SplitterOutput(sourceName, targetName)] =
                new ElkPortRef(nodeId, outPortId, ElkPortRole.SplitterOutput, range.Width);
        }

        graph.Children.Add(node);
    }

    private static int ResolveSignalWidth(ElkScopeData scope, string signalName)
    {
        HierarchyScopePortViewModel? boundary = scope.BoundaryPorts
            .FirstOrDefault(p => string.Equals(p.Name, signalName, StringComparison.OrdinalIgnoreCase));
        if (boundary is not null) return boundary.Width;

        HierarchyScopeLocalSignalViewModel? local = scope.LocalSignals
            .FirstOrDefault(s => string.Equals(s.Name, signalName, StringComparison.OrdinalIgnoreCase));
        if (local is not null) return local.Width;

        foreach (HierarchyScopeInstanceViewModel child in scope.ChildScopes)
        {
            HierarchyScopeInstancePortConnectionViewModel? pin = child.PortConnections
                .FirstOrDefault(p => string.Equals(p.SignalName, signalName, StringComparison.OrdinalIgnoreCase));
            if (pin is not null) return pin.Width;
        }

        return 1;
    }

    private static Dictionary<string, string> BuildRootOptions(bool compactLayout)
    {
        double nodeSpacing = compactLayout ? 36 : 48;
        double layerSpacing = compactLayout ? 72 : 96;
        double edgeNodeSpacing = compactLayout ? 18 : 24;
        double edgeEdgeSpacing = compactLayout ? 10 : 14;

        return new Dictionary<string, string>
        {
            ["elk.algorithm"] = "layered",
            ["elk.direction"] = "RIGHT",
            ["elk.edgeRouting"] = "ORTHOGONAL",
            ["elk.hierarchyHandling"] = "INCLUDE_CHILDREN",
            ["elk.spacing.nodeNode"] = nodeSpacing.ToString(CultureInfo.InvariantCulture),
            ["elk.layered.spacing.nodeNodeBetweenLayers"] = layerSpacing.ToString(CultureInfo.InvariantCulture),
            ["elk.layered.spacing.edgeNodeBetweenLayers"] = edgeNodeSpacing.ToString(CultureInfo.InvariantCulture),
            ["elk.layered.spacing.edgeEdgeBetweenLayers"] = edgeEdgeSpacing.ToString(CultureInfo.InvariantCulture),
            ["elk.layered.nodePlacement.strategy"] = "NETWORK_SIMPLEX",
            ["elk.layered.crossingMinimization.semiInteractive"] = "true",
            ["elk.layered.feedbackEdges"] = "true",
            // thoroughness=10 was choking on arnicomp-scale graphs (>8 s ELK runtime).
            // Dropping to 3 trades a small amount of layout quality (slightly more edge
            // crossings) for a 3-5× speedup. Re-evaluate after Phase 2 reduces graph
            // size at the source.
            ["elk.layered.thoroughness"] = "3"
        };
    }

    private static Dictionary<string, string> FixedOrderPortConstraints() =>
        new() { [ElkPortConstraintsKey] = PortConstraintsFixedOrder };

    private static Dictionary<string, string> PortLayout(string side, int index) =>
        new()
        {
            [ElkPortSideKey] = side,
            [ElkPortIndexKey] = index.ToString(CultureInfo.InvariantCulture)
        };

    private static void AddBoundaryInputNode(ElkGraph graph, ElkScopeData scope, Dictionary<string, ElkPortRef> portRefs)
    {
        HierarchyScopePortViewModel[] inputs = scope.BoundaryPorts.Where(p => p.IsInput).ToArray();
        if (inputs.Length == 0)
        {
            return;
        }

        ElkNode node = new()
        {
            Id = ElkNodeIds.BoundaryIn,
            Width = BoundaryNodeWidth,
            Height = Math.Max(60, inputs.Length * PortRowHeight + 20),
            LayoutOptions = FixedOrderPortConstraints(),
            Labels = [new ElkLabel { Text = "inputs" }],
            Ports = []
        };

        for (int i = 0; i < inputs.Length; i++)
        {
            HierarchyScopePortViewModel port = inputs[i];
            string id = $"{ElkNodeIds.BoundaryIn}.{port.Name}";
            node.Ports!.Add(new ElkPort
            {
                Id = id,
                LayoutOptions = PortLayout(PortSideEast, i),
                Labels = [new ElkLabel { Text = port.DisplayLabel }]
            });
            portRefs[port.Name] = new ElkPortRef(ElkNodeIds.BoundaryIn, id, ElkPortRole.BoundaryInput, port.Width);
        }

        graph.Children.Add(node);
    }

    private static void AddBoundaryOutputNode(ElkGraph graph, ElkScopeData scope, Dictionary<string, ElkPortRef> portRefs)
    {
        HierarchyScopePortViewModel[] outputs = scope.BoundaryPorts.Where(p => p.IsOutput).ToArray();
        if (outputs.Length == 0)
        {
            return;
        }

        ElkNode node = new()
        {
            Id = ElkNodeIds.BoundaryOut,
            Width = BoundaryNodeWidth,
            Height = Math.Max(60, outputs.Length * PortRowHeight + 20),
            LayoutOptions = FixedOrderPortConstraints(),
            Labels = [new ElkLabel { Text = "outputs" }],
            Ports = []
        };

        for (int i = 0; i < outputs.Length; i++)
        {
            HierarchyScopePortViewModel port = outputs[i];
            string id = $"{ElkNodeIds.BoundaryOut}.{port.Name}";
            node.Ports!.Add(new ElkPort
            {
                Id = id,
                LayoutOptions = PortLayout(PortSideWest, i),
                Labels = [new ElkLabel { Text = port.DisplayLabel }]
            });
            portRefs[ElkSignalKey.BoundaryOutput(port.Name)] = new ElkPortRef(ElkNodeIds.BoundaryOut, id, ElkPortRole.BoundaryOutput, port.Width);
        }

        graph.Children.Add(node);
    }

    private static void AddChildNode(
        ElkGraph graph,
        HierarchyScopeInstanceViewModel child,
        Dictionary<string, ElkPortRef> portRefs,
        IReadOnlySet<string>? expandedPaths)
    {
        ElkNode node = BuildChildNode(child, portRefs, expandedPaths);
        graph.Children.Add(node);
    }

    private static ElkNode BuildChildNode(
        HierarchyScopeInstanceViewModel child,
        Dictionary<string, ElkPortRef> portRefs,
        IReadOnlySet<string>? expandedPaths)
    {
        HierarchyScopeInstancePortConnectionViewModel[] inputs = child.PortConnections.Where(c => c.IsInput).ToArray();
        HierarchyScopeInstancePortConnectionViewModel[] outputs = child.PortConnections.Where(c => c.IsOutput).ToArray();
        int portRows = Math.Max(inputs.Length, outputs.Length);

        bool isExpanded = expandedPaths is not null
            && child.ChildInstances.Count > 0
            && expandedPaths.Contains(child.HierarchyPath);

        string nodeId = ElkNodeIds.ForChild(child.HierarchyPath);
        double width = ComputeChildNodeWidth(child, inputs, outputs);
        double height = Math.Max(80, ModuleHeaderHeight + portRows * PortRowHeight + ModuleFooterHeight);

        ElkNode node = new()
        {
            Id = nodeId,
            Width = width,
            Height = height,
            LayoutOptions = FixedOrderPortConstraints(),
            // labels[0] = rendered instance name; labels[1] = hierarchy path; labels[2] =
            // "expandable" sentinel when the instance has sub-instances. The renderer uses
            // these to attach a +/- expansion button at the correct depth.
            Labels = BuildChildLabels(child),
            Ports = []
        };

        for (int i = 0; i < inputs.Length; i++)
        {
            HierarchyScopeInstancePortConnectionViewModel pin = inputs[i];
            string id = $"{nodeId}.in.{pin.PortName}";
            node.Ports!.Add(new ElkPort
            {
                Id = id,
                LayoutOptions = PortLayout(PortSideWest, i),
                Labels = [new ElkLabel { Text = FormatPortLabel(pin.PortName, pin.Width) }]
            });
            portRefs[ElkSignalKey.ChildInput(child.HierarchyPath, pin.PortName)] = new ElkPortRef(nodeId, id, ElkPortRole.ChildInput, pin.Width);
        }

        for (int i = 0; i < outputs.Length; i++)
        {
            HierarchyScopeInstancePortConnectionViewModel pin = outputs[i];
            string id = $"{nodeId}.out.{pin.PortName}";
            node.Ports!.Add(new ElkPort
            {
                Id = id,
                LayoutOptions = PortLayout(PortSideEast, i),
                Labels = [new ElkLabel { Text = FormatPortLabel(pin.PortName, pin.Width) }]
            });
            portRefs[ElkSignalKey.ChildOutput(child.HierarchyPath, pin.PortName)] = new ElkPortRef(nodeId, id, ElkPortRole.ChildOutput, pin.Width);
        }

        if (isExpanded)
        {
            AttachCompoundChildren(node, child, portRefs, expandedPaths);
        }

        return node;
    }

    public static bool TryGetHierarchyPath(ElkNode node, out string hierarchyPath)
    {
        if (node.Labels is { Count: > 1 } labels && !string.IsNullOrWhiteSpace(labels[1].Text))
        {
            hierarchyPath = labels[1].Text;
            return true;
        }

        hierarchyPath = string.Empty;
        return false;
    }

    public static bool IsExpandableChild(ElkNode node) =>
        node.Labels is { Count: > 2 }
        && string.Equals(node.Labels[2].Text, ExpandableSentinel, StringComparison.Ordinal);

    private const string ExpandableSentinel = "expandable";

    private static List<ElkLabel> BuildChildLabels(HierarchyScopeInstanceViewModel child)
    {
        List<ElkLabel> labels =
        [
            new ElkLabel { Text = child.InstanceName },
            new ElkLabel { Text = child.HierarchyPath }
        ];
        if (child.ChildInstances.Count > 0)
        {
            labels.Add(new ElkLabel { Text = ExpandableSentinel });
        }

        return labels;
    }

    // When a child instance is in the expanded set, embed its sub-instances as ELK
    // compound children so the viewer can drill in Vivado-style. Internal edge routing
    // is left to a future pass — the goal here is to surface the nested structure.
    private static void AttachCompoundChildren(
        ElkNode parent,
        HierarchyScopeInstanceViewModel child,
        Dictionary<string, ElkPortRef> portRefs,
        IReadOnlySet<string>? expandedPaths)
    {
        parent.Children ??= [];
        foreach (HierarchyScopeInstanceViewModel grandchild in child.ChildInstances)
        {
            parent.Children.Add(BuildChildNode(grandchild, portRefs, expandedPaths));
        }

        parent.LayoutOptions ??= new Dictionary<string, string>();
        parent.LayoutOptions[ElkPortConstraintsKey] = PortConstraintsFixedOrder;
        parent.LayoutOptions["elk.algorithm"] = "layered";
        parent.LayoutOptions["elk.direction"] = "RIGHT";
        parent.LayoutOptions["elk.padding"] = "[top=48,left=24,right=24,bottom=20]";
        // Compound nodes need a generous min size so their children layout cleanly.
        parent.Width = Math.Max(parent.Width, 320);
        parent.Height = Math.Max(parent.Height, 200);
    }

    private static void AddEdges(ElkGraph graph, ElkScopeData scope, Dictionary<string, ElkPortRef> portRefs)
    {
        Dictionary<string, List<ElkPortRef>> producers = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<ElkPortRef>> consumers = new(StringComparer.OrdinalIgnoreCase);

        CollectBoundaryEndpoints(scope, portRefs, producers, consumers);
        CollectChildEndpoints(scope, portRefs, producers, consumers);
        CollectOperatorEndpoints(scope, portRefs, producers, consumers);
        CollectSplitterEndpoints(scope, portRefs, producers, consumers);
        CollectExpandedCompoundEndpoints(scope, portRefs, producers, consumers);
        ExpandConsumersThroughContAssigns(scope.ContAssigns, producers, consumers);
        EmitEdges(graph, producers, consumers);
    }

    // For each expanded compound child, treat its inside as a separate signal namespace
    // so edges between its grandchildren do not collide with the outer scope. The compound's
    // own ports double as endpoints in this inner namespace (an input port produces the
    // inner-side wire, an output port consumes it), letting ELK route edges that cross
    // the compound's boundary thanks to elk.hierarchyHandling=INCLUDE_CHILDREN.
    private static void CollectExpandedCompoundEndpoints(
        ElkScopeData scope,
        IReadOnlyDictionary<string, ElkPortRef> portRefs,
        Dictionary<string, List<ElkPortRef>> producers,
        Dictionary<string, List<ElkPortRef>> consumers)
    {
        if (scope.ExpandedPaths is null || scope.ExpandedPaths.Count == 0) return;
        foreach (HierarchyScopeInstanceViewModel child in scope.ChildScopes)
        {
            if (child.ChildInstances.Count == 0) continue;
            if (!scope.ExpandedPaths.Contains(child.HierarchyPath)) continue;
            CollectInsideCompound(child, portRefs, producers, consumers, scope.ExpandedPaths);
        }
    }

    private static void CollectInsideCompound(
        HierarchyScopeInstanceViewModel compound,
        IReadOnlyDictionary<string, ElkPortRef> portRefs,
        Dictionary<string, List<ElkPortRef>> producers,
        Dictionary<string, List<ElkPortRef>> consumers,
        IReadOnlySet<string> expandedPaths)
    {
        CollectCompoundBoundaryEndpoints(compound, portRefs, producers, consumers);
        foreach (HierarchyScopeInstanceViewModel grandchild in compound.ChildInstances)
        {
            CollectGrandchildEndpoints(compound.HierarchyPath, grandchild, portRefs, producers, consumers);
            if (expandedPaths.Contains(grandchild.HierarchyPath) && grandchild.ChildInstances.Count > 0)
            {
                CollectInsideCompound(grandchild, portRefs, producers, consumers, expandedPaths);
            }
        }
    }

    // The compound's own ports map into its *inner* namespace: the wire name on the
    // inside of a module is the port name itself (assign-by-name semantics). A compound
    // input feeds inwards (producer of inner wire); a compound output receives from
    // inwards (consumer of inner wire).
    private static void CollectCompoundBoundaryEndpoints(
        HierarchyScopeInstanceViewModel compound,
        IReadOnlyDictionary<string, ElkPortRef> portRefs,
        Dictionary<string, List<ElkPortRef>> producers,
        Dictionary<string, List<ElkPortRef>> consumers)
    {
        foreach (HierarchyScopeInstancePortConnectionViewModel pin in compound.PortConnections)
        {
            string innerKey = ScopedSignalKey(compound.HierarchyPath, pin.PortName);
            string portRefKey = pin.IsInput
                ? ElkSignalKey.ChildInput(compound.HierarchyPath, pin.PortName)
                : ElkSignalKey.ChildOutput(compound.HierarchyPath, pin.PortName);
            if (!portRefs.TryGetValue(portRefKey, out ElkPortRef? portRef)) continue;
            AddTo(pin.IsInput ? producers : consumers, innerKey, portRef);
        }
    }

    private static void CollectGrandchildEndpoints(
        string compoundPath,
        HierarchyScopeInstanceViewModel grandchild,
        IReadOnlyDictionary<string, ElkPortRef> portRefs,
        Dictionary<string, List<ElkPortRef>> producers,
        Dictionary<string, List<ElkPortRef>> consumers)
    {
        foreach (HierarchyScopeInstancePortConnectionViewModel pin in grandchild.PortConnections)
        {
            string innerKey = ScopedSignalKey(compoundPath, pin.SignalName);
            string portRefKey = pin.IsInput
                ? ElkSignalKey.ChildInput(grandchild.HierarchyPath, pin.PortName)
                : ElkSignalKey.ChildOutput(grandchild.HierarchyPath, pin.PortName);
            if (!portRefs.TryGetValue(portRefKey, out ElkPortRef? portRef)) continue;
            AddTo(pin.IsInput ? consumers : producers, innerKey, portRef);
        }
    }

    // Inner-scope signal namespace prefix — keeps grandchild wires distinct from outer
    // scope wires that share the same name (e.g. both have a "clk").
    private static string ScopedSignalKey(string scopePath, string signalName) =>
        $"@inner::{scopePath}::{signalName}";

    private static void CollectOperatorEndpoints(
        ElkScopeData scope,
        Dictionary<string, ElkPortRef> portRefs,
        Dictionary<string, List<ElkPortRef>> producers,
        Dictionary<string, List<ElkPortRef>> consumers)
    {
        foreach (DesignContAssign assign in scope.ContAssigns.Where(a => a.SourceNames.Count >= 2))
        {
            if (IsConcatAssign(assign))
            {
                CollectJoinerEndpoints(assign, portRefs, producers, consumers);
                continue;
            }

            // Operator output is a producer of the target signal.
            if (portRefs.TryGetValue(ElkSignalKey.OpOutput(assign.TargetName), out ElkPortRef? outRef))
            {
                AddTo(producers, assign.TargetName, outRef);
            }

            // Each operator input consumes its respective source signal.
            for (int i = 0; i < assign.SourceNames.Count; i++)
            {
                if (portRefs.TryGetValue(ElkSignalKey.OpInput(assign.TargetName, i), out ElkPortRef? inRef))
                {
                    AddTo(consumers, assign.SourceNames[i], inRef);
                }
            }
        }
    }

    private static void CollectJoinerEndpoints(
        DesignContAssign assign,
        IReadOnlyDictionary<string, ElkPortRef> portRefs,
        Dictionary<string, List<ElkPortRef>> producers,
        Dictionary<string, List<ElkPortRef>> consumers)
    {
        if (portRefs.TryGetValue(ElkSignalKey.JoinerOutput(assign.TargetName), out ElkPortRef? outRef))
        {
            AddTo(producers, assign.TargetName, outRef);
        }

        for (int i = 0; i < assign.SourceNames.Count; i++)
        {
            if (portRefs.TryGetValue(ElkSignalKey.JoinerInput(assign.TargetName, i), out ElkPortRef? inRef))
            {
                AddTo(consumers, assign.SourceNames[i], inRef);
            }
        }
    }

    private static void CollectSplitterEndpoints(
        ElkScopeData scope,
        Dictionary<string, ElkPortRef> portRefs,
        Dictionary<string, List<ElkPortRef>> producers,
        Dictionary<string, List<ElkPortRef>> consumers)
    {
        foreach (IGrouping<string, DesignContAssign> group in scope.ContAssigns
                     .Where(static a => a.SourceRange.HasValue && a.SourceNames.Count == 1)
                     .GroupBy(static a => a.SourceNames[0], StringComparer.OrdinalIgnoreCase))
        {
            string sourceName = group.Key;

            if (portRefs.TryGetValue(ElkSignalKey.SplitterInput(sourceName), out ElkPortRef? inRef))
            {
                AddTo(consumers, sourceName, inRef);
            }

            foreach (string targetName in group
                         .Select(static s => s.TargetName)
                         .Where(t => portRefs.ContainsKey(ElkSignalKey.SplitterOutput(sourceName, t))))
            {
                AddTo(producers, targetName, portRefs[ElkSignalKey.SplitterOutput(sourceName, targetName)]);
            }
        }
    }

    private static void CollectBoundaryEndpoints(
        ElkScopeData scope,
        Dictionary<string, ElkPortRef> portRefs,
        Dictionary<string, List<ElkPortRef>> producers,
        Dictionary<string, List<ElkPortRef>> consumers)
    {
        foreach (HierarchyScopePortViewModel port in scope.BoundaryPorts)
        {
            if (port.IsInput && portRefs.TryGetValue(port.Name, out ElkPortRef? inRef))
            {
                AddTo(producers, port.Name, inRef);
            }

            if (port.IsOutput && portRefs.TryGetValue(ElkSignalKey.BoundaryOutput(port.Name), out ElkPortRef? outRef))
            {
                AddTo(consumers, port.Name, outRef);
            }
        }
    }

    private static void CollectChildEndpoints(
        ElkScopeData scope,
        Dictionary<string, ElkPortRef> portRefs,
        Dictionary<string, List<ElkPortRef>> producers,
        Dictionary<string, List<ElkPortRef>> consumers)
    {
        foreach (HierarchyScopeInstanceViewModel child in scope.ChildScopes)
        {
            foreach (HierarchyScopeInstancePortConnectionViewModel pin in child.PortConnections)
            {
                string key = pin.IsInput
                    ? ElkSignalKey.ChildInput(child.HierarchyPath, pin.PortName)
                    : ElkSignalKey.ChildOutput(child.HierarchyPath, pin.PortName);
                if (!portRefs.TryGetValue(key, out ElkPortRef? portRef))
                {
                    continue;
                }

                Dictionary<string, List<ElkPortRef>> target = pin.IsInput ? consumers : producers;
                AddTo(target, pin.SignalName, portRef);
            }
        }
    }

    private static void EmitEdges(
        ElkGraph graph,
        Dictionary<string, List<ElkPortRef>> producers,
        Dictionary<string, List<ElkPortRef>> consumers)
    {
        int edgeCounter = 0;
        foreach ((string signal, List<ElkPortRef> sourceList) in producers)
        {
            if (!consumers.TryGetValue(signal, out List<ElkPortRef>? consumerList))
            {
                continue;
            }

            foreach (ElkPortRef source in sourceList)
            {
                foreach (ElkPortRef target in consumerList)
                {
                    if (string.Equals(source.PortId, target.PortId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    int width = ResolveEdgeWidth(source, target);
                    // labels[0] = signal name (rendered + selection key)
                    // labels[1] = bit width metadata (Logisim colour selection)
                    graph.Edges.Add(new ElkEdge
                    {
                        Id = $"e{edgeCounter++}",
                        Sources = [source.PortId],
                        Targets = [target.PortId],
                        Labels = [new ElkLabel { Text = signal }, new ElkLabel { Text = width.ToString(CultureInfo.InvariantCulture) }]
                    });
                }
            }
        }
    }

    private static int ResolveEdgeWidth(ElkPortRef source, ElkPortRef target)
    {
        if (source.Width == target.Width)
        {
            return Math.Max(1, source.Width);
        }

        return Math.Max(1, Math.Min(source.Width, target.Width));
    }

    // Expands alias chains for single-source contassigns so that e.g.
    // assign opcode = instruction[15:12] draws a cable directly from the instruction port.
    // Multi-source assigns are handled by operator nodes added during the graph build phase.
    private static void ExpandConsumersThroughContAssigns(
        IReadOnlyList<DesignContAssign> contAssigns,
        IReadOnlyDictionary<string, List<ElkPortRef>> producers,
        Dictionary<string, List<ElkPortRef>> consumers)
    {
        if (contAssigns.Count == 0 || consumers.Count == 0)
        {
            return;
        }

        Dictionary<string, List<string>> sourcesByTarget = BuildSourcesByTarget(contAssigns);
        if (sourcesByTarget.Count == 0)
        {
            return;
        }

        foreach ((string signal, List<ElkPortRef> signalConsumers) in consumers.ToArray())
        {
            if (producers.ContainsKey(signal)
                || !sourcesByTarget.TryGetValue(signal, out List<string>? sources)
                || sources.Count != 1)
            {
                continue;
            }

            ExpandAlias(signal, signalConsumers, sourcesByTarget, consumers);
        }
    }

    private static void ExpandAlias(
        string signal,
        List<ElkPortRef> signalConsumers,
        IReadOnlyDictionary<string, List<string>> sourcesByTarget,
        Dictionary<string, List<ElkPortRef>> consumers)
    {
        foreach (string source in ResolveAssignSources(signal, sourcesByTarget)
            .Where(s => !string.Equals(s, signal, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (ElkPortRef consumer in signalConsumers)
            {
                AddTo(consumers, source, consumer);
            }
        }
    }

    private static Dictionary<string, List<string>> BuildSourcesByTarget(IReadOnlyList<DesignContAssign> contAssigns)
    {
        Dictionary<string, List<string>> sourcesByTarget = new(StringComparer.OrdinalIgnoreCase);
        // Sel assigns (SourceRange != null) are handled by splitter nodes; skip them here.
        foreach (DesignContAssign assign in contAssigns.Where(static a => a.SourceRange is null))
        {
            foreach (string source in assign.SourceNames
                .Where(static s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(s => !string.Equals(s, assign.TargetName, StringComparison.OrdinalIgnoreCase)))
            {
                AddTo(sourcesByTarget, assign.TargetName, source);
            }
        }

        return sourcesByTarget;
    }

    private static IEnumerable<string> ResolveAssignSources(
        string signal,
        IReadOnlyDictionary<string, List<string>> sourcesByTarget)
    {
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        Stack<string> pending = new();
        pending.Push(signal);

        while (pending.Count > 0)
        {
            string current = pending.Pop();
            if (!visited.Add(current) || !sourcesByTarget.TryGetValue(current, out List<string>? sources))
            {
                continue;
            }

            foreach (string source in sources)
            {
                yield return source;
                pending.Push(source);
            }
        }
    }

    private static double ComputeChildNodeWidth(
        HierarchyScopeInstanceViewModel child,
        IReadOnlyList<HierarchyScopeInstancePortConnectionViewModel> inputs,
        IReadOnlyList<HierarchyScopeInstancePortConnectionViewModel> outputs)
    {
        double widestInput = inputs.Count == 0 ? 0 : inputs.Max(p => MeasureLabelWidth(FormatPortLabel(p.PortName, p.Width)));
        double widestOutput = outputs.Count == 0 ? 0 : outputs.Max(p => MeasureLabelWidth(FormatPortLabel(p.PortName, p.Width)));
        double headerWidth = MeasureLabelWidth(child.InstanceName);
        double moduleWidth = ModuleSidePadding * 2 + widestInput + widestOutput + 40;
        return Math.Max(ModuleMinWidth, Math.Max(moduleWidth, headerWidth + 40));
    }

    private static double MeasureLabelWidth(string text) => text.Length * PortLabelCharWidth;

    private static string FormatPortLabel(string portName, int width) =>
        width == 1 ? portName : $"{portName}[{width}b]";

    private static void AddTo<TValue>(Dictionary<string, List<TValue>> map, string key, TValue value)
    {
        if (!map.TryGetValue(key, out List<TValue>? list))
        {
            list = [];
            map[key] = list;
        }

        list.Add(value);
    }
}

public sealed record ElkScopeData(
    IReadOnlyList<HierarchyScopePortViewModel> BoundaryPorts,
    IReadOnlyList<HierarchyScopeInstanceViewModel> ChildScopes,
    IReadOnlyList<HierarchyScopeLocalSignalViewModel> LocalSignals,
    IReadOnlyList<DesignContAssign> ContAssigns,
    IReadOnlySet<string>? ExpandedPaths = null);

public sealed record ElkBuildResult(ElkGraph Graph, IReadOnlyDictionary<string, ElkPortRef> PortRefs);

public sealed record ElkPortRef(string NodeId, string PortId, ElkPortRole Role, int Width);

public enum ElkPortRole
{
    BoundaryInput,
    BoundaryOutput,
    ChildInput,
    ChildOutput,
    OperatorInput,
    OperatorOutput,
    SplitterInput,
    SplitterOutput,
    JoinerInput,
    JoinerOutput
}

internal static class ElkNodeIds
{
    public const string BoundaryIn = "boundary_in";
    public const string BoundaryOut = "boundary_out";

    public static string ForChild(string hierarchyPath) => "child_" + SanitizeId(hierarchyPath);

    public static string ForOperator(string targetName) => "op_" + SanitizeId(targetName);
    public static string ForSplitter(string sourceName) => "split_" + SanitizeId(sourceName);
    public static string ForJoiner(string targetName) => "join_" + SanitizeId(targetName);

    public static bool IsOperator(string? nodeId) =>
        nodeId is not null && nodeId.StartsWith("op_", StringComparison.Ordinal);

    public static bool IsSplitter(string? nodeId) =>
        nodeId is not null && nodeId.StartsWith("split_", StringComparison.Ordinal);

    public static bool IsJoiner(string? nodeId) =>
        nodeId is not null && nodeId.StartsWith("join_", StringComparison.Ordinal);

    private static string SanitizeId(string raw) =>
        raw.Replace('.', '_').Replace('/', '_').Replace(':', '_').Replace('[', '_').Replace(']', '_');
}

internal static class ElkSignalKey
{
    public static string BoundaryOutput(string portName) => $"::boundary_out::{portName}";
    public static string ChildInput(string hierarchyPath, string portName) => $"{hierarchyPath}::in::{portName}";
    public static string ChildOutput(string hierarchyPath, string portName) => $"{hierarchyPath}::out::{portName}";
    public static string OpInput(string targetName, int index) => $"::op_in::{targetName}::{index}";
    public static string OpOutput(string targetName) => $"::op_out::{targetName}";
    public static string SplitterInput(string sourceName) => $"::split_in::{sourceName}";
    public static string SplitterOutput(string sourceName, string targetName) => $"::split_out::{sourceName}::{targetName}";
    public static string JoinerInput(string targetName, int index) => $"::join_in::{targetName}::{index}";
    public static string JoinerOutput(string targetName) => $"::join_out::{targetName}";
}
