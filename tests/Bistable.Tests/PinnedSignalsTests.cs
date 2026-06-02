using Bistable.App.Services;
using Bistable.App.ViewModels;
using Bistable.Verilator;

namespace Bistable.Tests;

/// <summary>
/// Phase 2.7 P2.7-5 coverage. The schematic preview control owns the canonical
/// pinned set (it lives where the Ctrl+click actually happens). The VM mirrors
/// it through `RefreshPinnedSignals`, and the chip strip's "Clear" command
/// fires through `ClearPinnedSignalsRequested` so the control can wipe its own
/// HashSet then emit a fresh change event. These tests cover the seam logic
/// the UI relies on — the WPF/Avalonia pointer events themselves aren't
/// exercised here because they need a UI thread.
/// </summary>
public sealed class PinnedSignalsTests
{
    private static MainWindowViewModel CreateViewModel()
    {
        string layoutPath = Path.Combine(Path.GetTempPath(), $"bistable-tests-{Guid.NewGuid():N}", "layout.json");
        string prefsPath  = Path.Combine(Path.GetTempPath(), $"bistable-prefs-pin-{Guid.NewGuid():N}.json");
        BistableWorkspace workspace = new(
            new ProjectDialogService(),
            new DesignLoadService(),
            new SimulationWorkerBuilder(),
            new PreviewSimulationService(),
            new LayoutStateService(layoutPath));
        return new MainWindowViewModel(workspace, loadPersistedLayout: false, preferencesStore: new UserPreferencesStore(prefsPath));
    }

    [Fact]
    public void Initially_NoPinnedSignals()
    {
        MainWindowViewModel vm = CreateViewModel();
        Assert.Empty(vm.PinnedSignals);
    }

    [Fact]
    public void RefreshPinnedSignals_MirrorsExactly()
    {
        MainWindowViewModel vm = CreateViewModel();
        vm.RefreshPinnedSignals(new[] { "alu_a", "alu_b", "pc_d" });
        Assert.Equal(3, vm.PinnedSignals.Count);
        Assert.Contains("alu_a", vm.PinnedSignals);
        Assert.Contains("alu_b", vm.PinnedSignals);
        Assert.Contains("pc_d", vm.PinnedSignals);
    }

    [Fact]
    public void RefreshPinnedSignals_ReplacesPreviousContent()
    {
        // Mirror semantics: every refresh is authoritative, not additive — the
        // control owns the truth so stale entries must not linger.
        MainWindowViewModel vm = CreateViewModel();
        vm.RefreshPinnedSignals(new[] { "a", "b" });
        vm.RefreshPinnedSignals(new[] { "c" });
        Assert.Single(vm.PinnedSignals);
        Assert.Contains("c", vm.PinnedSignals);
    }

    [Fact]
    public void ClearPinnedSignalsCommand_FiresRequestedEvent()
    {
        MainWindowViewModel vm = CreateViewModel();
        int fired = 0;
        vm.ClearPinnedSignalsRequested += (_, _) => fired++;
        vm.ClearPinnedSignalsCommand.Execute(null);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void RefreshPinnedSignals_EmptyClearsCollection()
    {
        MainWindowViewModel vm = CreateViewModel();
        vm.RefreshPinnedSignals(new[] { "x" });
        vm.RefreshPinnedSignals(Array.Empty<string>());
        Assert.Empty(vm.PinnedSignals);
    }
}
