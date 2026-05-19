namespace Bistable.App.ViewModels;

public sealed class HierarchyTraceScopeSummaryViewModel(
    string hierarchyPath,
    int exactSignalCount,
    int descendantSignalCount)
{
    public string HierarchyPath { get; } = hierarchyPath;

    public int ExactSignalCount { get; } = exactSignalCount;

    public int DescendantSignalCount { get; } = descendantSignalCount;
}
