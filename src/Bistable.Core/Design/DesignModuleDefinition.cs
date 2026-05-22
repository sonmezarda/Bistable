namespace Bistable.Core.Design;

public sealed record DesignModuleDefinition(
    ModuleMetadata Metadata,
    IReadOnlyList<DesignLocalSignal> LocalSignals,
    IReadOnlyList<DesignInstanceDefinition> Instances,
    IReadOnlyList<DesignContAssign> ContAssigns);
