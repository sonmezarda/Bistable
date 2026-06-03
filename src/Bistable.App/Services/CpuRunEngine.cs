using Bistable.Core.Projects;
using Bistable.Protocol;

namespace Bistable.App.Services;

/// <summary>
/// Phase 5: orchestrates reset → program-load → run-cycles for a CPU-shaped
/// design described by <see cref="CpuRuntimeConfiguration"/>. Talks to the
/// worker through the same client the rest of the app uses. Stops on:
///   - max-cycles cap from the preset (always present),
///   - halted signal == 1 (when the design exposes one and no StopWhen),
///   - explicit <c>StopWhen</c> expression of the form
///     <c>&lt;probePath&gt; == &lt;hexOrDec&gt;</c>.
/// More complex stop expressions are out of scope for v1.
/// </summary>
public sealed class CpuRunEngine
{
    private readonly LiveProbeService _liveProbes;

    public CpuRunEngine(LiveProbeService liveProbes)
    {
        _liveProbes = liveProbes;
    }

    /// <summary>
    /// Apply the reset sequence: drive `Signal` to the active level for
    /// `Cycles` ticks, then de-assert. The clock signal is needed because
    /// reset is sampled on edges.
    /// </summary>
    public async Task ApplyResetAsync(
        SimulationWorkerClient worker,
        CpuResetSequence reset,
        string clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(worker);
        ArgumentNullException.ThrowIfNull(reset);
        // Drive reset to its active level…
        string assertedValue = reset.ActiveLevel == 0 ? "0" : "1";
        string releasedValue = reset.ActiveLevel == 0 ? "1" : "0";
        await worker.SetInputAsync(reset.Signal, assertedValue, cancellationToken);
        // …tick `Cycles` times while asserted…
        for (int i = 0; i < Math.Max(1, reset.Cycles); i++)
        {
            await worker.TickAsync(clock, cancellationToken);
        }
        // …then de-assert so the CPU can run on the next tick.
        await worker.SetInputAsync(reset.Signal, releasedValue, cancellationToken);
    }

    /// <summary>
    /// Write each cell of the parsed program image into the worker via the
    /// memory probe table. Errors are surfaced via the return value so the
    /// caller can update the run-status text.
    /// </summary>
    public async Task<ProgramLoadResult> LoadProgramAsync(
        SimulationWorkerClient worker,
        ProgramImageBinding binding,
        MemoryFileLoader.MemoryImage image,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(worker);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(image);
        int written = 0;
        int failed = 0;
        foreach (MemoryFileLoader.MemoryImageCell cell in image.Cells)
        {
            try
            {
                await worker.WriteMemoryAsync(
                    binding.ProbePath,
                    (ulong)(binding.BaseAddress + (long)cell.Address),
                    cell.HexValue,
                    cancellationToken);
                written++;
            }
            catch (InvalidOperationException)
            {
                failed++;
            }
        }
        return new ProgramLoadResult(written, failed, image.Errors);
    }

    /// <summary>
    /// Tick the clock up to <paramref name="preset"/>.MaxCycles times, stopping
    /// early when the stop predicate matches. Returns the final cycle count so
    /// the UI can show "Ran N cycles" / "Halted at N".
    /// </summary>
    public async Task<RunResult> RunAsync(
        SimulationWorkerClient worker,
        RunPreset preset,
        CpuStateProbeMap? state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(worker);
        ArgumentNullException.ThrowIfNull(preset);
        StopPredicate? predicate = ResolveStopPredicate(preset, state);
        int cycles = 0;
        bool stopHit = false;
        for (; cycles < Math.Max(1, preset.MaxCycles); cycles++)
        {
            if (cancellationToken.IsCancellationRequested) break;
            await worker.TickAsync(preset.Clock, cancellationToken);
            if (predicate is not null && await predicate.EvaluateAsync(worker, cancellationToken))
            {
                stopHit = true;
                cycles++;
                break;
            }
        }
        return new RunResult(cycles, stopHit);
    }

    private static StopPredicate? ResolveStopPredicate(RunPreset preset, CpuStateProbeMap? state)
    {
        if (!string.IsNullOrWhiteSpace(preset.StopWhen))
        {
            return ParseStopWhen(preset.StopWhen!);
        }
        // Sensible default: stop when the design's halted output goes high.
        if (state?.Halted is { } halted)
        {
            return new EqualsPredicate(halted, 1);
        }
        return null;
    }

    private static StopPredicate? ParseStopWhen(string expr)
    {
        // Accept `<path> == <value>` (single-equality form). Anything fancier
        // can land in a later phase if real designs need it.
        int idx = expr.IndexOf("==", StringComparison.Ordinal);
        if (idx <= 0 || idx >= expr.Length - 2) return null;
        string path = expr[..idx].Trim();
        string raw = expr[(idx + 2)..].Trim();
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(raw)) return null;
        ulong value = raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? ulong.Parse(raw.AsSpan(2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture)
            : ulong.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
        return new EqualsPredicate(path, value);
    }

    private abstract class StopPredicate
    {
        public abstract Task<bool> EvaluateAsync(SimulationWorkerClient worker, CancellationToken cancellationToken);
    }

    private sealed class EqualsPredicate(string path, ulong expected) : StopPredicate
    {
        public override async Task<bool> EvaluateAsync(SimulationWorkerClient worker, CancellationToken cancellationToken)
        {
            try
            {
                SignalReadResult r = await worker.ReadSignalAsync(path, cancellationToken);
                string raw = r.Value;
                ulong actual = raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? ulong.Parse(raw.AsSpan(2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture)
                    : ulong.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out ulong dec) ? dec : 0;
                return actual == expected;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }
}

public sealed record ProgramLoadResult(int Written, int Failed, int ParseErrors);

public sealed record RunResult(int Cycles, bool StopConditionHit);
