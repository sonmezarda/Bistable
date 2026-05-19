namespace Bistable.App.Services;

public sealed record BistableWorkspace(
    ProjectDialogService Dialogs,
    DesignLoadService DesignLoader,
    Bistable.Verilator.SimulationWorkerBuilder WorkerBuilder,
    PreviewSimulationService PreviewSimulation,
    LayoutStateService LayoutState);
