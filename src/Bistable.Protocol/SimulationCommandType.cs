namespace Bistable.Protocol;

public enum SimulationCommandType
{
    // ── Simulation stepping ─────────────────────────────────────────
    SetInput,
    Eval,
    Tick,
    RunCycles,
    Reset,
    GetSnapshot,
    Pause,

    // ── Live internal probe ─────────────────────────────────────────
    /// <summary>Read a single hierarchy-path signal's live value.</summary>
    ReadSignal,

    /// <summary>
    /// One-shot write to a hierarchy-path signal. Subsequent <c>Eval</c> may
    /// overwrite the value as simulation propagates. For sticky writes use
    /// <see cref="ForceSignal"/>.
    /// </summary>
    WriteSignal,

    /// <summary>
    /// Pin a hierarchy-path signal to a value. The worker re-applies the
    /// forced value at the top of every subsequent <c>Eval</c> until
    /// <see cref="ReleaseSignal"/> clears it.
    /// </summary>
    ForceSignal,

    /// <summary>Clears a prior <see cref="ForceSignal"/>.</summary>
    ReleaseSignal,

    /// <summary>Read a contiguous range of cells from a memory signal.</summary>
    ReadMemory,

    /// <summary>Write a single memory cell.</summary>
    WriteMemory,

    /// <summary>
    /// Enumerate every probe available in the worker's probe table. Used by
    /// the GUI to populate signal pickers and to verify the worker's probe
    /// table shape matches the AST.
    /// </summary>
    ListProbes
}
