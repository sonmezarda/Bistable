using Bistable.Core.Design.Schematic;
using Bistable.Core.Synthesis;

namespace Bistable.Yosys;

/// <summary>
/// Phase 6 P6-5: maps Yosys generic cell types to the renderer's existing
/// symbol families + structural metadata (which pins are inputs, which is
/// the output, whether the symbol carries a bubble). The graph builder uses
/// this to decide what symbol to draw for each cell and how to wire its pins
/// into the ELK graph.
///
/// The mapping is intentionally explicit — every cell type the renderer
/// supports has a row here, and unknown cells become an "unknown" descriptor
/// so the renderer can still place them as a generic block instead of
/// silently dropping them.
/// </summary>
public static class GateCellLibrary
{
    /// <summary>Look up a cell by its Yosys type string (e.g. "$_AND_").</summary>
    public static GateCellDescriptor Lookup(string cellType) =>
        s_descriptors.TryGetValue(cellType, out GateCellDescriptor? d)
            ? d
            : GateCellDescriptor.Unknown(cellType);

    public static bool IsKnown(string cellType) => s_descriptors.ContainsKey(cellType);

    private static readonly Dictionary<string, GateCellDescriptor> s_descriptors =
        new(StringComparer.Ordinal)
        {
            // Combinational logic — generic single-bit gates.
            ["$_AND_"]  = Gate("$_AND_",  GateKind.And,  ["A", "B"], "Y"),
            ["$_OR_"]   = Gate("$_OR_",   GateKind.Or,   ["A", "B"], "Y"),
            ["$_XOR_"]  = Gate("$_XOR_",  GateKind.Xor,  ["A", "B"], "Y"),
            ["$_NAND_"] = Gate("$_NAND_", GateKind.Nand, ["A", "B"], "Y"),
            ["$_NOR_"]  = Gate("$_NOR_",  GateKind.Nor,  ["A", "B"], "Y"),
            ["$_XNOR_"] = Gate("$_XNOR_", GateKind.Xnor, ["A", "B"], "Y"),

            // Inverter / buffer.
            ["$_NOT_"] = new GateCellDescriptor(
                "$_NOT_", GateCellShape.Inverter, GateKind: null,
                Inputs: ["A"], Output: "Y", ClockPin: null, EnablePin: null),
            ["$_BUF_"] = new GateCellDescriptor(
                "$_BUF_", GateCellShape.Buffer, GateKind: null,
                Inputs: ["A"], Output: "Y", ClockPin: null, EnablePin: null),

            // 2:1 mux. S selects between A and B.
            ["$_MUX_"] = new GateCellDescriptor(
                "$_MUX_", GateCellShape.Mux, GateKind: null,
                Inputs: ["A", "B"], Output: "Y", ClockPin: null, EnablePin: "S"),

            // Positive- and negative-edge D flip-flops. Yosys names them
            // $_DFF_P_ (posedge) and $_DFF_N_ (negedge); we treat the edge
            // polarity as decorative — the renderer always uses the FF symbol.
            ["$_DFF_P_"] = Dff("$_DFF_P_"),
            ["$_DFF_N_"] = Dff("$_DFF_N_"),

            // Level-sensitive latches. Yosys spelling: $_DLATCH_P_ / $_DLATCH_N_.
            ["$_DLATCH_P_"] = Latch("$_DLATCH_P_"),
            ["$_DLATCH_N_"] = Latch("$_DLATCH_N_"),
        };

    private static GateCellDescriptor Gate(string type, GateKind kind, string[] inputs, string output) =>
        new(type, GateCellShape.Gate, kind, inputs, output, ClockPin: null, EnablePin: null);

    private static GateCellDescriptor Dff(string type) =>
        new(type, GateCellShape.FlipFlop, GateKind: null,
            Inputs: ["D"], Output: "Q", ClockPin: "C", EnablePin: null);

    private static GateCellDescriptor Latch(string type) =>
        new(type, GateCellShape.Latch, GateKind: null,
            Inputs: ["D"], Output: "Q", ClockPin: null, EnablePin: "E");
}

/// <summary>
/// Visual + structural recipe for one Yosys cell type. The renderer dispatches
/// on <see cref="Shape"/>; the graph builder uses <see cref="Inputs"/> /
/// <see cref="Output"/> / <see cref="ClockPin"/> / <see cref="EnablePin"/> to
/// translate Yosys's port_directions into ELK ports.
/// </summary>
public sealed record GateCellDescriptor(
    string CellType,
    GateCellShape Shape,
    GateKind? GateKind,
    IReadOnlyList<string> Inputs,
    string Output,
    string? ClockPin,
    string? EnablePin)
{
    public bool IsUnknown => Shape == GateCellShape.Unknown;

    public static GateCellDescriptor Unknown(string cellType) =>
        new(cellType, GateCellShape.Unknown, GateKind: null,
            Inputs: Array.Empty<string>(), Output: string.Empty,
            ClockPin: null, EnablePin: null);
}

public enum GateCellShape
{
    Gate,       // AND/OR/XOR family — uses GateKind for the polygon
    Inverter,   // triangle + bubble
    Buffer,     // triangle, no bubble
    Mux,        // trapezoid
    FlipFlop,   // FF box with D / C / Q
    Latch,      // latch box with D / E / Q
    Unknown,    // unrecognised cell — fall back to generic block
}
