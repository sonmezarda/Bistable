using Bistable.Core.Design;

namespace Bistable.App.ViewModels;

public sealed class SignalViewModel(SignalPort port) : ViewModelBase
{
    private bool _isInWaveform;
    private string _value = port.Width == 1 ? "0" : "0x0";

    public string Name { get; } = port.Name;

    public SignalDirection Direction { get; } = port.Direction;

    public int Width { get; } = port.Width;

    public bool IsSigned { get; } = port.IsSigned;

    public string DirectionLabel => Direction.ToString().ToUpperInvariant();

    public string WidthLabel => Width == 1 ? "1 bit" : $"{Width} bits";

    public bool IsInput => Direction == SignalDirection.Input;

    public bool IsBoolean => Width == 1;

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
