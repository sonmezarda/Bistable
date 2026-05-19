namespace Bistable.Core.Design;

public sealed record ElaboratedDesign(
    ModuleMetadata TopModule,
    DesignHierarchyNode HierarchyRoot);
