namespace Bistable.Core.Design;

public sealed record SignalPort(
    string Name,
    SignalDirection Direction,
    int Width,
    bool IsSigned,
    int PinIndex)
{
    public bool IsScalar => Width == 1;
}
