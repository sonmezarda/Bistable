using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Numerics;
using Bistable.App.Services;

namespace Bistable.App.ViewModels;

public sealed class SignalValueEditorViewModel : ViewModelBase
{
    private bool _suppressUpdates;
    private SignalValueFormat _selectedFormat;
    private string _valueText;
    private string _errorMessage = string.Empty;

    public SignalValueEditorViewModel(string signalName, int width, string initialValue)
    {
        SignalName = signalName;
        Width = Math.Max(1, width);
        AvailableFormats =
        [
            SignalValueFormat.Hex,
            SignalValueFormat.Decimal,
            SignalValueFormat.Binary
        ];

        _selectedFormat = Width == 1 ? SignalValueFormat.Binary : SignalValueFormat.Hex;
        if (!SignalValueCodec.TryParse(initialValue, _selectedFormat, out BigInteger initial))
        {
            initial = BigInteger.Zero;
        }

        CurrentValue = SignalValueCodec.MaskToWidth(initial, Width);
        IReadOnlyList<bool> bits = SignalValueCodec.ToBits(CurrentValue, Width);
        foreach (bool bit in bits)
        {
            SignalBitViewModel viewModel = new(Bits.Count, bit);
            viewModel.PropertyChanged += OnBitPropertyChanged;
            Bits.Add(viewModel);
        }

        _valueText = SignalValueCodec.FormatForDisplay(CurrentValue, Width, _selectedFormat);
    }

    public string SignalName { get; }

    public int Width { get; }

    public BigInteger CurrentValue { get; private set; }

    public IReadOnlyList<SignalValueFormat> AvailableFormats { get; }

    public ObservableCollection<SignalBitViewModel> Bits { get; } = [];

    public string WidthLabel => Width == 1 ? "1 bit" : $"{Width} bits";

    public string CanonicalValue => SignalValueCodec.FormatCanonical(CurrentValue, Width);

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public SignalValueFormat SelectedFormat
    {
        get => _selectedFormat;
        set
        {
            if (SetProperty(ref _selectedFormat, value))
            {
                UpdateValueTextFromCurrent();
            }
        }
    }

    public string ValueText
    {
        get => _valueText;
        set => SetProperty(ref _valueText, value);
    }

    public bool TryApplyText()
    {
        if (!SignalValueCodec.TryParse(ValueText, SelectedFormat, out BigInteger parsed))
        {
            ErrorMessage = "Use hexadecimal, decimal, or binary digits.";
            return false;
        }

        ApplyValue(parsed);
        ErrorMessage = string.Empty;
        return true;
    }

    private void ApplyValue(BigInteger value)
    {
        CurrentValue = SignalValueCodec.MaskToWidth(value, Width);
        UpdateBitsFromCurrent();
        UpdateValueTextFromCurrent();
        OnPropertyChanged(nameof(CanonicalValue));
    }

    private void UpdateBitsFromCurrent()
    {
        IReadOnlyList<bool> bits = SignalValueCodec.ToBits(CurrentValue, Width);
        _suppressUpdates = true;
        try
        {
            for (int index = 0; index < Bits.Count; index++)
            {
                Bits[index].IsSet = bits[index];
            }
        }
        finally
        {
            _suppressUpdates = false;
        }
    }

    private void UpdateValueTextFromCurrent()
    {
        _suppressUpdates = true;
        try
        {
            ValueText = SignalValueCodec.FormatForDisplay(CurrentValue, Width, SelectedFormat);
        }
        finally
        {
            _suppressUpdates = false;
        }
    }

    private void OnBitPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressUpdates || e.PropertyName != nameof(SignalBitViewModel.IsSet))
        {
            return;
        }

        CurrentValue = SignalValueCodec.FromBits(Bits.Select(bit => bit.IsSet));
        ErrorMessage = string.Empty;
        UpdateValueTextFromCurrent();
        OnPropertyChanged(nameof(CanonicalValue));
    }
}
