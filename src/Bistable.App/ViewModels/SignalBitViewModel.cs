namespace Bistable.App.ViewModels;

public sealed class SignalBitViewModel(int index, bool isSet) : ViewModelBase
{
    private bool _isSet = isSet;

    public int Index { get; } = index;

    public string Label => $"b{Index}";

    public bool IsSet
    {
        get => _isSet;
        set => SetProperty(ref _isSet, value);
    }
}
