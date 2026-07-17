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
        string[] inputs = instance.Pins
            .Where(static pin => IsInput(pin.Direction))
            .Select(static pin => pin.SignalName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] outputs = instance.Pins
            .Where(static pin => IsOutput(pin.Direction))
            .Select(static pin => pin.SignalName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new EngineSchematicNode(instance.Id, "Instance", instance.InstanceName, inputs, outputs);
    }

    private static EngineSchematicNode ProjectLogic(SchematicPrimitive primitive) => primitive switch
    {
        FlipFlopPrimitive value => Node(value.Id, "FlipFlop", "DFF", Inputs(value.DSignal, value.ClockSignal, value.AsyncResetSignal), [value.QSignal]),
        LatchPrimitive value => Node(value.Id, "Latch", "Latch", [value.DSignal, value.GateSignal], [value.QSignal]),
        MuxPrimitive value => Node(value.Id, "Mux", "MUX", value.SelectSignals
            .Concat(value.Inputs.Select(static input => input.Source).OfType<MuxSignalSource>().Select(static source => source.SignalName)), [value.OutputSignal]),
        BufferPrimitive value => Node(value.Id, "Buffer", "Buffer", [value.InputSignal], [value.OutputSignal]),
        ConstantTiePrimitive value => Node(value.Id, "Constant", value.Literal, [], [value.OutputSignal]),
        TriStatePrimitive value => Node(value.Id, "TriState", "Tri-state", [value.DataSignal, value.EnableSignal], [value.OutputSignal]),
        InverterPrimitive value => Node(value.Id, "Inverter", "NOT", [value.InputSignal], [value.OutputSignal]),
        GatePrimitive value => Node(value.Id, "Gate", value.Kind.ToString(), value.InputSignals, [value.OutputSignal]),
        ArithPrimitive value => Node(value.Id, "Arithmetic", value.Kind.ToString(), [value.LeftSignal, value.RightSignal], [value.OutputSignal]),
        SplitterPrimitive value => Node(value.Id, "Splitter", $"Slice {value.Range}", [value.InputSignal], [value.OutputSignal]),
        JoinerPrimitive value => Node(value.Id, "Joiner", "Concat", value.InputSignals, [value.OutputSignal]),
        MemoryPrimitive value => Node(value.Id, "Memory", value.SignalName, [], [value.SignalName]),
        MemoryReadPrimitive value => Node(value.Id, "MemoryRead", "Memory read", [value.MemorySignal, value.AddressSignal], [value.OutputSignal]),
        StructFanOutPrimitive value => Node(value.Id, "StructFanOut", value.StructTypeName, [value.StructSignal], value.Legs.SelectMany(static leg => leg.Consumers)),
        _ => Node(primitive.Id, primitive.GetType().Name, primitive.GetType().Name, [], [])
    };

    private static EngineSchematicNode Node(
        string id,
        string kind,
        string label,
        IEnumerable<string?> inputs,
        IEnumerable<string?> outputs) => new(
            id,
            kind,
            label,
            NormalizeSignals(inputs),
            NormalizeSignals(outputs));

    private static string[] Inputs(params string?[] values) => NormalizeSignals(values);

    private static string[] NormalizeSignals(IEnumerable<string?> values) => values
        .Where(static value => !string.IsNullOrWhiteSpace(value))
        .Select(static value => value!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

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

    private static bool IsInput(string direction) =>
        direction.Equals("in", StringComparison.OrdinalIgnoreCase)
        || direction.Equals("input", StringComparison.OrdinalIgnoreCase)
        || direction.Equals("inout", StringComparison.OrdinalIgnoreCase);

    private static bool IsOutput(string direction) =>
        direction.Equals("out", StringComparison.OrdinalIgnoreCase)
        || direction.Equals("output", StringComparison.OrdinalIgnoreCase)
        || direction.Equals("inout", StringComparison.OrdinalIgnoreCase);
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
    IReadOnlyList<string> Outputs);

public sealed record EngineSchematicEdge(
    string Id,
    string Signal,
    string SourceNodeId,
    string TargetNodeId);
