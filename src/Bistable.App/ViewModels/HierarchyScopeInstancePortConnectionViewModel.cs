namespace Bistable.App.ViewModels;

public sealed class HierarchyScopeInstancePortConnectionViewModel
{
    public HierarchyScopeInstancePortConnectionViewModel(string portName, string signalName, bool isInput, int width)
    {
        PortName = portName;
        SignalName = signalName;
        IsInput = isInput;
        Width = width;
    }

    public string PortName { get; }

    public string SignalName { get; }

    public bool IsInput { get; }

    public bool IsOutput => !IsInput;

    public int Width { get; }

    public string WidthLabel => Width == 1 ? "1b" : $"{Width}b";
}
