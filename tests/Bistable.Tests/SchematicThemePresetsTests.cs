using Bistable.App.Services;
using Bistable.App.ViewModels;
using Bistable.Verilator;

namespace Bistable.Tests;

/// <summary>
/// Phase 2.7 P2.7-9 coverage. Theme presets must:
///   (a) all four enum values resolve to a non-null SchematicTheme record
///   (b) display names are stable strings (the combo box label binds to them)
///   (c) UserPreferencesStore roundtrips the chosen preset to disk
///   (d) a corrupted preferences file falls back to defaults without throwing
/// </summary>
public sealed class SchematicThemePresetsTests
{
    [Theory]
    [InlineData(SchematicThemePreset.Dark)]
    [InlineData(SchematicThemePreset.Light)]
    [InlineData(SchematicThemePreset.HighContrast)]
    [InlineData(SchematicThemePreset.Print)]
    public void Get_ReturnsNonNullThemeForEveryPreset(SchematicThemePreset preset)
    {
        SchematicTheme theme = SchematicThemePresets.Get(preset);
        Assert.NotNull(theme);
        Assert.NotNull(theme.Background);
        Assert.NotNull(theme.ModuleStroke);
        Assert.NotNull(theme.LogicHigh);
    }

    [Fact]
    public void Get_DistinctPresetsReturnDistinctThemes()
    {
        // Sanity check that we're not accidentally aliasing two presets to the
        // same record — would silently break theme switching.
        var seen = new HashSet<SchematicTheme>();
        foreach (SchematicThemePreset preset in Enum.GetValues<SchematicThemePreset>())
        {
            Assert.True(seen.Add(SchematicThemePresets.Get(preset)),
                $"Preset {preset} resolves to a theme already in use.");
        }
    }

    [Fact]
    public void DisplayName_ReturnsHumanReadableStringForEveryPreset()
    {
        foreach (SchematicThemePreset preset in Enum.GetValues<SchematicThemePreset>())
        {
            string name = SchematicThemePresets.DisplayName(preset);
            Assert.False(string.IsNullOrWhiteSpace(name));
        }
    }

    [Fact]
    public void UserPreferencesStore_RoundtripsSchematicThemePreset()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"bistable-prefs-{Guid.NewGuid():N}.json");
        try
        {
            UserPreferencesStore store = new(tempPath);
            store.Save(new UserPreferences { SchematicTheme = SchematicThemePreset.HighContrast });

            UserPreferences loaded = new UserPreferencesStore(tempPath).Load();
            Assert.Equal(SchematicThemePreset.HighContrast, loaded.SchematicTheme);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void UserPreferencesStore_DefaultsWhenFileMissing()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"bistable-prefs-missing-{Guid.NewGuid():N}.json");
        Assert.False(File.Exists(tempPath));

        UserPreferences loaded = new UserPreferencesStore(tempPath).Load();
        Assert.Equal(SchematicThemePreset.Dark, loaded.SchematicTheme);
    }

    [Fact]
    public void UserPreferencesStore_DefaultsWhenFileCorrupted()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"bistable-prefs-corrupt-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(tempPath, "{ this is not valid json");

            UserPreferences loaded = new UserPreferencesStore(tempPath).Load();
            Assert.Equal(SchematicThemePreset.Dark, loaded.SchematicTheme);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void MainWindowViewModel_SchematicThemePresetChange_UpdatesThemeAndPersists()
    {
        string layoutPath = Path.Combine(Path.GetTempPath(), $"bistable-tests-{Guid.NewGuid():N}", "layout.json");
        string prefsPath = Path.Combine(Path.GetTempPath(), $"bistable-prefs-vm-{Guid.NewGuid():N}.json");
        try
        {
            BistableWorkspace workspace = new(
                new ProjectDialogService(),
                new DesignLoadService(),
                new SimulationWorkerBuilder(),
                new PreviewSimulationService(),
                new LayoutStateService(layoutPath));
            UserPreferencesStore store = new(prefsPath);

            MainWindowViewModel vm = new(workspace, loadPersistedLayout: false, preferencesStore: store);
            Assert.Equal(SchematicThemePreset.Dark, vm.SchematicThemePreset);
            Assert.Same(SchematicTheme.Dark, vm.SchematicTheme);

            vm.SchematicThemePreset = SchematicThemePreset.HighContrast;

            // (a) ViewModel's resolved theme record swapped atomically.
            Assert.Same(SchematicTheme.HighContrast, vm.SchematicTheme);
            // (b) The change persisted to disk so the next launch restores it.
            UserPreferences reloaded = new UserPreferencesStore(prefsPath).Load();
            Assert.Equal(SchematicThemePreset.HighContrast, reloaded.SchematicTheme);
        }
        finally
        {
            if (File.Exists(prefsPath)) File.Delete(prefsPath);
            string? layoutDir = Path.GetDirectoryName(layoutPath);
            if (layoutDir is not null && Directory.Exists(layoutDir)) Directory.Delete(layoutDir, recursive: true);
        }
    }

    [Fact]
    public void UserPreferencesStore_RoundtripsSchematicRouter()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"bistable-prefs-router-{Guid.NewGuid():N}.json");
        try
        {
            UserPreferencesStore store = new(tempPath);
            store.Save(new UserPreferences
            {
                SchematicTheme = SchematicThemePreset.Light,
                SchematicRouter = SchematicRoutingEngine.GraphvizDot,
            });

            UserPreferences loaded = new UserPreferencesStore(tempPath).Load();
            Assert.Equal(SchematicThemePreset.Light, loaded.SchematicTheme);
            Assert.Equal(SchematicRoutingEngine.GraphvizDot, loaded.SchematicRouter);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void MainWindowViewModel_SchematicRouterChange_PersistsAndKeepsTheme()
    {
        string layoutPath = Path.Combine(Path.GetTempPath(), $"bistable-tests-{Guid.NewGuid():N}", "layout.json");
        string prefsPath  = Path.Combine(Path.GetTempPath(), $"bistable-prefs-vmr-{Guid.NewGuid():N}.json");
        try
        {
            BistableWorkspace workspace = new(
                new ProjectDialogService(),
                new DesignLoadService(),
                new SimulationWorkerBuilder(),
                new PreviewSimulationService(),
                new LayoutStateService(layoutPath));

            MainWindowViewModel vm = new(workspace, loadPersistedLayout: false, preferencesStore: new UserPreferencesStore(prefsPath));
            vm.SchematicThemePreset = SchematicThemePreset.HighContrast;
            vm.SchematicRouter = SchematicRoutingEngine.Internal;

            // Both preferences land in the same file with the same write — earlier
            // VM writes must not blow away the unsaved sibling.
            UserPreferences reloaded = new UserPreferencesStore(prefsPath).Load();
            Assert.Equal(SchematicThemePreset.HighContrast, reloaded.SchematicTheme);
            Assert.Equal(SchematicRoutingEngine.Internal, reloaded.SchematicRouter);
        }
        finally
        {
            if (File.Exists(prefsPath)) File.Delete(prefsPath);
            string? layoutDir = Path.GetDirectoryName(layoutPath);
            if (layoutDir is not null && Directory.Exists(layoutDir)) Directory.Delete(layoutDir, recursive: true);
        }
    }

    [Fact]
    public void MainWindowViewModel_LoadsPersistedSchematicTheme()
    {
        string layoutPath = Path.Combine(Path.GetTempPath(), $"bistable-tests-{Guid.NewGuid():N}", "layout.json");
        string prefsPath = Path.Combine(Path.GetTempPath(), $"bistable-prefs-load-{Guid.NewGuid():N}.json");
        try
        {
            new UserPreferencesStore(prefsPath).Save(new UserPreferences { SchematicTheme = SchematicThemePreset.Print });

            BistableWorkspace workspace = new(
                new ProjectDialogService(),
                new DesignLoadService(),
                new SimulationWorkerBuilder(),
                new PreviewSimulationService(),
                new LayoutStateService(layoutPath));

            MainWindowViewModel vm = new(workspace, loadPersistedLayout: false, preferencesStore: new UserPreferencesStore(prefsPath));

            Assert.Equal(SchematicThemePreset.Print, vm.SchematicThemePreset);
            Assert.Same(SchematicTheme.Print, vm.SchematicTheme);
        }
        finally
        {
            if (File.Exists(prefsPath)) File.Delete(prefsPath);
            string? layoutDir = Path.GetDirectoryName(layoutPath);
            if (layoutDir is not null && Directory.Exists(layoutDir)) Directory.Delete(layoutDir, recursive: true);
        }
    }
}
