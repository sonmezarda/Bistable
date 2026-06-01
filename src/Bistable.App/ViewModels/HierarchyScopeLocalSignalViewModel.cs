namespace Bistable.App.ViewModels;

public sealed class HierarchyScopeLocalSignalViewModel
{
    public HierarchyScopeLocalSignalViewModel(
        string name,
        int width,
        bool isSigned,
        bool isTraced,
        string currentValue,
        string? resolvedSignalName,
        MemoryShape? memory = null)
    {
        Name = name;
        Width = width;
        IsSigned = isSigned;
        IsTraced = isTraced;
        CurrentValue = currentValue;
        ResolvedSignalName = resolvedSignalName;
        Memory = memory;
    }

    public string Name { get; }

    public int Width { get; }

    public bool IsSigned { get; }

    public bool IsTraced { get; }

    public string CurrentValue { get; }

    public string? ResolvedSignalName { get; }

    /// <summary>P3-6: non-null when this signal is an unpacked-array memory.</summary>
    public MemoryShape? Memory { get; }

    public bool IsMemory => Memory is not null;

    public int MemoryDepth => Memory?.Depth ?? 0;

    public string WidthLabel
    {
        get
        {
            if (Memory is { } m) return $"{Width}b × {m.Depth}";
            return Width == 1 ? "1b" : $"{Width}b";
        }
    }
}

/// <summary>Unpacked-array shape metadata for a memory probe.</summary>
public sealed record MemoryShape(int Depth);
