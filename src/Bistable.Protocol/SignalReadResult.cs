namespace Bistable.Protocol;

/// <summary>
/// Result of a <see cref="SimulationCommandType.ReadSignal"/> command. The value
/// is carried as a decimal string (or "0x.." hex string) — wide signals exceed
/// <c>ulong</c> on 65+ bit buses, and the GUI may want to display in multiple
/// radices. The width + signedness lets the GUI format/sign-extend correctly.
/// </summary>
/// <param name="Path">Hierarchy path the value was read from (echoed for routing).</param>
/// <param name="Value">String-encoded numeric value: decimal by default, "0x..." hex when wider than 64 bits.</param>
/// <param name="Width">Bit width of the signal.</param>
/// <param name="IsSigned">True if the signal was declared signed; affects how the GUI sign-extends.</param>
public sealed record SignalReadResult(
    string Path,
    string Value,
    int Width,
    bool IsSigned);
