namespace Bistable.App.ViewModels;

/// <summary>
/// One row of the Live Probe panel's memory viewer: address + hex value.
/// Immutable; the panel rebuilds the collection on every snapshot refresh
/// rather than mutating cells in place — keeps the change-detection logic
/// in <see cref="Services.LiveProbeService"/> simple.
/// </summary>
public sealed class MemoryCellViewModel(ulong address, string hexValue)
{
    public ulong Address { get; } = address;
    public string AddressLabel => $"0x{Address:X3}";
    public string HexValue { get; } = hexValue;
}
