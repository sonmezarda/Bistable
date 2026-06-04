using System.Text.Json;

namespace Bistable.Core.Projects;

public sealed record ProjectConfiguration
{
    public required string TopModule { get; init; }

    public IReadOnlyList<string> Sources { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> IncludeDirs { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, string> Defines { get; init; } = new Dictionary<string, string>();

    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();

    public IReadOnlyList<string> VerilatorOptions { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Optional physical build-directory label for generated native workers.
    /// The Verilator top module remains <see cref="TopModule"/>; this only
    /// separates artifacts for alternate builds of the same logical design
    /// such as RTL vs gate-level workers.
    /// </summary>
    public string? WorkerBuildName { get; init; }

    /// <summary>
    /// Phase 3 (P3-2): when true, the worker is built with Verilator's
    /// <c>--public-flat-rw</c> flag so every hierarchical signal becomes a
    /// publicly addressable field on the compiled model. The GUI then probes
    /// any internal signal via <c>ReadSignal</c>/<c>WriteSignal</c>/<c>ForceSignal</c>.
    /// <para>Default: <c>true</c>. Set to <c>false</c> on very large designs
    /// where the flag noticeably slows compilation or bloats the binary —
    /// the probe API will then return a structured error.</para>
    /// </summary>
    public bool EnableInternalProbes { get; init; } = true;

    public IReadOnlyList<ClockHint> Clocks { get; init; } = Array.Empty<ClockHint>();

    public IReadOnlyList<ResetHint> Resets { get; init; } = Array.Empty<ResetHint>();

    public TraceConfiguration Trace { get; init; } = new();

    /// <summary>
    /// Phase 5: optional CPU-style runtime metadata. When present, the GUI
    /// exposes a Run panel that drives reset cycles, loads program images
    /// into the probed memory paths, and ticks the clock through one of the
    /// declared presets. Null for designs that aren't CPU-shaped.
    /// </summary>
    public CpuRuntimeConfiguration? Runtime { get; init; }

    /// <summary>
    /// Phase 6: optional gate-level synthesis configuration. When
    /// <see cref="SynthesisConfiguration.Enabled"/> is true, the GUI offers
    /// a "Synthesize" action that invokes Yosys (or another backend) on the
    /// project's sources and renders the resulting netlist alongside the RTL.
    /// Null for designs that don't synthesise.
    /// </summary>
    public SynthesisConfiguration? Synthesis { get; init; }

    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    public static async Task<ProjectConfiguration> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using FileStream stream = File.OpenRead(path);
        ProjectConfiguration? configuration = await JsonSerializer.DeserializeAsync<ProjectConfiguration>(
            stream,
            JsonOptions,
            cancellationToken);

        return configuration is null
            ? throw new InvalidDataException($"Project file '{path}' is empty or invalid.")
            : configuration;
    }
}
