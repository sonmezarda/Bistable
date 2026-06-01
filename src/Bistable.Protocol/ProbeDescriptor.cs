namespace Bistable.Protocol;

/// <summary>
/// Describes one entry in the worker's probe table. Returned by
/// <see cref="SimulationCommandType.ListProbes"/>. The GUI uses this to
/// populate signal pickers, verify the worker's probe-table shape matches
/// the AST, and decide which probes need memory-range read APIs.
/// </summary>
/// <param name="Path">Hierarchical path (e.g. <c>arnicomp_top.acc.q</c>).</param>
/// <param name="Width">Bit width of one cell (for memories) or the whole signal.</param>
/// <param name="IsSigned">Declared signed.</param>
/// <param name="IsRegistered">
/// True when the signal is the target of a <c>SequentialBlockAst</c> in its
/// module (i.e. an FF Q value). Lets Phase 4 highlight stable values.
/// </param>
/// <param name="IsMemory">
/// True when <c>SignalDecl.ArrayDims</c> is non-empty. For memories,
/// <see cref="MemoryDepth"/> carries the total cell count.
/// </param>
/// <param name="MemoryDepth">For memory probes, total addressable cell count. Null for scalar/vector signals.</param>
public sealed record ProbeDescriptor(
    string Path,
    int Width,
    bool IsSigned,
    bool IsRegistered,
    bool IsMemory,
    int? MemoryDepth);
