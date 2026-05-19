using Bistable.Core.Design;

namespace Bistable.App.ViewModels;

public sealed class SignalViewModel : ViewModelBase
{
    private bool _isInWaveform;
    private readonly string _shortName;
    private readonly string? _scopePath;
    private string _value;

    public SignalViewModel(SignalPort port)
        : this(port.Name, port.Name, null, port.Direction, port.Width, port.IsSigned)
    {
    }

    public SignalViewModel(
        string name,
        string shortName,
        string? scopePath,
        SignalDirection direction,
        int width,
        bool isSigned)
    {
        Name = name;
        _shortName = shortName;
        _scopePath = scopePath;
        Direction = direction;
        Width = width;
        IsSigned = isSigned;
        _value = width == 1 ? "0" : "0x0";
    }

    public string Name { get; }

    public string ShortName => _shortName;

    public string? ScopePath => _scopePath;

    public SignalDirection Direction { get; }

    public int Width { get; }

    public bool IsSigned { get; }

    public string DirectionLabel => Direction.ToString().ToUpperInvariant();

    public string WidthLabel => Width == 1 ? "1 bit" : $"{Width} bits";

    public string DisplayName => string.IsNullOrWhiteSpace(_scopePath) ? ShortName : Name;

    public string BrowseLabel => $"{DirectionLabel,-8} {DisplayName}[{WidthLabel}]";

    public bool IsInput => Direction == SignalDirection.Input;

    public bool IsBoolean => Width == 1;

    public bool IsTraceOnly => Direction == SignalDirection.Internal;

    public bool IsInWaveform
    {
        get => _isInWaveform;
        set => SetProperty(ref _isInWaveform, value);
    }

    public bool BooleanValue
    {
        get => Value == "1";
        set => Value = value ? "1" : "0";
    }

    public string Value
    {
        get => _value;
        set
        {
            if (SetProperty(ref _value, value))
            {
                OnPropertyChanged(nameof(BooleanValue));
            }
        }
    }
}
