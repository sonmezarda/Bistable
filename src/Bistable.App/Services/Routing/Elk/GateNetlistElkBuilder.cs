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
    /// <summary>
    /// Build the ELK graph for the netlist's top module. Returns the
    /// pre-layout graph plus the <see cref="ElkPortRef"/> map for callers who
    /// want to render edge values later.
    /// </summary>
    public static GateNetlistElkBuildResult Build(GateNetlist netlist)
    {
        ArgumentNullException.ThrowIfNull(netlist);
        if (!netlist.Modules.TryGetValue(netlist.TopModule, out GateModule? topModule))
        {
            throw new InvalidOperationException(
                $"Netlist top module '{netlist.TopModule}' missing from Modules dictionary.");
        }
        return BuildModule(topModule, netlist);
    }

    /// <summary>
    /// Phase 6.5 Wave 2: build the ELK graph for an arbitrary module in the
    /// hierarchy, identified by walking <paramref name="scopePath"/> from the
    /// top module's instance names. An empty/single-element path renders the
    /// top; deeper paths drill into sub-module instances. Throws if the path
    /// can't be resolved (instance name typo, missing sub-module definition).
    /// </summary>
    public static GateNetlistElkBuildResult BuildScope(GateNetlist netlist, IReadOnlyList<string> scopePath)
    {
        ArgumentNullException.ThrowIfNull(netlist);
        ArgumentNullException.ThrowIfNull(scopePath);
        if (scopePath.Count == 0) return Build(netlist);

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
        return BuildModule(current, netlist);
    }

    private static GateNetlistElkBuildResult BuildModule(GateModule module, GateNetlist netlist)
    {
        ElkGraph graph = new() { Id = "root", LayoutOptions = BuildRootLayoutOptions() };
        Dictionary<string, ElkPortRef> portRefs = new(StringComparer.Ordinal);

        // Producer / consumer maps keyed by Yosys net id. We emit edges per
        // net so multi-fanout signals flow out as N parallel edges from one
        // driver to N receivers — same shape ELK already routes in the RTL.
        Dictionary<int, List<ElkPortRef>> producers = new();
        Dictionary<int, List<ElkPortRef>> consumers = new();

        AddBoundaryNodes(module, graph, producers, consumers, portRefs);
        int cellIndex = 0;
        foreach (GateCell cell in module.Cells)
        {
            // Phase 6.5 Wave 2: cells whose type names another module in the
            // netlist are sub-module instances — render as expandable boxes
            // (one ELK pin per declared module port) so the user can drill in.
            // Primitive cells ($_AND_/$_DFF_/...) keep the existing path.
            if (netlist.Modules.TryGetValue(cell.Type, out GateModule? childModule))
            {
                AddSubModuleInstanceNode(cell, cellIndex++, childModule, graph, producers, consumers, portRefs);
            }
            else
            {
                AddCellNode(cell, cellIndex++, graph, producers, consumers, portRefs);
            }
        }

        EmitEdges(graph, producers, consumers);

        return new GateNetlistElkBuildResult(graph, portRefs);
    }

    // ── Boundary ports ────────────────────────────────────────────────────

    private static void AddBoundaryNodes(
        GateModule module,
        ElkGraph graph,
        Dictionary<int, List<ElkPortRef>> producers,
        Dictionary<int, List<ElkPortRef>> consumers,
        Dictionary<string, ElkPortRef> portRefs)
    {
        graph.Children ??= [];
        ElkNode inputs  = NewBoundaryNode(ElkNodeIds.BoundaryIn);
        ElkNode outputs = NewBoundaryNode(ElkNodeIds.BoundaryOut);
        graph.Children.Add(inputs);
        graph.Children.Add(outputs);

        int inIndex = 0;
        int outIndex = 0;
        foreach (GatePort port in module.Ports)
        {
            bool isInput = port.Direction == GatePortDirection.Input;
            ElkNode owner = isInput ? inputs : outputs;
            int rowIndex = isInput ? inIndex++ : outIndex++;

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
                    if (isInput) AddTo(producers, bit.NetId, portRef);
                    else         AddTo(consumers, bit.NetId, portRef);
                }
            }
        }
    }

    private static ElkNode NewBoundaryNode(string id) => new()
    {
        Id = id,
        Width = 32,
        Height = 200,
        LayoutOptions = FixedOrderPortConstraints(),
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
        ElkGraph graph,
        Dictionary<int, List<ElkPortRef>> producers,
        Dictionary<int, List<ElkPortRef>> consumers,
        Dictionary<string, ElkPortRef> portRefs)
    {
        string nodeId = "inst_" + Sanitize(cell.Name) + "_" + cellIndex;

        // Size scales with the larger of input or output port count so wide
        // sub-modules (RV32I imem with prog_rdata + instruction) don't crush
        // their pins on top of each other.
        int inputCount  = childModule.Ports.Count(p => p.Direction == GatePortDirection.Input);
        int outputCount = childModule.Ports.Count(p => p.Direction == GatePortDirection.Output);
        int rowCount    = Math.Max(inputCount, outputCount);

        ElkNode node = new()
        {
            Id = nodeId,
            Width = 140,
            Height = Math.Max(70, 24 + rowCount * 18),
            LayoutOptions = FixedOrderPortConstraints(),
            // labels[0] = instance display name (e.g. "u_imem"),
            // labels[1] = child module type (e.g. "riscv_instruction_memory")
            //             — canvas reads it for the under-title chip.
            Labels =
            [
                new ElkLabel { Text = cell.Name },
                new ElkLabel { Text = cell.Type },
            ],
            Ports = [],
        };
        graph.Children!.Add(node);

        PinPlacementContext ctx = new(node, nodeId, new CellPinSlots(),
            producers, consumers, portRefs);

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
    }

    // ── Cells ─────────────────────────────────────────────────────────────

    private static void AddCellNode(
        GateCell cell,
        int cellIndex,
        ElkGraph graph,
        Dictionary<int, List<ElkPortRef>> producers,
        Dictionary<int, List<ElkPortRef>> consumers,
        Dictionary<string, ElkPortRef> portRefs)
    {
        GateCellDescriptor descriptor = GateCellLibrary.Lookup(cell.Type);
        string nodeId = BuildCellNodeId(cell, cellIndex, descriptor);
        ElkNode node = new()
        {
            Id = nodeId,
            Width = ResolveNodeWidth(descriptor),
            Height = ResolveNodeHeight(descriptor),
            LayoutOptions = FixedOrderPortConstraints(),
            Labels = BuildCellLabels(cell, descriptor),
            Ports = [],
        };
        graph.Children!.Add(node);

        PinPlacementContext ctx = new(node, nodeId, new CellPinSlots(),
            producers, consumers, portRefs);
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
        Dictionary<int, List<ElkPortRef>> Producers,
        Dictionary<int, List<ElkPortRef>> Consumers,
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
                if (isInput) AddTo(ctx.Consumers, bit.NetId, portRef);
                else         AddTo(ctx.Producers, bit.NetId, portRef);
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

    private static string BuildCellNodeId(GateCell cell, int cellIndex, GateCellDescriptor descriptor)
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
        return prefix + Sanitize(cell.Name) + "_" + cellIndex;
    }

    private static List<ElkLabel> BuildCellLabels(GateCell cell, GateCellDescriptor descriptor)
    {
        // The gate renderer reads the first whitespace token as the GateKind
        // ("And" / "Or" / "Xor" / …). For non-gate cells we just use the cell
        // name so the user can recognise it.
        string primary = descriptor.Shape == GateCellShape.Gate && descriptor.GateKind is { } gk
            ? gk.ToString()
            : cell.Type;
        return [new ElkLabel { Text = primary }];
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
        Dictionary<int, List<ElkPortRef>> producers,
        Dictionary<int, List<ElkPortRef>> consumers)
    {
        graph.Edges ??= [];
        int edgeId = 0;
        foreach ((int netId, List<ElkPortRef> sources) in producers)
        {
            if (!consumers.TryGetValue(netId, out List<ElkPortRef>? targets)) continue;
            foreach (ElkPortRef source in sources)
            {
                foreach (ElkPortRef target in targets)
                {
                    graph.Edges.Add(new ElkEdge
                    {
                        Id = $"e{edgeId++}",
                        Sources = [source.PortId],
                        Targets = [target.PortId],
                        Labels = [new ElkLabel { Text = $"net{netId}" }],
                    });
                }
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static Dictionary<string, string> FixedOrderPortConstraints() =>
        new() { ["elk.portConstraints"] = "FIXED_ORDER" };

    private static Dictionary<string, string> PortLayout(string side, int index) =>
        new()
        {
            ["elk.port.side"] = side,
            ["elk.port.index"] = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

    private static Dictionary<string, string> BuildRootLayoutOptions() => new()
    {
        ["elk.algorithm"] = "layered",
        ["elk.direction"] = "RIGHT",
        ["elk.edgeRouting"] = "ORTHOGONAL",
        ["elk.hierarchyHandling"] = "INCLUDE_CHILDREN",
        ["elk.spacing.nodeNode"] = "40",
        ["elk.layered.spacing.nodeNodeBetweenLayers"] = "80",
    };

    private static void AddTo(Dictionary<int, List<ElkPortRef>> map, int key, ElkPortRef value)
    {
        if (!map.TryGetValue(key, out List<ElkPortRef>? list))
        {
            list = [];
            map[key] = list;
        }
        list.Add(value);
    }

    private static string Sanitize(string raw) =>
        raw.Replace('$', '_').Replace('.', '_').Replace('/', '_').Replace(':', '_')
           .Replace('[', '_').Replace(']', '_').Replace(' ', '_');
}

/// <summary>Result of <see cref="GateNetlistElkBuilder.Build"/>.</summary>
public sealed record GateNetlistElkBuildResult(
    ElkGraph Graph,
    IReadOnlyDictionary<string, ElkPortRef> PortRefs);
