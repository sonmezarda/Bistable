namespace Bistable.App.ViewModels;

public sealed class HierarchyScopeInstanceViewModel
{
    public HierarchyScopeInstanceViewModel(
        string hierarchyPath,
        string instanceName,
        string moduleName,
        int inputCount,
        int outputCount,
        int exactSignalCount,
        int descendantSignalCount,
        IReadOnlyList<HierarchyScopeInstancePortConnectionViewModel> portConnections,
        IReadOnlyList<HierarchyScopePortViewModel>? ports = null,
        IReadOnlyList<HierarchyScopeLocalSignalViewModel>? localSignals = null,
        IReadOnlyList<HierarchyScopeInstanceViewModel>? childInstances = null,
        IReadOnlyList<Bistable.Core.Design.DesignContAssign>? contAssigns = null)
    {
        HierarchyPath = hierarchyPath;
        InstanceName = instanceName;
        ModuleName = moduleName;
        InputCount = inputCount;
        OutputCount = outputCount;
        ExactSignalCount = exactSignalCount;
        DescendantSignalCount = descendantSignalCount;
        PortConnections = portConnections;
        Ports = ports ?? [];
        LocalSignals = localSignals ?? [];
        ChildInstances = childInstances ?? [];
        ContAssigns = contAssigns ?? [];
    }

    public string HierarchyPath { get; }

    public string InstanceName { get; }

    public string ModuleName { get; }

    public int InputCount { get; }

    public int OutputCount { get; }

    public int ExactSignalCount { get; }

    public int DescendantSignalCount { get; }

    public IReadOnlyList<HierarchyScopeInstancePortConnectionViewModel> PortConnections { get; }

    public IReadOnlyList<HierarchyScopePortViewModel> Ports { get; }

    public IReadOnlyList<HierarchyScopeLocalSignalViewModel> LocalSignals { get; }

    public IReadOnlyList<HierarchyScopeInstanceViewModel> ChildInstances { get; }

    public IReadOnlyList<Bistable.Core.Design.DesignContAssign> ContAssigns { get; }

    public bool HasTraceActivity => ExactSignalCount > 0 || DescendantSignalCount > 0;

    public string ScopeBadgeText =>
        DescendantSignalCount > 0
            ? $"S {ExactSignalCount}  D {DescendantSignalCount}"
            : $"S {ExactSignalCount}";
}
