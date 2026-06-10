using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Projects;
using Bistable.Verilator;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bistable.App.Services;

/// <summary>
/// Builds a simulation worker from Yosys's synthesized Verilog artifact. This
/// is intentionally separate from <see cref="MainWindowViewModel"/>-style UI
/// orchestration: the service owns the artifact contract and delegates the
/// actual metadata extraction + native build to the existing Verilator
/// pipeline.
/// </summary>
public sealed class GateLevelWorkerBuildService(
    DesignLoadService? designLoader = null,
    SimulationWorkerBuilder? workerBuilder = null)
{
    private readonly DesignLoadService _designLoader = designLoader ?? new DesignLoadService();
    private readonly SimulationWorkerBuilder _workerBuilder = workerBuilder ?? new SimulationWorkerBuilder();

    public async Task<GateLevelWorkerBuildResult> BuildAsync(
        ProjectConfiguration rtlProject,
        SynthesisConfiguration synthesis,
        string projectDirectory,
        CancellationToken cancellationToken = default,
        IProgress<SimulationWorkerBuildProgress>? progress = null,
        DesignAst? rtlAst = null)
    {
        ArgumentNullException.ThrowIfNull(rtlProject);
        ArgumentNullException.ThrowIfNull(synthesis);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);

        string synthesizedVerilog = ResolvePath(projectDirectory, synthesis.OutputVerilog);
        if (!File.Exists(synthesizedVerilog))
        {
            throw new FileNotFoundException(
                $"Synthesized Verilog artifact not found: {synthesis.OutputVerilog}",
                synthesizedVerilog);
        }

        ProjectConfiguration gateProject = BuildGateLevelProject(rtlProject, synthesis, synthesizedVerilog);
        DesignLoadResult gateDesign = await _designLoader.ElaborateAsync(
            gateProject,
            projectDirectory,
            cancellationToken);
        DesignAst gateAst = gateDesign.Ast
            ?? throw new InvalidOperationException("Gate-level Verilator elaboration did not produce an AST.");

        DesignAst? sourceAst = rtlAst;
        if (sourceAst is null && gateProject.EnableInternalProbes)
        {
            DesignLoadResult rtlDesign = await _designLoader.ElaborateAsync(
                rtlProject,
                projectDirectory,
                cancellationToken);
            sourceAst = rtlDesign.Ast;
        }

        LoweredMemoryProbeMap memoryMap = sourceAst is not null && gateProject.EnableInternalProbes
            ? LoweredMemoryProbeMapper.Build(sourceAst, gateAst, gateProject.TopModule)
            : new LoweredMemoryProbeMap(
                Array.Empty<ProbeEntry>(),
                new GateRuntimeProbeManifest(gateProject.TopModule, Array.Empty<GateMemoryProbeMapping>()));
        ValidateRuntimeMemoryBindings(rtlProject.Runtime, memoryMap.Manifest);

        SimulationWorkerBuildResult worker = await _workerBuilder.BuildAsync(
            gateProject,
            gateDesign.Metadata,
            projectDirectory,
            cancellationToken,
            progress,
            gateAst,
            memoryMap.SupplementalProbes);
        string manifestPath = await WriteRuntimeManifestAsync(
            worker.BuildDirectory,
            memoryMap.Manifest,
            cancellationToken);

        return new GateLevelWorkerBuildResult(
            gateProject,
            gateDesign.Design,
            gateAst,
            worker,
            synthesizedVerilog,
            memoryMap.Manifest,
            manifestPath);
    }

    public static ProjectConfiguration BuildGateLevelProject(
        ProjectConfiguration rtlProject,
        SynthesisConfiguration synthesis,
        string synthesizedVerilogPath)
    {
        ArgumentNullException.ThrowIfNull(rtlProject);
        ArgumentNullException.ThrowIfNull(synthesis);
        ArgumentException.ThrowIfNullOrWhiteSpace(synthesizedVerilogPath);

        string top = synthesis.TopModule ?? rtlProject.TopModule;
        return new ProjectConfiguration
        {
            TopModule = top,
            WorkerBuildName = top + "__gate",
            Sources = [synthesizedVerilogPath],
            IncludeDirs = Array.Empty<string>(),
            Defines = new Dictionary<string, string>(),
            Parameters = new Dictionary<string, string>(),
            VerilatorOptions = MergeVerilatorOptions(rtlProject.VerilatorOptions),
            EnableInternalProbes = rtlProject.EnableInternalProbes,
            Clocks = rtlProject.Clocks,
            Resets = rtlProject.Resets,
            Trace = rtlProject.Trace,
            Runtime = rtlProject.Runtime,
            Synthesis = null,
        };
    }

    private static string ResolvePath(string projectDirectory, string maybeRelative) =>
        Path.IsPathRooted(maybeRelative)
            ? maybeRelative
            : Path.GetFullPath(Path.Combine(projectDirectory, maybeRelative));

    private static IReadOnlyList<string> MergeVerilatorOptions(IReadOnlyList<string> rtlOptions)
    {
        List<string> options = [];
        options.AddRange(rtlOptions);
        if (!options.Contains("--Wno-UNOPTFLAT", StringComparer.Ordinal))
        {
            options.Add("--Wno-UNOPTFLAT");
        }
        return options;
    }

    private static void ValidateRuntimeMemoryBindings(
        CpuRuntimeConfiguration? runtime,
        GateRuntimeProbeManifest manifest)
    {
        if (runtime?.ProgramImages is not { Count: > 0 })
        {
            return;
        }

        Dictionary<string, GateMemoryProbeMapping> mappings = manifest.Memories
            .ToDictionary(static memory => memory.LogicalPath, StringComparer.Ordinal);
        foreach (ProgramImageBinding binding in runtime.ProgramImages)
        {
            if (!mappings.TryGetValue(binding.ProbePath, out GateMemoryProbeMapping? mapping))
            {
                throw new InvalidOperationException(
                    $"Gate-level program memory '{binding.ProbePath}' is not present in the source design memory map.");
            }
            if (mapping.Kind == GateMemoryMappingKind.Unresolved)
            {
                throw new InvalidOperationException(
                    $"Gate-level program memory '{binding.ProbePath}' is not addressable after synthesis. "
                    + mapping.Diagnostic);
            }
        }
    }

    private static async Task<string> WriteRuntimeManifestAsync(
        string buildDirectory,
        GateRuntimeProbeManifest manifest,
        CancellationToken cancellationToken)
    {
        string path = Path.Combine(buildDirectory, "gate-runtime-map.json");
        string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        });
        await File.WriteAllTextAsync(path, json, cancellationToken);
        return path;
    }
}

public sealed record GateLevelWorkerBuildResult(
    ProjectConfiguration Project,
    ElaboratedDesign Design,
    DesignAst Ast,
    SimulationWorkerBuildResult Worker,
    string SynthesizedVerilogPath,
    GateRuntimeProbeManifest RuntimeProbeManifest,
    string RuntimeProbeManifestPath);
