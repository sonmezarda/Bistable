namespace Bistable.Protocol;

public sealed record SimulationCommand(
    SimulationCommandType Type,
    string? Signal = null,
    string? Value = null,
    long Cycles = 0);
