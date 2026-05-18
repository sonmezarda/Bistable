namespace Bistable.Protocol;

public enum SimulationCommandType
{
    SetInput,
    Eval,
    Tick,
    RunCycles,
    Reset,
    GetSnapshot,
    Pause
}
