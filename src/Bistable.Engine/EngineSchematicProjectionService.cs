using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Design.Schematic;

namespace Bistable.Engine;

/// <summary>
/// Produces a transport-safe, layout-agnostic graph for IDE frontends. Exact
/// AST signal references remain explicit on every edge; presentation grouping
/// is not performed in this boundary.
/// </summary>
public sealed class EngineSchematicProjectionService
{
    public EngineSchematicGraph Project(ModuleAst module) => Project(SchematicDecoder.Decode(module));

    public EngineSchematicGraph Project(SchematicPrimitiveList primitives)
    {
        List<EngineSchematicNode> nodes = [];
        foreach (PortPrimitive port in primitives.Ports)
        {
            IReadOnlyList<string> inputs = port.Direction is SignalDirection.Output or SignalDirection.InOut
                ? [port.Name]
                : [];
            IReadOnlyList<string> outputs = port.Direction is SignalDirection.Input or SignalDirection.InOut
                ? [port.Name]
                : [];
            nodes.Add(new EngineSchematicNode(port.Id, "Port", port.Name, inputs, outputs));
        }
        nodes.AddRange(primitives.Instances.Select(ProjectInstance));
        nodes.AddRange(primitives.Logic.Select(ProjectLogic));

        Dictionary<string, List<string>> producers = BuildEndpointIndex(nodes, static node => node.Outputs);
        Dictionary<string, List<string>> consumers = BuildEndpointIndex(nodes, static node => node.Inputs);
        foreach (string signal in consumers.Keys.Except(producers.Keys, StringComparer.OrdinalIgnoreCase).ToArray())
        {
            // Skip decoder placeholders (e.g. "?") — an unresolved expression
            // source is not a real net and would render as noise.
            if (!IsRenderableSignalName(signal)) continue;
            string nodeId = $"net:{signal}";
            nodes.Add(new EngineSchematicNode(nodeId, "Net", signal, [], [signal]));
            producers[signal] = [nodeId];
        }

        List<EngineSchematicEdge> edges = [];
        int edgeIndex = 0;
        foreach ((string signal, List<string> sourceIds) in producers.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!consumers.TryGetValue(signal, out List<string>? targetIds)) continue;
            foreach (string sourceId in sourceIds)
            {
                foreach (string targetId in targetIds)
                {
                    if (string.Equals(sourceId, targetId, StringComparison.Ordinal)) continue;
                    edges.Add(new EngineSchematicEdge($"edge:{edgeIndex++}", signal, sourceId, targetId));
                }
            }
        }
        return new EngineSchematicGraph(primitives.ModuleName, nodes, edges);
    }

    private static EngineSchematicNode ProjectInstance(InstancePrimitive instance)
    {
        ProjectedPin[] inputs = instance.Pins
            .Where(static pin => IsInput(pin.Direction))
            .Select(static pin => new ProjectedPin(pin.SignalName, pin.PortName))
            .ToArray();
        ProjectedPin[] outputs = instance.Pins
            .Where(static pin => IsOutput(pin.Direction))
            .Select(static pin => new ProjectedPin(pin.SignalName, pin.PortName))
            .ToArray();
        return Node(instance.Id, "Instance", instance.InstanceName, inputs, outputs, instance.ModuleName);
    }

    private static EngineSchematicNode ProjectLogic(SchematicPrimitive primitive) => primitive switch
    {
        FlipFlopPrimitive value => Node(value.Id, "FlipFlop", "DFF",
            Pins((value.DSignal, "D"), (value.ClockSignal, "CLK"), (value.AsyncResetSignal, "ARST")),
            Pins((value.QSignal, "Q"))),
        LatchPrimitive value => Node(value.Id, "Latch", "Latch",
            Pins((value.DSignal, "D"), (value.GateSignal, "G")),
            Pins((value.QSignal, "Q"))),
        MuxPrimitive value => ProjectMux(value),
        BufferPrimitive value => Node(value.Id, "Buffer", "Buffer",
            Pins((value.InputSignal, "I")), Pins((value.OutputSignal, "O"))),
        ConstantTiePrimitive value => Node(value.Id, "Constant", value.Literal,
            [], Pins((value.OutputSignal, "O"))),
        TriStatePrimitive value => Node(value.Id, "TriState", "Tri-state",
            Pins((value.DataSignal, "D"), (value.EnableSignal, "EN")),
            Pins((value.OutputSignal, "Y"))),
        InverterPrimitive value => Node(value.Id, "Inverter", "NOT",
            Pins((value.InputSignal, "I")), Pins((value.OutputSignal, "O"))),
        GatePrimitive value => Node(value.Id, "Gate", value.Kind.ToString(),
            value.InputSignals.Select((signal, index) => new ProjectedPin(signal, GateInputLabel(index))),
            Pins((value.OutputSignal, "Y"))),
        ArithPrimitive value => Node(value.Id, "Arithmetic", value.Kind.ToString(),
            Pins((value.LeftSignal, "A"), (value.RightSignal, "B")),
            Pins((value.OutputSignal, "Y"))),
        SplitterPrimitive value => Node(value.Id, "Splitter", $"Slice {value.Range}",
            Pins((value.InputSignal, "IN")), Pins((value.OutputSignal, "OUT"))),
        JoinerPrimitive value => Node(value.Id, "Joiner", "Concat",
            value.InputSignals.Select((signal, index) => new ProjectedPin(signal, $"I{index}")),
            Pins((value.OutputSignal, "OUT"))),
        MemoryPrimitive value => Node(value.Id, "Memory", value.SignalName,
            [], Pins((value.SignalName, "DATA"))),
        MemoryReadPrimitive value => Node(value.Id, "MemoryRead", "Memory read",
            Pins((value.MemorySignal, "MEM"), (value.AddressSignal, "ADDR")),
            Pins((value.OutputSignal, "DATA"))),
        StructFanOutPrimitive value => Node(value.Id, "StructFanOut", value.StructTypeName,
            Pins((value.StructSignal, "IN")),
            value.Legs.SelectMany(static leg => leg.Consumers)
                .Select((signal, index) => new ProjectedPin(signal, $"O{index}"))),
        _ => Node(primitive.Id, primitive.GetType().Name, primitive.GetType().Name, [], [])
    };

    private static EngineSchematicNode ProjectMux(MuxPrimitive mux)
    {
        List<ProjectedPin> inputs = [];
        for (int index = 0; index < mux.SelectSignals.Count; index++)
        {
            string label = mux.SelectSignals.Count == 1 ? "S" : $"S{index}";
            inputs.Add(new ProjectedPin(mux.SelectSignals[index], label));
        }
        for (int index = 0; index < mux.Inputs.Count; index++)
        {
            if (mux.Inputs[index].Source is MuxSignalSource signal)
            {
                inputs.Add(new ProjectedPin(signal.SignalName, $"I{index}"));
            }
        }
        return Node(mux.Id, "Mux", "MUX", inputs, Pins((mux.OutputSignal, "Y")));
    }

    private static EngineSchematicNode Node(
        string id,
        string kind,
        string label,
        IEnumerable<ProjectedPin> inputs,
        IEnumerable<ProjectedPin> outputs,
        string? typeLabel = null)
    {
        (string[] inputSignals, string[] inputLabels) = NormalizePins(inputs);
        (string[] outputSignals, string[] outputLabels) = NormalizePins(outputs);
        return new EngineSchematicNode(
            id,
            kind,
            label,
            inputSignals,
            outputSignals,
            inputLabels,
            outputLabels,
            typeLabel);
    }

    private static ProjectedPin[] Pins(params (string? Signal, string Label)[] values) => values
        .Select(static value => new ProjectedPin(value.Signal, value.Label))
        .ToArray();

    private static (string[] Signals, string[] Labels) NormalizePins(IEnumerable<ProjectedPin> values)
    {
        List<string> signals = [];
        List<string> labels = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (ProjectedPin value in values)
        {
            if (string.IsNullOrWhiteSpace(value.Signal) || !seen.Add(value.Signal))
            {
                continue;
            }
            signals.Add(value.Signal);
            labels.Add(value.Label);
        }
        return ([.. signals], [.. labels]);
    }

    private static string GateInputLabel(int index) => index switch
    {
        0 => "A",
        1 => "B",
        2 => "C",
        3 => "D",
        _ => $"I{index}"
    };

    private static Dictionary<string, List<string>> BuildEndpointIndex(
        IEnumerable<EngineSchematicNode> nodes,
        Func<EngineSchematicNode, IReadOnlyList<string>> selectSignals)
    {
        Dictionary<string, List<string>> index = new(StringComparer.OrdinalIgnoreCase);
        foreach (EngineSchematicNode node in nodes)
        {
            foreach (string signal in selectSignals(node))
            {
                if (!index.TryGetValue(signal, out List<string>? endpoints))
                {
                    endpoints = [];
                    index.Add(signal, endpoints);
                }
                endpoints.Add(node.Id);
            }
        }
        return index;
    }

    /// <summary>
    /// True for a signal name that names a real net. Filters decoder placeholders
    /// like "?" that stand in for an unresolved expression source.
    /// </summary>
    private static bool IsRenderableSignalName(string signal) =>
        !string.IsNullOrWhiteSpace(signal)
        && signal.All(static c => char.IsLetterOrDigit(c) || c is '_' or '.' or '[' or ']' or '$');

    private static bool IsInput(string direction) =>
        direction.Equals("in", StringComparison.OrdinalIgnoreCase)
        || direction.Equals("input", StringComparison.OrdinalIgnoreCase)
        || direction.Equals("inout", StringComparison.OrdinalIgnoreCase);

    private static bool IsOutput(string direction) =>
        direction.Equals("out", StringComparison.OrdinalIgnoreCase)
        || direction.Equals("output", StringComparison.OrdinalIgnoreCase)
        || direction.Equals("inout", StringComparison.OrdinalIgnoreCase);

    private readonly record struct ProjectedPin(string? Signal, string Label);
}

public sealed record EngineSchematicGraph(
    string ModuleName,
    IReadOnlyList<EngineSchematicNode> Nodes,
    IReadOnlyList<EngineSchematicEdge> Edges);

public sealed record EngineSchematicNode(
    string Id,
    string Kind,
    string Label,
    IReadOnlyList<string> Inputs,
    IReadOnlyList<string> Outputs,
    IReadOnlyList<string>? InputLabels = null,
    IReadOnlyList<string>? OutputLabels = null,
    string? TypeLabel = null);

public sealed record EngineSchematicEdge(
    string Id,
    string Signal,
    string SourceNodeId,
    string TargetNodeId);
