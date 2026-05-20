namespace Bistable.App.ViewModels;

public sealed class HierarchyScopeLocalSignalViewModel
{
    public HierarchyScopeLocalSignalViewModel(
        string name,
        int width,
        bool isSigned,
        bool isTraced,
        string currentValue,
        string? resolvedSignalName)
    {
        Name = name;
        Width = width;
        IsSigned = isSigned;
        IsTraced = isTraced;
        CurrentValue = currentValue;
        ResolvedSignalName = resolvedSignalName;
    }

    public string Name { get; }

    public int Width { get; }

    public bool IsSigned { get; }

    public bool IsTraced { get; }

    public string CurrentValue { get; }

    public string? ResolvedSignalName { get; }

    public string WidthLabel => Width == 1 ? "1b" : $"{Width}b";
}
