using Bistable.Protocol;

namespace Bistable.App.Services;

/// <summary>
/// Phase 6.5 P6.5-11: cycle-by-cycle smoke comparator between an RTL
/// simulation worker and a gate-level (post-synthesis) worker built by
/// <see cref="GateLevelWorkerBuildService"/>. The two workers must speak
/// identical top-level port shapes — that is the load-bearing assumption
/// synthesis correctness rests on. If a Yosys script accidentally changes
/// port behaviour, this comparator catches it on the first divergent cycle.
///
/// The comparator owns NO ownership of the workers — callers wire the build,
/// the comparator only drives the eval/tick loop and diffs frames. That keeps
/// it trivially fakeable in unit tests with stub workers and lets integration
/// tests reuse the existing CPU run plumbing.
/// </summary>
public sealed class RtlVsGateLevelComparator
{
    /// <summary>
    /// Run <paramref name="program"/> against both workers in lockstep and
    /// return a per-cycle comparison report. Stops early when the program
    /// completes (or when a stop predicate fires, when added — for now we
    /// always run the full cycle budget so the report covers the full window).
    /// </summary>
    public async Task<CompareReport> CompareProgramAsync(
        SimulationWorkerClient rtl,
        SimulationWorkerClient gate,
        CompareProgram program,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rtl);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(program);

        // Reset both sides to a known state.
        await rtl.StepAsync(new SimulationCommand(SimulationCommandType.Reset), cancellationToken);
        await gate.StepAsync(new SimulationCommand(SimulationCommandType.Reset), cancellationToken);

        // Apply identical setup commands to both workers.
        foreach (SimulationCommand setup in program.Setup)
        {
            await rtl.StepAsync(setup, cancellationToken);
            await gate.StepAsync(setup, cancellationToken);
        }

        // Eval both — the first frame is the pre-tick state we want to verify
        // before driving the clock; mismatches here are typically wiring bugs
        // (boundary port not exposed, default value drift).
        SimulationFrame rtlFrame = await rtl.StepAsync(
            new SimulationCommand(SimulationCommandType.Eval), cancellationToken);
        SimulationFrame gateFrame = await gate.StepAsync(
            new SimulationCommand(SimulationCommandType.Eval), cancellationToken);

        List<CycleComparison> cycles = new(capacity: program.Cycles + 1);
        cycles.Add(BuildCycleComparison(0, rtlFrame, gateFrame, program.SignalsToCompare));

        for (int cycle = 1; cycle <= program.Cycles; cycle++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SimulationCommand tick = new(SimulationCommandType.Tick, Signal: program.Clock);
            rtlFrame  = await rtl.StepAsync(tick, cancellationToken);
            gateFrame = await gate.StepAsync(tick, cancellationToken);
            cycles.Add(BuildCycleComparison(cycle, rtlFrame, gateFrame, program.SignalsToCompare));
        }

        return new CompareReport(cycles);
    }

    /// <summary>
    /// Diff a single pair of frames. Exposed so callers that already own a
    /// custom run loop (CPU run engine, breakpoint stepping) can plug into
    /// the same diff format.
    /// </summary>
    public static CycleComparison Compare(
        int cycle,
        SimulationFrame rtlFrame,
        SimulationFrame gateFrame,
        IReadOnlyCollection<string>? signalsToCompare = null) =>
        BuildCycleComparison(cycle, rtlFrame, gateFrame, signalsToCompare);

    private static CycleComparison BuildCycleComparison(
        int cycle,
        SimulationFrame rtl,
        SimulationFrame gate,
        IReadOnlyCollection<string>? signalsToCompare)
    {
        Dictionary<string, string> rtlByName  = rtl.Signals.ToDictionary(s => s.Signal, s => s.Value, StringComparer.Ordinal);
        Dictionary<string, string> gateByName = gate.Signals.ToDictionary(s => s.Signal, s => s.Value, StringComparer.Ordinal);

        // When the caller didn't constrain the set, compare every signal both
        // sides surface. Caller-supplied lists let callers ignore wires they
        // know are synthesis-altered (e.g. internal __V tmps, signals only
        // exposed by --public-flat-rw on the RTL side).
        IEnumerable<string> signalNames = signalsToCompare is { Count: > 0 }
            ? signalsToCompare
            : rtlByName.Keys.Intersect(gateByName.Keys, StringComparer.Ordinal);

        List<SignalDiff> diffs = [];
        foreach (string signal in signalNames)
        {
            rtlByName.TryGetValue(signal, out string? rtlValue);
            gateByName.TryGetValue(signal, out string? gateValue);
            diffs.Add(new SignalDiff(signal, rtlValue, gateValue));
        }

        return new CycleComparison(cycle, diffs);
    }
}

/// <summary>Setup + drive plan for a comparison run.</summary>
public sealed record CompareProgram
{
    public required string Clock { get; init; }
    public required int Cycles { get; init; }

    /// <summary>
    /// Commands to apply to BOTH workers before the first eval (e.g.
    /// <c>SetInput("enable","1")</c>, program-image writes). Order is preserved.
    /// </summary>
    public IReadOnlyList<SimulationCommand> Setup { get; init; } = Array.Empty<SimulationCommand>();

    /// <summary>
    /// Optional whitelist of signals to compare. Null/empty means "every signal
    /// both workers expose" (intersection of the two top-port sets).
    /// </summary>
    public IReadOnlyCollection<string>? SignalsToCompare { get; init; }
}

/// <summary>Per-cycle diff between RTL and gate-level workers.</summary>
public sealed record CycleComparison(int Cycle, IReadOnlyList<SignalDiff> Signals)
{
    public IEnumerable<SignalDiff> Mismatches => Signals.Where(s => !s.Matches);
    public bool HasMismatch => Mismatches.Any();
}

/// <summary>One signal's value on each side, with a derived equality flag.</summary>
public sealed record SignalDiff(string Signal, string? RtlValue, string? GateValue)
{
    public bool Matches => string.Equals(RtlValue, GateValue, StringComparison.Ordinal);
}

/// <summary>Full comparison report across all cycles.</summary>
public sealed record CompareReport(IReadOnlyList<CycleComparison> Cycles)
{
    public bool AllMatch => Cycles.All(c => !c.HasMismatch);
    public CycleComparison? FirstMismatch => Cycles.FirstOrDefault(c => c.HasMismatch);

    public int TotalMismatchCount => Cycles.Sum(c => c.Mismatches.Count());

    /// <summary>
    /// Format the first few mismatching signal-cycle pairs as a human-readable
    /// table so test assertions surface useful diagnostics. Cap at 10 lines —
    /// the rest are summarised by count.
    /// </summary>
    public string FormatSummary(int maxLines = 10)
    {
        if (AllMatch) return "All cycles matched.";
        System.Text.StringBuilder sb = new();
        int shown = 0;
        foreach (CycleComparison cycle in Cycles)
        {
            foreach (SignalDiff diff in cycle.Mismatches)
            {
                sb.AppendLine($"cycle {cycle.Cycle,3}  {diff.Signal,-20}  rtl={diff.RtlValue ?? "<missing>"}  gate={diff.GateValue ?? "<missing>"}");
                if (++shown >= maxLines) break;
            }
            if (shown >= maxLines) break;
        }
        int total = TotalMismatchCount;
        if (total > shown) sb.AppendLine($"... and {total - shown} more mismatches.");
        return sb.ToString().TrimEnd();
    }
}
