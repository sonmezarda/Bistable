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
            AddChildNode(graph, child, portRefs);
        }

        AddEdges(graph, scope, portRefs);
        return new ElkBuildResult(graph, portRefs);
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
            ["elk.layered.thoroughness"] = "10"
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
        Dictionary<string, ElkPortRef> portRefs)
    {
        HierarchyScopeInstancePortConnectionViewModel[] inputs = child.PortConnections.Where(c => c.IsInput).ToArray();
        HierarchyScopeInstancePortConnectionViewModel[] outputs = child.PortConnections.Where(c => c.IsOutput).ToArray();
        int portRows = Math.Max(inputs.Length, outputs.Length);

        string nodeId = ElkNodeIds.ForChild(child.HierarchyPath);
        double width = ComputeChildNodeWidth(child, inputs, outputs);
        double height = Math.Max(80, ModuleHeaderHeight + portRows * PortRowHeight + ModuleFooterHeight);

        ElkNode node = new()
        {
            Id = nodeId,
            Width = width,
            Height = height,
            LayoutOptions = FixedOrderPortConstraints(),
            Labels = [new ElkLabel { Text = child.InstanceName }],
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

        graph.Children.Add(node);
    }

    private static void AddEdges(ElkGraph graph, ElkScopeData scope, Dictionary<string, ElkPortRef> portRefs)
    {
        Dictionary<string, List<ElkPortRef>> producers = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<ElkPortRef>> consumers = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> fanInPairs = new(StringComparer.OrdinalIgnoreCase);

        CollectBoundaryEndpoints(scope, portRefs, producers, consumers);
        CollectChildEndpoints(scope, portRefs, producers, consumers);
        ExpandConsumersThroughContAssigns(scope.ContAssigns, producers, consumers, fanInPairs);
        EmitEdges(graph, producers, consumers, fanInPairs);
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
        Dictionary<string, List<ElkPortRef>> consumers,
        HashSet<string> fanInPairs)
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
                    bool isFanIn = fanInPairs.Contains(FanInKey(signal, target.PortId));
                    // labels[0] = signal name (rendered + selection key)
                    // labels[1] = bit width metadata (Logisim colour selection)
                    // labels[2] = "fanin" when edge represents a combinational fan-in
                    //             contribution rather than a direct wire (renderer draws dashed)
                    List<ElkLabel> labels = isFanIn
                        ? [new ElkLabel { Text = signal }, new ElkLabel { Text = width.ToString(CultureInfo.InvariantCulture) }, new ElkLabel { Text = "fanin" }]
                        : [new ElkLabel { Text = signal }, new ElkLabel { Text = width.ToString(CultureInfo.InvariantCulture) }];
                    graph.Edges.Add(new ElkEdge
                    {
                        Id = $"e{edgeCounter++}",
                        Sources = [source.PortId],
                        Targets = [target.PortId],
                        Labels = labels
                    });
                }
            }
        }
    }

    private static string FanInKey(string signal, string targetPortId) => $"{signal}|{targetPortId}";

    private static int ResolveEdgeWidth(ElkPortRef source, ElkPortRef target)
    {
        if (source.Width == target.Width)
        {
            return Math.Max(1, source.Width);
        }

        return Math.Max(1, Math.Min(source.Width, target.Width));
    }

    private static void ExpandConsumersThroughContAssigns(
        IReadOnlyList<DesignContAssign> contAssigns,
        IReadOnlyDictionary<string, List<ElkPortRef>> producers,
        Dictionary<string, List<ElkPortRef>> consumers,
        HashSet<string> fanInPairs)
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
            if (producers.ContainsKey(signal) || !sourcesByTarget.TryGetValue(signal, out List<string>? sources))
            {
                continue;
            }

            if (sources.Count == 1)
            {
                ExpandAlias(signal, signalConsumers, sourcesByTarget, consumers);
            }
            else
            {
                ExpandFanIn(sources, signalConsumers, producers, consumers, fanInPairs);
            }
        }
    }

    // Single-source: transparent wire alias — follow the full chain so that e.g.
    // assign opcode = instruction[15:12] draws a cable from the instruction port.
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

    // Multi-source (combinational fan-in): only connect sources that already have producers.
    // Following alias chains here would create false long-range edges.
    // Fan-in edges are tagged in fanInPairs so the renderer can draw them dashed.
    private static void ExpandFanIn(
        List<string> sources,
        List<ElkPortRef> signalConsumers,
        IReadOnlyDictionary<string, List<ElkPortRef>> producers,
        Dictionary<string, List<ElkPortRef>> consumers,
        HashSet<string> fanInPairs)
    {
        foreach (string source in sources.Where(producers.ContainsKey))
        {
            foreach (ElkPortRef consumer in signalConsumers)
            {
                AddTo(consumers, source, consumer);
                fanInPairs.Add(FanInKey(source, consumer.PortId));
            }
        }
    }

    private static Dictionary<string, List<string>> BuildSourcesByTarget(IReadOnlyList<DesignContAssign> contAssigns)
    {
        Dictionary<string, List<string>> sourcesByTarget = new(StringComparer.OrdinalIgnoreCase);
        foreach (DesignContAssign assign in contAssigns)
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
    IReadOnlyList<DesignContAssign> ContAssigns);

public sealed record ElkBuildResult(ElkGraph Graph, IReadOnlyDictionary<string, ElkPortRef> PortRefs);

public sealed record ElkPortRef(string NodeId, string PortId, ElkPortRole Role, int Width);

public enum ElkPortRole
{
    BoundaryInput,
    BoundaryOutput,
    ChildInput,
    ChildOutput
}

internal static class ElkNodeIds
{
    public const string BoundaryIn = "boundary_in";
    public const string BoundaryOut = "boundary_out";

    public static string ForChild(string hierarchyPath) =>
        "child_" + SanitizeId(hierarchyPath);

    private static string SanitizeId(string raw) =>
        raw.Replace('.', '_').Replace('/', '_').Replace(':', '_').Replace('[', '_').Replace(']', '_');
}

internal static class ElkSignalKey
{
    public static string BoundaryOutput(string portName) => $"::boundary_out::{portName}";
    public static string ChildInput(string hierarchyPath, string portName) => $"{hierarchyPath}::in::{portName}";
    public static string ChildOutput(string hierarchyPath, string portName) => $"{hierarchyPath}::out::{portName}";
}
