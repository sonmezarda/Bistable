using Bistable.App.ViewModels;
using Bistable.Core.Design;

namespace Bistable.Tests;

public sealed class WaveformLaneViewModelTests
{
    [Fact]
    public void DeduplicatesConsecutiveSamplesAndResolvesCursorValue()
    {
        SignalPort port = new("count", SignalDirection.Output, 8, false, 0);
        SignalViewModel signal = new(port)
        {
            Value = "0x00"
        };
        WaveformLaneViewModel lane = new(signal);

        Assert.True(lane.AppendSample(1, 0, "0x00", force: true));
        Assert.False(lane.AppendSample(2, 0, "0x00"));
        Assert.True(lane.AppendSample(3, 1, "0x01"));
        Assert.True(lane.AppendSample(4, 2, "0x02"));

        Assert.Equal(3, lane.Samples.Count);
        Assert.Equal("0x00", lane.GetValueAtOrBefore(2));
        Assert.Equal("0x01", lane.GetValueAtOrBefore(3));
        Assert.Equal("0x02", lane.GetValueAtOrBefore(10));
        Assert.Equal((ulong)1, lane.GetTimeAtOrBefore(3));
    }
}
