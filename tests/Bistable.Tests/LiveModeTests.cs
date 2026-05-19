using Bistable.App.Services;
using Bistable.App.ViewModels;
using Bistable.Verilator;

namespace Bistable.Tests;

public sealed class LiveModeTests
{
    [Fact]
    public async Task LiveModeUpdatesPreviewOutputsWhenInputsChange()
    {
        string root = FindRepositoryRoot();
        string samplePath = Path.Combine(root, "samples", "alu", "alu.bistable.json");
        MainWindowViewModel viewModel = CreateViewModel();

        await viewModel.LoadProjectFromPathAsync(samplePath, CancellationToken.None);

        viewModel.LiveModeEnabled = true;
        GetInput(viewModel, "a").Value = "0x12";
        GetInput(viewModel, "b").Value = "0x22";
        GetInput(viewModel, "op").Value = "0";

        await Task.Delay(250);

        Assert.Equal("0x034", GetOutput(viewModel, "y").Value);
        Assert.Equal("0", GetOutput(viewModel, "zero").Value);
    }

    [Fact]
    public async Task DisabledLiveModeDoesNotAutoEvaluatePreview()
    {
        string root = FindRepositoryRoot();
        string samplePath = Path.Combine(root, "samples", "alu", "alu.bistable.json");
        MainWindowViewModel viewModel = CreateViewModel();

        await viewModel.LoadProjectFromPathAsync(samplePath, CancellationToken.None);

        viewModel.LiveModeEnabled = false;
        GetInput(viewModel, "a").Value = "0x12";
        GetInput(viewModel, "b").Value = "0x22";
        GetInput(viewModel, "op").Value = "0";

        await Task.Delay(250);

        Assert.Equal("0x0", GetOutput(viewModel, "y").Value);
    }

    private static MainWindowViewModel CreateViewModel()
    {
        string layoutPath = Path.Combine(Path.GetTempPath(), "bistable-tests", Guid.NewGuid().ToString("N"), "layout.json");
        BistableWorkspace workspace = new(
            new ProjectDialogService(),
            new DesignLoadService(),
            new SimulationWorkerBuilder(),
            new PreviewSimulationService(),
            new LayoutStateService(layoutPath));

        return new MainWindowViewModel(workspace, loadPersistedLayout: false, liveEvaluationDelayMs: 20);
    }

    private static SignalViewModel GetInput(MainWindowViewModel viewModel, string name) =>
        viewModel.Inputs.Single(signal => string.Equals(signal.Name, name, StringComparison.OrdinalIgnoreCase));

    private static SignalViewModel GetOutput(MainWindowViewModel viewModel, string name) =>
        viewModel.Outputs.Single(signal => string.Equals(signal.Name, name, StringComparison.OrdinalIgnoreCase));

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Bistable.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root could not be found.");
    }
}
