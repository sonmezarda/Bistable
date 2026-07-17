using Avalonia.Headless.XUnit;
using Bistable.App.Services;
using Bistable.App.ViewModels;
using Bistable.App.Views;
using Bistable.Core.Design.Schematic;

namespace Bistable.UiTests;

[Trait("Category", "UI")]
public sealed class LiveReloadWorkspaceTests
{
    [AvaloniaFact]
    public async Task FileWrite_ReloadsGraph_SurfacesError_AndRecovers()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"bistable-live-ui-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string sourcePath = Path.Combine(directory, "top.sv");
        string projectPath = Path.Combine(directory, "top.bistable.json");
        string prefsPath = Path.Combine(directory, "preferences.json");
        await File.WriteAllTextAsync(sourcePath, ValidSource(inverted: false));
        await File.WriteAllTextAsync(projectPath, """
            {
              "topModule": "top",
              "sources": ["top.sv"],
              "trace": { "enabled": false, "format": "vcd", "depth": 1 },
              "liveReload": { "enabled": false, "debounceMs": 100 }
            }
            """);

        MainWindowViewModel vm = new(
            new BistableWorkspace(
                new ProjectDialogService(),
                new DesignLoadService(),
                new Bistable.Verilator.SimulationWorkerBuilder(),
                new PreviewSimulationService(),
                new LayoutStateService(Path.Combine(directory, "layout.json"))),
            loadPersistedLayout: false,
            preferencesStore: new UserPreferencesStore(prefsPath));
        try
        {
            await vm.LoadProjectFromPathAsync(projectPath, CancellationToken.None);
            Assert.True(vm.PrimitivesByModule.ContainsKey("top"), vm.Status);
            Assert.IsType<BufferPrimitive>(Assert.Single(vm.PrimitivesByModule["top"]));
            Assert.NotEmpty(vm.SourceDocuments);

            await File.WriteAllTextAsync(sourcePath, ValidSource(inverted: true));
            vm.QueueLiveReloadForTest([sourcePath]);
            await vm.WhenLiveReloadIdleAsync().WaitAsync(TimeSpan.FromSeconds(4));

            Assert.IsType<InverterPrimitive>(Assert.Single(vm.PrimitivesByModule["top"]));
            Assert.False(vm.IsSchematicStale);
            Assert.True(vm.LastLiveReloadElapsedMs <= 2000, $"Reload took {vm.LastLiveReloadElapsedMs:F0} ms.");

            await File.WriteAllTextAsync(sourcePath, "module top(input logic a, output logic y); assign y = ; endmodule");
            vm.QueueLiveReloadForTest([sourcePath]);
            await vm.WhenLiveReloadIdleAsync().WaitAsync(TimeSpan.FromSeconds(4));

            Assert.True(vm.IsSchematicStale);
            ElaborationDiagnostic diagnostic = Assert.Single(vm.ElaborationDiagnostics, d => d.Severity == ElaborationDiagnosticSeverity.Error);
            Assert.Equal(sourcePath, diagnostic.FilePath);
            vm.SelectedElaborationDiagnostic = diagnostic;
            Assert.Equal(sourcePath, vm.SelectedSourceDocument?.FilePath);
            Assert.True(vm.SourceNavigationLine >= 1);

            await File.WriteAllTextAsync(sourcePath, ValidSource(inverted: false));
            vm.QueueLiveReloadForTest([sourcePath]);
            await vm.WhenLiveReloadIdleAsync().WaitAsync(TimeSpan.FromSeconds(4));

            Assert.False(vm.IsSchematicStale);
            Assert.Empty(vm.ElaborationDiagnostics);
            Assert.IsType<BufferPrimitive>(Assert.Single(vm.PrimitivesByModule["top"]));

            await File.WriteAllTextAsync(sourcePath, """
                module top(input logic a, input logic b, output logic y);
                    assign y = a & b;
                endmodule
                """);
            vm.QueueLiveReloadForTest([sourcePath]);
            await vm.WhenLiveReloadIdleAsync().WaitAsync(TimeSpan.FromSeconds(4));

            Assert.Equal(["a", "b"], vm.Inputs.Select(static input => input.Name));
            Assert.False(vm.IsSchematicStale);
        }
        finally
        {
            vm.StopLiveReload();
            Directory.Delete(directory, recursive: true);
        }
    }

    [AvaloniaFact]
    public void SourceWorkspaceView_CreatesIdeSurface()
    {
        SourceWorkspaceView view = new();
        Assert.NotNull(view.Content);
    }

    private static string ValidSource(bool inverted) => $$"""
        module top(input logic a, output logic y);
            assign y = {{(inverted ? "~a" : "a")}};
        endmodule
        """;
}
