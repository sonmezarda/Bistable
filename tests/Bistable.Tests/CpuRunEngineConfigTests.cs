using System.Text.Json;
using Bistable.App.Services;
using Bistable.Core.Projects;

namespace Bistable.Tests;

/// <summary>
/// Phase 5 P5-2/P5-6 coverage. The runtime config must:
///   - round-trip through the same JSON pipeline ProjectConfiguration uses,
///   - tolerate missing optional fields (reset-only configs, no presets, …),
///   - load the bundled RISC-V sample without losing state probe paths.
///
/// Engine-level tests live in CpuRunEngineTests once we can spin up a worker
/// in-process; this file just pins the config schema so future changes can't
/// silently drop fields the UI depends on.
/// </summary>
public sealed class CpuRunEngineConfigTests
{
    [Fact]
    public void Runtime_RoundTripsThroughProjectConfigurationJson()
    {
        ProjectConfiguration original = new()
        {
            TopModule = "top",
            Sources = ["top.sv"],
            Runtime = new CpuRuntimeConfiguration(
                Reset: new CpuResetSequence("rst_n", ActiveLevel: 0, Cycles: 4),
                ProgramImages: [new ProgramImageBinding("foo.hex", "hex", "top.imem.mem")],
                RunPresets: [new RunPreset("smoke", "clk", MaxCycles: 100, StopWhen: "top.halted == 1")],
                State: new CpuStateProbeMap(Pc: "top.pc", Halted: "top.halted")),
        };

        string json = JsonSerializer.Serialize(original, ProjectConfiguration.JsonOptions);
        ProjectConfiguration? roundTripped = JsonSerializer.Deserialize<ProjectConfiguration>(json, ProjectConfiguration.JsonOptions);

        Assert.NotNull(roundTripped);
        Assert.NotNull(roundTripped!.Runtime);
        Assert.Equal("rst_n", roundTripped.Runtime!.Reset!.Signal);
        Assert.Equal(0, roundTripped.Runtime.Reset.ActiveLevel);
        Assert.Equal(4, roundTripped.Runtime.Reset.Cycles);
        Assert.Equal("foo.hex", roundTripped.Runtime.ProgramImages![0].Path);
        Assert.Equal("hex", roundTripped.Runtime.ProgramImages[0].Format);
        Assert.Equal("smoke", roundTripped.Runtime.RunPresets![0].Name);
        Assert.Equal(100, roundTripped.Runtime.RunPresets[0].MaxCycles);
        Assert.Equal("top.halted == 1", roundTripped.Runtime.RunPresets[0].StopWhen);
        Assert.Equal("top.pc", roundTripped.Runtime.State!.Pc);
        Assert.Equal("top.halted", roundTripped.Runtime.State.Halted);
    }

    [Fact]
    public void Runtime_IsOptional_WhenAbsentFromJson()
    {
        string json = """
            {
              "topModule": "top",
              "sources": ["top.sv"]
            }
            """;
        ProjectConfiguration? config = JsonSerializer.Deserialize<ProjectConfiguration>(json, ProjectConfiguration.JsonOptions);
        Assert.NotNull(config);
        Assert.Null(config!.Runtime);
    }

    [Fact]
    public async Task RiscvSample_LoadsRuntimeConfig()
    {
        // Catches accidental schema breakage of the bundled CPU sample —
        // its runtime config is what makes "Run sample program" work in the GUI.
        string path = LocateRiscvConfig();
        Assert.True(File.Exists(path), $"Sample config missing at {path}");

        ProjectConfiguration config = await ProjectConfiguration.LoadAsync(path, CancellationToken.None);
        Assert.NotNull(config.Runtime);
        Assert.Equal("rst_n", config.Runtime!.Reset!.Signal);
        Assert.Equal("riscv_single_cycle_top.u_imem.mem", config.Runtime.ProgramImages![0].ProbePath);
        Assert.Equal("riscv_single_cycle_top.pc", config.Runtime.State!.Pc);
        Assert.Equal("riscv_single_cycle_top.halted", config.Runtime.State.Halted);
        Assert.Equal("Run sample program", config.Runtime.RunPresets![0].Name);
    }

    [Fact]
    public async Task LoadCpuProgramOverride_ChangesProgramDisplayName_AndPropagatesAtRunTime()
    {
        // Lightweight VM-level test: setting an override path must surface in
        // CpuProgramDisplayName so the toolbar label reflects the user's pick,
        // and re-setting null must restore the config default. Heavyweight
        // worker-driven runs that actually use the override are covered by
        // CpuRunEngineIntegrationTests.
        string layoutPath = Path.Combine(Path.GetTempPath(), $"bistable-tests-{Guid.NewGuid():N}", "layout.json");
        string prefsPath  = Path.Combine(Path.GetTempPath(), $"bistable-prefs-cpu-{Guid.NewGuid():N}.json");
        try
        {
            Bistable.App.Services.BistableWorkspace workspace = new(
                new Bistable.App.Services.ProjectDialogService(),
                new Bistable.App.Services.DesignLoadService(),
                new Bistable.Verilator.SimulationWorkerBuilder(),
                new Bistable.App.Services.PreviewSimulationService(),
                new Bistable.App.Services.LayoutStateService(layoutPath));

            Bistable.App.ViewModels.MainWindowViewModel vm = new(
                workspace,
                loadPersistedLayout: false,
                preferencesStore: new Bistable.App.Services.UserPreferencesStore(prefsPath));

            // No project loaded yet: display name falls back to "(no program)".
            Assert.Equal("(no program)", vm.CpuProgramDisplayName);
            Assert.Null(vm.CpuProgramOverridePath);

            vm.SetCpuProgramOverride("/tmp/custom_program.hex");
            Assert.Equal("custom_program.hex", vm.CpuProgramDisplayName);
            Assert.Equal("/tmp/custom_program.hex", vm.CpuProgramOverridePath);

            vm.SetCpuProgramOverride(null);
            Assert.Null(vm.CpuProgramOverridePath);
            Assert.Equal("(no program)", vm.CpuProgramDisplayName);

            await Task.CompletedTask;
        }
        finally
        {
            if (File.Exists(prefsPath)) File.Delete(prefsPath);
            string? layoutDir = Path.GetDirectoryName(layoutPath);
            if (layoutDir is not null && Directory.Exists(layoutDir)) Directory.Delete(layoutDir, recursive: true);
        }
    }

    private static string LocateRiscvConfig()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "samples", "riscv_single_cycle", "riscv_single_cycle.bistable.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return string.Empty;
    }
}
