namespace Bistable.Core.Design;

public sealed record DesignHierarchyNode(
    string InstanceName,
    string ModuleName,
    string HierarchyPath,
    IReadOnlyList<DesignHierarchyNode> Children);
