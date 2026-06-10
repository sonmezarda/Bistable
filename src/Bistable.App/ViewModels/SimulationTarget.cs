namespace Bistable.App.ViewModels;

/// <summary>
/// Selects which compiled model receives interactive simulation commands.
/// Both targets share the project's top-level input/output controls, while
/// keeping independent native worker state.
/// </summary>
public enum SimulationTarget
{
    Rtl,
    GateLevel,
}
