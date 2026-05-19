using Bistable.App.Services;
using Bistable.App.ViewModels;
using Bistable.Verilator;

namespace Bistable.Tests;

public sealed class DockingCommandTests
{
    [Theory]
    [InlineData(DockPanelKind.Project, DockZone.Left)]
    [InlineData(DockPanelKind.Project, DockZone.Right)]
    [InlineData(DockPanelKind.Project, DockZone.Bottom)]
    [InlineData(DockPanelKind.Project, DockZone.Hidden)]
    [InlineData(DockPanelKind.Waveform, DockZone.Left)]
    [InlineData(DockPanelKind.Waveform, DockZone.Right)]
    [InlineData(DockPanelKind.Waveform, DockZone.Bottom)]
    [InlineData(DockPanelKind.Waveform, DockZone.Hidden)]
    [InlineData(DockPanelKind.Schematic, DockZone.Left)]
    [InlineData(DockPanelKind.Schematic, DockZone.Right)]
    [InlineData(DockPanelKind.Schematic, DockZone.Bottom)]
    [InlineData(DockPanelKind.Schematic, DockZone.Hidden)]
    public void DockPanelCommandMovesRequestedPanelToRequestedZone(DockPanelKind panelKind, DockZone zone)
    {
        MainWindowViewModel viewModel = CreateViewModel();

        viewModel.DockPanelCommand.Execute(new DockCommandParameter(panelKind, zone));

        Assert.Equal(zone, GetZone(viewModel, panelKind));
        if (zone == DockZone.Hidden)
        {
            Assert.DoesNotContain(viewModel.LeftDockPanels, panel => panel.Kind == panelKind);
            Assert.DoesNotContain(viewModel.RightDockPanels, panel => panel.Kind == panelKind);
            Assert.DoesNotContain(viewModel.BottomDockPanels, panel => panel.Kind == panelKind);
        }
        else
        {
            Assert.Contains(GetCollection(viewModel, zone), panel => panel.Kind == panelKind);
        }
    }

    [Fact]
    public void DockPanelCommandOnlyMovesRequestedPanel()
    {
        MainWindowViewModel viewModel = CreateViewModel();

        viewModel.DockPanelCommand.Execute(new DockCommandParameter(DockPanelKind.Waveform, DockZone.Right));

        Assert.Equal(DockZone.Left, viewModel.ProjectDockZone);
        Assert.Equal(DockZone.Right, viewModel.WaveformDockZone);
        Assert.Equal(DockZone.Right, viewModel.SchematicDockZone);
        Assert.DoesNotContain(viewModel.LeftDockPanels, panel => panel.Kind == DockPanelKind.Waveform);
        Assert.Contains(viewModel.RightDockPanels, panel => panel.Kind == DockPanelKind.Waveform);
    }

    [Fact]
    public void DockZoneChangeNotificationSeesRebuiltDockCollections()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        bool observedUpdatedCollections = false;

        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.WaveformDockZone))
            {
                observedUpdatedCollections =
                    viewModel.WaveformDockZone == DockZone.Left
                    && viewModel.LeftDockPanels.Any(panel => panel.Kind == DockPanelKind.Waveform)
                    && viewModel.SelectedLeftDockPanel?.Kind == DockPanelKind.Waveform;
            }
        };

        viewModel.DockPanelCommand.Execute(new DockCommandParameter(DockPanelKind.Waveform, DockZone.Left));

        Assert.True(observedUpdatedCollections);
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

        return new MainWindowViewModel(workspace, loadPersistedLayout: false);
    }

    private static DockZone GetZone(MainWindowViewModel viewModel, DockPanelKind panelKind) =>
        panelKind switch
        {
            DockPanelKind.Project => viewModel.ProjectDockZone,
            DockPanelKind.Waveform => viewModel.WaveformDockZone,
            DockPanelKind.Schematic => viewModel.SchematicDockZone,
            _ => throw new ArgumentOutOfRangeException(nameof(panelKind), panelKind, null)
        };

    private static IReadOnlyCollection<DockPanelViewModel> GetCollection(MainWindowViewModel viewModel, DockZone zone) =>
        zone switch
        {
            DockZone.Left => viewModel.LeftDockPanels,
            DockZone.Right => viewModel.RightDockPanels,
            DockZone.Bottom => viewModel.BottomDockPanels,
            DockZone.Hidden => Array.Empty<DockPanelViewModel>(),
            _ => throw new ArgumentOutOfRangeException(nameof(zone), zone, null)
        };
}
