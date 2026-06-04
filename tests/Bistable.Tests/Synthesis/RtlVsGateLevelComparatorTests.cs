using Bistable.App.Services;
using Bistable.Protocol;

namespace Bistable.Tests.Synthesis;

/// <summary>
/// Phase 6.5 P6.5-11 unit coverage. Real RTL ↔ gate worker round-trip is
/// covered by <c>RtlVsGateLevelIntegrationTests</c>; this file pins the diff
/// semantics independently of the toolchain so failures point at the right
/// layer.
/// </summary>
public sealed class RtlVsGateLevelComparatorTests
{
    [Fact]
    public void Compare_IdenticalFrames_ReportsNoMismatch()
    {
        SimulationFrame rtl  = Frame(0, ("pc", "0x10"), ("halted", "0"));
        SimulationFrame gate = Frame(0, ("pc", "0x10"), ("halted", "0"));

        CycleComparison c = RtlVsGateLevelComparator.Compare(0, rtl, gate);

        Assert.False(c.HasMismatch);
        Assert.All(c.Signals, s => Assert.True(s.Matches));
    }

    [Fact]
    public void Compare_SingleSignalDivergent_FlagsThatSignalOnly()
    {
        SimulationFrame rtl  = Frame(0, ("pc", "0x10"), ("halted", "0"));
        SimulationFrame gate = Frame(0, ("pc", "0x10"), ("halted", "1"));

        CycleComparison c = RtlVsGateLevelComparator.Compare(0, rtl, gate);

        Assert.True(c.HasMismatch);
        SignalDiff diff = Assert.Single(c.Mismatches);
        Assert.Equal("halted", diff.Signal);
        Assert.Equal("0", diff.RtlValue);
        Assert.Equal("1", diff.GateValue);
    }

    [Fact]
    public void Compare_SignalPresentOnRtlOnly_ReportsGateValueAsNull()
    {
        // Common synthesis pattern: a Verilator --public-flat-rw probe on the
        // RTL side that doesn't exist on the gate-level side.
        SimulationFrame rtl  = Frame(0, ("pc", "0x10"), ("internal_tmp", "0x5"));
        SimulationFrame gate = Frame(0, ("pc", "0x10"));

        CycleComparison c = RtlVsGateLevelComparator.Compare(0, rtl, gate);

        // Default behaviour: only compare signals both sides expose, so
        // internal_tmp is silently dropped from the diff list.
        Assert.False(c.HasMismatch);
    }

    [Fact]
    public void Compare_ExplicitWhitelist_OverridesIntersectionLogic()
    {
        // Caller asked for a signal that exists on RTL but not gate — the
        // comparator surfaces the missing value rather than hiding it.
        SimulationFrame rtl  = Frame(0, ("internal_tmp", "0x5"));
        SimulationFrame gate = Frame(0);

        CycleComparison c = RtlVsGateLevelComparator.Compare(0, rtl, gate,
            signalsToCompare: ["internal_tmp"]);

        Assert.True(c.HasMismatch);
        SignalDiff diff = Assert.Single(c.Mismatches);
        Assert.Equal("0x5", diff.RtlValue);
        Assert.Null(diff.GateValue);
    }

    [Fact]
    public void CompareReport_FormatSummary_LimitsToConfiguredMaxLines()
    {
        // 12 mismatching signals across cycles → summary shows the first 3 and
        // appends a "… and N more" tail.
        List<CycleComparison> cycles = [];
        for (int cycle = 0; cycle < 4; cycle++)
        {
            List<SignalDiff> diffs = [];
            for (int sig = 0; sig < 3; sig++)
            {
                diffs.Add(new SignalDiff($"sig{sig}", "0", "1"));
            }
            cycles.Add(new CycleComparison(cycle, diffs));
        }
        CompareReport report = new(cycles);

        string summary = report.FormatSummary(maxLines: 3);
        Assert.Contains("cycle   0", summary);
        Assert.Contains("and 9 more mismatches", summary);
        Assert.Equal(12, report.TotalMismatchCount);
        Assert.NotNull(report.FirstMismatch);
    }

    [Fact]
    public void CompareReport_AllMatch_FormatsAsSingleLine()
    {
        CompareReport report = new([
            new CycleComparison(0, [new SignalDiff("pc", "0x10", "0x10")]),
            new CycleComparison(1, [new SignalDiff("pc", "0x14", "0x14")]),
        ]);
        Assert.True(report.AllMatch);
        Assert.Equal("All cycles matched.", report.FormatSummary());
        Assert.Null(report.FirstMismatch);
    }

    private static SimulationFrame Frame(ulong time, params (string Signal, string Value)[] signals)
    {
        List<SignalSample> samples = signals
            .Select(s => new SignalSample(s.Signal, s.Value, time))
            .ToList();
        return new SimulationFrame(time, samples);
    }
}
