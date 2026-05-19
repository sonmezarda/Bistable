namespace Bistable.App.ViewModels;

public sealed class HierarchyScopeNodeViewModel(
    string hierarchyPath,
    string instanceName,
    string moduleName,
    int exactSignalCount,
    int descendantSignalCount)
{
    public string HierarchyPath { get; } = hierarchyPath;

    public string InstanceName { get; } = instanceName;

    public string ModuleName { get; } = moduleName;

    public int ExactSignalCount { get; } = exactSignalCount;

    public int DescendantSignalCount { get; } = descendantSignalCount;

    public bool HasTraceActivity => ExactSignalCount > 0 || DescendantSignalCount > 0;

    public string ScopeBadgeText =>
        DescendantSignalCount > 0
            ? $"S {ExactSignalCount}  D {DescendantSignalCount}"
            : $"S {ExactSignalCount}";

    public string DisplayLabel => $"{InstanceName} : {ModuleName}";
}
