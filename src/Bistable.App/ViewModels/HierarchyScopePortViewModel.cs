using Bistable.Core.Design;

namespace Bistable.App.ViewModels;

public sealed class HierarchyScopePortViewModel
{
    public HierarchyScopePortViewModel(string name, SignalDirection direction, int width, bool isSigned)
    {
        Name = name;
        Direction = direction;
        Width = width;
        IsSigned = isSigned;
    }

    public string Name { get; }

    public SignalDirection Direction { get; }

    public int Width { get; }

    public bool IsSigned { get; }

    public bool IsInput => Direction == SignalDirection.Input;

    public bool IsOutput => Direction == SignalDirection.Output;

    /// <summary>P2.6-4: bidirectional port (e.g. tri-state bus, I²C SDA).</summary>
    public bool IsInOut => Direction == SignalDirection.InOut;

    public string WidthLabel => Width == 1 ? "1b" : $"{Width}b";

    public string DisplayLabel => $"{Name} [{WidthLabel}]";
}
