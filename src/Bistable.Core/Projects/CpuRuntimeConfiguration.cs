namespace Bistable.Core.Projects;

/// <summary>
/// Phase 5: optional CPU-style runtime metadata. Describes how to bring the
/// design out of reset, where to load program images, and which hierarchy
/// signals constitute the architectural state. The Run panel uses this to
/// drive reset/run cycles and surface PC/registers/pass/fail without
/// hardcoding RV32I internals.
/// </summary>
public sealed record CpuRuntimeConfiguration(
    CpuResetSequence? Reset = null,
    IReadOnlyList<ProgramImageBinding>? ProgramImages = null,
    IReadOnlyList<RunPreset>? RunPresets = null,
    CpuStateProbeMap? State = null);

/// <summary>
/// How to assert and de-assert reset before the CPU starts executing.
/// Active level + tick count matches what the existing reset infra accepts.
/// </summary>
public sealed record CpuResetSequence(
    string Signal,
    int ActiveLevel,
    int Cycles);

/// <summary>
/// One program image to write into a memory probe path after reset.
/// `BaseAddress` is the cell index (0-based), matching how
/// <c>WriteMemoryAsync</c> addresses cells.
/// </summary>
public sealed record ProgramImageBinding(
    string Path,
    string Format,         // "hex" or "bin"
    string ProbePath,      // e.g. "rv32i_top.imem.mem"
    int BaseAddress = 0);

/// <summary>
/// A named "Run" button preset. Caps execution so the worker never spins
/// forever and lets the user pick "run smoke" / "run 10k" / etc.
/// </summary>
public sealed record RunPreset(
    string Name,
    string Clock,
    int MaxCycles,
    string? StopWhen = null);   // e.g. "halted == 1" — interpreted by the engine

/// <summary>
/// Hierarchy paths to the canonical CPU state. Any field may be null when the
/// design doesn't expose that signal; the Run panel only renders the ones
/// that have a path.
/// </summary>
public sealed record CpuStateProbeMap(
    string? Pc = null,
    string? Instruction = null,
    string? Halted = null,
    string? RegisterFile = null,
    string? DataMemory = null,
    string? Pass = null,
    string? Fail = null);
