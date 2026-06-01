namespace Bistable.App.ViewModels;

public sealed class HierarchyScopeInstancePortConnectionViewModel
{
    public HierarchyScopeInstancePortConnectionViewModel(
        string portName,
        string signalName,
        bool isInput,
        int width,
        IReadOnlyList<string>? concatParts = null)
    {
        PortName = portName;
        SignalName = signalName;
        IsInput = isInput;
        Width = width;
        ConcatParts = concatParts;
    }

    public string PortName { get; }

    public string SignalName { get; }

    public bool IsInput { get; }

    public bool IsOutput => !IsInput;

    public int Width { get; }

    public string WidthLabel => Width == 1 ? "1b" : $"{Width}b";

    // P4.5: concat-bundled pin (e.g. `.d({a, b, c})`). MSB-first constituents.
    // Null when the connection is a single signal.
    public IReadOnlyList<string>? ConcatParts { get; }

    public bool IsConcat => ConcatParts is { Count: > 0 };
}
