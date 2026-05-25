using System.Globalization;
using Bistable.App.ViewModels;
using Bistable.Core.Design;
using Bistable.Core.Design.Schematic;

namespace Bistable.App.Services.Routing.Elk;

/// <summary>
/// Converts the in-memory scope view models (boundary ports, child instances,
/// local signals) into an <see cref="ElkGraph"/> suitable for elkjs layered routing.
/// </summary>
internal sealed class ElkGraphBuilder
{
    // P2.5-2: bumped from 36 → 48 to give the sub-instance title baseline enough
    // top padding so it never collides with the first port row. The title font
    // is 13pt and the first port label can be ~14px tall — 36px was too tight
    // and produced visible overlap (jump_decoder ↔ jmp_cond[3b] in arnicomp).
    private const double ModuleHeaderHeight = 48;
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
    private const string PortSideSouth = "SOUTH";
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
            AddChildNode(graph, child, portRefs, scope.ExpandedPaths, scope.PrimitivesByModule);
        }

        // Collect target names that primitives will render. When a primitive owns a
        // target signal, the LEGACY operator-node generation for that same target
        // must be suppressed to avoid duplicate nodes. This list MUST match the
        // primitives that DispatchPrimitives actually renders — adding a primitive
        // here without a corresponding dispatch case would silently drop the node.
        //
        // Joiner and Splitter primitives are NOT in this set because the builder
        // keeps using the legacy AddJoinerNode/AddSplitterNode path for them; the
        // decoder emits them but DispatchPrimitives skips them. Suppressing legacy
        // for those primitives would erase the joiner/splitter from rendering.
        HashSet<string> primitiveOwnedTargets = scope.Primitives is null
            ? []
            : new HashSet<string>(
                scope.Primitives
                    .Select(static p => p switch
                    {
                        GatePrimitive g       => g.OutputSignal,
                        ArithPrimitive a      => a.OutputSignal,
                        MuxPrimitive mux      => mux.OutputSignal,
                        BufferPrimitive buf   => buf.OutputSignal,
                        InverterPrimitive inv => inv.OutputSignal,
                        _ => null
                    })
                    .Where(static t => t is not null)!,
                StringComparer.OrdinalIgnoreCase);

        foreach (DesignContAssign assign in scope.ContAssigns
                     .Where(a => a.SourceNames.Count >= 2
                                 && !primitiveOwnedTargets.Contains(a.TargetName)
                                 && !SchematicDecoder.IsVerilatorInternalSignal(a.TargetName)))
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

        // P2-11 fix: when a StructFanOutPrimitive owns a struct signal, the
        // legacy `assign target = struct[hi:lo];` contassigns must NOT also produce
        // a SplitterPrimitive node — the fan-out wedge already serves the same role
        // with proper field labels, and a duplicate splitter would visually compete.
        HashSet<string> fanOutStructBases = scope.Primitives is null
            ? []
            : new HashSet<string>(
                scope.Primitives.OfType<StructFanOutPrimitive>().Select(static f => f.StructSignal),
                StringComparer.OrdinalIgnoreCase);

        foreach (IGrouping<string, DesignContAssign> group in scope.ContAssigns
                     .Where(static a => a.SourceRange.HasValue && a.SourceNames.Count == 1)
                     .Where(a => !fanOutStructBases.Contains(a.SourceNames[0]))
                     .Where(static a => !SchematicDecoder.IsVerilatorInternalSignal(a.TargetName)
                                        && !SchematicDecoder.IsVerilatorInternalSignal(a.SourceNames[0]))
                     .GroupBy(static a => a.SourceNames[0], StringComparer.OrdinalIgnoreCase))
        {
            AddSplitterNode(graph, scope, group.Key, [.. group.OrderByDescending(static a => a.SourceRange!.Value.Hi)], portRefs);
        }

        // Phase 2: emit flip-flop nodes from primitives (Phase 1 AST decoder output).
        // This adds visible FF symbols for signals previously invisible to the legacy
        // contassign-only path. No-op when scope.Primitives is null/empty.
        DispatchPrimitives(graph, scope.Primitives, portRefs);

        AddEdges(graph, scope, portRefs);
        PruneOrphanPrimitives(graph);
        return new ElkBuildResult(graph, portRefs);
    }

    // Routes each primitive in the scope to its node-builder. Pulled out of Build()
    // to keep that method's cognitive complexity manageable.
    private static void DispatchPrimitives(
        ElkGraph graph,
        IReadOnlyList<SchematicPrimitive>? primitives,
        Dictionary<string, ElkPortRef> portRefs)
    {
        if (primitives is null) return;
        foreach (SchematicPrimitive primitive in primitives)
        {
            switch (primitive)
            {
                case FlipFlopPrimitive ff:  AddFlipFlopNode(graph.Children, ff, portRefs); break;
                case MuxPrimitive mux:      AddMuxNode(graph.Children, mux, portRefs); break;
                case LatchPrimitive lt:     AddLatchNode(graph.Children, lt, portRefs); break;
                case MemoryPrimitive mem:   AddMemoryNode(graph.Children, mem); break;
                case BufferPrimitive buf:   AddBufferNode(graph.Children, buf, portRefs); break;
                case InverterPrimitive inv: AddInverterNode(graph.Children, inv, portRefs); break;
                case GatePrimitive gate:    AddGateNode(graph.Children, gate, portRefs); break;
                case ArithPrimitive arith:  AddArithNode(graph.Children, arith, portRefs); break;
                case StructFanOutPrimitive fanOut: AddStructFanOutNode(graph, fanOut, portRefs); break;
            }
        }
    }

    private static void AddFlipFlopNode(
        IList<ElkNode> target,
        FlipFlopPrimitive ff,
        Dictionary<string, ElkPortRef> portRefs,
        string? nodeIdOverride = null,
        string? portRefKeyPrefix = null)
    {
        string nodeId = nodeIdOverride ?? ElkNodeIds.ForFlipFlop(ff.QSignal);
        string kp = portRefKeyPrefix ?? string.Empty;
        bool hasReset = !string.IsNullOrEmpty(ff.AsyncResetSignal);

        ElkNode node = new()
        {
            Id = nodeId,
            Width = 64,
            Height = hasReset ? 60 : 48,
            LayoutOptions = FixedOrderPortConstraints(),
            Labels = [new ElkLabel { Text = "FF " + ff.QSignal }],
            Ports = []
        };

        // West side ports: D (top), Clk (middle), Rst (bottom, optional).
        // Port.Labels carry the IEEE 91 pin glyph ("D", ">", "R", "Q") which the
        // renderer uses to anchor port labels onto the symbol.
        string dPortId = $"{nodeId}.d";
        node.Ports!.Add(new ElkPort
        {
            Id = dPortId,
            LayoutOptions = PortLayout(PortSideWest, 0),
            Labels = [new ElkLabel { Text = IsUnresolvedSignal(ff.DSignal) ? "D·X" : "D" }]
        });
        portRefs[kp + ElkSignalKey.FlipFlopD(ff.QSignal)] =
            new ElkPortRef(nodeId, dPortId, ElkPortRole.FlipFlopD, ff.Width);

        string clkPortId = $"{nodeId}.clk";
        node.Ports!.Add(new ElkPort
        {
            Id = clkPortId,
            LayoutOptions = PortLayout(PortSideWest, 1),
            Labels = [new ElkLabel { Text = ">" }]   // edge-trigger marker
        });
        portRefs[kp + ElkSignalKey.FlipFlopClock(ff.QSignal)] =
            new ElkPortRef(nodeId, clkPortId, ElkPortRole.FlipFlopClock, 1);

        if (hasReset)
        {
            string rstPortId = $"{nodeId}.rst";
            node.Ports!.Add(new ElkPort
            {
                Id = rstPortId,
                LayoutOptions = PortLayout(PortSideWest, 2),
                Labels = [new ElkLabel { Text = "R" }]
            });
            portRefs[kp + ElkSignalKey.FlipFlopReset(ff.QSignal)] =
                new ElkPortRef(nodeId, rstPortId, ElkPortRole.FlipFlopReset, 1);
        }

        // East side: Q output
        string qPortId = $"{nodeId}.q";
        node.Ports!.Add(new ElkPort
        {
            Id = qPortId,
            LayoutOptions = PortLayout(PortSideEast, 0),
            Labels = [new ElkLabel { Text = "Q" }]
        });
        portRefs[kp + ElkSignalKey.FlipFlopQ(ff.QSignal)] =
            new ElkPortRef(nodeId, qPortId, ElkPortRole.FlipFlopQ, ff.Width);

        target.Add(node);
    }

    private static void AddMuxNode(
        IList<ElkNode> target,
        MuxPrimitive mux,
        Dictionary<string, ElkPortRef> portRefs,
        string? nodeIdOverride = null,
        string? portRefKeyPrefix = null)
    {
        string nodeId = nodeIdOverride ?? ElkNodeIds.ForMux(mux.OutputSignal);
        string kp = portRefKeyPrefix ?? string.Empty;
        int dataInputCount = mux.Inputs.Count;
        int selectorCount = mux.SelectSignals.Count;

        double height = Math.Max(48, 16 + dataInputCount * 14);
        double width = ComputeMuxWidth(mux);

        ElkNode node = new()
        {
            Id = nodeId,
            Width = width,
            Height = height,
            LayoutOptions = FixedOrderPortConstraints(),
            Labels = [new ElkLabel { Text = "MUX " + mux.OutputSignal }],
            Ports = []
        };

        // West-side data inputs (top → bottom). Labels are the branch labels from the
        // decoder (e.g. "0", "1") — the renderer paints them inside the trapezoid.
        int westIndex = 0;
        for (int i = 0; i < dataInputCount; i++)
        {
            string portId = $"{nodeId}.in.{i}";
            string label = i < mux.Inputs.Count ? mux.Inputs[i].Label : i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            node.Ports!.Add(new ElkPort
            {
                Id = portId,
                LayoutOptions = PortLayout(PortSideWest, westIndex++),
                Labels = [new ElkLabel { Text = label }]
            });
            portRefs[kp + ElkSignalKey.MuxInput(mux.OutputSignal, i)] =
                new ElkPortRef(nodeId, portId, ElkPortRole.MuxInput, mux.Width);
        }

        // South-side selectors. This follows the Logisim/Vivado convention:
        // west pins are data inputs, bottom pins are select controls.
        // P2.5-6: label each selector port with its ACTUAL signal name (e.g. "sel",
        // "op_sel", "s2") instead of generic "S0/S1/S2" placeholders that previously
        // implied bits of a single multi-bit selector. The N selectors in a chained
        // ternary are N separate signals — this labeling makes that explicit.
        // When the decoder provided richer display labels via SelectorLabels (e.g.
        // "ctrl[3:2]" for a bit-select condition), use those for the port glyph —
        // SelectSignals stays as the BARE name for endpoint wire-up.
        for (int i = 0; i < selectorCount; i++)
        {
            string portId = $"{nodeId}.sel.{i}";
            string displayLabel = mux.SelectorLabels is { } labels && i < labels.Count && !string.IsNullOrEmpty(labels[i])
                ? labels[i]
                : mux.SelectSignals[i];
            string label = string.IsNullOrEmpty(displayLabel) ? $"S{i}" : displayLabel;
            node.Ports!.Add(new ElkPort
            {
                Id = portId,
                LayoutOptions = PortLayout(PortSideSouth, i),
                Labels = [new ElkLabel { Text = label }]
            });
            portRefs[kp + ElkSignalKey.MuxSelect(mux.OutputSignal, i)] =
                new ElkPortRef(nodeId, portId, ElkPortRole.MuxSelect, 1);
        }

        // East-side output
        string outPortId = $"{nodeId}.out";
        node.Ports!.Add(new ElkPort
        {
            Id = outPortId,
            LayoutOptions = PortLayout(PortSideEast, 0),
            Labels = [new ElkLabel { Text = "Y" }]
        });
        portRefs[kp + ElkSignalKey.MuxOutput(mux.OutputSignal)] =
            new ElkPortRef(nodeId, outPortId, ElkPortRole.MuxOutput, mux.Width);

        target.Add(node);
    }

    private static double ComputeMuxWidth(MuxPrimitive mux)
    {
        double titleWidth = EstimateTextWidth("MUX " + mux.OutputSignal) + 16;
        double dataLabelWidth = mux.Inputs
            .Select(static i => i.Label)
            .Where(static label => !IsDiagnosticLabel(label))
            .DefaultIfEmpty(string.Empty)
            .Max(static label => EstimateTextWidth(label)) + 20;
        double selectorWidth = 24 + mux.SelectSignals.Count * 30;
        return Math.Max(72, Math.Max(titleWidth, Math.Max(dataLabelWidth, selectorWidth)));
    }

    private static double EstimateTextWidth(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : text.Length * 7;

    private static bool IsUnresolvedSignal(string? signalName) =>
        string.IsNullOrWhiteSpace(signalName) || string.Equals(signalName, "?", StringComparison.Ordinal);

    private static bool IsDiagnosticLabel(string? label) =>
        !string.IsNullOrEmpty(label) && label.Contains('·', StringComparison.Ordinal);

    private static void AddLatchNode(
        IList<ElkNode> target,
        LatchPrimitive latch,
        Dictionary<string, ElkPortRef> portRefs,
        string? nodeIdOverride = null,
        string? portRefKeyPrefix = null)
    {
        string nodeId = nodeIdOverride ?? ElkNodeIds.ForLatch(latch.QSignal);
        string kp = portRefKeyPrefix ?? string.Empty;

        ElkNode node = new()
        {
            Id = nodeId,
            Width = 56,
            Height = 48,
            LayoutOptions = FixedOrderPortConstraints(),
            Labels = [new ElkLabel { Text = "L " + latch.QSignal }],
            Ports = []
        };

        // West: D (top), G (bottom). No clock triangle — latches are level-sensitive.
        string dPortId = $"{nodeId}.d";
        node.Ports!.Add(new ElkPort
        {
            Id = dPortId,
            LayoutOptions = PortLayout(PortSideWest, 0),
            Labels = [new ElkLabel { Text = "D" }]
        });
        portRefs[kp + ElkSignalKey.LatchD(latch.QSignal)] =
            new ElkPortRef(nodeId, dPortId, ElkPortRole.LatchD, latch.Width);

        string gPortId = $"{nodeId}.g";
        node.Ports!.Add(new ElkPort
        {
            Id = gPortId,
            LayoutOptions = PortLayout(PortSideWest, 1),
            Labels = [new ElkLabel { Text = "G" }]
        });
        portRefs[kp + ElkSignalKey.LatchGate(latch.QSignal)] =
            new ElkPortRef(nodeId, gPortId, ElkPortRole.LatchGate, 1);

        // East: Q
        string qPortId = $"{nodeId}.q";
        node.Ports!.Add(new ElkPort
        {
            Id = qPortId,
            LayoutOptions = PortLayout(PortSideEast, 0),
            Labels = [new ElkLabel { Text = "Q" }]
        });
        portRefs[kp + ElkSignalKey.LatchQ(latch.QSignal)] =
            new ElkPortRef(nodeId, qPortId, ElkPortRole.LatchQ, latch.Width);

        target.Add(node);
    }

    // ── P2.5-5: inner-primitive dispatch for expanded compounds ──────────────
    //
    // When a compound child is expanded, each of its module's primitives is added
    // as an ELK child node of the compound using the same Add{FlipFlop,Mux,...}Node
    // builders the outer scope uses — so the renderer's IsFlipFlop/IsMux/...
    // discriminators dispatch to the proper symbol drawer (clock triangle, mux
    // trapezoid, inverter bubble, etc.) instead of falling back to the generic
    // DrawElkNodeCard path.
    //
    // The inner node IDs encode the compound's hierarchy path AFTER the type
    // prefix (`ff_<scope>__<sig>`) so the StartsWith-based dispatch still fires.
    // PortRef keys are stored under "@inner::<compoundPath>" so the inner-scope
    // edge collector (CollectInsidePrimitiveEndpoints) can resolve them without
    // colliding with outer-scope keys for the same signal name.
    private static void AddInnerPrimitiveNode(
        IList<ElkNode> target,
        SchematicPrimitive primitive,
        string compoundPath,
        Dictionary<string, ElkPortRef> portRefs)
    {
        string keyPrefix = "@inner::" + compoundPath;
        switch (primitive)
        {
            case FlipFlopPrimitive ff:
                AddFlipFlopNode(target, ff, portRefs,
                    nodeIdOverride: ElkNodeIds.ForInnerFlipFlop(compoundPath, ff.QSignal),
                    portRefKeyPrefix: keyPrefix);
                break;
            case MuxPrimitive mux:
                AddMuxNode(target, mux, portRefs,
                    nodeIdOverride: ElkNodeIds.ForInnerMux(compoundPath, mux.OutputSignal),
                    portRefKeyPrefix: keyPrefix);
                break;
            case LatchPrimitive lt:
                AddLatchNode(target, lt, portRefs,
                    nodeIdOverride: ElkNodeIds.ForInnerLatch(compoundPath, lt.QSignal),
                    portRefKeyPrefix: keyPrefix);
                break;
            case BufferPrimitive buf:
                AddBufferNode(target, buf, portRefs,
                    nodeIdOverride: ElkNodeIds.ForInnerBuffer(compoundPath, buf.OutputSignal),
                    portRefKeyPrefix: keyPrefix);
                break;
            case InverterPrimitive inv:
                AddInverterNode(target, inv, portRefs,
                    nodeIdOverride: ElkNodeIds.ForInnerInverter(compoundPath, inv.OutputSignal),
                    portRefKeyPrefix: keyPrefix);
                break;
            case GatePrimitive gate:
                AddGateNode(target, gate, portRefs,
                    nodeIdOverride: ElkNodeIds.ForInnerGate(compoundPath, gate.OutputSignal),
                    portRefKeyPrefix: keyPrefix);
                break;
            case ArithPrimitive arith:
                AddArithNode(target, arith, portRefs,
                    nodeIdOverride: ElkNodeIds.ForInnerArith(compoundPath, arith.OutputSignal),
                    portRefKeyPrefix: keyPrefix);
                break;
            case MemoryPrimitive mem:
                AddMemoryNode(target, mem,
                    nodeIdOverride: ElkNodeIds.ForInnerMemory(compoundPath, mem.SignalName));
                break;
        }
    }

    // ── Buffer / Inverter / Gate / Arith (P2-4d) ─────────────────────────

    private static void AddBufferNode(
        IList<ElkNode> target,
        BufferPrimitive buf,
        Dictionary<string, ElkPortRef> portRefs,
        string? nodeIdOverride = null,
        string? portRefKeyPrefix = null)
    {
        string nodeId = nodeIdOverride ?? ElkNodeIds.ForBuffer(buf.OutputSignal);
        string kp = portRefKeyPrefix ?? string.Empty;
        ElkNode node = new()
        {
            Id = nodeId,
            Width = 40,
            Height = 28,
            LayoutOptions = FixedOrderPortConstraints(),
            Labels = [new ElkLabel { Text = "BUF " + buf.OutputSignal }],
            Ports = []
        };

        string inPortId = $"{nodeId}.in";
        node.Ports!.Add(new ElkPort
        {
            Id = inPortId,
            LayoutOptions = PortLayout(PortSideWest, 0),
            Labels = [new ElkLabel { Text = "A" }]
        });
        portRefs[kp + ElkSignalKey.BufferIn(buf.OutputSignal)] =
            new ElkPortRef(nodeId, inPortId, ElkPortRole.BufferIn, buf.Width);

        string outPortId = $"{nodeId}.out";
        node.Ports!.Add(new ElkPort
        {
            Id = outPortId,
            LayoutOptions = PortLayout(PortSideEast, 0),
            Labels = [new ElkLabel { Text = "Y" }]
        });
        portRefs[kp + ElkSignalKey.BufferOut(buf.OutputSignal)] =
            new ElkPortRef(nodeId, outPortId, ElkPortRole.BufferOut, buf.Width);

        target.Add(node);
    }

    private static void AddInverterNode(
        IList<ElkNode> target,
        InverterPrimitive inv,
        Dictionary<string, ElkPortRef> portRefs,
        string? nodeIdOverride = null,
        string? portRefKeyPrefix = null)
    {
        string nodeId = nodeIdOverride ?? ElkNodeIds.ForInverter(inv.OutputSignal);
        string kp = portRefKeyPrefix ?? string.Empty;
        ElkNode node = new()
        {
            Id = nodeId,
            Width = 44,
            Height = 32,
            LayoutOptions = FixedOrderPortConstraints(),
            Labels = [new ElkLabel { Text = "INV " + inv.OutputSignal }],
            Ports = []
        };

        string inPortId = $"{nodeId}.in";
        node.Ports!.Add(new ElkPort
        {
            Id = inPortId,
            LayoutOptions = PortLayout(PortSideWest, 0),
            Labels = [new ElkLabel { Text = "A" }]
        });
        portRefs[kp + ElkSignalKey.InverterIn(inv.OutputSignal)] =
            new ElkPortRef(nodeId, inPortId, ElkPortRole.InverterIn, inv.Width);

        string outPortId = $"{nodeId}.out";
        node.Ports!.Add(new ElkPort
        {
            Id = outPortId,
            LayoutOptions = PortLayout(PortSideEast, 0),
            Labels = [new ElkLabel { Text = "Y" }]
        });
        portRefs[kp + ElkSignalKey.InverterOut(inv.OutputSignal)] =
            new ElkPortRef(nodeId, outPortId, ElkPortRole.InverterOut, inv.Width);

        target.Add(node);
    }

    private static void AddGateNode(
        IList<ElkNode> target,
        GatePrimitive gate,
        Dictionary<string, ElkPortRef> portRefs,
        string? nodeIdOverride = null,
        string? portRefKeyPrefix = null)
    {
        string nodeId = nodeIdOverride ?? ElkNodeIds.ForGate(gate.OutputSignal);
        string kp = portRefKeyPrefix ?? string.Empty;
        int inputCount = gate.InputSignals.Count;
        double height = Math.Max(40, 16 + inputCount * 14);

        ElkNode node = new()
        {
            Id = nodeId,
            Width = 56,
            Height = height,
            LayoutOptions = FixedOrderPortConstraints(),
            // Label includes gate kind so the renderer can pick the right body shape
            Labels = [new ElkLabel { Text = $"{gate.Kind} {gate.OutputSignal}" }],
            Ports = []
        };

        for (int i = 0; i < inputCount; i++)
        {
            string portId = $"{nodeId}.in.{i}";
            string label = inputCount switch
            {
                1 => "A",
                2 => i == 0 ? "A" : "B",
                _ => $"I{i}"
            };
            if (IsUnresolvedSignal(gate.InputSignals[i]))
                label += "·X";
            node.Ports!.Add(new ElkPort
            {
                Id = portId,
                LayoutOptions = PortLayout(PortSideWest, i),
                Labels = [new ElkLabel { Text = label }]
            });
            portRefs[kp + ElkSignalKey.GateInput(gate.OutputSignal, i)] =
                new ElkPortRef(nodeId, portId, ElkPortRole.GateInput, gate.Width);
        }

        string outPortId = $"{nodeId}.out";
        node.Ports!.Add(new ElkPort
        {
            Id = outPortId,
            LayoutOptions = PortLayout(PortSideEast, 0),
            Labels = [new ElkLabel { Text = "Y" }]
        });
        portRefs[kp + ElkSignalKey.GateOutput(gate.OutputSignal)] =
            new ElkPortRef(nodeId, outPortId, ElkPortRole.GateOutput, gate.Width);

        target.Add(node);
    }

    private static void AddArithNode(
        IList<ElkNode> target,
        ArithPrimitive arith,
        Dictionary<string, ElkPortRef> portRefs,
        string? nodeIdOverride = null,
        string? portRefKeyPrefix = null)
    {
        string nodeId = nodeIdOverride ?? ElkNodeIds.ForArith(arith.OutputSignal);
        string kp = portRefKeyPrefix ?? string.Empty;
        ElkNode node = new()
        {
            Id = nodeId,
            Width = 60,
            Height = 44,
            LayoutOptions = FixedOrderPortConstraints(),
            // Label includes arith kind so the renderer can paint the op symbol
            Labels = [new ElkLabel { Text = $"{arith.Kind} {arith.OutputSignal}" }],
            Ports = []
        };

        string leftId = $"{nodeId}.l";
        node.Ports!.Add(new ElkPort
        {
            Id = leftId,
            LayoutOptions = PortLayout(PortSideWest, 0),
            Labels = [new ElkLabel { Text = IsUnresolvedSignal(arith.LeftSignal) ? "A·X" : "A" }]
        });
        portRefs[kp + ElkSignalKey.ArithLeft(arith.OutputSignal)] =
            new ElkPortRef(nodeId, leftId, ElkPortRole.ArithLeft, arith.Width);

        string rightId = $"{nodeId}.r";
        node.Ports!.Add(new ElkPort
        {
            Id = rightId,
            LayoutOptions = PortLayout(PortSideWest, 1),
            Labels = [new ElkLabel { Text = IsUnresolvedSignal(arith.RightSignal) ? "B·X" : "B" }]
        });
        portRefs[kp + ElkSignalKey.ArithRight(arith.OutputSignal)] =
            new ElkPortRef(nodeId, rightId, ElkPortRole.ArithRight, arith.Width);

        string outPortId = $"{nodeId}.out";
        node.Ports!.Add(new ElkPort
        {
            Id = outPortId,
            LayoutOptions = PortLayout(PortSideEast, 0),
            Labels = [new ElkLabel { Text = "Y" }]
        });
        portRefs[kp + ElkSignalKey.ArithOutput(arith.OutputSignal)] =
            new ElkPortRef(nodeId, outPortId, ElkPortRole.ArithOutput, arith.Width);

        target.Add(node);
    }

    // ── Struct fan-out (P2-11) ───────────────────────────────────────────
    //
    // Wider on the east (output) side than the west — the inverse of the splitter
    // wedge. One west-side input port consumes the struct signal; one east-side
    // port per leg drives the consumers of that field. Each leg port's label
    // carries the field name (e.g. "ops") which the renderer paints inside the
    // wedge body.
    private static void AddStructFanOutNode(
        ElkGraph graph, StructFanOutPrimitive fanOut, Dictionary<string, ElkPortRef> portRefs)
    {
        string nodeId = ElkNodeIds.ForStructFanOut(fanOut.StructSignal);
        double height = Math.Max(60, 24 + fanOut.Legs.Count * 18);

        ElkNode node = new()
        {
            Id = nodeId,
            Width = 96,
            Height = height,
            LayoutOptions = FixedOrderPortConstraints(),
            Labels = [new ElkLabel { Text = $"{fanOut.StructSignal} ({fanOut.StructTypeName})" }],
            Ports = []
        };

        // West-side: single input port consuming the entire struct
        string inPortId = $"{nodeId}.in";
        node.Ports!.Add(new ElkPort
        {
            Id = inPortId,
            LayoutOptions = PortLayout(PortSideWest, 0),
            Labels = [new ElkLabel { Text = fanOut.StructSignal }]
        });
        portRefs[ElkSignalKey.StructFanOutInput(fanOut.StructSignal)] =
            new ElkPortRef(nodeId, inPortId, ElkPortRole.StructFanOutInput, fanOut.StructWidth);

        // East-side: one labelled port per consumed field
        for (int i = 0; i < fanOut.Legs.Count; i++)
        {
            StructFanOutLeg leg = fanOut.Legs[i];
            string legPortId = $"{nodeId}.leg.{i}";
            string legLabel = leg.Range.Width == 1 ? leg.FieldName : $"{leg.FieldName}[{leg.Range.Hi}:{leg.Range.Lo}]";
            node.Ports!.Add(new ElkPort
            {
                Id = legPortId,
                LayoutOptions = PortLayout(PortSideEast, i),
                Labels = [new ElkLabel { Text = legLabel }]
            });
            portRefs[ElkSignalKey.StructFanOutLeg(fanOut.StructSignal, leg.FieldName)] =
                new ElkPortRef(nodeId, legPortId, ElkPortRole.StructFanOutLeg, leg.Range.Width);
        }

        graph.Children.Add(node);
    }

    private static void AddMemoryNode(
        IList<ElkNode> target,
        MemoryPrimitive mem,
        string? nodeIdOverride = null)
    {
        // Memory is a tile node (no edges yet — array access plumbing comes later).
        string nodeId = nodeIdOverride ?? ElkNodeIds.ForMemory(mem.SignalName);
        double height = Math.Min(120, Math.Max(48, 24 + mem.Depth * 2));

        ElkNode node = new()
        {
            Id = nodeId,
            Width = 96,
            Height = height,
            LayoutOptions = FixedOrderPortConstraints(),
            Labels = [new ElkLabel { Text = $"MEM {mem.SignalName} [{mem.DepthHi}:{mem.DepthLo}]×{mem.CellWidth}" }],
            Ports = []
        };

        target.Add(node);
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
        IReadOnlySet<string>? expandedPaths,
        IReadOnlyDictionary<string, IReadOnlyList<SchematicPrimitive>>? primitivesByModule = null)
    {
        ElkNode node = BuildChildNode(child, portRefs, expandedPaths, primitivesByModule);
        graph.Children.Add(node);
    }

    private static ElkNode BuildChildNode(
        HierarchyScopeInstanceViewModel child,
        Dictionary<string, ElkPortRef> portRefs,
        IReadOnlySet<string>? expandedPaths,
        IReadOnlyDictionary<string, IReadOnlyList<SchematicPrimitive>>? primitivesByModule = null)
    {
        HierarchyScopeInstancePortConnectionViewModel[] inputs = child.PortConnections.Where(c => c.IsInput).ToArray();
        HierarchyScopeInstancePortConnectionViewModel[] outputs = child.PortConnections.Where(c => c.IsOutput).ToArray();
        int portRows = Math.Max(inputs.Length, outputs.Length);

        // P2-8: a compound is expandable when it has either sub-instances OR primitives
        // in its module. The latter lets leaf modules (FF + combinational logic, no
        // sub-instances) reveal their interior when the user clicks expand.
        bool hasInnerPrimitives = primitivesByModule is not null
            && primitivesByModule.TryGetValue(child.ModuleName, out var innerPrims)
            && innerPrims.Count > 0;
        bool isExpanded = expandedPaths is not null
            && (child.ChildInstances.Count > 0 || hasInnerPrimitives)
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
            Labels = BuildChildLabels(child, hasInnerPrimitives),
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
            AttachCompoundChildren(node, child, portRefs, expandedPaths, primitivesByModule);
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

    private static List<ElkLabel> BuildChildLabels(HierarchyScopeInstanceViewModel child, bool hasInnerPrimitives = false)
    {
        List<ElkLabel> labels =
        [
            new ElkLabel { Text = child.InstanceName },
            new ElkLabel { Text = child.HierarchyPath }
        ];
        if (child.ChildInstances.Count > 0 || hasInnerPrimitives)
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
        IReadOnlySet<string>? expandedPaths,
        IReadOnlyDictionary<string, IReadOnlyList<SchematicPrimitive>>? primitivesByModule = null)
    {
        parent.Children ??= [];
        foreach (HierarchyScopeInstanceViewModel grandchild in child.ChildInstances)
        {
            parent.Children.Add(BuildChildNode(grandchild, portRefs, expandedPaths, primitivesByModule));
        }

        // P2.5-5: render the compound's inner primitives (FF / Mux / Latch / Memory /
        // Buffer / Inverter / Gate / Arith) using the SAME Add{FlipFlop,...}Node
        // builders the outer scope uses, with scope-encoded node IDs (so the
        // StartsWith-based symbol dispatch in DrawElkNodesRecursive fires correctly)
        // and a "@inner::<path>" port-ref key prefix (so CollectInsidePrimitiveEndpoints
        // can resolve inner pins without colliding with outer scope keys).
        int innerCount = 0;
        if (primitivesByModule is not null
            && primitivesByModule.TryGetValue(child.ModuleName, out IReadOnlyList<SchematicPrimitive>? innerPrimitives)
            && innerPrimitives.Count > 0)
        {
            foreach (SchematicPrimitive primitive in innerPrimitives)
            {
                AddInnerPrimitiveNode(parent.Children, primitive, child.HierarchyPath, portRefs);
            }
            innerCount = innerPrimitives.Count;
        }

        parent.LayoutOptions ??= new Dictionary<string, string>();
        parent.LayoutOptions[ElkPortConstraintsKey] = PortConstraintsFixedOrder;
        parent.LayoutOptions["elk.algorithm"] = "layered";
        parent.LayoutOptions["elk.direction"] = "RIGHT";
        parent.LayoutOptions["elk.padding"] = "[top=48,left=24,right=24,bottom=20]";
        // Compound nodes need a generous min size so their children layout cleanly.
        // P2.5-5: grow with inner content so symbols don't pile on top of each other —
        // each grandchild instance is ~module-width worth of horizontal layered space,
        // each inner primitive is narrower but still needs room for its title + ports.
        int grandchildCount = child.ChildInstances.Count;
        double requiredWidth  = 320 + grandchildCount * 80 + innerCount * 40;
        double requiredHeight = 200 + Math.Max(0, innerCount - 4) * 24;
        parent.Width  = Math.Max(parent.Width,  requiredWidth);
        parent.Height = Math.Max(parent.Height, requiredHeight);
    }

    private static void AddEdges(ElkGraph graph, ElkScopeData scope, Dictionary<string, ElkPortRef> portRefs)
    {
        Dictionary<string, List<ElkPortRef>> producers = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<ElkPortRef>> consumers = new(StringComparer.OrdinalIgnoreCase);

        CollectBoundaryEndpoints(scope, portRefs, producers, consumers);
        CollectChildEndpoints(scope, portRefs, producers, consumers);
        CollectOperatorEndpoints(scope, portRefs, producers, consumers);
        CollectSplitterEndpoints(scope, portRefs, producers, consumers);
        CollectFlipFlopEndpoints(scope, portRefs, producers, consumers);
        CollectMuxEndpoints(scope, portRefs, producers, consumers);
        CollectLatchEndpoints(scope, portRefs, producers, consumers);
        CollectBufferEndpoints(scope, portRefs, producers, consumers);
        CollectInverterEndpoints(scope, portRefs, producers, consumers);
        CollectGateEndpoints(scope, portRefs, producers, consumers);
        CollectArithEndpoints(scope, portRefs, producers, consumers);
        CollectStructFanOutEndpoints(scope, portRefs, producers, consumers);
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
            bool hasInnerPrims = scope.PrimitivesByModule is not null
                && scope.PrimitivesByModule.TryGetValue(child.ModuleName, out var prims)
                && prims.Count > 0;
            if (child.ChildInstances.Count == 0 && !hasInnerPrims) continue;
            if (!scope.ExpandedPaths.Contains(child.HierarchyPath)) continue;
            CollectInsideCompound(child, portRefs, producers, consumers, scope.ExpandedPaths, scope.PrimitivesByModule);
        }
    }

    private static void CollectInsideCompound(
        HierarchyScopeInstanceViewModel compound,
        IReadOnlyDictionary<string, ElkPortRef> portRefs,
        Dictionary<string, List<ElkPortRef>> producers,
        Dictionary<string, List<ElkPortRef>> consumers,
        IReadOnlySet<string> expandedPaths,
        IReadOnlyDictionary<string, IReadOnlyList<SchematicPrimitive>>? primitivesByModule = null)
    {
        CollectCompoundBoundaryEndpoints(compound, portRefs, producers, consumers);

        // P2-8b: wire inner primitive pins to boundary signals via @inner:: namespace.
        if (primitivesByModule is not null
            && primitivesByModule.TryGetValue(compound.ModuleName, out IReadOnlyList<SchematicPrimitive>? innerPrimitives)
            && innerPrimitives.Count > 0)
        {
            CollectInsidePrimitiveEndpoints(compound.HierarchyPath, innerPrimitives, portRefs, producers, consumers);
        }

        foreach (HierarchyScopeInstanceViewModel grandchild in compound.ChildInstances)
        {
            CollectGrandchildEndpoints(compound.HierarchyPath, grandchild, portRefs, producers, consumers);
            bool gcHasInnerPrims = primitivesByModule is not null
                && primitivesByModule.TryGetValue(grandchild.ModuleName, out var gcPrims)
                && gcPrims.Count > 0;
            if (expandedPaths.Contains(grandchild.HierarchyPath)
                && (grandchild.ChildInstances.Count > 0 || gcHasInnerPrims))
            {
                CollectInsideCompound(grandchild, portRefs, producers, consumers, expandedPaths, primitivesByModule);
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

    // P2-8b: for each inner primitive inside an expanded compound, register its ports
    // as producers/consumers in the compound's @inner:: signal namespace so EmitEdges
    // can wire them to the compound's boundary inputs/outputs.
    private static void CollectInsidePrimitiveEndpoints(
        string compoundPath,
        IReadOnlyList<SchematicPrimitive> innerPrimitives,
        IReadOnlyDictionary<string, ElkPortRef> portRefs,
        Dictionary<string, List<ElkPortRef>> producers,
        Dictionary<string, List<ElkPortRef>> consumers)
    {
        string keyPrefix = "@inner::" + compoundPath;

        bool TryRef(string portRefKey, out ElkPortRef r)
        {
            bool ok = portRefs.TryGetValue(keyPrefix + portRefKey, out ElkPortRef? v);
            r = v!;
            return ok;
        }

        foreach (SchematicPrimitive primitive in innerPrimitives)
        {
            switch (primitive)
            {
                case FlipFlopPrimitive ff:
                    if (TryRef(ElkSignalKey.FlipFlopQ(ff.QSignal), out var ffQ))
                        AddTo(producers, ScopedSignalKey(compoundPath, ff.QSignal), ffQ);
                    if (TryRef(ElkSignalKey.FlipFlopD(ff.QSignal), out var ffD))
                        AddTo(consumers, ScopedSignalKey(compoundPath, ff.DSignal), ffD);
                    if (TryRef(ElkSignalKey.FlipFlopClock(ff.QSignal), out var ffClk))
                        AddTo(consumers, ScopedSignalKey(compoundPath, ff.ClockSignal), ffClk);
                    if (!string.IsNullOrEmpty(ff.AsyncResetSignal)
                        && TryRef(ElkSignalKey.FlipFlopReset(ff.QSignal), out var ffRst))
                        AddTo(consumers, ScopedSignalKey(compoundPath, ff.AsyncResetSignal!), ffRst);
                    break;
                case LatchPrimitive lt:
                    if (TryRef(ElkSignalKey.LatchQ(lt.QSignal), out var ltQ))
                        AddTo(producers, ScopedSignalKey(compoundPath, lt.QSignal), ltQ);
                    if (TryRef(ElkSignalKey.LatchD(lt.QSignal), out var ltD))
                        AddTo(consumers, ScopedSignalKey(compoundPath, lt.DSignal), ltD);
                    if (TryRef(ElkSignalKey.LatchGate(lt.QSignal), out var ltG))
                        AddTo(consumers, ScopedSignalKey(compoundPath, lt.GateSignal), ltG);
                    break;
                case MuxPrimitive mux:
                    if (TryRef(ElkSignalKey.MuxOutput(mux.OutputSignal), out var muxOut))
                        AddTo(producers, ScopedSignalKey(compoundPath, mux.OutputSignal), muxOut);
                    for (int i = 0; i < mux.Inputs.Count; i++)
                        if (mux.Inputs[i].Source is MuxSignalSource sig
                            && TryRef(ElkSignalKey.MuxInput(mux.OutputSignal, i), out var muxIn))
                            AddTo(consumers, ScopedSignalKey(compoundPath, sig.SignalName), muxIn);
                    for (int i = 0; i < mux.SelectSignals.Count; i++)
                        if (TryRef(ElkSignalKey.MuxSelect(mux.OutputSignal, i), out var muxSel))
                            AddTo(consumers, ScopedSignalKey(compoundPath, mux.SelectSignals[i]), muxSel);
                    break;
                case BufferPrimitive buf:
                    if (TryRef(ElkSignalKey.BufferOut(buf.OutputSignal), out var bufOut))
                        AddTo(producers, ScopedSignalKey(compoundPath, buf.OutputSignal), bufOut);
                    if (TryRef(ElkSignalKey.BufferIn(buf.OutputSignal), out var bufIn))
                        AddTo(consumers, ScopedSignalKey(compoundPath, buf.InputSignal), bufIn);
                    break;
                case InverterPrimitive inv:
                    if (TryRef(ElkSignalKey.InverterOut(inv.OutputSignal), out var invOut))
                        AddTo(producers, ScopedSignalKey(compoundPath, inv.OutputSignal), invOut);
                    if (TryRef(ElkSignalKey.InverterIn(inv.OutputSignal), out var invIn))
                        AddTo(consumers, ScopedSignalKey(compoundPath, inv.InputSignal), invIn);
                    break;
                case GatePrimitive gate:
                    if (TryRef(ElkSignalKey.GateOutput(gate.OutputSignal), out var gateOut))
                        AddTo(producers, ScopedSignalKey(compoundPath, gate.OutputSignal), gateOut);
                    for (int i = 0; i < gate.InputSignals.Count; i++)
                        if (TryRef(ElkSignalKey.GateInput(gate.OutputSignal, i), out var gateIn))
                            AddTo(consumers, ScopedSignalKey(compoundPath, gate.InputSignals[i]), gateIn);
                    break;
                case ArithPrimitive arith:
                    if (TryRef(ElkSignalKey.ArithOutput(arith.OutputSignal), out var arithOut))
                        AddTo(producers, ScopedSignalKey(compoundPath, arith.OutputSignal), arithOut);
                    if (TryRef(ElkSignalKey.ArithLeft(arith.OutputSignal), out var arithL))
                        AddTo(consumers, ScopedSignalKey(compoundPath, arith.LeftSignal), arithL);
                    if (TryRef(ElkSignalKey.ArithRight(arith.OutputSignal), out var arithR))
                        AddTo(consumers, ScopedSignalKey(compoundPath, arith.RightSignal), arithR);
                    break;
            }
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

    private static void CollectFlipFlopEndpoints(
        ElkScopeData scope,
        IReadOnlyDictionary<string, ElkPortRef> portRefs,
        Dictionary<string, List<ElkPortRef>> producers,
        Dictionary<string, List<ElkPortRef>> consumers)
    {
        if (scope.Primitives is null) return;

        foreach (FlipFlopPrimitive ff in scope.Primitives.OfType<FlipFlopPrimitive>())
        {
            // Q output is a producer of the QSignal
            if (portRefs.TryGetValue(ElkSignalKey.FlipFlopQ(ff.QSignal), out ElkPortRef? qRef))
            {
                AddTo(producers, ff.QSignal, qRef);
            }

            // D input consumes the DSignal
            if (portRefs.TryGetValue(ElkSignalKey.FlipFlopD(ff.QSignal), out ElkPortRef? dRef))
            {
                AddTo(consumers, ff.DSignal, dRef);
            }

            // Clk input consumes the clock signal
            if (portRefs.TryGetValue(ElkSignalKey.FlipFlopClock(ff.QSignal), out ElkPortRef? clkRef))
            {
                AddTo(consumers, ff.ClockSignal, clkRef);
            }

            // Async reset (optional) consumes the reset signal
            if (!string.IsNullOrEmpty(ff.AsyncResetSignal) &&
                portRefs.TryGetValue(ElkSignalKey.FlipFlopReset(ff.QSignal), out ElkPortRef? rstRef))
            {
                AddTo(consumers, ff.AsyncResetSignal, rstRef);
            }
        }
    }

    private static void CollectMuxEndpoints(
        ElkScopeData scope,
        IReadOnlyDictionary<string, ElkPortRef> portRefs,
        Dictionary<string, List<ElkPortRef>> producers,
        Dictionary<string, List<ElkPortRef>> consumers)
    {
        if (scope.Primitives is null) return;

        foreach (MuxPrimitive mux in scope.Primitives.OfType<MuxPrimitive>())
        {
            // Output produces the output signal
            if (portRefs.TryGetValue(ElkSignalKey.MuxOutput(mux.OutputSignal), out ElkPortRef? outRef))
            {
                AddTo(producers, mux.OutputSignal, outRef);
            }

            // Each data input consumes its source signal (skip constants — they have no producer)
            for (int i = 0; i < mux.Inputs.Count; i++)
            {
                if (mux.Inputs[i].Source is MuxSignalSource sig &&
                    portRefs.TryGetValue(ElkSignalKey.MuxInput(mux.OutputSignal, i), out ElkPortRef? inRef))
                {
                    AddTo(consumers, sig.SignalName, inRef);
                }
            }

            // Each selector consumes its selector signal
            for (int i = 0; i < mux.SelectSignals.Count; i++)
            {
                if (portRefs.TryGetValue(ElkSignalKey.MuxSelect(mux.OutputSignal, i), out ElkPortRef? selRef))
                {
                    AddTo(consumers, mux.SelectSignals[i], selRef);
                }
            }
        }
    }

    private static void CollectLatchEndpoints(
        ElkScopeData scope,
        IReadOnlyDictionary<string, ElkPortRef> portRefs,
        Dictionary<string, List<ElkPortRef>> producers,
        Dictionary<string, List<ElkPortRef>> consumers)
    {
        if (scope.Primitives is null) return;

        foreach (LatchPrimitive latch in scope.Primitives.OfType<LatchPrimitive>())
        {
            if (portRefs.TryGetValue(ElkSignalKey.LatchQ(latch.QSignal), out ElkPortRef? qRef))
            {
                AddTo(producers, latch.QSignal, qRef);
            }
            if (portRefs.TryGetValue(ElkSignalKey.LatchD(latch.QSignal), out ElkPortRef? dRef))
            {
                AddTo(consumers, latch.DSignal, dRef);
            }
            if (portRefs.TryGetValue(ElkSignalKey.LatchGate(latch.QSignal), out ElkPortRef? gRef))
            {
                AddTo(consumers, latch.GateSignal, gRef);
            }
        }
    }

    private static void CollectBufferEndpoints(
        ElkScopeData scope,
        IReadOnlyDictionary<string, ElkPortRef> portRefs,
        Dictionary<string, List<ElkPortRef>> producers,
        Dictionary<string, List<ElkPortRef>> consumers)
    {
        if (scope.Primitives is null) return;
        foreach (BufferPrimitive buf in scope.Primitives.OfType<BufferPrimitive>())
        {
            if (portRefs.TryGetValue(ElkSignalKey.BufferOut(buf.OutputSignal), out ElkPortRef? outRef))
                AddTo(producers, buf.OutputSignal, outRef);
            if (portRefs.TryGetValue(ElkSignalKey.BufferIn(buf.OutputSignal), out ElkPortRef? inRef))
                AddTo(consumers, buf.InputSignal, inRef);
        }
    }

    private static void CollectInverterEndpoints(
        ElkScopeData scope,
        IReadOnlyDictionary<string, ElkPortRef> portRefs,
        Dictionary<string, List<ElkPortRef>> producers,
        Dictionary<string, List<ElkPortRef>> consumers)
    {
        if (scope.Primitives is null) return;
        foreach (InverterPrimitive inv in scope.Primitives.OfType<InverterPrimitive>())
        {
            if (portRefs.TryGetValue(ElkSignalKey.InverterOut(inv.OutputSignal), out ElkPortRef? outRef))
                AddTo(producers, inv.OutputSignal, outRef);
            if (portRefs.TryGetValue(ElkSignalKey.InverterIn(inv.OutputSignal), out ElkPortRef? inRef))
                AddTo(consumers, inv.InputSignal, inRef);
        }
    }

    private static void CollectGateEndpoints(
        ElkScopeData scope,
        IReadOnlyDictionary<string, ElkPortRef> portRefs,
        Dictionary<string, List<ElkPortRef>> producers,
        Dictionary<string, List<ElkPortRef>> consumers)
    {
        if (scope.Primitives is null) return;
        foreach (GatePrimitive gate in scope.Primitives.OfType<GatePrimitive>())
        {
            if (portRefs.TryGetValue(ElkSignalKey.GateOutput(gate.OutputSignal), out ElkPortRef? outRef))
                AddTo(producers, gate.OutputSignal, outRef);
            for (int i = 0; i < gate.InputSignals.Count; i++)
            {
                if (portRefs.TryGetValue(ElkSignalKey.GateInput(gate.OutputSignal, i), out ElkPortRef? inRef))
                    AddTo(consumers, gate.InputSignals[i], inRef);
            }
        }
    }

    private static void CollectArithEndpoints(
        ElkScopeData scope,
        IReadOnlyDictionary<string, ElkPortRef> portRefs,
        Dictionary<string, List<ElkPortRef>> producers,
        Dictionary<string, List<ElkPortRef>> consumers)
    {
        if (scope.Primitives is null) return;
        foreach (ArithPrimitive arith in scope.Primitives.OfType<ArithPrimitive>())
        {
            if (portRefs.TryGetValue(ElkSignalKey.ArithOutput(arith.OutputSignal), out ElkPortRef? outRef))
                AddTo(producers, arith.OutputSignal, outRef);
            if (portRefs.TryGetValue(ElkSignalKey.ArithLeft(arith.OutputSignal), out ElkPortRef? lRef))
                AddTo(consumers, arith.LeftSignal, lRef);
            if (portRefs.TryGetValue(ElkSignalKey.ArithRight(arith.OutputSignal), out ElkPortRef? rRef))
                AddTo(consumers, arith.RightSignal, rRef);
        }
    }

    // P2-11: wire the fan-out node's input port to the struct's producer (boundary
    // pin or upstream instance output), and each leg port to its declared consumers.
    // The leg → consumer hop bypasses the per-consumer contassign edges the decoder
    // already suppressed in this scope, so the visual result is one wedge instead
    // of N overlapping wires.
    private static void CollectStructFanOutEndpoints(
        ElkScopeData scope,
        IReadOnlyDictionary<string, ElkPortRef> portRefs,
        Dictionary<string, List<ElkPortRef>> producers,
        Dictionary<string, List<ElkPortRef>> consumers)
    {
        if (scope.Primitives is null) return;
        foreach (StructFanOutPrimitive fanOut in scope.Primitives.OfType<StructFanOutPrimitive>())
        {
            RegisterFanOutInput(fanOut, portRefs, consumers);
            foreach (StructFanOutLeg leg in fanOut.Legs)
                RegisterFanOutLeg(fanOut, leg, scope, portRefs, producers, consumers);
        }
    }

    private static void RegisterFanOutInput(
        StructFanOutPrimitive fanOut,
        IReadOnlyDictionary<string, ElkPortRef> portRefs,
        Dictionary<string, List<ElkPortRef>> consumers)
    {
        // The single west input port consumes the struct signal as a whole.
        if (portRefs.TryGetValue(ElkSignalKey.StructFanOutInput(fanOut.StructSignal), out ElkPortRef? inRef))
            AddTo(consumers, fanOut.StructSignal, inRef);
    }

    // Each east leg produces a synthetic "fanout::struct.field" signal name; consumers
    // are re-routed to that synthetic key so EmitEdges draws leg → consumer instead of
    // boundary_in.struct → consumer (the visual that previously created the fat overlap).
    //
    // For each consumer there are three cases:
    //  (a) "instanceName.portName" → look up the ChildInput port ref and consume.
    //  (b) plain signal name with an existing portRef → consume directly.
    //  (c) bare local sink (no portRef) → treat the leg port as the producer of the
    //      ordinary signal name so downstream contassign expansion picks it up.
    //
    // Cases (a) and (b) also UN-register the same pin/ref from the struct's consumer
    // list — otherwise EmitEdges would draw two edges to the same pin (one from the
    // fan-out leg, one straight from the unsliced boundary struct producer).
    private static void RegisterFanOutLeg(
        StructFanOutPrimitive fanOut,
        StructFanOutLeg leg,
        ElkScopeData scope,
        IReadOnlyDictionary<string, ElkPortRef> portRefs,
        Dictionary<string, List<ElkPortRef>> producers,
        Dictionary<string, List<ElkPortRef>> consumers)
    {
        if (!portRefs.TryGetValue(
                ElkSignalKey.StructFanOutLeg(fanOut.StructSignal, leg.FieldName),
                out ElkPortRef? legRef)) return;

        string syntheticName = FanOutLegSignalKey(fanOut.StructSignal, leg.FieldName);
        AddTo(producers, syntheticName, legRef);

        foreach (string consumer in leg.Consumers)
        {
            if (TryResolveInstancePin(consumer, scope, portRefs, out ElkPortRef? pinRef) && pinRef is not null)
            {
                AddTo(consumers, syntheticName, pinRef);
                RemoveFrom(consumers, fanOut.StructSignal, pinRef);
            }
            else if (portRefs.TryGetValue(consumer, out ElkPortRef? plainRef) && plainRef is not null)
            {
                AddTo(consumers, syntheticName, plainRef);
                RemoveFrom(consumers, fanOut.StructSignal, plainRef);
            }
            else
            {
                AddTo(producers, consumer, legRef);
            }
        }
    }

    private static void RemoveFrom(Dictionary<string, List<ElkPortRef>> map, string key, ElkPortRef value)
    {
        if (map.TryGetValue(key, out List<ElkPortRef>? list))
            list.Remove(value);
    }

    private static string FanOutLegSignalKey(string structSignal, string fieldName)
        => $"::fanout::{structSignal}.{fieldName}";

    // Splits "instanceName.portName" and looks up the matching ChildInput port ref.
    // Returns false (and null) for plain signal names.
    private static bool TryResolveInstancePin(
        string consumer,
        ElkScopeData scope,
        IReadOnlyDictionary<string, ElkPortRef> portRefs,
        out ElkPortRef? pinRef)
    {
        pinRef = null;
        int dotIndex = consumer.IndexOf('.', StringComparison.Ordinal);
        if (dotIndex <= 0) return false;
        string instanceName = consumer[..dotIndex];
        string portName = consumer[(dotIndex + 1)..];

        // Resolve hierarchy path from the scope's child list (instance name → child)
        HierarchyScopeInstanceViewModel? child = scope.ChildScopes
            .FirstOrDefault(c => string.Equals(c.InstanceName, instanceName, StringComparison.OrdinalIgnoreCase));
        if (child is null) return false;

        return portRefs.TryGetValue(ElkSignalKey.ChildInput(child.HierarchyPath, portName), out pinRef);
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
                    string displayLabel = PrettifySignalLabel(signal);
                    // labels[0] = signal name (rendered + selection key)
                    // labels[1] = bit width metadata (Logisim colour selection)
                    graph.Edges.Add(new ElkEdge
                    {
                        Id = $"e{edgeCounter++}",
                        Sources = [source.PortId],
                        Targets = [target.PortId],
                        Labels = [new ElkLabel { Text = displayLabel }, new ElkLabel { Text = width.ToString(CultureInfo.InvariantCulture) }]
                    });
                }
            }
        }
    }

    private static void PruneOrphanPrimitives(ElkGraph graph)
    {
        HashSet<string> connectedPorts = graph.Edges
            .SelectMany(static e => e.Sources.Concat(e.Targets))
            .ToHashSet(StringComparer.Ordinal);

        while (true)
        {
            HashSet<string> removedPorts = [];
            PruneOrphanPrimitives(graph.Children, connectedPorts, removedPorts);
            if (removedPorts.Count == 0) break;

            graph.Edges.RemoveAll(e => e.Sources.Any(removedPorts.Contains) || e.Targets.Any(removedPorts.Contains));
            connectedPorts = graph.Edges
                .SelectMany(static e => e.Sources.Concat(e.Targets))
                .ToHashSet(StringComparer.Ordinal);
        }
    }

    private static void PruneOrphanPrimitives(List<ElkNode> nodes, HashSet<string> connectedPorts, HashSet<string> removedPorts)
    {
        foreach (ElkNode node in nodes)
        {
            if (node.Children is { Count: > 0 })
                PruneOrphanPrimitives(node.Children, connectedPorts, removedPorts);
        }

        nodes.RemoveAll(node =>
        {
            if (!IsPrunablePrimitive(node)
                || node.Ports is not { Count: > 0 } ports
                || ports.Any(p => connectedPorts.Contains(p.Id)))
            {
                return false;
            }

            foreach (ElkPort port in ports)
                removedPorts.Add(port.Id);
            return true;
        });
    }

    private static bool IsPrunablePrimitive(ElkNode node) =>
        ElkNodeIds.IsOperator(node.Id)
        || ElkNodeIds.IsGate(node.Id)
        || ElkNodeIds.IsArith(node.Id)
        || ElkNodeIds.IsFlipFlop(node.Id);

    // Strips internal namespace prefixes from synthetic signal keys (e.g. "::fanout::ctrl.ops"
    // becomes "ctrl.ops") so the user sees a clean wire label instead of plumbing details.
    private static string PrettifySignalLabel(string signal)
    {
        const string fanOutPrefix = "::fanout::";
        if (signal.StartsWith(fanOutPrefix, StringComparison.Ordinal))
            return signal[fanOutPrefix.Length..];
        const string innerPrefix = "@inner::";
        if (signal.StartsWith(innerPrefix, StringComparison.Ordinal))
        {
            // "@inner::top.compound::sig" → "sig"
            int lastColon = signal.LastIndexOf("::", StringComparison.Ordinal);
            if (lastColon > 0 && lastColon + 2 < signal.Length)
                return signal[(lastColon + 2)..];
        }
        return signal;
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
    IReadOnlySet<string>? ExpandedPaths = null,
    IReadOnlyList<SchematicPrimitive>? Primitives = null,
    // P2-8: when expanding a compound child, look up its module's primitives here
    // so the compound's interior renders FF/Mux/Latch/Buffer/etc. nodes alongside
    // its sub-instances. Keyed by module name (case-insensitive). Null/missing
    // entries mean "no inner primitives to render", which keeps the existing
    // sub-instance-only expansion working unchanged.
    IReadOnlyDictionary<string, IReadOnlyList<SchematicPrimitive>>? PrimitivesByModule = null);

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
    JoinerOutput,
    FlipFlopD,
    FlipFlopClock,
    FlipFlopReset,
    FlipFlopQ,
    MuxInput,
    MuxSelect,
    MuxOutput,
    LatchD,
    LatchGate,
    LatchQ,
    MemoryNode,
    BufferIn,
    BufferOut,
    InverterIn,
    InverterOut,
    GateInput,
    GateOutput,
    ArithLeft,
    ArithRight,
    ArithOutput,
    StructFanOutInput,
    StructFanOutLeg
}

internal static class ElkNodeIds
{
    public const string BoundaryIn = "boundary_in";
    public const string BoundaryOut = "boundary_out";

    public static string ForChild(string hierarchyPath) => "child_" + SanitizeId(hierarchyPath);

    public static string ForOperator(string targetName) => "op_" + SanitizeId(targetName);
    public static string ForSplitter(string sourceName) => "split_" + SanitizeId(sourceName);
    public static string ForJoiner(string targetName) => "join_" + SanitizeId(targetName);
    public static string ForFlipFlop(string qSignal) => "ff_" + SanitizeId(qSignal);
    public static string ForMux(string outputSignal) => "mux_" + SanitizeId(outputSignal);
    public static string ForLatch(string qSignal) => "latch_" + SanitizeId(qSignal);
    public static string ForMemory(string signalName) => "mem_" + SanitizeId(signalName);
    public static string ForBuffer(string outputSignal) => "buf_" + SanitizeId(outputSignal);
    public static string ForInverter(string outputSignal) => "inv_" + SanitizeId(outputSignal);
    public static string ForGate(string outputSignal) => "gate_" + SanitizeId(outputSignal);
    public static string ForArith(string outputSignal) => "arith_" + SanitizeId(outputSignal);

    // P2.5-5: inner-primitive IDs keep the type prefix at the START so the renderer's
    // IsFlipFlop/IsMux/... discriminators (StartsWith) dispatch to the proper symbol
    // drawer. Scope is encoded as a suffix separated by `__` to avoid colliding with
    // outer-scope primitives that share the same signal name.
    public static string ForInnerFlipFlop(string scopePath, string qSignal)        => "ff_"    + SanitizeId(scopePath) + "__" + SanitizeId(qSignal);
    public static string ForInnerMux(string scopePath, string outputSignal)        => "mux_"   + SanitizeId(scopePath) + "__" + SanitizeId(outputSignal);
    public static string ForInnerLatch(string scopePath, string qSignal)           => "latch_" + SanitizeId(scopePath) + "__" + SanitizeId(qSignal);
    public static string ForInnerMemory(string scopePath, string signalName)       => "mem_"   + SanitizeId(scopePath) + "__" + SanitizeId(signalName);
    public static string ForInnerBuffer(string scopePath, string outputSignal)     => "buf_"   + SanitizeId(scopePath) + "__" + SanitizeId(outputSignal);
    public static string ForInnerInverter(string scopePath, string outputSignal)   => "inv_"   + SanitizeId(scopePath) + "__" + SanitizeId(outputSignal);
    public static string ForInnerGate(string scopePath, string outputSignal)       => "gate_"  + SanitizeId(scopePath) + "__" + SanitizeId(outputSignal);
    public static string ForInnerArith(string scopePath, string outputSignal)      => "arith_" + SanitizeId(scopePath) + "__" + SanitizeId(outputSignal);

    public static bool IsOperator(string? nodeId) =>
        nodeId is not null && nodeId.StartsWith("op_", StringComparison.Ordinal);

    public static bool IsSplitter(string? nodeId) =>
        nodeId is not null && nodeId.StartsWith("split_", StringComparison.Ordinal);

    public static bool IsJoiner(string? nodeId) =>
        nodeId is not null && nodeId.StartsWith("join_", StringComparison.Ordinal);

    public static bool IsFlipFlop(string? nodeId) =>
        nodeId is not null && nodeId.StartsWith("ff_", StringComparison.Ordinal);

    public static bool IsMux(string? nodeId) =>
        nodeId is not null && nodeId.StartsWith("mux_", StringComparison.Ordinal);

    public static bool IsLatch(string? nodeId) =>
        nodeId is not null && nodeId.StartsWith("latch_", StringComparison.Ordinal);

    public static bool IsMemory(string? nodeId) =>
        nodeId is not null && nodeId.StartsWith("mem_", StringComparison.Ordinal);

    public static bool IsBuffer(string? nodeId) =>
        nodeId is not null && nodeId.StartsWith("buf_", StringComparison.Ordinal);

    public static bool IsInverter(string? nodeId) =>
        nodeId is not null && nodeId.StartsWith("inv_", StringComparison.Ordinal);

    public static bool IsGate(string? nodeId) =>
        nodeId is not null && nodeId.StartsWith("gate_", StringComparison.Ordinal);

    public static bool IsArith(string? nodeId) =>
        nodeId is not null && nodeId.StartsWith("arith_", StringComparison.Ordinal);

    public static string ForStructFanOut(string structSignal) => "fanout_" + SanitizeId(structSignal);

    public static bool IsStructFanOut(string? nodeId) =>
        nodeId is not null && nodeId.StartsWith("fanout_", StringComparison.Ordinal);

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
    public static string FlipFlopD(string qSignal) => $"::ff_d::{qSignal}";
    public static string FlipFlopClock(string qSignal) => $"::ff_clk::{qSignal}";
    public static string FlipFlopReset(string qSignal) => $"::ff_rst::{qSignal}";
    public static string FlipFlopQ(string qSignal) => $"::ff_q::{qSignal}";
    public static string MuxInput(string outputSignal, int index) => $"::mux_in::{outputSignal}::{index}";
    public static string MuxSelect(string outputSignal, int index) => $"::mux_sel::{outputSignal}::{index}";
    public static string MuxOutput(string outputSignal) => $"::mux_out::{outputSignal}";
    public static string LatchD(string qSignal) => $"::latch_d::{qSignal}";
    public static string LatchGate(string qSignal) => $"::latch_g::{qSignal}";
    public static string LatchQ(string qSignal) => $"::latch_q::{qSignal}";
    public static string BufferIn(string output) => $"::buf_in::{output}";
    public static string BufferOut(string output) => $"::buf_out::{output}";
    public static string InverterIn(string output) => $"::inv_in::{output}";
    public static string InverterOut(string output) => $"::inv_out::{output}";
    public static string GateInput(string output, int index) => $"::gate_in::{output}::{index}";
    public static string GateOutput(string output) => $"::gate_out::{output}";
    public static string ArithLeft(string output) => $"::arith_l::{output}";
    public static string ArithRight(string output) => $"::arith_r::{output}";
    public static string ArithOutput(string output) => $"::arith_out::{output}";
    public static string StructFanOutInput(string structSignal) => $"::fanout_in::{structSignal}";
    public static string StructFanOutLeg(string structSignal, string fieldName) => $"::fanout_leg::{structSignal}::{fieldName}";
}
