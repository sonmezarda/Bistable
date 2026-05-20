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
        IReadOnlyList<HierarchyScopeInstancePortConnectionViewModel> portConnections)
    {
        HierarchyPath = hierarchyPath;
        InstanceName = instanceName;
        ModuleName = moduleName;
        InputCount = inputCount;
        OutputCount = outputCount;
        ExactSignalCount = exactSignalCount;
        DescendantSignalCount = descendantSignalCount;
        PortConnections = portConnections;
    }

    public string HierarchyPath { get; }

    public string InstanceName { get; }

    public string ModuleName { get; }

    public int InputCount { get; }

    public int OutputCount { get; }

    public int ExactSignalCount { get; }

    public int DescendantSignalCount { get; }

    public IReadOnlyList<HierarchyScopeInstancePortConnectionViewModel> PortConnections { get; }

    public bool HasTraceActivity => ExactSignalCount > 0 || DescendantSignalCount > 0;

    public string ScopeBadgeText =>
        DescendantSignalCount > 0
            ? $"S {ExactSignalCount}  D {DescendantSignalCount}"
            : $"S {ExactSignalCount}";
}
