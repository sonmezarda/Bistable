namespace Bistable.App.ViewModels;

public sealed record WaveformEventViewModel(long Order, ulong Time, string Signal, string Value);
