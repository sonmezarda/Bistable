namespace Bistable.Protocol;

/// <summary>
/// One command sent from the GUI to the worker over the JSON IPC channel.
/// Single shape covers every <see cref="SimulationCommandType"/>; only the
/// fields relevant to the command are non-null.
/// </summary>
/// <param name="Type">Command discriminator.</param>
/// <param name="Signal">Top-level port name (<c>SetInput</c>) or clock signal (<c>Tick</c>/<c>RunCycles</c>).</param>
/// <param name="Value">Value literal for <c>SetInput</c>/<c>WriteSignal</c>/<c>ForceSignal</c>/<c>WriteMemory</c>.</param>
/// <param name="Cycles">Cycle count for <see cref="SimulationCommandType.RunCycles"/>.</param>
/// <param name="Path">Hierarchical signal path (e.g. <c>arnicomp_top.acc.q</c>) for probe commands.</param>
/// <param name="MemoryAddress">Starting cell index for <see cref="SimulationCommandType.ReadMemory"/>/<see cref="SimulationCommandType.WriteMemory"/>.</param>
/// <param name="MemoryCount">Cell count for <see cref="SimulationCommandType.ReadMemory"/>. Default 1.</param>
public sealed record SimulationCommand(
    SimulationCommandType Type,
    string? Signal = null,
    string? Value = null,
    long Cycles = 0,
    string? Path = null,
    ulong? MemoryAddress = null,
    int? MemoryCount = null);
