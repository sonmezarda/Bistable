namespace Bistable.Core.Design;

public sealed record DesignInstanceDefinition(
    string Name,
    string ModuleName,
    IReadOnlyList<DesignInstancePortConnection> PortConnections);
