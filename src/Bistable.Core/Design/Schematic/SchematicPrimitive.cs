using System.Text.Json.Serialization;
using Bistable.Core.Design.Ast;

namespace Bistable.Core.Design.Schematic;

/// <summary>
/// Sealed hierarchy of schematic primitives produced by <c>SchematicDecoder</c>.
/// Each primitive carries enough metadata to render a symbol and connect its pins.
/// Primitives are layout-agnostic — geometry is decided later by the layout engine.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(FlipFlopPrimitive),  "FlipFlop")]
[JsonDerivedType(typeof(LatchPrimitive),     "Latch")]
[JsonDerivedType(typeof(MuxPrimitive),       "Mux")]
[JsonDerivedType(typeof(BufferPrimitive),    "Buffer")]
[JsonDerivedType(typeof(InverterPrimitive),  "Inverter")]
[JsonDerivedType(typeof(GatePrimitive),      "Gate")]
[JsonDerivedType(typeof(ArithPrimitive),     "Arith")]
[JsonDerivedType(typeof(SplitterPrimitive),  "Splitter")]
[JsonDerivedType(typeof(JoinerPrimitive),    "Joiner")]
[JsonDerivedType(typeof(MemoryPrimitive),    "Memory")]
[JsonDerivedType(typeof(InstancePrimitive),  "Instance")]
[JsonDerivedType(typeof(PortPrimitive),      "Port")]
[JsonDerivedType(typeof(SignalPrimitive),    "Signal")]
[JsonDerivedType(typeof(StructFanOutPrimitive), "StructFanOut")]
public abstract record SchematicPrimitive(string Id);

// ── Sequential ────────────────────────────────────────────────────────────────

/// <summary>D flip-flop. Source: <see cref="SequentialBlockAst"/> with a single non-blocking assign.</summary>
public sealed record FlipFlopPrimitive(
    string Id,
    string QSignal,
    string ClockSignal,
    EdgeKind ClockEdge,
    string? AsyncResetSignal,
    EdgeKind? AsyncResetEdge,
    string DSignal,
    int Width) : SchematicPrimitive(Id);

/// <summary>Level-sensitive latch. Source: <see cref="SequentialBlockAst"/> with no edge-triggered senitem.</summary>
public sealed record LatchPrimitive(
    string Id,
    string QSignal,
    string GateSignal,
    string DSignal,
    int Width) : SchematicPrimitive(Id);

// ── Combinational ─────────────────────────────────────────────────────────────

/// <summary>
/// N-to-1 multiplexer. Source: <see cref="CondExpr"/>, possibly nested.
/// <para>
/// <see cref="SelectSignals"/> carries the BARE wire-up names used by the builder
/// to register consumer endpoints (e.g. "control_pins"). <see cref="SelectorLabels"/>,
/// when present, carries the human-readable display variant (e.g. "control_pins[3:2]")
/// that the renderer paints next to each selector port. If null, the builder falls
/// back to using <see cref="SelectSignals"/> for both wiring and display.
/// </para>
/// <para>
/// The split exists because chained ternaries on bit-selects (e.g.
/// <c>ctrl[2] ? a : ctrl[1] ? b : c</c>) need DISTINGUISHABLE selector labels
/// at render time, but their wire endpoints all converge on the same parent signal
/// "ctrl" — using the readable label as the wire-up key would leave the selector
/// ports unconnected (no producer named "ctrl[2]").
/// </para>
/// </summary>
public sealed record MuxPrimitive(
    string Id,
    string OutputSignal,
    IReadOnlyList<string> SelectSignals,   // bare wire-up name, one per condition node; MSB-first
    IReadOnlyList<MuxInput> Inputs,         // one entry per branch
    int Width,
    IReadOnlyList<string>? SelectorLabels = null   // display labels (optional, same arity as SelectSignals)
) : SchematicPrimitive(Id);

/// <summary>One input branch of a mux: its selector pattern and the signal/constant feeding it.</summary>
public sealed record MuxInput(string Label, MuxSource Source);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(MuxSignalSource),   "Signal")]
[JsonDerivedType(typeof(MuxConstantSource), "Const")]
public abstract record MuxSource;
public sealed record MuxSignalSource(string SignalName) : MuxSource;
public sealed record MuxConstantSource(string Literal, int Width) : MuxSource;

/// <summary>Wire-alias / buffer. Source: contassign with `SignalRef` RHS.</summary>
public sealed record BufferPrimitive(
    string Id,
    string OutputSignal,
    string InputSignal,
    int Width) : SchematicPrimitive(Id);

/// <summary>
/// P2.6-8: constant tie. Source: <c>assign x = 8'h00;</c> or similar where
/// the RHS is a numeric literal. Rendered as a small GND/VDD-style symbol
/// instead of a buffer with a dangling input.
/// </summary>
public sealed record ConstantTiePrimitive(
    string Id,
    string OutputSignal,
    string Literal,
    int Width) : SchematicPrimitive(Id);

/// <summary>
/// P2.6-3: tri-state buffer. Source: <c>assign bus = en ? data : 'z;</c>.
/// Rendered as a classic tri-state triangle with the enable pin entering
/// from the side.
/// </summary>
public sealed record TriStatePrimitive(
    string Id,
    string OutputSignal,
    string DataSignal,
    string EnableSignal,
    bool EnableActiveHigh,
    int Width) : SchematicPrimitive(Id);

/// <summary>Inverter. Source: contassign with `UnaryExpr(Not, …)`.</summary>
public sealed record InverterPrimitive(
    string Id,
    string OutputSignal,
    string InputSignal,
    int Width) : SchematicPrimitive(Id);

/// <summary>Logic gate (AND/OR/XOR/reduce). Source: contassign with `BinaryExpr` (And/Or/Xor) or `UnaryExpr` (reduce).</summary>
public sealed record GatePrimitive(
    string Id,
    string OutputSignal,
    GateKind Kind,
    IReadOnlyList<string> InputSignals,
    int Width) : SchematicPrimitive(Id);

public enum GateKind { And, Or, Xor, Nand, Nor, Xnor, ReduceAnd, ReduceOr, ReduceXor }

/// <summary>Arithmetic block (Add/Sub/Mul/...). Source: contassign with `BinaryExpr` arithmetic op.</summary>
public sealed record ArithPrimitive(
    string Id,
    string OutputSignal,
    ArithKind Kind,
    string LeftSignal,
    string RightSignal,
    int Width) : SchematicPrimitive(Id);

public enum ArithKind { Add, Sub, Mul, Div, Mod, ShiftLeft, ShiftRight, ShiftRightArithmetic, Equal, NotEqual, LessThan, GreaterThan, LessOrEqual, GreaterOrEqual }

/// <summary>Bit-range slice on a wider bus. Source: contassign with `BitSelectExpr` RHS.</summary>
public sealed record SplitterPrimitive(
    string Id,
    string OutputSignal,
    string InputSignal,
    BitRange Range,
    int InputWidth,
    int OutputWidth) : SchematicPrimitive(Id);

/// <summary>Concat / bit join. Source: contassign with `ConcatExpr` RHS.</summary>
public sealed record JoinerPrimitive(
    string Id,
    string OutputSignal,
    IReadOnlyList<string> InputSignals,   // MSB-first
    int OutputWidth) : SchematicPrimitive(Id);

// ── Storage ───────────────────────────────────────────────────────────────────

/// <summary>Memory tile (unpacked array). Source: <see cref="SignalDecl"/> with non-empty <see cref="SignalDecl.ArrayDims"/>.</summary>
public sealed record MemoryPrimitive(
    string Id,
    string SignalName,
    int CellWidth,
    int DepthHi,
    int DepthLo) : SchematicPrimitive(Id)
{
    public int Depth => DepthHi - DepthLo + 1;
}

// ── Topology ──────────────────────────────────────────────────────────────────

/// <summary>Sub-module instance. Source: <see cref="InstanceDecl"/>.</summary>
public sealed record InstancePrimitive(
    string Id,
    string InstanceName,
    string ModuleName,
    IReadOnlyList<InstancePinBinding> Pins) : SchematicPrimitive(Id);

public sealed record InstancePinBinding(string PortName, string SignalName, string Direction, int PortIndex);

/// <summary>Module boundary port. Source: <see cref="PortDecl"/>.</summary>
public sealed record PortPrimitive(
    string Id,
    string Name,
    SignalDirection Direction,
    int Width) : SchematicPrimitive(Id);

/// <summary>Bare net node for signals without a structural driver in this scope.</summary>
public sealed record SignalPrimitive(
    string Id,
    string Name,
    int Width,
    bool IsRegistered) : SchematicPrimitive(Id);

// ── Fan-out (P2-11) ────────────────────────────────────────────────────────────

/// <summary>
/// Packed-struct fan-out: a single struct signal (e.g. <c>control_pins</c>) feeds many
/// consumers, each reading a different field. The renderer paints this as one
/// inverse wedge with N labelled legs — one per field — instead of N overlapping
/// edges originating from the same boundary pin.
/// </summary>
public sealed record StructFanOutPrimitive(
    string Id,
    string StructSignal,
    string StructTypeName,
    int StructWidth,
    IReadOnlyList<StructFanOutLeg> Legs) : SchematicPrimitive(Id);

/// <summary>One fan-out leg = one field of the packed struct being read by at least one consumer in the scope.</summary>
public sealed record StructFanOutLeg(
    string FieldName,
    BitRange Range,
    // Consumers identifies which downstream targets read this leg. Each entry is the
    // target signal name (the LHS of the contassign that reads struct.field, or the
    // instance pin SignalName that wraps a <sel> on the struct). The builder uses
    // this list to suppress duplicate legacy edges and to wire the leg's output
    // port to each consumer.
    IReadOnlyList<string> Consumers);
