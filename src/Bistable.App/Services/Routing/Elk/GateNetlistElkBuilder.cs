using Bistable.Core.Projects;
using Bistable.Core.Synthesis;
using Bistable.Yosys;

namespace Bistable.App.Services.Routing.Elk;

/// <summary>
/// Phase 6 P6-6: turns a post-synthesis <see cref="GateNetlist"/> module into
/// an <see cref="ElkGraph"/> that flows through the same ELK renderer the RTL
/// schematic uses. One cell becomes one ELK node with the matching prefix
/// (<c>gate_</c>, <c>ff_</c>, <c>mux_</c>, <c>inv_</c>, <c>buf_</c>,
/// <c>latch_</c>), so the existing `IsGate / IsFlipFlop / IsMux / …`
/// dispatchers in <see cref="SchematicPreviewControl"/> pick the right symbol
/// without any new render code.
///
/// Net edges come from shared bit ids: every cell pin / boundary port is
/// indexed into a map keyed by net id, then for each net we emit edges from
/// every producer to every consumer.
/// </summary>
public static class GateNetlistElkBuilder
{
    private static readonly IReadOnlySet<string> EmptyExpandedInstancePaths =
        new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Build the ELK graph for the netlist's top module. Returns the
    /// pre-layout graph plus the <see cref="ElkPortRef"/> map for callers who
    /// want to render edge values later.
    /// </summary>
    public static GateNetlistElkBuildResult Build(GateNetlist netlist, SchematicLayoutOptions? layoutOptions = null)
    {
        ArgumentNullException.ThrowIfNull(netlist);
        if (!netlist.Modules.TryGetValue(netlist.TopModule, out GateModule? topModule))
        {
            throw new InvalidOperationException(
                $"Netlist top module '{netlist.TopModule}' missing from Modules dictionary.");
        }
        return BuildModule(topModule, netlist, expandedInstancePaths: EmptyExpandedInstancePaths, layoutOptions);
    }

    /// <summary>
    /// Phase 6.5 Wave 2: build the ELK graph for an arbitrary module in the
    /// hierarchy, identified by walking <paramref name="scopePath"/> from the
    /// top module's instance names. An empty/single-element path renders the
    /// top; deeper paths drill into sub-module instances. Throws if the path
    /// can't be resolved (instance name typo, missing sub-module definition).
    /// </summary>
    public static GateNetlistElkBuildResult BuildScope(GateNetlist netlist, IReadOnlyList<string> scopePath)
        => BuildScope(netlist, scopePath, expandedInstancePaths: EmptyExpandedInstancePaths, layoutOptions: null);

    public static GateNetlistElkBuildResult BuildScope(
        GateNetlist netlist,
        IReadOnlyList<string> scopePath,
        IReadOnlySet<string> expandedInstancePaths,
        SchematicLayoutOptions? layoutOptions = null)
    {
        ArgumentNullException.ThrowIfNull(netlist);
        ArgumentNullException.ThrowIfNull(scopePath);
        ArgumentNullException.ThrowIfNull(expandedInstancePaths);
        if (scopePath.Count == 0) return Build(netlist, layoutOptions);

        if (!netlist.Modules.TryGetValue(scopePath[0], out GateModule? current))
        {
            throw new InvalidOperationException($"Scope root '{scopePath[0]}' not in netlist.");
        }
        for (int i = 1; i < scopePath.Count; i++)
        {
            string instanceName = scopePath[i];
            GateCell? cell = current.Cells.FirstOrDefault(c => c.Name == instanceName);
            if (cell is null)
            {
                throw new InvalidOperationException(
                    $"Scope path step '{instanceName}' (depth {i}) not found inside module '{current.Name}'.");
            }
            if (!netlist.Modules.TryGetValue(cell.Type, out GateModule? next))
            {
                throw new InvalidOperationException(
                    $"Instance '{instanceName}' refers to module '{cell.Type}' which isn't in the netlist.");
            }
            current = next;
        }
        return BuildModule(current, netlist, expandedInstancePaths, layoutOptions);
    }

    private static GateNetlistElkBuildResult BuildModule(
        GateModule module,
        GateNetlist netlist,
        IReadOnlySet<string> expandedInstancePaths,
        SchematicLayoutOptions? layoutOptions)
    {
        // Null preserves the legacy inline option values via the Balanced
        // preset, so callers that haven't been migrated yet keep behaving
        // exactly as they did before Wave 5.
        SchematicLayoutOptions effective = layoutOptions ?? ElkLayoutOptionsFactory.For(RoutingQuality.Balanced);
        ElkGraph graph = new() { Id = "root", LayoutOptions = effective.ToElkOptions() };
        Dictionary<string, ElkPortRef> portRefs = new(StringComparer.Ordinal);

        // Producer / consumer maps keyed by Yosys net id. We emit edges per
        // net so multi-fanout signals flow out as N parallel edges from one
        // driver to N receivers — same shape ELK already routes in the RTL.
        Dictionary<string, List<ElkPortRef>> producers = new(StringComparer.Ordinal);
        Dictionary<string, List<ElkPortRef>> consumers = new(StringComparer.Ordinal);

        AddBoundaryNodes(module, graph, producers, consumers, portRefs);
        AddModuleCells(
            module,
            netlist,
            graph.Children,
            instancePathPrefix: string.Empty,
            nodeIdPrefix: string.Empty,
            netScope: "root",
            expandedInstancePaths,
            producers,
            consumers,
            portRefs);

        EmitEdges(graph, producers, consumers);

        return new GateNetlistElkBuildResult(graph, portRefs);
    }

    private static void AddModuleCells(
        GateModule module,
        GateNetlist netlist,
        List<ElkNode> ownerChildren,
        string instancePathPrefix,
        string nodeIdPrefix,
        string netScope,
        IReadOnlySet<string> expandedInstancePaths,
        Dictionary<string, List<ElkPortRef>> producers,
        Dictionary<string, List<ElkPortRef>> consumers,
        Dictionary<string, ElkPortRef> portRefs)
    {
        int cellIndex = 0;
        foreach (GateCell cell in module.Cells)
        {
            string instancePath = string.IsNullOrEmpty(instancePathPrefix)
                ? cell.Name
                : instancePathPrefix + "/" + cell.Name;

            if (netlist.Modules.TryGetValue(cell.Type, out GateModule? childModule))
            {
                bool expanded = expandedInstancePaths.Contains(instancePath);
                AddSubModuleInstanceNode(
                    cell,
                    cellIndex++,
                    childModule,
                    netlist,
                    ownerChildren,
                    instancePath,
                    nodeIdPrefix,
                    netScope,
                    expanded,
                    expandedInstancePaths,
                    producers,
                    consumers,
                    portRefs);
            }
            else
            {
                AddCellNode(cell, cellIndex++, ownerChildren, nodeIdPrefix, netScope, producers, consumers, portRefs);
            }
        }
    }

    // ── Boundary ports ────────────────────────────────────────────────────

    private static void AddBoundaryNodes(
        GateModule module,
        ElkGraph graph,
        Dictionary<string, List<ElkPortRef>> producers,
        Dictionary<string, List<ElkPortRef>> consumers,
        Dictionary<string, ElkPortRef> portRefs)
    {
        graph.Children ??= [];
        int inputRows = CountPortBitRows(module.Ports, isInputSide: true);
        int outputRows = CountPortBitRows(module.Ports, isInputSide: false);
        ElkNode inputs  = NewBoundaryNode(ElkNodeIds.BoundaryIn, inputRows);
        ElkNode outputs = NewBoundaryNode(ElkNodeIds.BoundaryOut, outputRows);
        graph.Children.Add(inputs);
        graph.Children.Add(outputs);

        int inRow = 0;
        int outRow = 0;
        foreach (GatePort port in module.Ports)
        {
            bool isInput = port.Direction == GatePortDirection.Input;
            ElkNode owner = isInput ? inputs : outputs;
            int rowIndex = isInput ? inRow : outRow;

            // Multi-bit ports get one ELK pin per bit so each wire is
            // independently routed — matches what the user sees on the RTL view.
            for (int bitOrdinal = 0; bitOrdinal < port.Bits.Count; bitOrdinal++)
            {
                GateBit bit = port.Bits[bitOrdinal];
                string pinName = port.Bits.Count == 1 ? port.Name : $"{port.Name}[{bitOrdinal}]";
                string pinId   = $"{owner.Id}.{pinName}";
                owner.Ports!.Add(new ElkPort
                {
                    Id = pinId,
                    LayoutOptions = PortLayout(isInput ? "EAST" : "WEST", rowIndex + bitOrdinal),
                    Labels = [new ElkLabel { Text = pinName }],
                });
                ElkPortRole role = isInput ? ElkPortRole.BoundaryInput : ElkPortRole.BoundaryOutput;
                ElkPortRef portRef = new(owner.Id, pinId, role, Width: 1);
                portRefs[pinId] = portRef;

                if (bit.Kind == BitKind.Net)
                {
                    // Boundary input drives the net; boundary output consumes it.
                    string key = NetKey("root", bit.NetId);
                    if (isInput) AddTo(producers, key, portRef);
                    else         AddTo(consumers, key, portRef);
                }
            }
            if (isInput) inRow += Math.Max(1, port.Bits.Count);
            else         outRow += Math.Max(1, port.Bits.Count);
        }
    }

    private static int CountPortBitRows(IReadOnlyList<GatePort> ports, bool isInputSide) =>
        ports.Where(p => (p.Direction == GatePortDirection.Input) == isInputSide)
             .Sum(p => Math.Max(1, p.Bits.Count));

    private static ElkNode NewBoundaryNode(string id, int rows) => new()
    {
        Id = id,
        Width = 44,
        Height = Math.Max(80, 28 + rows * 18),
        LayoutOptions = BoundaryLayoutOptions(),
        Ports = [],
    };

    // ── Sub-module instances ──────────────────────────────────────────────

    // Phase 6.5 Wave 2: drawn as a labelled rectangular block carrying one pin
    // per declared port on the child module. Node id prefix is `inst_` so the
    // canvas can recognise it as expandable (double-click → drill in).
    private static void AddSubModuleInstanceNode(
        GateCell cell,
        int cellIndex,
        GateModule childModule,
        GateNetlist netlist,
        List<ElkNode> ownerChildren,
        string instancePath,
        string nodeIdPrefix,
        string parentNetScope,
        bool expanded,
        IReadOnlySet<string> expandedInstancePaths,
        Dictionary<string, List<ElkPortRef>> producers,
        Dictionary<string, List<ElkPortRef>> consumers,
        Dictionary<string, ElkPortRef> portRefs)
    {
        string nodeId = "inst_" + nodeIdPrefix + Sanitize(cell.Name) + "_" + cellIndex;

        // Size scales with the number of *rendered pin rows*, not the number
        // of declared ports. A single 32-bit bus becomes 32 ELK ports; sizing
        // only by declared-port count crushes every wire onto the same edge.
        int inputRows = 0;
        int outputRows = 0;
        foreach (GatePort port in childModule.Ports)
        {
            if (!cell.Connections.TryGetValue(port.Name, out GateConnection? conn))
            {
                continue;
            }

            int rows = Math.Max(1, conn.Bits.Count);
            if (port.Direction == GatePortDirection.Output)
            {
                outputRows += rows;
            }
            else
            {
                inputRows += rows;
            }
        }
        int rowCount = Math.Max(inputRows, outputRows);
        int longestLabel = Math.Max(cell.Name.Length, cell.Type.Length);
        Dictionary<string, string> layoutOptions = FixedOrderPortConstraints();
        layoutOptions["elk.padding"] = expanded
            ? "[top=48,left=36,right=36,bottom=24]"
            : "[top=32,left=0,right=0,bottom=8]";
        layoutOptions["elk.spacing.portPort"] = "18";

        ElkNode node = new()
        {
            Id = nodeId,
            Width = Math.Max(170, 24 + longestLabel * 8),
            Height = Math.Max(80, 36 + rowCount * 18),
            LayoutOptions = layoutOptions,
            // labels[0] = instance display name (e.g. "u_imem"),
            // labels[1] = child module type (e.g. "riscv_instruction_memory")
            //             — canvas reads it for the under-title chip.
            Labels =
            [
                new ElkLabel { Text = cell.Name },
                new ElkLabel { Text = cell.Type },
                new ElkLabel { Text = instancePath },
            ],
            Ports = [],
            Children = expanded ? [] : null,
        };
        ownerChildren.Add(node);

        PinPlacementContext ctx = new(node, nodeId, new CellPinSlots(),
            parentNetScope, producers, consumers, portRefs);

        // Iterate the child module's declared ports in the order they were
        // emitted by Yosys; the instance pin name == the port name.
        foreach (GatePort port in childModule.Ports)
        {
            if (!cell.Connections.TryGetValue(port.Name, out GateConnection? conn))
            {
                continue;
            }
            PinRole role = port.Direction == GatePortDirection.Output ? PinRole.Output : PinRole.Input;
            AddPin(port.Name, conn.Bits, role, ctx);
        }

        if (!expanded)
        {
            return;
        }

        BridgeExpandedInstancePortsToChildNets(cell, childModule, node, instancePath,
            producers, consumers, portRefs);
        string childNodePrefix = nodeIdPrefix + Sanitize(cell.Name) + "__";
        string childNetScope = "inst:" + instancePath;
        AddModuleCells(
            childModule,
            netlist,
            node.Children!,
            instancePathPrefix: instancePath,
            nodeIdPrefix: childNodePrefix,
            netScope: childNetScope,
            expandedInstancePaths,
            producers,
            consumers,
            portRefs);
    }

    private static void BridgeExpandedInstancePortsToChildNets(
        GateCell cell,
        GateModule childModule,
        ElkNode instanceNode,
        string instancePath,
        Dictionary<string, List<ElkPortRef>> producers,
        Dictionary<string, List<ElkPortRef>> consumers,
        Dictionary<string, ElkPortRef> portRefs)
    {
        string childNetScope = "inst:" + instancePath;
        foreach (GatePort port in childModule.Ports)
        {
            if (!cell.Connections.TryGetValue(port.Name, out GateConnection? conn))
            {
                continue;
            }

            int bitCount = Math.Min(port.Bits.Count, conn.Bits.Count);
            for (int bitOrdinal = 0; bitOrdinal < bitCount; bitOrdinal++)
            {
                GateBit childBit = port.Bits[bitOrdinal];
                if (childBit.Kind != BitKind.Net)
                {
                    continue;
                }

                string pinName = conn.Bits.Count == 1 ? port.Name : $"{port.Name}[{bitOrdinal}]";
                string pinId = $"{instanceNode.Id}.{pinName}";
                if (!portRefs.TryGetValue(pinId, out ElkPortRef? portRef))
                {
                    continue;
                }

                string childKey = NetKey(childNetScope, childBit.NetId);
                if (port.Direction == GatePortDirection.Output)
                {
                    // Child output: internal logic produces the child net, the
                    // compound boundary port consumes it before driving the
                    // parent net already registered by AddPin.
                    AddTo(consumers, childKey, portRef);
                }
                else
                {
                    // Child input/inout: parent drives the compound boundary
                    // port; inside the expanded module that same port is the
                    // producer for the child's local net.
                    AddTo(producers, childKey, portRef);
                }
            }
        }
    }

    // ── Cells ─────────────────────────────────────────────────────────────

    private static void AddCellNode(
        GateCell cell,
        int cellIndex,
        List<ElkNode> ownerChildren,
        string nodeIdPrefix,
        string netScope,
        Dictionary<string, List<ElkPortRef>> producers,
        Dictionary<string, List<ElkPortRef>> consumers,
        Dictionary<string, ElkPortRef> portRefs)
    {
        GateCellDescriptor descriptor = GateCellLibrary.Lookup(cell.Type);
        string nodeId = BuildCellNodeId(cell, cellIndex, descriptor, nodeIdPrefix);
        ElkNode node = new()
        {
            Id = nodeId,
            Width = ResolveNodeWidth(descriptor),
            Height = ResolveNodeHeight(descriptor),
            LayoutOptions = FixedOrderPortConstraints(),
            Labels = BuildCellLabels(cell, descriptor),
            Ports = [],
        };
        ownerChildren.Add(node);

        PinPlacementContext ctx = new(node, nodeId, new CellPinSlots(),
            netScope, producers, consumers, portRefs);
        if (descriptor.IsUnknown)
        {
            AddUnknownCellPins(cell, ctx);
        }
        else
        {
            AddKnownCellPins(cell, descriptor, ctx);
        }
    }

    // Slot indices tracked separately so AddPin can stay short.
    private sealed class CellPinSlots
    {
        public int WestIndex;
        public int EastIndex;
    }

    // Bundles the heap of "where do I put this pin and where do I record it"
    // state so AddPin / TryAddPinFromConnection / etc. stay below the
    // 7-parameter cap. Plain struct — no behaviour, just plumbing.
    private readonly record struct PinPlacementContext(
        ElkNode Node,
        string NodeId,
        CellPinSlots Slots,
        string NetScope,
        Dictionary<string, List<ElkPortRef>> Producers,
        Dictionary<string, List<ElkPortRef>> Consumers,
        Dictionary<string, ElkPortRef> PortRefs);

    // Use the library's pin ordering so renderers (FF: D / C / Q) line up
    // with the existing RTL symbol expectations.
    private static void AddKnownCellPins(GateCell cell, GateCellDescriptor descriptor, PinPlacementContext ctx)
    {
        foreach (string inputName in descriptor.Inputs)
        {
            TryAddPinFromConnection(cell, inputName, PinRole.Input, ctx);
        }
        if (descriptor.ClockPin is { } clkName)
        {
            TryAddPinFromConnection(cell, clkName, PinRole.Clock, ctx);
        }
        if (descriptor.EnablePin is { } enName)
        {
            TryAddPinFromConnection(cell, enName, PinRole.Enable, ctx);
        }
        if (!string.IsNullOrEmpty(descriptor.Output))
        {
            TryAddPinFromConnection(cell, descriptor.Output, PinRole.Output, ctx);
        }
    }

    // Unknown cells have no descriptor metadata — still place every connection
    // as a pin so the user can see the cell exists.
    private static void AddUnknownCellPins(GateCell cell, PinPlacementContext ctx)
    {
        foreach ((string portName, GateConnection conn) in cell.Connections)
        {
            bool isInput = !cell.PortDirections.TryGetValue(portName, out GatePortDirection d)
                || d != GatePortDirection.Output;
            AddPin(portName, conn.Bits, isInput ? PinRole.Input : PinRole.Output, ctx);
        }
    }

    private static void TryAddPinFromConnection(GateCell cell, string portName, PinRole role, PinPlacementContext ctx)
    {
        if (!cell.Connections.TryGetValue(portName, out GateConnection? conn)) return;
        AddPin(portName, conn.Bits, role, ctx);
    }

    private static void AddPin(string portName, IReadOnlyList<GateBit> bits, PinRole role, PinPlacementContext ctx)
    {
        bool isInput = role != PinRole.Output;
        bool isClock = role == PinRole.Clock;
        bool isEnable = role == PinRole.Enable;

        for (int b = 0; b < bits.Count; b++)
        {
            GateBit bit = bits[b];
            string pinName = bits.Count == 1 ? portName : $"{portName}[{b}]";
            int rowIndex = isInput ? ctx.Slots.WestIndex++ : ctx.Slots.EastIndex++;
            string side = isInput ? "WEST" : "EAST";
            string pinId = $"{ctx.NodeId}.{pinName}";
            ctx.Node.Ports!.Add(new ElkPort
            {
                Id = pinId,
                LayoutOptions = PortLayout(side, rowIndex),
                Labels = [new ElkLabel { Text = isClock ? ">" : pinName }],
            });
            ElkPortRef portRef = new(ctx.NodeId, pinId, ResolveRole(isInput, isClock, isEnable), Width: 1);
            ctx.PortRefs[pinId] = portRef;

            if (bit.Kind == BitKind.Net)
            {
                string key = NetKey(ctx.NetScope, bit.NetId);
                if (isInput) AddTo(ctx.Consumers, key, portRef);
                else         AddTo(ctx.Producers, key, portRef);
            }
        }
    }

    private enum PinRole { Input, Output, Clock, Enable }

    private static ElkPortRole ResolveRole(bool isInput, bool isClock, bool isEnable)
    {
        if (isClock) return ElkPortRole.FlipFlopClock;
        if (isEnable) return ElkPortRole.LatchGate;
        return isInput ? ElkPortRole.ChildInput : ElkPortRole.ChildOutput;
    }

    private static string BuildCellNodeId(
        GateCell cell,
        int cellIndex,
        GateCellDescriptor descriptor,
        string nodeIdPrefix)
    {
        // The prefix is the load-bearing piece — SchematicPreviewControl's
        // node dispatchers use StartsWith to pick the right symbol. Anything
        // after the prefix only needs to be unique within the module.
        string prefix = descriptor.Shape switch
        {
            GateCellShape.FlipFlop => "ff_",
            GateCellShape.Latch    => "latch_",
            GateCellShape.Mux      => "mux_",
            GateCellShape.Inverter => "inv_",
            GateCellShape.Buffer   => "buf_",
            GateCellShape.Gate     => "gate_",
            _                      => "node_",
        };
        return prefix + nodeIdPrefix + Sanitize(cell.Name) + "_" + cellIndex;
    }

    private static List<ElkLabel> BuildCellLabels(GateCell cell, GateCellDescriptor descriptor)
    {
        // The gate renderer reads the first whitespace token as the GateKind
        // ("And" / "Or" / "Xor" / …). For non-gate cells we just use the cell
        // name so the user can recognise it.
        string primary = descriptor.Shape == GateCellShape.Gate && descriptor.GateKind is { } gk
            ? gk.ToString()
            : cell.Type;
        return
        [
            new ElkLabel { Text = primary },
            new ElkLabel { Text = cell.Type },
            new ElkLabel { Text = cell.Name },
        ];
    }

    private static double ResolveNodeWidth(GateCellDescriptor descriptor) => descriptor.Shape switch
    {
        GateCellShape.FlipFlop => 56,
        GateCellShape.Latch    => 56,
        GateCellShape.Mux      => 48,
        GateCellShape.Gate     => 48,
        _                      => 48,
    };

    private static double ResolveNodeHeight(GateCellDescriptor descriptor)
    {
        int pinCount = descriptor.Inputs.Count
            + (descriptor.ClockPin is null ? 0 : 1)
            + (descriptor.EnablePin is null ? 0 : 1)
            + 1; // output
        return Math.Max(48, pinCount * 20);
    }

    // ── Edge emission ─────────────────────────────────────────────────────

    private static void EmitEdges(
        ElkGraph graph,
        Dictionary<string, List<ElkPortRef>> producers,
        Dictionary<string, List<ElkPortRef>> consumers)
    {
        graph.Edges ??= [];
        int edgeId = 0;
        foreach ((string netKey, List<ElkPortRef> sources) in producers)
        {
            if (!consumers.TryGetValue(netKey, out List<ElkPortRef>? targets)) continue;
            foreach (ElkPortRef source in sources)
            {
                foreach (ElkPortRef target in targets)
                {
                    graph.Edges.Add(new ElkEdge
                    {
                        Id = $"e{edgeId++}",
                        Sources = [source.PortId],
                        Targets = [target.PortId],
                        Labels = [new ElkLabel { Text = NetLabel(netKey) }],
                    });
                }
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static Dictionary<string, string> FixedOrderPortConstraints() =>
        new() { ["elk.portConstraints"] = "FIXED_ORDER" };

    private static Dictionary<string, string> BoundaryLayoutOptions()
    {
        Dictionary<string, string> options = FixedOrderPortConstraints();
        options["elk.spacing.portPort"] = "18";
        options["elk.padding"] = "[top=20,left=0,right=0,bottom=8]";
        return options;
    }

    private static Dictionary<string, string> PortLayout(string side, int index) =>
        new()
        {
            ["elk.port.side"] = side,
            ["elk.port.index"] = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

    private static void AddTo(Dictionary<string, List<ElkPortRef>> map, string key, ElkPortRef value)
    {
        if (!map.TryGetValue(key, out List<ElkPortRef>? list))
        {
            list = [];
            map[key] = list;
        }
        list.Add(value);
    }

    private static string NetKey(string scope, int netId) =>
        scope + "#" + netId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string NetLabel(string netKey)
    {
        int hash = netKey.LastIndexOf('#');
        return hash >= 0 && hash + 1 < netKey.Length
            ? "net" + netKey[(hash + 1)..]
            : "net" + netKey;
    }

    private static string Sanitize(string raw) =>
        raw.Replace('$', '_').Replace('.', '_').Replace('/', '_').Replace(':', '_')
           .Replace('[', '_').Replace(']', '_').Replace(' ', '_');
}

/// <summary>Result of <see cref="GateNetlistElkBuilder.Build"/>.</summary>
public sealed record GateNetlistElkBuildResult(
    ElkGraph Graph,
    IReadOnlyDictionary<string, ElkPortRef> PortRefs);
