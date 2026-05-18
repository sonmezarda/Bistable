namespace Bistable.Core.Design;

public sealed record ModuleMetadata(
    string Name,
    IReadOnlyList<SignalPort> Ports,
    IReadOnlyList<DesignParameter> Parameters)
{
    public IReadOnlyList<SignalPort> Inputs => Ports.Where(static p => p.Direction == SignalDirection.Input).ToArray();

    public IReadOnlyList<SignalPort> Outputs => Ports.Where(static p => p.Direction == SignalDirection.Output).ToArray();
}
