namespace Bistable.Protocol;

/// <summary>
/// Result of a <see cref="SimulationCommandType.ReadMemory"/> command. Carries
/// a contiguous range of cell values starting at <see cref="StartAddress"/>.
/// </summary>
/// <param name="Path">Hierarchy path of the memory signal (echoed for routing).</param>
/// <param name="StartAddress">First cell index in the returned range.</param>
/// <param name="CellWidth">Bit width of each cell.</param>
/// <param name="Cells">
/// Ordered list of cell values, each encoded the same way as
/// <see cref="SignalReadResult.Value"/> (decimal or "0x.." hex).
/// </param>
public sealed record MemoryReadResult(
    string Path,
    ulong StartAddress,
    int CellWidth,
    IReadOnlyList<string> Cells);
