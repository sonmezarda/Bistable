using Bistable.App.Services;
using Bistable.App.ViewModels;
using Bistable.Verilator;

namespace Bistable.Tests;

public sealed class HierarchyScopeTests
{
    [Fact]
    public async Task SelectingHierarchyNodeFiltersTraceSignalsByExactScope()
    {
        string root = FindRepositoryRoot();
        string samplePath = Path.Combine(root, "samples", "hierarchy", "hierarchy.bistable.json");
        MainWindowViewModel viewModel = CreateViewModel();

        await viewModel.LoadProjectFromPathAsync(samplePath, CancellationToken.None);
        viewModel.LiveModeEnabled = false;
        viewModel.BuildCommand.Execute(null);

        await WaitUntilAsync(() => viewModel.TraceSignals.Count > 0, TimeSpan.FromSeconds(20));

        viewModel.SelectedHierarchyPath = "system_top.u_core.u_logic";

        await WaitUntilAsync(() => viewModel.HierarchyScopeSignals.Count > 0, TimeSpan.FromSeconds(5));

        Assert.NotEmpty(viewModel.HierarchyScopeSignals);
        Assert.All(viewModel.HierarchyScopeSignals, signal =>
            Assert.Equal("system_top.u_core.u_logic", signal.ScopePath));
        Assert.Contains(viewModel.HierarchyScopeSignals, signal => signal.Name == "system_top.u_core.u_logic.sum");
        Assert.DoesNotContain(viewModel.HierarchyScopeSignals, signal => signal.Name.Contains("u_status", StringComparison.OrdinalIgnoreCase));

        int before = viewModel.WaveformLanes.Count;
        viewModel.AddHierarchyScopeSignalsToWaveformCommand.Execute(null);
        int expectedAdded = viewModel.HierarchyScopeSignals.Count(signal =>
            viewModel.WaveformLanes.Any(lane => string.Equals(lane.Name, signal.Name, StringComparison.OrdinalIgnoreCase)));

        Assert.True(viewModel.WaveformLanes.Count >= before);
        Assert.Equal(viewModel.HierarchyScopeSignals.Count, expectedAdded);
    }

    [Fact]
    public async Task SelectingInternalSignalKeepsHierarchyContextAndBuildsScopeSummaries()
    {
        string root = FindRepositoryRoot();
        string samplePath = Path.Combine(root, "samples", "hierarchy", "hierarchy.bistable.json");
        MainWindowViewModel viewModel = CreateViewModel();

        await viewModel.LoadProjectFromPathAsync(samplePath, CancellationToken.None);
        viewModel.LiveModeEnabled = false;
        viewModel.BuildCommand.Execute(null);

        await WaitUntilAsync(() => viewModel.TraceSignals.Count > 0, TimeSpan.FromSeconds(20));
        await WaitUntilAsync(() => viewModel.HierarchyTraceScopeSummaries.Count > 0, TimeSpan.FromSeconds(5));

        SignalViewModel sum = viewModel.TraceSignals.Single(signal => signal.Name == "system_top.u_core.u_logic.sum");
        viewModel.SelectedSignal = sum;

        Assert.Equal("system_top", viewModel.SelectedHierarchyPath);

        HierarchyTraceScopeSummaryViewModel logicSummary = viewModel.HierarchyTraceScopeSummaries
            .Single(summary => summary.HierarchyPath == "system_top.u_core.u_logic");
        HierarchyTraceScopeSummaryViewModel coreSummary = viewModel.HierarchyTraceScopeSummaries
            .Single(summary => summary.HierarchyPath == "system_top.u_core");

        Assert.True(logicSummary.ExactSignalCount > 0);
        Assert.Equal(0, logicSummary.DescendantSignalCount);
        Assert.True(coreSummary.DescendantSignalCount >= logicSummary.ExactSignalCount);
    }

    [Fact]
    public async Task SelectedSchematicSignalNameResolvesInternalTraceSignals()
    {
        string root = FindRepositoryRoot();
        string samplePath = Path.Combine(root, "samples", "hierarchy", "hierarchy.bistable.json");
        MainWindowViewModel viewModel = CreateViewModel();

        await viewModel.LoadProjectFromPathAsync(samplePath, CancellationToken.None);
        viewModel.LiveModeEnabled = false;
        viewModel.BuildCommand.Execute(null);

        await WaitUntilAsync(() => viewModel.TraceSignals.Count > 0, TimeSpan.FromSeconds(20));

        viewModel.SelectedSchematicSignalName = "system_top.u_core.u_logic.parity";

        Assert.NotNull(viewModel.SelectedSignal);
        Assert.Equal("system_top.u_core.u_logic.parity", viewModel.SelectedSignal!.Name);
        Assert.Equal("system_top", viewModel.SelectedHierarchyPath);
    }

    [Fact]
    public async Task SelectingHierarchyNodeUpdatesScopeMetadata()
    {
        string root = FindRepositoryRoot();
        string samplePath = Path.Combine(root, "samples", "hierarchy", "hierarchy.bistable.json");
        MainWindowViewModel viewModel = CreateViewModel();

        await viewModel.LoadProjectFromPathAsync(samplePath, CancellationToken.None);
        viewModel.LiveModeEnabled = false;
        viewModel.BuildCommand.Execute(null);

        await WaitUntilAsync(() => viewModel.TraceSignals.Count > 0, TimeSpan.FromSeconds(20));

        viewModel.SelectedHierarchyPath = "system_top.u_core.u_logic";

        await WaitUntilAsync(() => viewModel.HierarchyScopeSignals.Count > 0, TimeSpan.FromSeconds(5));

        Assert.Equal("logic_unit", viewModel.SelectedHierarchyScopeModuleName);
        Assert.Equal("system_top.u_core.u_logic", viewModel.SelectedHierarchyScopePath);
        Assert.Contains("Double-click", viewModel.SelectedHierarchyScopeHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SelectingIntermediateHierarchyNodeBuildsParentAndChildScopeNeighborhood()
    {
        string root = FindRepositoryRoot();
        string samplePath = Path.Combine(root, "samples", "hierarchy", "hierarchy.bistable.json");
        MainWindowViewModel viewModel = CreateViewModel();

        await viewModel.LoadProjectFromPathAsync(samplePath, CancellationToken.None);
        viewModel.LiveModeEnabled = false;
        viewModel.BuildCommand.Execute(null);

        await WaitUntilAsync(() => viewModel.TraceSignals.Count > 0, TimeSpan.FromSeconds(20));

        viewModel.SelectedHierarchyPath = "system_top.u_core";

        await WaitUntilAsync(() => viewModel.SelectedHierarchyChildScopes.Count == 2, TimeSpan.FromSeconds(5));

        Assert.NotNull(viewModel.SelectedHierarchyParentScope);
        Assert.Equal("system_top", viewModel.SelectedHierarchyParentScope!.HierarchyPath);
        Assert.Contains(viewModel.SelectedHierarchyChildScopes, scope => scope.HierarchyPath == "system_top.u_core.u_logic");
        Assert.Contains(viewModel.SelectedHierarchyChildScopes, scope => scope.HierarchyPath == "system_top.u_core.u_status");
        Assert.Contains(viewModel.SelectedHierarchyChildScopes, scope => scope is { HierarchyPath: "system_top.u_core.u_logic", InputCount: 2, OutputCount: 2 });
        Assert.Equal(["system_top", "u_core"], viewModel.SelectedHierarchyBreadcrumbs.Select(breadcrumb => breadcrumb.Title).ToArray());
        Assert.Contains("Click a child instance", viewModel.SelectedHierarchyScopeHint, StringComparison.OrdinalIgnoreCase);

        viewModel.SelectHierarchyScopeCommand.Execute("system_top.u_core.u_status");

        Assert.Equal("system_top.u_core.u_status", viewModel.SelectedHierarchyPath);
    }

    [Fact]
    public async Task SelectingHierarchyNodeBuildsModulePortCatalogForScope()
    {
        string root = FindRepositoryRoot();
        string samplePath = Path.Combine(root, "samples", "hierarchy", "hierarchy.bistable.json");
        MainWindowViewModel viewModel = CreateViewModel();

        await viewModel.LoadProjectFromPathAsync(samplePath, CancellationToken.None);
        viewModel.LiveModeEnabled = false;
        viewModel.BuildCommand.Execute(null);

        await WaitUntilAsync(() => viewModel.TraceSignals.Count > 0, TimeSpan.FromSeconds(20));

        viewModel.SelectedHierarchyPath = "system_top.u_core";

        await WaitUntilAsync(() => viewModel.SelectedHierarchyPorts.Count > 0, TimeSpan.FromSeconds(5));

        Assert.Contains(viewModel.SelectedHierarchyPorts, port => port is { Name: "clk", IsInput: true, Width: 1 });
        Assert.Contains(viewModel.SelectedHierarchyPorts, port => port is { Name: "a", IsInput: true, Width: 8 });
        Assert.Contains(viewModel.SelectedHierarchyPorts, port => port is { Name: "result", IsOutput: true, Width: 8 });
        Assert.Contains(viewModel.SelectedHierarchyPorts, port => port is { Name: "valid", IsOutput: true, Width: 1 });
    }

    [Fact]
    public async Task SelectingHierarchyNodeBuildsLocalSignalsAndChildConnections()
    {
        string root = FindRepositoryRoot();
        string samplePath = Path.Combine(root, "samples", "hierarchy", "hierarchy.bistable.json");
        MainWindowViewModel viewModel = CreateViewModel();

        await viewModel.LoadProjectFromPathAsync(samplePath, CancellationToken.None);
        viewModel.LiveModeEnabled = false;
        viewModel.BuildCommand.Execute(null);

        await WaitUntilAsync(() => viewModel.TraceSignals.Count > 0, TimeSpan.FromSeconds(20));

        viewModel.SelectedHierarchyPath = "system_top.u_core";

        await WaitUntilAsync(() => viewModel.SelectedHierarchyChildInstances.Count == 2, TimeSpan.FromSeconds(5));

        Assert.Contains(viewModel.SelectedHierarchyLocalSignals, signal => signal is { Name: "parity_i", Width: 1, IsTraced: true });
        Assert.Contains(viewModel.SelectedHierarchyLocalSignals, signal =>
            signal is { Name: "parity_i", IsTraced: true, ResolvedSignalName: "system_top.u_core.parity_i" });

        HierarchyScopeInstanceViewModel logic = Assert.Single(
            viewModel.SelectedHierarchyChildInstances,
            instance => instance.HierarchyPath == "system_top.u_core.u_logic");
        Assert.Contains(logic.PortConnections, connection => connection is { PortName: "a", SignalName: "a", IsInput: true, Width: 8 });
        Assert.Contains(logic.PortConnections, connection => connection is { PortName: "sum", SignalName: "result", IsOutput: true, Width: 8 });
        Assert.Contains(logic.PortConnections, connection => connection is { PortName: "parity", SignalName: "parity_i", IsOutput: true, Width: 1 });

        viewModel.SelectedHierarchyPath = "system_top";

        await WaitUntilAsync(() => viewModel.SelectedHierarchyChildInstances.Count == 1, TimeSpan.FromSeconds(5));

        HierarchyScopeInstanceViewModel core = Assert.Single(viewModel.SelectedHierarchyChildInstances);
        Assert.Equal("system_top.u_core", core.HierarchyPath);
        Assert.Contains(core.Ports, port => port is { Name: "clk", IsInput: true, Width: 1 });
        Assert.Contains(core.Ports, port => port is { Name: "result", IsOutput: true, Width: 8 });
        Assert.Contains(core.LocalSignals, signal => signal is { Name: "parity_i", Width: 1 });
        Assert.Contains(core.ChildInstances, child => child.HierarchyPath == "system_top.u_core.u_logic");
        Assert.Contains(core.ChildInstances, child => child.HierarchyPath == "system_top.u_core.u_status");
    }

    [Fact]
    public async Task SchematicExpansionStateStartsCollapsedAndIsIndependentFromSelection()
    {
        string root = FindRepositoryRoot();
        string samplePath = Path.Combine(root, "samples", "hierarchy", "hierarchy.bistable.json");
        MainWindowViewModel viewModel = CreateViewModel();

        await viewModel.LoadProjectFromPathAsync(samplePath, CancellationToken.None);

        Assert.Empty(viewModel.SchematicExpandedPaths);
        Assert.Equal("system_top", viewModel.SelectedHierarchyPath);
        Assert.False(viewModel.IsSelectedHierarchyScopeExpanded);

        viewModel.ToggleSchematicExpansionCommand.Execute("system_top");

        Assert.Contains("system_top", viewModel.SchematicExpandedPaths);
        Assert.True(viewModel.IsSelectedHierarchyScopeExpanded);

        viewModel.SelectedHierarchyPath = "system_top.u_core";

        Assert.Equal("system_top.u_core", viewModel.SelectedHierarchyPath);
        Assert.Contains("system_top", viewModel.SchematicExpandedPaths);
        Assert.False(viewModel.IsSelectedHierarchyScopeExpanded);

        viewModel.ToggleSchematicExpansionCommand.Execute("system_top.u_core");

        Assert.Contains("system_top.u_core", viewModel.SchematicExpandedPaths);
        Assert.True(viewModel.IsSelectedHierarchyScopeExpanded);

        viewModel.SelectedHierarchyPath = "system_top";
        viewModel.ToggleSchematicExpansionCommand.Execute("system_top.u_core");

        Assert.Equal("system_top", viewModel.SelectedHierarchyPath);
        Assert.Contains("system_top", viewModel.SchematicExpandedPaths);
        Assert.DoesNotContain("system_top.u_core", viewModel.SchematicExpandedPaths);
        Assert.True(viewModel.IsSelectedHierarchyScopeExpanded);

        viewModel.ToggleSchematicExpansionCommand.Execute("system_top.u_core");

        Assert.Equal("system_top", viewModel.SelectedHierarchyPath);
        Assert.Contains("system_top.u_core", viewModel.SchematicExpandedPaths);

        viewModel.SelectedHierarchyPath = "system_top.u_core";
        viewModel.ToggleSchematicExpansionCommand.Execute("system_top.u_core");

        Assert.DoesNotContain("system_top.u_core", viewModel.SchematicExpandedPaths);
        Assert.False(viewModel.IsSelectedHierarchyScopeExpanded);
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

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Condition was not satisfied within the expected time.");
    }

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
