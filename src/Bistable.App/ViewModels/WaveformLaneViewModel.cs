using System.Collections.ObjectModel;

namespace Bistable.App.ViewModels;

public sealed class WaveformLaneViewModel : ViewModelBase
{
    private const int MaxSampleCount = 4096;
    private readonly SignalViewModel _signal;
    private string _latestValue;

    public WaveformLaneViewModel(SignalViewModel signal)
    {
        _signal = signal;
        _latestValue = signal.Value;
    }

    public SignalViewModel Signal => _signal;

    public string Name => _signal.Name;

    public int Width => _signal.Width;

    public bool IsBoolean => _signal.IsBoolean;

    public string LatestValue
    {
        get => _latestValue;
        private set => SetProperty(ref _latestValue, value);
    }

    public ObservableCollection<WaveformSampleViewModel> Samples { get; } = [];

    public bool AppendSample(long order, ulong time, string value, bool force = false)
    {
        if (!force && Samples.Count > 0 && string.Equals(Samples[^1].Value, value, StringComparison.Ordinal))
        {
            return false;
        }

        Samples.Add(new WaveformSampleViewModel(order, time, value));
        while (Samples.Count > MaxSampleCount)
        {
            Samples.RemoveAt(0);
        }

        LatestValue = value;
        return true;
    }

    public string GetValueAtOrBefore(long order)
    {
        if (Samples.Count == 0)
        {
            return LatestValue;
        }

        int low = 0;
        int high = Samples.Count - 1;
        int index = -1;
        while (low <= high)
        {
            int mid = low + ((high - low) / 2);
            long sampleOrder = Samples[mid].Order;
            if (sampleOrder <= order)
            {
                index = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return index >= 0 ? Samples[index].Value : Samples[0].Value;
    }

    public ulong GetTimeAtOrBefore(long order)
    {
        if (Samples.Count == 0)
        {
            return 0;
        }

        int low = 0;
        int high = Samples.Count - 1;
        int index = -1;
        while (low <= high)
        {
            int mid = low + ((high - low) / 2);
            long sampleOrder = Samples[mid].Order;
            if (sampleOrder <= order)
            {
                index = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return index >= 0 ? Samples[index].Time : Samples[0].Time;
    }

    public void ClearSamples()
    {
        Samples.Clear();
        LatestValue = _signal.Value;
    }
}
