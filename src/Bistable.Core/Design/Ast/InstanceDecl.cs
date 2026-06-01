namespace Bistable.Core.Design.Ast;

public sealed record InstanceDecl(
    string InstanceName,
    string ModuleName,
    IReadOnlyList<PortConnectionDecl> PortConnections);
