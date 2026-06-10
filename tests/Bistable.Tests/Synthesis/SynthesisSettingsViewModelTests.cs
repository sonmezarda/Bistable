using System.Reflection;
using System.Text.Json;
using Bistable.App.Services;
using Bistable.App.ViewModels;
using Bistable.Core.Projects;
using Bistable.Verilator;

namespace Bistable.Tests.Synthesis;

public sealed class SynthesisSettingsViewModelTests
{
    [Fact]
    public void SynthesisSettings_DefaultsAreAvailableForProjectWithoutJsonBlock()
    {
        using TempVm temp = CreateVm(new ProjectConfiguration
        {
            TopModule = "top",
            Sources = ["top.sv"],
        });

        Assert.True(temp.ViewModel.IsSynthesisAvailable);
        Assert.True(temp.ViewModel.SynthesisEnabled);
        Assert.Equal("top", temp.ViewModel.SynthesisTopModule);
        Assert.Equal(".bistable/synthesis/netlist.json", temp.ViewModel.SynthesisOutputJson);
        Assert.Equal(".bistable/synthesis/netlist.sv", temp.ViewModel.SynthesisOutputVerilog);
        Assert.True(temp.ViewModel.SynthesizeCommand.CanExecute(null));
    }

    [Fact]
    public void SimulationTarget_DefaultsToRtl_AndGateRequiresBuiltWorker()
    {
        using TempVm temp = CreateVm(new ProjectConfiguration
        {
            TopModule = "top",
            Sources = ["top.sv"],
        });

        Assert.Equal(SimulationTarget.Rtl, temp.ViewModel.SelectedSimulationTarget);
        Assert.False(temp.ViewModel.IsGateLevelWorkerReady);
        Assert.False(temp.ViewModel.CanCompareRtlAndGate);
        Assert.Contains(SimulationTarget.GateLevel, temp.ViewModel.AvailableSimulationTargets);

        temp.ViewModel.SelectedSimulationTarget = SimulationTarget.GateLevel;

        Assert.Equal(SimulationTarget.Rtl, temp.ViewModel.SelectedSimulationTarget);
        Assert.Contains("Synthesize", temp.ViewModel.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void SynthesisSettings_UpdateProjectConfigurationInMemory()
    {
        using TempVm temp = CreateVm(new ProjectConfiguration
        {
            TopModule = "top",
            Sources = ["top.sv"],
        });

        temp.ViewModel.SynthesisTopModule = "gate_top";
        temp.ViewModel.SynthesisOutputJson = "build/gates.json";
        temp.ViewModel.SynthesisOutputVerilog = "build/gates.sv";
        temp.ViewModel.SynthesisGenericCells = false;
        temp.ViewModel.SynthesisFlatten = true;

        ProjectConfiguration project = ReadCurrentProject(temp.ViewModel);
        Assert.NotNull(project.Synthesis);
        Assert.Equal("gate_top", project.Synthesis!.TopModule);
        Assert.Equal("build/gates.json", project.Synthesis.OutputJson);
        Assert.Equal("build/gates.sv", project.Synthesis.OutputVerilog);
        Assert.False(project.Synthesis.GenericCells);
        Assert.True(project.Synthesis.Flatten);
    }

    [Fact]
    public void RoutingQuality_UpdateProjectConfigurationInMemory()
    {
        using TempVm temp = CreateVm(new ProjectConfiguration
        {
            TopModule = "top",
            Sources = ["top.sv"],
        });

        temp.ViewModel.GateRoutingQuality = RoutingQuality.FastPreview;
        temp.ViewModel.GateAutoDowngradeLargeGraphs = false;
        temp.ViewModel.GatePinLabelMode = GatePinLabelMode.Always;
        temp.ViewModel.GateGroupBusPinLabels = false;
        temp.ViewModel.GatePinLabelCompactZoom = 0.7;
        temp.ViewModel.GatePinLabelDetailedZoom = 1.2;

        ProjectConfiguration project = ReadCurrentProject(temp.ViewModel);
        Assert.Equal(RoutingQuality.FastPreview, project.Schematic.RoutingQuality);
        Assert.False(project.Schematic.AutoDowngradeLargeGraphs);
        Assert.Equal(GatePinLabelMode.Always, project.Schematic.GatePinLabelMode);
        Assert.False(project.Schematic.GroupGateBusPinLabels);
        Assert.Equal(0.7, project.Schematic.GatePinLabelCompactZoom);
        Assert.Equal(1.2, project.Schematic.GatePinLabelDetailedZoom);
    }

    [Fact]
    public async Task SaveSynthesisSettings_PersistsDefaultBlockWhenProjectHadNone()
    {
        using TempVm temp = CreateVm(new ProjectConfiguration
        {
            TopModule = "top",
            Sources = ["top.sv"],
        });

        await InvokeSaveSynthesisSettingsAsync(temp.ViewModel);

        ProjectConfiguration saved = (await ProjectConfiguration.LoadAsync(temp.ProjectPath, CancellationToken.None));
        Assert.NotNull(saved.Synthesis);
        Assert.True(saved.Synthesis!.Enabled);
        Assert.Equal("top", saved.Synthesis.TopModule);
        Assert.Equal(".bistable/synthesis/netlist.json", saved.Synthesis.OutputJson);
        Assert.Equal(".bistable/synthesis/netlist.sv", saved.Synthesis.OutputVerilog);
    }

    [Fact]
    public async Task SaveProjectSettings_PersistsRoutingQuality()
    {
        using TempVm temp = CreateVm(new ProjectConfiguration
        {
            TopModule = "top",
            Sources = ["top.sv"],
        });
        temp.ViewModel.GateRoutingQuality = RoutingQuality.Production;
        temp.ViewModel.GateAutoDowngradeLargeGraphs = false;
        temp.ViewModel.GatePinLabelMode = GatePinLabelMode.Hidden;
        temp.ViewModel.GateGroupBusPinLabels = false;
        temp.ViewModel.GatePinLabelCompactZoom = 0.65;
        temp.ViewModel.GatePinLabelDetailedZoom = 1.1;

        await InvokeSaveProjectSettingsAsync(temp.ViewModel);

        string json = await File.ReadAllTextAsync(temp.ProjectPath);
        ProjectConfiguration saved = await ProjectConfiguration.LoadAsync(temp.ProjectPath, CancellationToken.None);
        Assert.Contains("\"routingQuality\": \"Production\"", json, StringComparison.Ordinal);
        Assert.Contains("\"autoDowngradeLargeGraphs\": false", json, StringComparison.Ordinal);
        Assert.Contains("\"gatePinLabelMode\": \"Hidden\"", json, StringComparison.Ordinal);
        Assert.Contains("\"groupGateBusPinLabels\": false", json, StringComparison.Ordinal);
        Assert.Equal(RoutingQuality.Production, saved.Schematic.RoutingQuality);
        Assert.False(saved.Schematic.AutoDowngradeLargeGraphs);
        Assert.Equal(GatePinLabelMode.Hidden, saved.Schematic.GatePinLabelMode);
        Assert.False(saved.Schematic.GroupGateBusPinLabels);
        Assert.Equal(0.65, saved.Schematic.GatePinLabelCompactZoom);
        Assert.Equal(1.1, saved.Schematic.GatePinLabelDetailedZoom);
    }

    [Fact]
    public void SynthesisDisabled_DisablesSynthesizeCommand()
    {
        using TempVm temp = CreateVm(new ProjectConfiguration
        {
            TopModule = "top",
            Sources = ["top.sv"],
        });

        temp.ViewModel.SynthesisEnabled = false;

        Assert.False(temp.ViewModel.SynthesizeCommand.CanExecute(null));
    }

    private static TempVm CreateVm(ProjectConfiguration project)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"bistable-synth-vm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string projectPath = Path.Combine(dir, "top.bistable.json");
        File.WriteAllText(projectPath, JsonSerializer.Serialize(project, ProjectConfiguration.JsonOptions));
        MainWindowViewModel vm = new(
            new BistableWorkspace(
                new ProjectDialogService(),
                new DesignLoadService(),
                new SimulationWorkerBuilder(),
                new PreviewSimulationService(),
                new LayoutStateService(Path.Combine(dir, "layout.json"))),
            loadPersistedLayout: false,
            preferencesStore: new UserPreferencesStore(Path.Combine(dir, "prefs.json")));

        SetPrivate(vm, "_currentProject", project);
        SetPrivate(vm, "_currentProjectPath", projectPath);
        SetPrivate(vm, "_currentProjectDirectory", dir);
        InvokePrivate(vm, "RaiseSynthesisSettingsChanged");
        return new TempVm(vm, dir, projectPath);
    }

    private static ProjectConfiguration ReadCurrentProject(MainWindowViewModel vm) =>
        (ProjectConfiguration)GetPrivate(vm, "_currentProject")!;

    private static async Task InvokeSaveSynthesisSettingsAsync(MainWindowViewModel vm)
    {
        MethodInfo method = typeof(MainWindowViewModel).GetMethod(
            "SaveSynthesisSettingsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)method.Invoke(vm, [CancellationToken.None])!;
    }

    private static async Task InvokeSaveProjectSettingsAsync(MainWindowViewModel vm)
    {
        MethodInfo method = typeof(MainWindowViewModel).GetMethod(
            "SaveProjectSettingsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)method.Invoke(vm, [CancellationToken.None])!;
    }

    private static void InvokePrivate(object target, string methodName)
    {
        MethodInfo method = target.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        method.Invoke(target, []);
    }

    private static void SetPrivate(object target, string fieldName, object? value)
    {
        FieldInfo field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(target, value);
    }

    private static object? GetPrivate(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        return field.GetValue(target);
    }

    private sealed record TempVm(MainWindowViewModel ViewModel, string DirectoryPath, string ProjectPath) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                if (Directory.Exists(DirectoryPath))
                {
                    Directory.Delete(DirectoryPath, recursive: true);
                }
            }
            catch
            {
                // best effort
            }
        }
    }
}
