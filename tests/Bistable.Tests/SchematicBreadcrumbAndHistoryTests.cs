using System.IO;
using Bistable.App.Services;
using Bistable.App.ViewModels;
using Bistable.Verilator;

namespace Bistable.Tests;

/// <summary>
/// Phase 2.7 P2.7-2 coverage. Verifies:
///   - scope navigation history pushes the previous path on every change
///   - Back / Forward navigate without polluting the history stack
///   - distinct navigations clear the future stack (browser-style)
///   - CanExecute flags drive the back/forward button enable state
///
/// HierarchyRoot isn't loaded here — these tests drive SelectedHierarchyPath
/// strings directly through the navigation commands, which is enough to exercise
/// the history bookkeeping in isolation. Breadcrumb construction itself uses
/// HierarchyRoot, so end-to-end breadcrumb rendering is exercised manually.
/// </summary>
public sealed class SchematicBreadcrumbAndHistoryTests
{
    private static MainWindowViewModel CreateViewModel()
    {
        string layoutPath = Path.Combine(Path.GetTempPath(), $"bistable-tests-{Guid.NewGuid():N}", "layout.json");
        string prefsPath  = Path.Combine(Path.GetTempPath(), $"bistable-prefs-bcrumb-{Guid.NewGuid():N}.json");
        BistableWorkspace workspace = new(
            new ProjectDialogService(),
            new DesignLoadService(),
            new SimulationWorkerBuilder(),
            new PreviewSimulationService(),
            new LayoutStateService(layoutPath));
        return new MainWindowViewModel(workspace, loadPersistedLayout: false, preferencesStore: new UserPreferencesStore(prefsPath));
    }

    [Fact]
    public void Initially_BackAndForwardDisabled()
    {
        MainWindowViewModel vm = CreateViewModel();
        Assert.False(vm.CanNavigateScopeBack);
        Assert.False(vm.CanNavigateScopeForward);
        Assert.False(vm.NavigateScopeBackCommand.CanExecute(null));
        Assert.False(vm.NavigateScopeForwardCommand.CanExecute(null));
    }

    [Fact]
    public void Back_DoesNothingWhenHistoryEmpty()
    {
        MainWindowViewModel vm = CreateViewModel();
        // Back is a no-op when there's nothing to go back to — must not throw.
        vm.NavigateScopeBackCommand.Execute(null);
        Assert.False(vm.CanNavigateScopeBack);
    }

    [Fact]
    public void Forward_DoesNothingWhenFutureEmpty()
    {
        MainWindowViewModel vm = CreateViewModel();
        vm.NavigateScopeForwardCommand.Execute(null);
        Assert.False(vm.CanNavigateScopeForward);
    }

    // ── ScopeNavigationHistory direct coverage ─────────────────────────────
    // These exercise the back/forward bookkeeping in isolation from the VM so
    // we don't need a loaded HierarchyRoot to verify the stack semantics.

    [Fact]
    public void RecordNavigation_PushesPreviousOntoPast()
    {
        ScopeNavigationHistory h = new();
        h.RecordNavigation("top");
        h.RecordNavigation("top.a");
        Assert.True(h.CanGoBack);
        Assert.False(h.CanGoForward);
        Assert.Equal("top.a", h.Current);
    }

    [Fact]
    public void GoBack_PushesCurrentOntoFuture()
    {
        ScopeNavigationHistory h = new();
        h.RecordNavigation("top");
        h.RecordNavigation("top.a");

        Assert.Equal("top", h.GoBack());

        Assert.Equal("top", h.Current);
        Assert.False(h.CanGoBack);
        Assert.True(h.CanGoForward);
    }

    [Fact]
    public void GoForward_PushesCurrentOntoPast()
    {
        ScopeNavigationHistory h = new();
        h.RecordNavigation("top");
        h.RecordNavigation("top.a");
        h.GoBack();

        Assert.Equal("top.a", h.GoForward());
        Assert.Equal("top.a", h.Current);
        Assert.True(h.CanGoBack);
        Assert.False(h.CanGoForward);
    }

    [Fact]
    public void NewNavigationAfterBack_ClearsFuture()
    {
        ScopeNavigationHistory h = new();
        h.RecordNavigation("top");
        h.RecordNavigation("top.a");
        h.GoBack();
        Assert.True(h.CanGoForward);

        // Browser-style: navigating to a new path after going back drops the
        // redo trail. Otherwise users would re-land on stale paths.
        h.RecordNavigation("top.b");
        Assert.False(h.CanGoForward);
        Assert.True(h.CanGoBack);
        Assert.Equal("top.b", h.Current);
    }

    [Fact]
    public void RecordNavigation_IgnoresSamePathRepeated()
    {
        ScopeNavigationHistory h = new();
        h.RecordNavigation("top");
        h.RecordNavigation("top");   // no-op
        h.RecordNavigation("TOP");   // case-insensitive no-op
        Assert.False(h.CanGoBack);
    }

    [Fact]
    public void GoBack_ReturnsNullWhenEmpty()
    {
        ScopeNavigationHistory h = new();
        Assert.Null(h.GoBack());
    }

    [Fact]
    public void GoForward_ReturnsNullWhenEmpty()
    {
        ScopeNavigationHistory h = new();
        h.RecordNavigation("top");
        Assert.Null(h.GoForward());
    }

    [Fact]
    public void DeepNavigation_BackForwardRoundtrip()
    {
        ScopeNavigationHistory h = new();
        h.RecordNavigation("top");
        h.RecordNavigation("top.a");
        h.RecordNavigation("top.a.b");
        h.RecordNavigation("top.a.b.c");

        Assert.Equal("top.a.b",   h.GoBack());
        Assert.Equal("top.a",     h.GoBack());
        Assert.Equal("top",       h.GoBack());
        Assert.False(h.CanGoBack);

        Assert.Equal("top.a",     h.GoForward());
        Assert.Equal("top.a.b",   h.GoForward());
        Assert.Equal("top.a.b.c", h.GoForward());
        Assert.False(h.CanGoForward);
    }
}
