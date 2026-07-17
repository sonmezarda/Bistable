using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Bistable.App.Infrastructure;
using Bistable.App.Services;
using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Design.Ast.Passes;
using Bistable.Core.Projects;
using Bistable.Protocol;
using Bistable.Verilator;

namespace Bistable.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private const long SimulationRunChunkSize = 1024;

    private readonly BistableWorkspace _workspace;
    private readonly VcdTraceReader _traceReader = new();
    private string _status = "Ready. Open a project to inspect top-level ports.";
    private string _projectName = "No project";
    private string _topModule = "-";
    private string _verilatorVersion = "-";
    private string? _currentProjectPath;
    private ProjectConfiguration? _currentProject;
    private ModuleMetadata? _currentMetadata;
    private ElaboratedDesign? _currentDesign;
    private Bistable.Core.Design.Ast.DesignAst? _currentAst;
    private string? _currentProjectDirectory;
    private SimulationWorkerClient? _worker;
    private SimulationWorkerClient? _gateLevelWorker;
    private string? _gateLevelTraceFilePath;
    private string? _rtlTraceFilePath;
    private SimulationTarget _simulationTarget = SimulationTarget.Rtl;
    private readonly GateLevelWorkerBuildService _gateLevelWorkerBuilder = new();
    private readonly RtlVsGateLevelComparator _rtlVsGateComparator = new();
    private readonly LiveProbeService _liveProbes = new();
    private readonly DockPanelViewModel _projectPanel = new(DockPanelKind.Project, "Project");
    private readonly DockPanelViewModel _waveformPanel = new(DockPanelKind.Waveform, "Waveform");
    private readonly DockPanelViewModel _schematicPanel = new(DockPanelKind.Schematic, "Schematic");
    private HierarchyNodeViewModel? _hierarchyRoot;
    private HierarchyNodeViewModel? _selectedHierarchyNode;
    private SignalViewModel? _selectedSignal;
    private string? _selectedSchematicReferenceName;
    private bool _settingSelectedSchematicReference;
    private SignalViewModel? _observedSelectedSignal;
    private WaveformLaneViewModel? _selectedWaveformLane;
    private DockPanelViewModel? _selectedLeftDockPanel;
    private DockPanelViewModel? _selectedRightDockPanel;
    private DockPanelViewModel? _selectedBottomDockPanel;
    private DockPanelViewModel? _selectedCenterDockPanel;
    private DockZone _projectDockZone = DockZone.Left;
    private DockZone _waveformDockZone = DockZone.Bottom;
    private DockZone _schematicDockZone = DockZone.Right;
    private DockPanelKind? _preferredDockPanelSelection;
    private DockZone _projectLastVisibleZone = DockZone.Left;
    private DockZone _waveformLastVisibleZone = DockZone.Bottom;
    private DockZone _schematicLastVisibleZone = DockZone.Right;
    private double _waveformZoom = 1;
    private int _waveformOffset;
    private double _leftDockWidth = 260;
    private double _rightDockWidth = 320;
    private double _bottomDockHeight = 280;
    private long _waveformCursorOrder;
    private long _waveformOrder;
    private string? _selectedClockName;
    private string _runCyclesText = "10";
    private ulong _time;
    private string? _traceFilePath;
    private VcdTraceDocument _traceDocument = VcdTraceDocument.Empty;
    private string _schematicDriveValue = "0";
    private readonly int _liveEvaluationDelayMs;
    // P2.7-9: schematic theme — backed by UserPreferencesStore so the choice
    // sticks across app restarts. `SchematicTheme` is the resolved record bound
    // to the preview control's Palette property.
    private readonly UserPreferencesStore _preferencesStore;
    private readonly ProjectFileWatcherService _projectFileWatcher = new();
    private readonly ProjectReloadCoordinator _projectReloadCoordinator;
    private readonly SimulationWorkerHotSwapService _workerHotSwapService;
    private bool _liveReloadEnabled;
    private int _liveReloadDebounceMs;
    private bool _hasUserLiveReloadDebounceOverride;
    private bool _isSchematicStale;
    private bool _isLiveReloadBuilding;
    private string _liveReloadStatus = "Live reload idle";
    private double _lastLiveReloadElapsedMs;
    private SourceDocumentViewModel? _selectedSourceDocument;
    private ElaborationDiagnostic? _selectedElaborationDiagnostic;
    private int _sourceNavigationLine = 1;
    private int _sourceNavigationColumn = 1;
    private long _sourceNavigationVersion;
    private int _hotReloadWorkerSlot;
    private SchematicThemePreset _schematicThemePreset;
    private SchematicTheme _schematicTheme = SchematicTheme.Dark;
    private SchematicRoutingEngine _schematicRouter = SchematicRoutingEngine.Elk;
    // P2.7-2: scope navigation history. Owned by ScopeNavigationHistory so the
    // back/forward bookkeeping can be unit-tested in isolation.
    private readonly ScopeNavigationHistory _scopeHistory = new();
    private readonly SemaphoreSlim _simulationOperationGate = new(1, 1);
    private readonly List<AsyncCommand> _simulationCommands = [];
    private readonly RelayCommand _cancelSimulationCommand;
    private CancellationTokenSource? _activeSimulationCancellation;
    private bool _isSimulationBusy;
    private string _simulationOperationName = string.Empty;
    private bool _suppressScopeHistoryPush;
    private bool _liveModeEnabled = true;
    private bool _suppressInputLiveUpdate;
    private CancellationTokenSource? _liveEvaluationCts;
    private bool _isLiveEvaluationInFlight;
    private bool _liveEvaluationPending;
    private bool _isSubSimActive;
    private SimulationWorkerClient? _topLevelWorker;
    private ProjectConfiguration? _subSimProject;
    private List<SignalViewModel>? _savedTopInputs;
    private List<SignalViewModel>? _savedTopOutputs;
    private List<SignalViewModel>? _savedTopAllSignals;
    private List<SignalViewModel>? _savedTopTraceSignals;
    private string? _savedTopModule;
    private string? _savedTopTraceFilePath;
    private ElaboratedDesign? _savedTopDesign;
    private Bistable.Core.Design.Ast.DesignAst? _savedTopAst;
    private HierarchyNodeViewModel? _savedTopHierarchyRoot;
    private HierarchyNodeViewModel? _savedTopSelectedHierarchyNode;
    private List<string>? _savedTopExpandedPaths;

    public MainWindowViewModel(BistableWorkspace workspace, bool loadPersistedLayout = true, int liveEvaluationDelayMs = 120, UserPreferencesStore? preferencesStore = null)
    {
        _workspace = workspace;
        _liveEvaluationDelayMs = Math.Max(0, liveEvaluationDelayMs);
        _preferencesStore = preferencesStore ?? new UserPreferencesStore();
        UserPreferences prefs = _preferencesStore.Load();
        _schematicThemePreset = prefs.SchematicTheme;
        _schematicTheme = SchematicThemePresets.Get(_schematicThemePreset);
        _schematicRouter = prefs.SchematicRouter;
        _liveReloadEnabled = prefs.LiveReloadEnabled;
        _hasUserLiveReloadDebounceOverride = prefs.LiveReloadDebounceMs.HasValue;
        _liveReloadDebounceMs = Math.Clamp(prefs.LiveReloadDebounceMs ?? 400, 100, 5000);
        _projectReloadCoordinator = new ProjectReloadCoordinator(ReloadProjectFromChangesAsync);
        _workerHotSwapService = new SimulationWorkerHotSwapService(_workspace.WorkerBuilder);
        _projectFileWatcher.FilesChanged += OnProjectFilesChanged;
        _cancelSimulationCommand = new RelayCommand(
            CancelActiveSimulationOperation,
            () => IsSimulationBusy);
        CancelSimulationCommand = _cancelSimulationCommand;
        LoadProjectCommand = CreateSimulationCommand("Load project", LoadProjectAsync);
        BuildCommand = CreateSimulationCommand("Build", BuildAsync);
        EvalCommand = CreateSimulationCommand("Eval", EvaluateAsync);
        TickCommand = CreateSimulationCommand("Tick", TickAsync);
        RunCyclesCommand = CreateSimulationCommand("Run", RunCyclesAsync);
        ResetCommand = CreateSimulationCommand("Reset", ResetAsync);
        CompareRtlAndGateCommand = CreateSimulationCommand(
            "RTL/Gate comparison",
            CompareRtlAndGateAsync,
            () => CanCompareRtlAndGate);
        AddSelectedWaveformSignalCommand = new RelayCommand(AddSelectedWaveformSignal);
        AddHierarchyScopeSignalsToWaveformCommand = new RelayCommand(AddHierarchyScopeSignalsToWaveform);
        SelectHierarchyScopeCommand = new ParameterizedRelayCommand<string>(SelectHierarchyScope);
        // P2.7-2: scope back/forward commands. CanExecute reflects the stack
        // state so toolbar buttons disable when there's nothing to navigate to.
        NavigateScopeBackCommand    = new RelayCommand(NavigateScopeBack,    () => _scopeHistory.CanGoBack);
        NavigateScopeForwardCommand = new RelayCommand(NavigateScopeForward, () => _scopeHistory.CanGoForward);
        EnterSubSimAtPathCommand = new ParameterizedAsyncCommand<string>(
            (path, cancellationToken) => ExecuteSimulationOperationAsync(
                "Isolated simulation",
                token => EnterSubSimAtPathAsync(path, token),
                cancellationToken),
            _ => !IsSimulationBusy);
        ToggleSchematicExpansionCommand = new ParameterizedRelayCommand<string>(ToggleSchematicExpansion);
        ToggleInputSignalCommand = new ParameterizedRelayCommand<string>(ToggleInputSignal);
        OpenMemoryViewerCommand = new RelayCommand(() => MemoryViewerRequested?.Invoke(this, EventArgs.Empty));
        DriveSelectedSchematicInputCommand = CreateSimulationCommand(
            "Drive signal",
            DriveSelectedSchematicSignalAsync);
        ForceSelectedSchematicSignalCommand = CreateSimulationCommand(
            "Force signal",
            ForceSelectedSchematicSignalAsync);
        ReleaseSelectedSchematicSignalCommand = CreateSimulationCommand(
            "Release signal",
            ReleaseSelectedSchematicSignalAsync);
        ReleasePathCommand = new ParameterizedAsyncCommand<string>(
            (path, cancellationToken) => ExecuteSimulationOperationAsync(
                "Release signal",
                token => ReleasePathAsync(path, token),
                cancellationToken),
            _ => !IsSimulationBusy);
        ReleaseAllForcedCommand = CreateSimulationCommand(
            "Release forced signals",
            ReleaseAllForcedAsync);
        SaveProjectSettingsCommand = new AsyncCommand(SaveProjectSettingsAsync,
            () => _currentProject is not null && _currentProjectPath is not null);
        SaveSynthesisSettingsCommand = SaveProjectSettingsCommand;
        SaveSourceCommand = new AsyncCommand(SaveSelectedSourceAsync, () => SelectedSourceDocument?.IsDirty == true);
        RemoveSelectedWaveformSignalCommand = new RelayCommand(RemoveSelectedWaveformSignal);
        ClearWaveformCommand = new RelayCommand(ClearWaveform);
        // P2.7-5: chip-strip "Clear all" — fires through the wired action so the
        // SchematicPreviewControl can react (clear its HashSet + InvalidateVisual)
        // and the VM mirror gets refreshed by the resulting PinnedSignalsChanged.
        ClearPinnedSignalsCommand = new RelayCommand(() => ClearPinnedSignalsRequested?.Invoke(this, EventArgs.Empty));
        MoveWaveformSignalUpCommand = new RelayCommand(MoveSelectedWaveformSignalUp);
        MoveWaveformSignalDownCommand = new RelayCommand(MoveSelectedWaveformSignalDown);
        ZoomWaveformInCommand = new RelayCommand(() => WaveformZoom = Math.Min(8, WaveformZoom * 1.35));
        ZoomWaveformOutCommand = new RelayCommand(() => WaveformZoom = Math.Max(1, WaveformZoom / 1.35));
        PanWaveformLeftCommand = new RelayCommand(() => WaveformOffset = Math.Min(GetMaxWaveformOffset(), WaveformOffset + 20));
        PanWaveformRightCommand = new RelayCommand(() => WaveformOffset = Math.Max(0, WaveformOffset - 20));
        ToggleProjectPaneCommand = new RelayCommand(() => IsProjectPaneVisible = !IsProjectPaneVisible);
        ToggleWaveformPaneCommand = new RelayCommand(() => IsWaveformPaneVisible = !IsWaveformPaneVisible);
        DockPanelCommand = new ParameterizedRelayCommand<DockCommandParameter>(request => MoveDockPanel(request.PanelKind, request.Zone));
        ToggleSchematicPaneCommand = new RelayCommand(() => IsSchematicPaneVisible = !IsSchematicPaneVisible);
        EnterSubSimulationCommand = CreateSimulationCommand(
            "Isolated simulation",
            EnterSubSimulationAsync,
            () => CanEnterSubSim);
        ExitSubSimulationCommand  = new RelayCommand(
            ExitSubSimulation,
            () => _isSubSimActive && !IsSimulationBusy);
        FitWaveformCommand = new RelayCommand(() =>
        {
            WaveformZoom = 1;
            WaveformOffset = 0;
        });
        RebuildDockCollections();
        LoadSamples();
        // When a live probe value lands (asynchronously after the UI rendered
        // the previous cached value), re-raise the selected-signal-value PCE so
        // the bound TextBlock in the Live Probe panel rebinds.
        _liveProbes.ValueUpdated += OnLiveProbeValueUpdated;
        _liveProbes.ValuesUpdated += OnLiveProbeValuesUpdated;
        _liveProbes.MemoryUpdated += OnLiveMemoryUpdated;
        if (loadPersistedLayout)
        {
            _ = LoadLayoutStateAsync();
        }
    }

    private void OnLiveProbeValueUpdated(object? sender, ProbeValueUpdatedEventArgs e)
    {
        HierarchyScopeLocalSignalViewModel? local = SelectedSchematicLocalSignal;
        if (local?.ResolvedSignalName is { } resolved
            && string.Equals(resolved, e.Path, StringComparison.OrdinalIgnoreCase))
        {
            OnPropertyChanged(nameof(SelectedSchematicSignalValue));
        }
    }

    private void OnLiveProbeValuesUpdated(object? sender, ProbeValuesUpdatedEventArgs e)
    {
        string? selectedPath = SelectedSchematicLocalSignal?.ResolvedSignalName;
        if (selectedPath is not null
            && e.Values.Any(value => string.Equals(value.Path, selectedPath, StringComparison.OrdinalIgnoreCase)))
        {
            OnPropertyChanged(nameof(SelectedSchematicSignalValue));
        }
    }

    private void OnLiveMemoryUpdated(object? sender, MemorySnapshotUpdatedEventArgs e)
    {
        ApplyMemorySnapshot(e.Snapshot);
    }

    public ObservableCollection<SignalViewModel> Inputs { get; } = [];

    public ObservableCollection<SignalViewModel> Outputs { get; } = [];

    public ObservableCollection<SignalViewModel> AllSignals { get; } = [];

    public ObservableCollection<SignalViewModel> TraceSignals { get; } = [];

    public ObservableCollection<SignalViewModel> HierarchyScopeSignals { get; } = [];

    // P2.7-5: live mirror of SchematicPreviewControl.PinnedSignalNames so the
    // chip strip in the schematic panel can show them as buttons. The control
    // owns the canonical state (it lives inside the schematic frame); the VM
    // just observes via `RefreshPinnedSignals` and exposes a clear command.
    public ObservableCollection<string> PinnedSignals { get; } = [];

    public ObservableCollection<HierarchyTraceScopeSummaryViewModel> HierarchyTraceScopeSummaries { get; } = [];

    public ObservableCollection<HierarchyScopeNodeViewModel> SelectedHierarchyChildScopes { get; } = [];

    public ObservableCollection<HierarchyScopeInstanceViewModel> SelectedHierarchyChildInstances { get; } = [];

    public ObservableCollection<HierarchyScopePortViewModel> SelectedHierarchyPorts { get; } = [];

    public ObservableCollection<HierarchyScopeLocalSignalViewModel> SelectedHierarchyLocalSignals { get; } = [];

    /// <summary>
    /// Two-way binding target for the hierarchy panel's "Local Signals" list.
    /// Clicking a row sets this, which in turn updates the schematic selection
    /// so the Live Probe panel (and memory viewer) opens for the picked signal.
    /// </summary>
    public HierarchyScopeLocalSignalViewModel? SelectedHierarchyLocalSignal
    {
        get => _selectedHierarchyLocalSignal;
        set
        {
            if (!SetProperty(ref _selectedHierarchyLocalSignal, value)) return;
            if (value?.ResolvedSignalName is { } resolved)
            {
                SelectedSchematicSignalName = resolved;
            }
        }
    }
    private HierarchyScopeLocalSignalViewModel? _selectedHierarchyLocalSignal;

    public ObservableCollection<Bistable.Core.Design.DesignContAssign> SelectedHierarchyContAssigns { get; } = [];

    public ObservableCollection<Bistable.Core.Design.Schematic.SchematicPrimitive> SelectedHierarchyPrimitives { get; } = [];

    // P2-8: decoded primitives keyed by module name, used by the schematic renderer
    // when a compound child is expanded — its module's primitives are rendered inside.
    public IReadOnlyDictionary<string, IReadOnlyList<Bistable.Core.Design.Schematic.SchematicPrimitive>> PrimitivesByModule
        => _primitivesByModule;

    private Dictionary<string, IReadOnlyList<Bistable.Core.Design.Schematic.SchematicPrimitive>> _primitivesByModule =
        new(StringComparer.OrdinalIgnoreCase);

    private void RebuildPrimitivesByModule()
    {
        _primitivesByModule = new Dictionary<string, IReadOnlyList<Bistable.Core.Design.Schematic.SchematicPrimitive>>(StringComparer.OrdinalIgnoreCase);
        if (_currentAst is null) return;
        foreach (Bistable.Core.Design.Ast.ModuleAst module in _currentAst.Modules)
        {
            Bistable.Core.Design.Schematic.SchematicPrimitiveList decoded =
                Bistable.Core.Design.Schematic.SchematicDecoder.Decode(module);
            _primitivesByModule[module.Name] = decoded.Logic;
        }
        OnPropertyChanged(nameof(PrimitivesByModule));
    }

    public ObservableCollection<string> SchematicExpandedPaths { get; } = [];

    public ObservableCollection<SampleProjectViewModel> Samples { get; } = [];

    public ObservableCollection<WaveformLaneViewModel> WaveformLanes { get; } = [];

    public ObservableCollection<string> AvailableClocks { get; } = [];

    public ObservableCollection<SourceDocumentViewModel> SourceDocuments { get; } = [];

    public ObservableCollection<ElaborationDiagnostic> ElaborationDiagnostics { get; } = [];

    public SourceDocumentViewModel? SelectedSourceDocument
    {
        get => _selectedSourceDocument;
        set
        {
            if (ReferenceEquals(_selectedSourceDocument, value)) return;
            if (_selectedSourceDocument is not null)
            {
                _selectedSourceDocument.PropertyChanged -= OnSelectedSourceDocumentPropertyChanged;
            }
            if (!SetProperty(ref _selectedSourceDocument, value)) return;
            if (_selectedSourceDocument is not null)
            {
                _selectedSourceDocument.PropertyChanged += OnSelectedSourceDocumentPropertyChanged;
            }
            ((AsyncCommand)SaveSourceCommand).RaiseCanExecuteChanged();
        }
    }

    public ElaborationDiagnostic? SelectedElaborationDiagnostic
    {
        get => _selectedElaborationDiagnostic;
        set
        {
            if (SetProperty(ref _selectedElaborationDiagnostic, value) && value is not null)
            {
                NavigateToSource(value.FilePath, value.Line, value.Column);
            }
        }
    }

    public int SourceNavigationLine
    {
        get => _sourceNavigationLine;
        private set => SetProperty(ref _sourceNavigationLine, Math.Max(1, value));
    }

    public int SourceNavigationColumn
    {
        get => _sourceNavigationColumn;
        private set => SetProperty(ref _sourceNavigationColumn, Math.Max(1, value));
    }

    public long SourceNavigationVersion
    {
        get => _sourceNavigationVersion;
        private set => SetProperty(ref _sourceNavigationVersion, value);
    }

    public ICommand SaveSourceCommand { get; }

    public bool LiveReloadEnabled
    {
        get => _liveReloadEnabled;
        set
        {
            if (!SetProperty(ref _liveReloadEnabled, value)) return;
            SaveUserPreferences();
            ConfigureProjectFileWatcher();
            OnPropertyChanged(nameof(IsLiveReloadActive));
        }
    }

    public int LiveReloadDebounceMs
    {
        get => _liveReloadDebounceMs;
        set
        {
            int normalized = Math.Clamp(value, 100, 5000);
            if (!SetProperty(ref _liveReloadDebounceMs, normalized)) return;
            _hasUserLiveReloadDebounceOverride = true;
            SaveUserPreferences();
            ConfigureProjectFileWatcher();
        }
    }

    public bool IsLiveReloadActive =>
        _liveReloadEnabled && (_currentProject?.LiveReload.Enabled ?? false);

    public bool IsSchematicStale
    {
        get => _isSchematicStale;
        private set => SetProperty(ref _isSchematicStale, value);
    }

    public bool IsLiveReloadBuilding
    {
        get => _isLiveReloadBuilding;
        private set => SetProperty(ref _isLiveReloadBuilding, value);
    }

    public string LiveReloadStatus
    {
        get => _liveReloadStatus;
        private set => SetProperty(ref _liveReloadStatus, value);
    }

    public double LastLiveReloadElapsedMs
    {
        get => _lastLiveReloadElapsedMs;
        private set => SetProperty(ref _lastLiveReloadElapsedMs, value);
    }

    public ObservableCollection<DockPanelViewModel> LeftDockPanels { get; } = [];

    public ObservableCollection<DockPanelViewModel> RightDockPanels { get; } = [];

    public ObservableCollection<DockPanelViewModel> BottomDockPanels { get; } = [];

    public ObservableCollection<DockPanelViewModel> CenterDockPanels { get; } = [];

    public HierarchyNodeViewModel? HierarchyRoot
    {
        get => _hierarchyRoot;
        private set => SetProperty(ref _hierarchyRoot, value);
    }

    public HierarchyNodeViewModel? SelectedHierarchyNode
    {
        get => _selectedHierarchyNode;
        set
        {
            if (SetProperty(ref _selectedHierarchyNode, value))
            {
                // P2.7-2: push previous path onto history (unless this change
                // came from Back/Forward itself). The future stack is cleared on
                // any forward navigation so re-entering a branch invalidates the
                // old redo trail — same semantics as a browser address bar.
                PushScopeHistoryIfNeeded(value?.HierarchyPath);
                OnPropertyChanged(nameof(SelectedHierarchyPath));
                OnPropertyChanged(nameof(SelectedHierarchySummary));
                OnPropertyChanged(nameof(SelectedHierarchyScopeTitle));
                OnPropertyChanged(nameof(SelectedHierarchyScopeModuleName));
                OnPropertyChanged(nameof(SelectedHierarchyScopePath));
                OnPropertyChanged(nameof(IsSelectedHierarchyScopeExpanded));
                OnPropertyChanged(nameof(SelectedHierarchyScopeSummary));
                OnPropertyChanged(nameof(SelectedHierarchyScopeHint));
                OnPropertyChanged(nameof(SelectedHierarchyParentScope));
                OnPropertyChanged(nameof(SelectedHierarchyBreadcrumbs));
                RefreshHierarchyScopeSignals();
                RefreshSelectedHierarchyNeighborhood();
                RefreshSelectedHierarchyPorts();
                RefreshSelectedHierarchyLocalSignals();
                OnPropertyChanged(nameof(CanEnterSubSim));
                ((AsyncCommand)EnterSubSimulationCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public ICommand LoadProjectCommand { get; }

    public ICommand BuildCommand { get; }

    public ICommand EnterSubSimulationCommand { get; }

    public ICommand ExitSubSimulationCommand { get; }

    public ICommand EvalCommand { get; }

    public ICommand TickCommand { get; }

    public ICommand RunCyclesCommand { get; }

    public ICommand ResetCommand { get; }

    public ICommand CompareRtlAndGateCommand { get; }

    public ICommand CancelSimulationCommand { get; }

    public bool IsSimulationBusy
    {
        get => _isSimulationBusy;
        private set
        {
            if (!SetProperty(ref _isSimulationBusy, value)) return;
            OnPropertyChanged(nameof(SimulationOperationName));
            _cancelSimulationCommand.RaiseCanExecuteChanged();
            foreach (AsyncCommand command in _simulationCommands)
            {
                command.RaiseCanExecuteChanged();
            }
            ((ParameterizedAsyncCommand<string>)EnterSubSimAtPathCommand).RaiseCanExecuteChanged();
            ((ParameterizedAsyncCommand<string>)ReleasePathCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ExitSubSimulationCommand).RaiseCanExecuteChanged();
        }
    }

    public string SimulationOperationName
    {
        get => _simulationOperationName;
        private set => SetProperty(ref _simulationOperationName, value);
    }

    public SimulationTarget SelectedSimulationTarget
    {
        get => _simulationTarget;
        set
        {
            if (value == global::Bistable.App.ViewModels.SimulationTarget.GateLevel && _isSubSimActive)
            {
                Status = "Exit isolated simulation before selecting the gate-level target.";
                OnPropertyChanged();
                return;
            }

            if (value == global::Bistable.App.ViewModels.SimulationTarget.GateLevel && !IsGateLevelWorkerReady)
            {
                Status = "Gate-level simulation is not ready. Run Synthesize first.";
                OnPropertyChanged();
                return;
            }

            if (!SetProperty(ref _simulationTarget, value))
            {
                return;
            }

            ActivateSimulationTarget(value);
        }
    }

    public IReadOnlyList<SimulationTarget> AvailableSimulationTargets { get; } =
        Enum.GetValues<SimulationTarget>();

    public bool IsGateLevelWorkerReady => _gateLevelWorker is not null;

    public bool CanCompareRtlAndGate =>
        !_isSubSimActive && _worker is not null && _gateLevelWorker is not null;

    public string ActiveSimulationTargetLabel =>
        _simulationTarget == global::Bistable.App.ViewModels.SimulationTarget.GateLevel ? "Gate" : "RTL";

    public ICommand AddSelectedWaveformSignalCommand { get; }

    public ICommand AddHierarchyScopeSignalsToWaveformCommand { get; }

    public ICommand SelectHierarchyScopeCommand { get; }

    // P2.7-2: scope back/forward navigation. Bound to toolbar buttons + the
    // Alt+Left / Alt+Right keyboard shortcuts. CanExecute reflects stack state
    // so the buttons disable when there's nothing to navigate to.
    public ICommand NavigateScopeBackCommand { get; }

    public ICommand NavigateScopeForwardCommand { get; }

    public bool CanNavigateScopeBack => _scopeHistory.CanGoBack;
    public bool CanNavigateScopeForward => _scopeHistory.CanGoForward;

    /// <summary>Schematic double-click handler: select the clicked scope's hierarchy node AND enter sub-sim for it.</summary>
    public ICommand EnterSubSimAtPathCommand { get; }

    public ICommand ToggleSchematicExpansionCommand { get; }

    public ICommand ToggleInputSignalCommand { get; }

    public ICommand DriveSelectedSchematicInputCommand { get; }

    /// <summary>
    /// Fires when the user asks to open the standalone Memory Viewer window for
    /// the currently-selected memory probe. Handled by <see cref="Views.MainWindow"/>
    /// (the View owns the Window lifetime).
    /// </summary>
    public ICommand OpenMemoryViewerCommand { get; }
    public event EventHandler? MemoryViewerRequested;
    public LiveProbeService LiveProbes => _liveProbes;

    // ── Phase 2.9: Schematic coverage diagnostics ─────────────────────────

    /// <summary>
    /// Build the schematic coverage report for the currently-loaded design.
    /// Returns null when no project is open. Heavy enough to run on demand;
    /// the report is not cached because the underlying decode is fast and the
    /// user might rebuild between opens.
    /// </summary>
    public Bistable.Core.Design.Schematic.SchematicCoverageReport? BuildSchematicCoverageReport()
    {
        if (_currentAst is null) return null;
        return Bistable.Core.Design.Schematic.SchematicCoverageAnalyzer.Analyze(_currentAst);
    }

    public ICommand OpenDiagnosticsCommand =>
        _openDiagnosticsCommand ??= new RelayCommand(
            () => DiagnosticsRequested?.Invoke(this, EventArgs.Empty),
            () => _currentAst is not null);
    private ICommand? _openDiagnosticsCommand;

    public event EventHandler? DiagnosticsRequested;

    // ── Phase 6: gate-level synthesis ───────────────────────────────────────

    /// <summary>
    /// True when a project is loaded. Synthesis is no longer JSON-gated; the
    /// GUI supplies default synthesis settings and persists them only when the
    /// user saves project settings.
    /// </summary>
    public bool IsSynthesisAvailable =>
        _currentProject is not null && _currentProjectDirectory is not null;

    public bool SynthesisEnabled
    {
        get => CurrentSynthesis.Enabled;
        set => UpdateSynthesis(CurrentSynthesis with { Enabled = value });
    }

    public string SynthesisTopModule
    {
        get => CurrentSynthesis.TopModule ?? TopModuleForSynthesisFallback();
        set => UpdateSynthesis(CurrentSynthesis with { TopModule = NormalizeOptionalText(value) });
    }

    public string SynthesisOutputJson
    {
        get => CurrentSynthesis.OutputJson;
        set => UpdateSynthesis(CurrentSynthesis with { OutputJson = NormalizeRequiredText(value, DefaultSynthesis.OutputJson) });
    }

    public string SynthesisOutputVerilog
    {
        get => CurrentSynthesis.OutputVerilog;
        set => UpdateSynthesis(CurrentSynthesis with { OutputVerilog = NormalizeRequiredText(value, DefaultSynthesis.OutputVerilog) });
    }

    public bool SynthesisGenericCells
    {
        get => CurrentSynthesis.GenericCells;
        set => UpdateSynthesis(CurrentSynthesis with { GenericCells = value });
    }

    public bool SynthesisFlatten
    {
        get => CurrentSynthesis.Flatten;
        set => UpdateSynthesis(CurrentSynthesis with { Flatten = value });
    }

    public bool CanSaveSynthesisSettings => _currentProject is not null && _currentProjectPath is not null;

    public bool CanSaveProjectSettings => CanSaveSynthesisSettings;

    public ICommand SaveSynthesisSettingsCommand { get; }

    public ICommand SaveProjectSettingsCommand { get; }

    private static readonly SynthesisConfiguration DefaultSynthesis = new(Enabled: true);

    private static readonly SchematicConfiguration DefaultSchematic = new();

    private SynthesisConfiguration CurrentSynthesis =>
        _currentProject?.Synthesis ?? DefaultSynthesis with { TopModule = TopModuleForSynthesisFallback() };

    private SchematicConfiguration CurrentSchematic =>
        _currentProject?.Schematic ?? DefaultSchematic;

    public RoutingQuality GateRoutingQuality
    {
        get => CurrentSchematic.RoutingQuality;
        set => UpdateSchematic(CurrentSchematic with { RoutingQuality = value });
    }

    public bool GateAutoDowngradeLargeGraphs
    {
        get => CurrentSchematic.AutoDowngradeLargeGraphs;
        set => UpdateSchematic(CurrentSchematic with { AutoDowngradeLargeGraphs = value });
    }

    public GatePinLabelMode GatePinLabelMode
    {
        get => CurrentSchematic.GatePinLabelMode;
        set => UpdateSchematic(CurrentSchematic with { GatePinLabelMode = value });
    }

    public bool GateGroupBusPinLabels
    {
        get => CurrentSchematic.GroupGateBusPinLabels;
        set => UpdateSchematic(CurrentSchematic with { GroupGateBusPinLabels = value });
    }

    public GatePinVisibilityMode GatePinVisibilityMode
    {
        get => CurrentSchematic.GatePinVisibilityMode;
        set => UpdateSchematic(CurrentSchematic with { GatePinVisibilityMode = value });
    }

    public double GatePinLabelCompactZoom
    {
        get => CurrentSchematic.GatePinLabelCompactZoom;
        set
        {
            double compact = NormalizeZoomThreshold(value);
            double detailed = Math.Max(compact, CurrentSchematic.GatePinLabelDetailedZoom);
            UpdateSchematic(CurrentSchematic with
            {
                GatePinLabelCompactZoom = compact,
                GatePinLabelDetailedZoom = detailed,
            });
        }
    }

    public double GatePinLabelDetailedZoom
    {
        get => CurrentSchematic.GatePinLabelDetailedZoom;
        set => UpdateSchematic(CurrentSchematic with
        {
            GatePinLabelDetailedZoom = Math.Max(
                CurrentSchematic.GatePinLabelCompactZoom,
                NormalizeZoomThreshold(value)),
        });
    }

    public GateBusVisualizationMode GateBusVisualizationMode
    {
        get => CurrentSchematic.GateBusVisualizationMode;
        set => UpdateSchematic(CurrentSchematic with { GateBusVisualizationMode = value });
    }

    public double GateBusTrunkMaxZoom
    {
        get => CurrentSchematic.GateBusTrunkMaxZoom;
        set => UpdateSchematic(CurrentSchematic with
        {
            GateBusTrunkMaxZoom = NormalizeZoomThreshold(value),
        });
    }

    public SchematicConfiguration GateSchematicSettings => CurrentSchematic;

    public IReadOnlyList<RoutingQuality> AvailableRoutingQualities { get; } =
        Enum.GetValues<RoutingQuality>();

    public IReadOnlyList<GatePinLabelMode> AvailableGatePinLabelModes { get; } =
        Enum.GetValues<GatePinLabelMode>();

    public IReadOnlyList<GatePinVisibilityMode> AvailableGatePinVisibilityModes { get; } =
        Enum.GetValues<GatePinVisibilityMode>();

    public IReadOnlyList<GateBusVisualizationMode> AvailableGateBusVisualizationModes { get; } =
        Enum.GetValues<GateBusVisualizationMode>();

    public string SynthesisStatus
    {
        get => _synthesisStatus;
        private set
        {
            if (SetProperty(ref _synthesisStatus, value) && !string.IsNullOrWhiteSpace(value))
            {
                Status = value;
            }
        }
    }
    private string _synthesisStatus = string.Empty;

    public bool IsSynthesizing
    {
        get => _isSynthesizing;
        private set
        {
            if (SetProperty(ref _isSynthesizing, value))
            {
                ((AsyncCommand)SynthesizeCommand).RaiseCanExecuteChanged();
            }
        }
    }
    private bool _isSynthesizing;

    public ICommand SynthesizeCommand =>
        _synthesizeCommand ??= CreateSimulationCommand("Synthesis", SynthesizeAsync,
            () => !_isSynthesizing && IsSynthesisAvailable && SynthesisEnabled && _currentProjectDirectory is not null);
    private ICommand? _synthesizeCommand;

    /// <summary>Raised when synthesis succeeds — the View opens the gate-level window.</summary>
    public event EventHandler<Bistable.Core.Synthesis.GateNetlist>? GateNetlistReady;

    private async Task SynthesizeAsync(CancellationToken cancellationToken)
    {
        if (_currentProject is null || _currentProjectDirectory is null) return;
        SynthesisConfiguration synth = CurrentSynthesis;
        if (!synth.Enabled)
        {
            SynthesisStatus = "Synthesis is disabled in project settings.";
            return;
        }

        IsSynthesizing = true;
        try
        {
            Bistable.Yosys.YosysTool tool = new();
            if (!await tool.IsAvailableAsync(cancellationToken))
            {
                SynthesisStatus = "Yosys not found on PATH. Install yosys to use synthesis.";
                return;
            }

            string scriptPath = Path.Combine(_currentProjectDirectory, ".bistable", "synthesis", "synth.ys");
            Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
            string script = Bistable.Yosys.YosysScriptBuilder.Build(
                _currentProject!, synth, _currentProjectDirectory);
            await File.WriteAllTextAsync(scriptPath, script, cancellationToken);

            SynthesisStatus = "Running Yosys…";
            await tool.RunScriptAsync(scriptPath, _currentProjectDirectory, cancellationToken);

            string outputJson = Path.IsPathRooted(synth.OutputJson)
                ? synth.OutputJson
                : Path.Combine(_currentProjectDirectory, synth.OutputJson);
            if (!File.Exists(outputJson))
            {
                SynthesisStatus = $"Yosys produced no output at {synth.OutputJson}.";
                return;
            }

            Bistable.Core.Synthesis.GateNetlist netlist =
                await Bistable.Yosys.YosysJsonReader.ReadFileAsync(outputJson, cancellationToken);

            int cellCount = netlist.Modules.TryGetValue(netlist.TopModule, out var topModule)
                ? topModule.Cells.Count : 0;
            SynthesisStatus = $"Synthesised {netlist.TopModule}: {cellCount} cells.";
            GateNetlistReady?.Invoke(this, netlist);
            await BuildGateLevelWorkerAsync(synth, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            SynthesisStatus = "Synthesis cancelled.";
        }
        catch (Exception ex)
        {
            SynthesisStatus = $"Synthesis failed: {ex.Message}";
        }
        finally
        {
            IsSynthesizing = false;
        }
    }

    private void UpdateSynthesis(SynthesisConfiguration synthesis)
    {
        if (_currentProject is null) return;
        _currentProject = _currentProject with { Synthesis = synthesis };
        RaiseSynthesisSettingsChanged();
    }

    private async Task SaveSynthesisSettingsAsync(CancellationToken cancellationToken)
        => await SaveProjectSettingsAsync(cancellationToken);

    private void UpdateSchematic(SchematicConfiguration schematic)
    {
        if (_currentProject is null) return;
        _currentProject = _currentProject with { Schematic = schematic };
        RaiseSynthesisSettingsChanged();
    }

    private async Task SaveProjectSettingsAsync(CancellationToken cancellationToken)
    {
        if (_currentProject is null || _currentProjectPath is null) return;
        try
        {
            _currentProject = _currentProject with
            {
                Synthesis = CurrentSynthesis,
                Schematic = CurrentSchematic,
            };
            string json = System.Text.Json.JsonSerializer.Serialize(_currentProject, ProjectConfiguration.JsonOptions);
            await File.WriteAllTextAsync(_currentProjectPath, json, cancellationToken);
            SynthesisStatus = "Project settings saved to project file.";
            RaiseSynthesisSettingsChanged();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            SynthesisStatus = $"Saving synthesis settings failed: {ex.Message}";
        }
    }

    private void RaiseSynthesisSettingsChanged()
    {
        OnPropertyChanged(nameof(IsSynthesisAvailable));
        OnPropertyChanged(nameof(SynthesisEnabled));
        OnPropertyChanged(nameof(SynthesisTopModule));
        OnPropertyChanged(nameof(SynthesisOutputJson));
        OnPropertyChanged(nameof(SynthesisOutputVerilog));
        OnPropertyChanged(nameof(SynthesisGenericCells));
        OnPropertyChanged(nameof(SynthesisFlatten));
        OnPropertyChanged(nameof(GateRoutingQuality));
        OnPropertyChanged(nameof(GateAutoDowngradeLargeGraphs));
        OnPropertyChanged(nameof(GatePinLabelMode));
        OnPropertyChanged(nameof(GateGroupBusPinLabels));
        OnPropertyChanged(nameof(GatePinVisibilityMode));
        OnPropertyChanged(nameof(GatePinLabelCompactZoom));
        OnPropertyChanged(nameof(GatePinLabelDetailedZoom));
        OnPropertyChanged(nameof(GateBusVisualizationMode));
        OnPropertyChanged(nameof(GateBusTrunkMaxZoom));
        OnPropertyChanged(nameof(GateSchematicSettings));
        OnPropertyChanged(nameof(AvailableRoutingQualities));
        OnPropertyChanged(nameof(AvailableGatePinLabelModes));
        OnPropertyChanged(nameof(AvailableGatePinVisibilityModes));
        OnPropertyChanged(nameof(AvailableGateBusVisualizationModes));
        OnPropertyChanged(nameof(CanSaveSynthesisSettings));
        OnPropertyChanged(nameof(CanSaveProjectSettings));
        ((AsyncCommand)SynthesizeCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)SaveSynthesisSettingsCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)SaveProjectSettingsCommand).RaiseCanExecuteChanged();
    }

    private string TopModuleForSynthesisFallback() =>
        _currentProject?.TopModule ?? (TopModule == "-" ? string.Empty : TopModule);

    private static string? NormalizeOptionalText(string value)
    {
        string trimmed = value.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string NormalizeRequiredText(string value, string fallback)
    {
        string trimmed = value.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed;
    }

    private static double NormalizeZoomThreshold(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0.05, 8.0) : 0.55;

    private async Task BuildGateLevelWorkerAsync(
        SynthesisConfiguration synth,
        CancellationToken cancellationToken)
    {
        if (_currentProject is null || _currentProjectDirectory is null) return;

        try
        {
            await DisposeGateLevelWorkerAsync();
            Progress<SimulationWorkerBuildProgress> progress = new(report =>
            {
                if (!string.IsNullOrWhiteSpace(report.Message))
                {
                    SynthesisStatus = $"Gate build {report.Stage}: {TrimBuildStatus(report.Message)}";
                }
            });

            GateLevelWorkerBuildResult gateBuild = await _gateLevelWorkerBuilder.BuildAsync(
                _currentProject,
                synth,
                _currentProjectDirectory,
                cancellationToken,
                progress,
                _currentAst);

            _gateLevelWorker = await SimulationWorkerClient.StartAsync(
                gateBuild.Worker.ExecutablePath,
                cancellationToken);
            _gateLevelTraceFilePath = gateBuild.Worker.TraceFilePath;
            RaiseSimulationTargetChanged();
            SynthesisStatus = $"Gate-level worker ready: {Path.GetFileName(gateBuild.Worker.ExecutablePath)}.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SynthesisStatus = $"Gate-level worker build failed: {ex.Message}";
        }
    }

    private SimulationWorkerClient? ActiveSimulationWorker =>
        _simulationTarget == SimulationTarget.GateLevel
            ? _gateLevelWorker
            : _worker;

    private void ActivateSimulationTarget(SimulationTarget target)
    {
        CancelLiveEvaluation();
        _traceFilePath = target == SimulationTarget.GateLevel
            ? _gateLevelTraceFilePath
            : _rtlTraceFilePath;

        SimulationWorkerClient? activeWorker = ActiveSimulationWorker;
        _liveProbes.AttachWorker(activeWorker);
        _ = _liveProbes.RefreshDescriptorsAsync(CancellationToken.None);
        ForcedPaths.Clear();
        _traceDocument = VcdTraceDocument.Empty;
        Time = 0;
        ClearWaveformSamples();
        RaiseSimulationTargetChanged();
        Status = $"{ActiveSimulationTargetLabel} simulation target selected.";
    }

    private void RaiseSimulationTargetChanged()
    {
        OnPropertyChanged(nameof(SelectedSimulationTarget));
        OnPropertyChanged(nameof(AvailableSimulationTargets));
        OnPropertyChanged(nameof(IsGateLevelWorkerReady));
        OnPropertyChanged(nameof(CanCompareRtlAndGate));
        OnPropertyChanged(nameof(ActiveSimulationTargetLabel));
        ((AsyncCommand)CompareRtlAndGateCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)RunCpuPresetCommand).RaiseCanExecuteChanged();
    }

    /// <summary>
    /// P4-5: optional callback the View sets so the VM can narrow post-Tick
    /// scalar refreshes to "what the schematic actually rendered last frame."
    /// Null when no view is bound or when the legacy path (refresh all) is
    /// desired (tests / headless paths). Read-only path lists.
    /// </summary>
    public Func<IReadOnlyCollection<string>>? VisibleProbePathsProvider { get; set; }

    // ── Phase 5: CPU Run panel ──────────────────────────────────────────────

    /// <summary>
    /// The current project's runtime config, or null when the design isn't
    /// CPU-shaped (or no project loaded). Bound to the Run panel's IsVisible.
    /// </summary>
    public Bistable.Core.Projects.CpuRuntimeConfiguration? CpuRuntime =>
        _currentProject?.Runtime;

    /// <summary>
    /// P5-8 follow-up: optional absolute path to a program image that should
    /// override the one declared in the project's first ProgramImageBinding.
    /// Set by the toolbar "Load Program…" button after the user picks a file.
    /// Cleared on project load so a fresh project never inherits an old override.
    /// </summary>
    public string? CpuProgramOverridePath
    {
        get => _cpuProgramOverridePath;
        private set
        {
            if (SetProperty(ref _cpuProgramOverridePath, value))
            {
                OnPropertyChanged(nameof(CpuProgramDisplayName));
            }
        }
    }
    private string? _cpuProgramOverridePath;

    /// <summary>Filename of the active program (override if set, otherwise config default).</summary>
    public string CpuProgramDisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_cpuProgramOverridePath))
            {
                return Path.GetFileName(_cpuProgramOverridePath);
            }
            string? configPath = CpuRuntime?.ProgramImages is { Count: > 0 } imgs ? imgs[0].Path : null;
            return string.IsNullOrWhiteSpace(configPath) ? "(no program)" : Path.GetFileName(configPath);
        }
    }

    public ICommand LoadCpuProgramCommand =>
        _loadCpuProgramCommand ??= new RelayCommand(() => LoadCpuProgramRequested?.Invoke(this, EventArgs.Empty));
    private ICommand? _loadCpuProgramCommand;

    public event EventHandler? LoadCpuProgramRequested;

    /// <summary>
    /// Called by the View after the file picker returns a path. Setting null
    /// reverts to the config default.
    /// </summary>
    public void SetCpuProgramOverride(string? filePath) => CpuProgramOverridePath = filePath;

    /// <summary>
    /// Status line shown next to the Run button — "Loaded N cells", "Ran K
    /// cycles, halted", "Run failed: …", etc.
    /// </summary>
    public string CpuRunStatus
    {
        get => _cpuRunStatus;
        private set
        {
            if (SetProperty(ref _cpuRunStatus, value) && !string.IsNullOrWhiteSpace(value))
            {
                // Also surface CPU run progress on the global status bar so the
                // user notices long-running ticks even when the toolbar text is
                // clipped behind the dock panels.
                Status = value;
            }
        }
    }
    private string _cpuRunStatus = string.Empty;

    public bool IsCpuRunning
    {
        get => _isCpuRunning;
        private set
        {
            if (SetProperty(ref _isCpuRunning, value))
            {
                ((AsyncCommand)RunCpuPresetCommand).RaiseCanExecuteChanged();
            }
        }
    }
    private bool _isCpuRunning;

    public ICommand RunCpuPresetCommand =>
        _runCpuPresetCommand ??= CreateSimulationCommand("CPU run", RunCpuPresetAsync, () =>
            !_isCpuRunning && ActiveSimulationWorker is not null && CpuRuntime?.RunPresets is { Count: > 0 });
    private ICommand? _runCpuPresetCommand;

    private async Task RunCpuPresetAsync(CancellationToken cancellationToken)
    {
        SimulationWorkerClient? worker = ActiveSimulationWorker;
        if (worker is null || CpuRuntime is not { } runtime || _currentProjectDirectory is null) return;
        var preset = runtime.RunPresets?.FirstOrDefault();
        if (preset is null) return;

        IsCpuRunning = true;
        try
        {
            CpuRunEngine engine = new(_liveProbes);

            if (runtime.Reset is { } reset)
            {
                CpuRunStatus = "Resetting…";
                await engine.ApplyResetAsync(worker, reset, preset.Clock, cancellationToken);
            }

            // enable=1 is the canonical "let it run" gate — most CPU samples
            // expose it; harmless when absent (worker will return error and
            // we swallow it for non-CPU shapes).
            try
            {
                await worker.StepAsync(
                    new SimulationCommand(SimulationCommandType.SetInput, "enable", "1"),
                    cancellationToken);
            }
            catch (InvalidOperationException) { /* design has no enable port */ }

            if (runtime.ProgramImages is { Count: > 0 } images)
            {
                CpuRunStatus = "Loading program…";
                for (int i = 0; i < images.Count; i++)
                {
                    var img = images[i];
                    // P5-8 follow-up: override the FIRST image's path when the
                    // user picked a file. Other images keep their config paths
                    // so multi-image configs still work for trickier designs.
                    string sourcePath = i == 0 && !string.IsNullOrWhiteSpace(_cpuProgramOverridePath)
                        ? _cpuProgramOverridePath!
                        : img.Path;
                    string filePath = Path.IsPathRooted(sourcePath)
                        ? sourcePath
                        : Path.Combine(_currentProjectDirectory, sourcePath);
                    if (!File.Exists(filePath))
                    {
                        CpuRunStatus = $"Program image missing: {img.Path}";
                        return;
                    }
                    int width = ResolveMemoryCellWidth(img.ProbePath);
                    var imgFormat = string.Equals(img.Format, "bin", StringComparison.OrdinalIgnoreCase)
                        ? MemoryFileLoader.NumeralBase.Bin
                        : MemoryFileLoader.NumeralBase.Hex;
                    var image = MemoryFileLoader.LoadFromFile(filePath, width, depth: 0, imgFormat);
                    var loaded = await engine.LoadProgramAsync(worker, img, image, cancellationToken);
                    if (loaded.Failed > 0)
                    {
                        CpuRunStatus = $"Program load partial: {loaded.Written} written, {loaded.Failed} failed";
                        return;
                    }
                }
            }

            CpuRunStatus = $"Running '{preset.Name}'…";
            var result = await engine.RunAsync(worker, preset, runtime.State, cancellationToken);

            // P5-8: end the run with an Eval so the top-level snapshot (pc,
            // halted, debug_xN, …) is current. Without this, the toolbar shows
            // "Stopped after N cycles" but the output bindings still hold the
            // pre-run frame until the user manually presses Eval.
            SimulationFrame frame = await worker.StepAsync(
                new SimulationCommand(SimulationCommandType.Eval), cancellationToken);
            ApplyFrame(frame);

            CpuRunStatus = result.StopConditionHit
                ? $"Stopped after {result.Cycles} cycles."
                : $"Reached {result.Cycles}-cycle cap.";
        }
        catch (OperationCanceledException)
        {
            CpuRunStatus = "Cancelled.";
        }
        catch (Exception ex)
        {
            CpuRunStatus = $"Run failed: {ex.Message}";
        }
        finally
        {
            IsCpuRunning = false;
        }
    }

    private int ResolveMemoryCellWidth(string probePath)
    {
        var descriptor = _liveProbes.GetDescriptor(probePath);
        return descriptor?.Width ?? 32;
    }


    /// <summary>
    /// Enumerate every memory probe declared in the module at the given
    /// hierarchy path. Used by the schematic context menu to surface
    /// "Open Memory Viewer: X" entries when the user right-clicks on an
    /// instance/scope. Returns empty when the path doesn't resolve or the
    /// module has no memories.
    /// </summary>
    public IReadOnlyList<MemoryLocation> EnumerateMemoriesAt(string hierarchyPath)
    {
        if (_currentAst is null) return [];
        HierarchyNodeViewModel? node = HierarchyRoot is null
            ? null
            : FindHierarchyNodeStatic(HierarchyRoot, hierarchyPath);
        string? moduleName = node?.ModuleName;
        if (moduleName is null) return [];

        Bistable.Core.Design.Ast.ModuleAst? module = _currentAst.Modules
            .FirstOrDefault(m => string.Equals(m.Name, moduleName, StringComparison.OrdinalIgnoreCase));
        if (module is null) return [];

        List<MemoryLocation> result = [];
        foreach (Bistable.Core.Design.Ast.SignalDecl sig in module.LocalSignals
                     .Where(s => s.ArrayDims.Count == 1 && !s.Name.StartsWith("__V", StringComparison.Ordinal))
                     .OrderBy(static s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            string resolved = BuildResolvedLocalSignalName(hierarchyPath, sig.Name);
            result.Add(new MemoryLocation(sig.Name, resolved, sig.Width, sig.ArrayDims[0].Width));
        }
        return result;
    }

    private static HierarchyNodeViewModel? FindHierarchyNodeStatic(HierarchyNodeViewModel current, string hierarchyPath)
    {
        if (string.Equals(current.HierarchyPath, hierarchyPath, StringComparison.Ordinal)) return current;
        foreach (HierarchyNodeViewModel child in current.Children)
        {
            HierarchyNodeViewModel? match = FindHierarchyNodeStatic(child, hierarchyPath);
            if (match is not null) return match;
        }
        return null;
    }

    /// <summary>
    /// Open the memory viewer for a specific probe path (used from the schematic
    /// context menu where the path is known directly without going through
    /// SelectedSchematicSignalName).
    /// </summary>
    public void OpenMemoryViewerForPath(string resolvedPath)
    {
        if (string.IsNullOrWhiteSpace(resolvedPath)) return;
        SelectedSchematicSignalName = resolvedPath;
        MemoryViewerRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Phase 3 force: pin the selected internal signal across subsequent eval/tick cycles.</summary>
    public ICommand ForceSelectedSchematicSignalCommand { get; }

    /// <summary>Phase 3 release: unfreeze a previously-forced internal signal.</summary>
    public ICommand ReleaseSelectedSchematicSignalCommand { get; }

    /// <summary>Release a specific path (used from the Forced Signals list).</summary>
    public ICommand ReleasePathCommand { get; }

    /// <summary>Release every forced signal in one shot.</summary>
    public ICommand ReleaseAllForcedCommand { get; }

    public ICommand RemoveSelectedWaveformSignalCommand { get; }

    public ICommand ClearWaveformCommand { get; }

    // P2.7-5: command bound to the chip strip's "Clear all" button. Raises
    // ClearPinnedSignalsRequested so the schematic control owning the actual
    // HashSet can clear it and emit PinnedSignalsChanged; MainWindow listens
    // and calls RefreshPinnedSignals to mirror the new state.
    public ICommand ClearPinnedSignalsCommand { get; }

    public event EventHandler? ClearPinnedSignalsRequested;

    // P2.7-5 follow-up: chip "×" button asks the control to unpin one signal.
    // The control owns the canonical HashSet (it's where Ctrl+click also
    // toggles), so the VM just relays the request via this event.
    public event EventHandler<string>? UnpinSignalRequested;

    public void UnpinSignal(string signalName)
    {
        if (string.IsNullOrWhiteSpace(signalName)) return;
        UnpinSignalRequested?.Invoke(this, signalName);
    }

    public void RefreshPinnedSignals(IReadOnlyCollection<string> pinned)
    {
        PinnedSignals.Clear();
        foreach (string s in pinned) PinnedSignals.Add(s);
    }

    public ICommand MoveWaveformSignalUpCommand { get; }

    public ICommand MoveWaveformSignalDownCommand { get; }

    public ICommand ZoomWaveformInCommand { get; }

    public ICommand ZoomWaveformOutCommand { get; }

    public ICommand PanWaveformLeftCommand { get; }

    public ICommand PanWaveformRightCommand { get; }

    public ICommand ToggleProjectPaneCommand { get; }

    public ICommand ToggleWaveformPaneCommand { get; }

    public ICommand DockPanelCommand { get; }

    public ICommand ToggleSchematicPaneCommand { get; }

    public ICommand FitWaveformCommand { get; }

    public SignalViewModel? SelectedSignal
    {
        get => _selectedSignal;
        set
        {
            if (SetProperty(ref _selectedSignal, value))
            {
                if (value is not null)
                {
                    _selectedSchematicReferenceName = value.Name;
                }
                else if (!_settingSelectedSchematicReference)
                {
                    _selectedSchematicReferenceName = null;
                }

                if (_observedSelectedSignal is not null)
                {
                    _observedSelectedSignal.PropertyChanged -= OnSelectedSignalPropertyChanged;
                }

                _observedSelectedSignal = value;
                if (_observedSelectedSignal is not null)
                {
                    _observedSelectedSignal.PropertyChanged += OnSelectedSignalPropertyChanged;
                }

                if (value is not null && value.IsInput)
                {
                    SchematicDriveValue = value.Value;
                }

                SyncSelectedWaveformLaneFromSignal();
                RaiseSelectedSchematicSignalProperties();
            }
        }
    }

    public WaveformLaneViewModel? SelectedWaveformLane
    {
        get => _selectedWaveformLane;
        set
        {
            if (SetProperty(ref _selectedWaveformLane, value))
            {
                OnPropertyChanged(nameof(SelectedWaveformSignalName));
                OnPropertyChanged(nameof(WaveformCursorSummary));
                SyncSelectedSignalFromWaveformLane();
            }
        }
    }

    public string? SelectedWaveformSignalName
    {
        get => SelectedWaveformLane?.Name;
        set
        {
            if (string.Equals(SelectedWaveformLane?.Name, value, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            SelectedWaveformLane = value is null
                ? null
                : WaveformLanes.FirstOrDefault(lane => string.Equals(lane.Name, value, StringComparison.OrdinalIgnoreCase));
        }
    }

    public string? SelectedSchematicSignalName
    {
        get => SelectedSignal?.Name ?? _selectedSchematicReferenceName;
        set
        {
            if (string.Equals(SelectedSchematicSignalName, value, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _selectedSchematicReferenceName = value;
            _settingSelectedSchematicReference = true;
            try
            {
                SelectedSignal = value is null
                    ? null
                    : (FindAnySignalByName(value) ?? ResolveLiveSignalForLocalReference(value));
            }
            finally
            {
                _settingSelectedSchematicReference = false;
            }

            // Reset the Drive textbox to the new signal's current value so a
            // stale value from the previous selection doesn't accidentally get
            // applied. Top-level inputs already get this via the SelectedSignal
            // setter; this branch covers internal probes where SelectedSignal
            // stayed null.
            if (SelectedSignal is null)
            {
                SchematicDriveValue = SelectedSchematicSignalValue is { } v && v != "-" ? v : "0";
            }

            RaiseSelectedSchematicSignalProperties();
        }
    }

    // Edge labels carry the *local* signal name (e.g. "reg_a_we") while traced signals
    // are keyed by their full hierarchy path (e.g. "arnicomp_top.reg_a_we"). When the
    // local lookup hits, fall back through its ResolvedSignalName so the probe panel
    // subscribes to the live signal and shows the simulator's current value.
    private SignalViewModel? ResolveLiveSignalForLocalReference(string reference)
    {
        HierarchyScopeLocalSignalViewModel? local = FindSchematicLocalSignalReference(reference);
        if (local?.ResolvedSignalName is not { } resolved)
        {
            return null;
        }

        return FindAnySignalByName(resolved);
    }

    public string? SelectedHierarchyPath
    {
        get => SelectedHierarchyNode?.HierarchyPath;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                SelectedHierarchyNode = HierarchyRoot;
                return;
            }

            HierarchyNodeViewModel? node = FindHierarchyNode(HierarchyRoot, value);
            if (node is not null && !ReferenceEquals(node, SelectedHierarchyNode))
            {
                SelectedHierarchyNode = node;
            }
        }
    }

    public long WaveformCursorOrder
    {
        get => _waveformCursorOrder;
        set
        {
            if (SetProperty(ref _waveformCursorOrder, Math.Max(0, value)))
            {
                OnPropertyChanged(nameof(WaveformCursorTime));
                OnPropertyChanged(nameof(WaveformCursorSummary));
            }
        }
    }

    public double WaveformZoom
    {
        get => _waveformZoom;
        private set
        {
            if (SetProperty(ref _waveformZoom, value))
            {
                _ = PersistLayoutStateAsync();
            }
        }
    }

    public int WaveformOffset
    {
        get => _waveformOffset;
        private set
        {
            if (SetProperty(ref _waveformOffset, value))
            {
                _ = PersistLayoutStateAsync();
            }
        }
    }

    public bool IsProjectPaneVisible
    {
        get => ProjectDockZone != DockZone.Hidden;
        private set
        {
            if (value)
            {
                MoveDockPanel(DockPanelKind.Project, _projectLastVisibleZone);
            }
            else
            {
                MoveDockPanel(DockPanelKind.Project, DockZone.Hidden);
            }
        }
    }

    public bool IsWaveformPaneVisible
    {
        get => WaveformDockZone != DockZone.Hidden;
        private set
        {
            if (value)
            {
                MoveDockPanel(DockPanelKind.Waveform, _waveformLastVisibleZone);
            }
            else
            {
                MoveDockPanel(DockPanelKind.Waveform, DockZone.Hidden);
            }
        }
    }

    public bool IsSchematicPaneVisible
    {
        get => SchematicDockZone != DockZone.Hidden;
        private set
        {
            if (value)
            {
                MoveDockPanel(DockPanelKind.Schematic, _schematicLastVisibleZone);
            }
            else
            {
                MoveDockPanel(DockPanelKind.Schematic, DockZone.Hidden);
            }
        }
    }

    public DockZone ProjectDockZone
    {
        get => _projectDockZone;
        private set
        {
            if (_projectDockZone == value)
            {
                return;
            }

            _projectDockZone = value;
            if (value != DockZone.Hidden)
            {
                _projectLastVisibleZone = value;
            }

            _projectPanel.Zone = value;
            RebuildDockCollections();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsProjectPaneVisible));
            _ = PersistLayoutStateAsync();
        }
    }

    public DockZone WaveformDockZone
    {
        get => _waveformDockZone;
        private set
        {
            if (_waveformDockZone == value)
            {
                return;
            }

            _waveformDockZone = value;
            if (value != DockZone.Hidden)
            {
                _waveformLastVisibleZone = value;
            }

            _waveformPanel.Zone = value;
            RebuildDockCollections();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsWaveformPaneVisible));
            _ = PersistLayoutStateAsync();
        }
    }

    public DockZone SchematicDockZone
    {
        get => _schematicDockZone;
        private set
        {
            if (_schematicDockZone == value)
            {
                return;
            }

            _schematicDockZone = value;
            if (value != DockZone.Hidden)
            {
                _schematicLastVisibleZone = value;
            }

            _schematicPanel.Zone = value;
            RebuildDockCollections();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSchematicPaneVisible));
            _ = PersistLayoutStateAsync();
        }
    }

    public double LeftDockWidth
    {
        get => _leftDockWidth;
        private set
        {
            if (SetProperty(ref _leftDockWidth, Math.Max(220, value)))
            {
                _ = PersistLayoutStateAsync();
            }
        }
    }

    public double RightDockWidth
    {
        get => _rightDockWidth;
        private set
        {
            if (SetProperty(ref _rightDockWidth, Math.Max(220, value)))
            {
                _ = PersistLayoutStateAsync();
            }
        }
    }

    public double BottomDockHeight
    {
        get => _bottomDockHeight;
        private set
        {
            if (SetProperty(ref _bottomDockHeight, Math.Max(180, value)))
            {
                _ = PersistLayoutStateAsync();
            }
        }
    }

    public DockPanelViewModel? SelectedLeftDockPanel
    {
        get => _selectedLeftDockPanel;
        set => SetProperty(ref _selectedLeftDockPanel, value);
    }

    public DockPanelViewModel? SelectedRightDockPanel
    {
        get => _selectedRightDockPanel;
        set => SetProperty(ref _selectedRightDockPanel, value);
    }

    public DockPanelViewModel? SelectedBottomDockPanel
    {
        get => _selectedBottomDockPanel;
        set => SetProperty(ref _selectedBottomDockPanel, value);
    }

    public DockPanelViewModel? SelectedCenterDockPanel
    {
        get => _selectedCenterDockPanel;
        set => SetProperty(ref _selectedCenterDockPanel, value);
    }

    public string Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
            {
                ToastMessage = value;
                _ = ShowToastAsync();
            }
        }
    }

    /// <summary>Mirror of <see cref="Status"/> shown as an overlay toast for ~2.5s.</summary>
    public string ToastMessage
    {
        get => _toastMessage;
        private set => SetProperty(ref _toastMessage, value);
    }
    private string _toastMessage = string.Empty;

    // P2.7-9: schematic theme — the combo box binds to SchematicThemePreset;
    // changes resolve to the corresponding SchematicTheme record (which the
    // preview control binds to via its Palette property) and persist through
    // UserPreferencesStore.
    public SchematicThemePreset SchematicThemePreset
    {
        get => _schematicThemePreset;
        set
        {
            if (SetProperty(ref _schematicThemePreset, value))
            {
                SchematicTheme = SchematicThemePresets.Get(value);
                SaveUserPreferences();
            }
        }
    }

    public SchematicTheme SchematicTheme
    {
        get => _schematicTheme;
        private set => SetProperty(ref _schematicTheme, value);
    }

    public IReadOnlyList<SchematicThemePreset> AvailableSchematicThemes { get; } =
        Enum.GetValues<SchematicThemePreset>();

    // P2.7-9 follow-up: routing engine — bound to View menu radio items and the
    // Preferences window combo. Persists through UserPreferencesStore.
    public SchematicRoutingEngine SchematicRouter
    {
        get => _schematicRouter;
        set
        {
            if (SetProperty(ref _schematicRouter, value))
            {
                SaveUserPreferences();
            }
        }
    }

    public IReadOnlyList<SchematicRoutingEngine> AvailableSchematicRouters { get; } =
        Enum.GetValues<SchematicRoutingEngine>();

    private void SaveUserPreferences() => _preferencesStore.Save(new UserPreferences
    {
        SchematicTheme = _schematicThemePreset,
        SchematicRouter = _schematicRouter,
        LiveReloadEnabled = _liveReloadEnabled,
        LiveReloadDebounceMs = _hasUserLiveReloadDebounceOverride ? _liveReloadDebounceMs : null,
    });

    private void OnSelectedSourceDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SourceDocumentViewModel.IsDirty) or nameof(SourceDocumentViewModel.Text))
        {
            ((AsyncCommand)SaveSourceCommand).RaiseCanExecuteChanged();
        }
    }

    public bool IsToastVisible
    {
        get => _isToastVisible;
        private set => SetProperty(ref _isToastVisible, value);
    }
    private bool _isToastVisible;

    private CancellationTokenSource? _toastCancellation;

    private async Task ShowToastAsync()
    {
        // Cancel the previous toast so a rapid succession of status updates
        // doesn't accumulate timers that race to hide a still-fresh message.
        CancellationTokenSource? previous = _toastCancellation;
        if (previous is not null) await previous.CancelAsync();
        previous?.Dispose();

        CancellationTokenSource fresh = new();
        _toastCancellation = fresh;
        CancellationToken token = fresh.Token;

        IsToastVisible = true;
        try
        {
            await Task.Delay(2500, token);
        }
        catch (TaskCanceledException)
        {
            return;   // a newer toast superseded this one
        }
        if (!token.IsCancellationRequested)
        {
            IsToastVisible = false;
        }
    }

    public string ProjectName
    {
        get => _projectName;
        private set => SetProperty(ref _projectName, value);
    }

    public string TopModule
    {
        get => _topModule;
        private set
        {
            if (SetProperty(ref _topModule, value))
            {
                OnPropertyChanged(nameof(SchematicModuleName));
                OnPropertyChanged(nameof(SelectedHierarchyScopeModuleName));
                OnPropertyChanged(nameof(SelectedHierarchyScopePath));
            }
        }
    }

    public string VerilatorVersion
    {
        get => _verilatorVersion;
        private set => SetProperty(ref _verilatorVersion, value);
    }

    public bool IsSubSimActive
    {
        get => _isSubSimActive;
        private set
        {
            if (SetProperty(ref _isSubSimActive, value))
            {
                OnPropertyChanged(nameof(CanEnterSubSim));
                OnPropertyChanged(nameof(SubSimStatusLabel));
                ConfigureProjectFileWatcher();
                OnPropertyChanged(nameof(CanCompareRtlAndGate));
                ((RelayCommand)ExitSubSimulationCommand).RaiseCanExecuteChanged();
                ((AsyncCommand)CompareRtlAndGateCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanEnterSubSim =>
        !_isSubSimActive
        && SelectedHierarchyNode is { } node
        && !ReferenceEquals(node, HierarchyRoot)
        && _currentDesign?.ModuleCatalog.ContainsKey(node.ModuleName) == true
        && _currentProjectDirectory is not null;

    public string SubSimStatusLabel =>
        _isSubSimActive ? $"Isolated: {_subSimProject?.TopModule}" : string.Empty;

    private ProjectConfiguration? ActiveProject => _subSimProject ?? _currentProject;

    public ulong Time
    {
        get => _time;
        private set => SetProperty(ref _time, value);
    }

    public string SchematicModuleName => string.IsNullOrWhiteSpace(TopModule) || TopModule == "-" ? "module" : TopModule;

    public string SelectedHierarchySummary =>
        SelectedHierarchyNode is null
            ? "Hierarchy"
            : $"{SelectedHierarchyNode.InstanceName} : {SelectedHierarchyNode.ModuleName}";

    public string SelectedHierarchyScopeTitle =>
        SelectedHierarchyNode is null
            ? "Scope Signals"
            : $"{SelectedHierarchyNode.InstanceName} Scope";

    public string SelectedHierarchyScopeModuleName =>
        SelectedHierarchyNode?.ModuleName
        ?? (TopModule == "-" ? string.Empty : TopModule);

    public string SelectedHierarchyScopePath =>
        SelectedHierarchyNode?.HierarchyPath ?? string.Empty;

    public bool IsSelectedHierarchyScopeExpanded =>
        SelectedHierarchyNode is not null
        && SchematicExpandedPaths.Contains(SelectedHierarchyNode.HierarchyPath, StringComparer.OrdinalIgnoreCase);

    public string SelectedHierarchyScopeSummary
    {
        get
        {
            if (SelectedHierarchyNode is null)
            {
                return "Select an instance to inspect its internal trace scope.";
            }

            int exactCount = HierarchyScopeSignals.Count;
            int descendantCount = TraceSignals.Count(signal =>
                !string.IsNullOrWhiteSpace(signal.ScopePath)
                && signal.ScopePath.StartsWith($"{SelectedHierarchyNode.HierarchyPath}.", StringComparison.OrdinalIgnoreCase));

            return descendantCount == 0
                ? $"{exactCount} exact-scope traced signals."
                : $"{exactCount} exact-scope traced signals, {descendantCount} nested below.";
        }
    }

    public IReadOnlyList<HierarchyBreadcrumbItemViewModel> SelectedHierarchyBreadcrumbs =>
        BuildHierarchyBreadcrumbs();

    public string SelectedHierarchyScopeHint
    {
        get
        {
            if (SelectedHierarchyNode is null)
            {
                return "Select a hierarchy node to inspect internal probes.";
            }

            if (SelectedHierarchyChildScopes.Count > 0)
            {
                return "Click a child instance to navigate deeper. Double-click a probe to add it to waveform.";
            }

            return HierarchyScopeSignals.Count switch
            {
                0 => "No exact-scope traced locals in this instance.",
                1 => "Double-click the probe to add it to waveform.",
                _ => "Select a probe or double-click one to add it to waveform."
            };
        }
    }

    public HierarchyScopeNodeViewModel? SelectedHierarchyParentScope
    {
        get
        {
            HierarchyNodeViewModel? parent = FindHierarchyParentNode(HierarchyRoot, SelectedHierarchyNode);
            return parent is null ? null : CreateScopeNode(parent);
        }
    }

    public string? SelectedClockName
    {
        get => _selectedClockName;
        set => SetProperty(ref _selectedClockName, value);
    }

    public string RunCyclesText
    {
        get => _runCyclesText;
        set => SetProperty(ref _runCyclesText, value);
    }

    public string SchematicDriveValue
    {
        get => _schematicDriveValue;
        set => SetProperty(ref _schematicDriveValue, value);
    }

    public bool LiveModeEnabled
    {
        get => _liveModeEnabled;
        set
        {
            if (SetProperty(ref _liveModeEnabled, value))
            {
                if (value)
                {
                    ScheduleLiveEvaluation();
                }
                else
                {
                    CancelLiveEvaluation();
                }
            }
        }
    }

    public ulong WaveformCursorTime => SelectedWaveformLane?.GetTimeAtOrBefore(WaveformCursorOrder) ?? Time;

    public bool IsSchematicSignalSelected => SelectedSignal is not null || !string.IsNullOrWhiteSpace(_selectedSchematicReferenceName);

    /// <summary>
    /// True when the user can write a value to the selected schematic signal.
    /// Top-level inputs use the legacy `SetInput` pipeline; internal signals
    /// use the Phase 3 `WriteSignalAsync` probe write — available whenever a
    /// worker is attached and the signal has a resolvable hierarchy path.
    /// </summary>
    public bool CanDriveSelectedSchematicInput =>
        SelectedSignal?.IsInput == true
        || (_liveProbes.HasWorker && SelectedSchematicLocalSignal?.ResolvedSignalName is not null);

    /// <summary>True when the selected signal is an internal probe AND a worker is attached (force is internal-only).</summary>
    public bool CanForceSelectedSchematicSignal =>
        _liveProbes.HasWorker && SelectedSchematicLocalSignal?.ResolvedSignalName is not null;

    /// <summary>True when the selected internal signal is currently in the worker's forced-signal map.</summary>
    public bool IsSelectedSchematicSignalForced =>
        SelectedSchematicLocalSignal?.ResolvedSignalName is { } path && IsForced(path);

    /// <summary>
    /// True when the selected schematic signal's worker descriptor is flagged
    /// IsMemory — i.e. the Live Probe panel should expose the memory viewer
    /// section instead of a scalar value cell.
    /// </summary>
    public bool IsSelectedSchematicSignalMemory
    {
        get
        {
            HierarchyScopeLocalSignalViewModel? local = SelectedSchematicLocalSignal;
            if (local is null) return false;
            if (local.IsMemory) return true;   // AST-side metadata (available immediately)
            return local.ResolvedSignalName is { } memPath
                && _liveProbes.GetDescriptor(memPath)?.IsMemory == true;
        }
    }

    /// <summary>Cells from the most recent memory read of the selected probe; live-updated by <see cref="OnLiveMemoryUpdated"/>.</summary>
    public ObservableCollection<MemoryCellViewModel> SelectedMemoryCells { get; } = [];

    /// <summary>How many cells the memory viewer requests starting at address 0. Capped to the actual depth.</summary>
    public int MemoryViewerCellCount => Math.Min(MaxMemoryViewerCells, ResolveMemoryDepth() ?? MaxMemoryViewerCells);

    public string SelectedMemoryDepthLabel =>
        ResolveMemoryDepth() is { } d ? $"{d} cells × {ResolveMemoryCellWidth()}b" : "-";

    /// <summary>Public read of the memory cell width, used by the standalone Memory Viewer window.</summary>
    public int SelectedSchematicLocalSignalWidthForMemory => ResolveMemoryCellWidth();

    private const int MaxMemoryViewerCells = 256;

    private int? ResolveMemoryDepth()
    {
        HierarchyScopeLocalSignalViewModel? local = SelectedSchematicLocalSignal;
        if (local is null) return null;
        if (local.IsMemory) return local.MemoryDepth;
        if (local.ResolvedSignalName is { } path
            && _liveProbes.GetDescriptor(path) is { IsMemory: true } d)
        {
            return d.MemoryDepth;
        }
        return null;
    }

    private int ResolveMemoryCellWidth()
    {
        HierarchyScopeLocalSignalViewModel? local = SelectedSchematicLocalSignal;
        if (local is null) return 0;
        if (local.IsMemory) return local.Width;
        if (local.ResolvedSignalName is { } path
            && _liveProbes.GetDescriptor(path) is { IsMemory: true } d)
        {
            return d.Width;
        }
        return 0;
    }

    /// <summary>
    /// Tracking set: which hierarchy paths are currently forced (mirror of the
    /// worker's `forced_signals` map). Observable so the schematic renderer
    /// can colour forced edges and the Forced Signals panel can list them.
    /// </summary>
    public ObservableCollection<string> ForcedPaths { get; } = [];

    private bool IsForced(string path) =>
        ForcedPaths.Contains(path, StringComparer.OrdinalIgnoreCase);

    private void AddForcedPath(string path)
    {
        if (!IsForced(path)) ForcedPaths.Add(path);
    }

    private void RemoveForcedPath(string path)
    {
        for (int i = ForcedPaths.Count - 1; i >= 0; i--)
        {
            if (string.Equals(ForcedPaths[i], path, StringComparison.OrdinalIgnoreCase))
            {
                ForcedPaths.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// True for ~800ms after a successful Apply/Force/Release so the Live Probe
    /// panel's value can flash in a confirmation color. Cleared by a delayed
    /// continuation in <see cref="MarkLastSchematicWriteFreshAsync"/>.
    /// </summary>
    public bool IsLastSchematicWriteFresh
    {
        get => _isLastSchematicWriteFresh;
        private set => SetProperty(ref _isLastSchematicWriteFresh, value);
    }
    private bool _isLastSchematicWriteFresh;

    private async Task MarkLastSchematicWriteFreshAsync()
    {
        IsLastSchematicWriteFresh = true;
        await Task.Delay(800);
        IsLastSchematicWriteFresh = false;
    }

    public bool CanToggleSelectedSchematicInput => SelectedSignal?.IsInput == true && SelectedSignal.IsBoolean;

    public string SelectedSchematicSignalDisplayName =>
        SelectedSignal?.DisplayName
        ?? SelectedSchematicLocalSignal?.Name
        ?? _selectedSchematicReferenceName
        ?? "No signal selected";

    public string SelectedSchematicSignalDirection => SelectedSignal?.DirectionLabel ?? (SelectedSchematicLocalSignal is null ? "-" : "INTERNAL");

    public string SelectedSchematicSignalWidth => SelectedSignal?.WidthLabel ?? SelectedSchematicLocalSignal?.WidthLabel ?? "-";

    public string SelectedSchematicSignalValue
    {
        get
        {
            if (SelectedSignal?.Value is { } val) return val;

            // For internal signals (submodule outputs, locals), the local view-model only
            // snapshots its value at construction time. Re-fetch the live signal by its
            // resolved hierarchical name so probes track simulation updates.
            HierarchyScopeLocalSignalViewModel? local = SelectedSchematicLocalSignal;
            if (local?.ResolvedSignalName is { } resolved)
            {
                // Live probe (Phase 3 worker API). Returns the cached value if we
                // already read this path since the last Invalidate; otherwise kicks
                // an async refresh whose completion raises PropertyChanged via the
                // LiveProbeService.ValueUpdated event hooked up in the ctor.
                string? hot = _liveProbes.GetCached(resolved);
                if (hot is not null) return hot;
                if (_liveProbes.HasWorker) KickLiveProbeRefresh(resolved);

                // Fall back to whatever the trace document / waveform snapshot has.
                string? liveValue = FindAnySignalByName(resolved)?.Value;
                if (!string.IsNullOrWhiteSpace(liveValue) && liveValue != "-")
                {
                    return liveValue;
                }
            }

            string? localVal = local?.CurrentValue;
            if (localVal is not null and not "-") return localVal;
            return ComputeSliceValue(_selectedSchematicReferenceName) ?? localVal ?? "-";
        }
    }

    /// <summary>
    /// Fires the async <see cref="LiveProbeService.ReadAsync"/> for a path the
    /// UI just rendered as stale. Errors swallowed — the probe table may not
    /// include this path (wide signal, memory, Verilator tmp).
    /// </summary>
    private void KickLiveProbeRefresh(string hierarchyPath) =>
        _ = _liveProbes.ReadAsync(hierarchyPath, CancellationToken.None);

    private string? ComputeSliceValue(string? targetName)
    {
        if (string.IsNullOrWhiteSpace(targetName))
        {
            return null;
        }

        Bistable.Core.Design.DesignContAssign? assign = SelectedHierarchyContAssigns
            .FirstOrDefault(a =>
                string.Equals(a.TargetName, targetName, StringComparison.OrdinalIgnoreCase)
                && a.SourceRange.HasValue && a.SourceNames.Count == 1);

        if (assign is null)
        {
            return null;
        }

        string? sourceValue = FindAnySignalByName(assign.SourceNames[0])?.Value;
        if (string.IsNullOrWhiteSpace(sourceValue) || sourceValue == "-")
        {
            return null;
        }

        if (!Views.SchematicPreviewControl.TryParseNumericValue(sourceValue, out System.Numerics.BigInteger numeric))
        {
            return null;
        }

        Bistable.Core.Design.DesignBitRange range = assign.SourceRange!.Value;
        System.Numerics.BigInteger mask = (System.Numerics.BigInteger.One << range.Width) - 1;
        System.Numerics.BigInteger sliceValue = (numeric >> range.Lo) & mask;
        return range.Width == 1
            ? sliceValue.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : $"0x{sliceValue:X}";
    }

    private HierarchyScopeLocalSignalViewModel? SelectedSchematicLocalSignal =>
        string.IsNullOrWhiteSpace(_selectedSchematicReferenceName)
            ? null
            : FindSchematicLocalSignalReference(_selectedSchematicReferenceName);

    private HierarchyScopeLocalSignalViewModel? FindSchematicLocalSignalReference(string referenceName) =>
        SelectedHierarchyLocalSignals.FirstOrDefault(local => IsSchematicLocalSignalReference(local, referenceName))
        ?? EnumerateScopeLocalSignals(SelectedHierarchyChildInstances)
            .FirstOrDefault(local => IsSchematicLocalSignalReference(local, referenceName));

    private static IEnumerable<HierarchyScopeLocalSignalViewModel> EnumerateScopeLocalSignals(IEnumerable<HierarchyScopeInstanceViewModel> instances)
    {
        foreach (HierarchyScopeInstanceViewModel instance in instances)
        {
            foreach (HierarchyScopeLocalSignalViewModel local in instance.LocalSignals)
            {
                yield return local;
            }

            foreach (HierarchyScopeLocalSignalViewModel childLocal in EnumerateScopeLocalSignals(instance.ChildInstances))
            {
                yield return childLocal;
            }
        }
    }

    private static bool IsSchematicLocalSignalReference(HierarchyScopeLocalSignalViewModel local, string referenceName) =>
        string.Equals(local.ResolvedSignalName, referenceName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(local.Name, referenceName, StringComparison.OrdinalIgnoreCase);

    private void RaiseSelectedSchematicSignalProperties()
    {
        OnPropertyChanged(nameof(SelectedSchematicSignalDisplayName));
        OnPropertyChanged(nameof(SelectedSchematicSignalDirection));
        OnPropertyChanged(nameof(SelectedSchematicSignalWidth));
        OnPropertyChanged(nameof(SelectedSchematicSignalValue));
        OnPropertyChanged(nameof(IsSchematicSignalSelected));
        OnPropertyChanged(nameof(CanDriveSelectedSchematicInput));
        OnPropertyChanged(nameof(CanToggleSelectedSchematicInput));
        OnPropertyChanged(nameof(CanForceSelectedSchematicSignal));
        OnPropertyChanged(nameof(IsSelectedSchematicSignalForced));
        OnPropertyChanged(nameof(IsSelectedSchematicSignalMemory));
        OnPropertyChanged(nameof(SelectedMemoryDepthLabel));
        OnPropertyChanged(nameof(MemoryViewerCellCount));
        OnPropertyChanged(nameof(SelectedSchematicSignalName));
        // When the user selects a memory probe, kick a refresh so the cells
        // populate without an extra round-trip via Eval/Tick.
        if (SelectedSchematicLocalSignal?.ResolvedSignalName is { } path
            && IsSelectedSchematicSignalMemory)
        {
            KickMemoryRefresh(path);
        }
        else if (SelectedMemoryCells.Count > 0)
        {
            SelectedMemoryCells.Clear();
        }
    }

    private void KickMemoryRefresh(string path)
    {
        int count = MemoryViewerCellCount;
        if (count <= 0 || !_liveProbes.HasWorker) return;
        _ = RefreshMemoryAsync(path, count);
    }

    private async Task RefreshMemoryAsync(string path, int count)
    {
        MemorySnapshot? snap = await _liveProbes.ReadMemoryAsync(path, startAddress: 0, count, CancellationToken.None);
        if (snap is null) return;
        // Best-effort UI thread dispatch — Avalonia's binding system tolerates
        // ObservableCollection mutations from background threads but explicit
        // dispatch keeps deterministic ordering with PropertyChanged.
        ApplyMemorySnapshot(snap);
    }

    private void ApplyMemorySnapshot(MemorySnapshot snap)
    {
        // Only update if this snapshot is still for the currently-selected probe
        // (the user may have selected something else while the async read was in flight).
        if (SelectedSchematicLocalSignal?.ResolvedSignalName is not { } activePath
            || !string.Equals(activePath, snap.Path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SelectedMemoryCells.Clear();
        for (int i = 0; i < snap.Cells.Count; i++)
        {
            SelectedMemoryCells.Add(new MemoryCellViewModel(snap.StartAddress + (ulong)i, snap.Cells[i]));
        }
    }

    public string WaveformCursorSummary
    {
        get
        {
            if (SelectedWaveformLane is null)
            {
                return $"Cursor t={WaveformCursorTime}";
            }

            string value = SelectedWaveformLane.GetValueAtOrBefore(WaveformCursorOrder);
            return $"{SelectedWaveformLane.Name} @ t={WaveformCursorTime} = {value}";
        }
    }

    private async Task LoadProjectAsync(CancellationToken cancellationToken)
    {
        Window? owner = Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (owner is null)
        {
            Status = "Cannot open file picker before the main window is ready.";
            return;
        }

        string? path = await _workspace.Dialogs.PickProjectFileAsync(owner);
        if (path is null)
        {
            return;
        }

        await LoadProjectFromPathAsync(path, cancellationToken);
    }

    public async Task LoadProjectFromPathAsync(string path, CancellationToken cancellationToken)
    {
        _projectFileWatcher.Stop();
        try
        {
            Status = "Running Verilator XML elaboration...";
            DesignLoadResult result = await _workspace.DesignLoader.LoadAsync(path, cancellationToken);

            Inputs.Clear();
            Outputs.Clear();
            AllSignals.Clear();
            TraceSignals.Clear();
            HierarchyScopeSignals.Clear();
            HierarchyTraceScopeSummaries.Clear();
            SelectedHierarchyChildScopes.Clear();
            SelectedHierarchyChildInstances.Clear();
            SelectedHierarchyPorts.Clear();
            SelectedHierarchyLocalSignals.Clear();
            SelectedHierarchyContAssigns.Clear();
            WaveformLanes.Clear();
            AvailableClocks.Clear();
            UnsubscribeFromInputs();
            _waveformOrder = 0;
            WaveformOffset = 0;
            WaveformCursorOrder = 0;
            _traceFilePath = null;
            _rtlTraceFilePath = null;
            _gateLevelTraceFilePath = null;
            _simulationTarget = SimulationTarget.Rtl;
            RaiseSimulationTargetChanged();
            _traceDocument = VcdTraceDocument.Empty;
            HierarchyRoot = null;
            SelectedHierarchyNode = null;
            SelectedSignal = null;
            SelectedWaveformLane = null;

            foreach (SignalViewModel signal in result.Metadata.Ports.Select(static p => new SignalViewModel(p)))
            {
                AllSignals.Add(signal);
                if (signal.IsInput)
                {
                    Inputs.Add(signal);
                }
                else
                {
                    Outputs.Add(signal);
                }

                if (signal.Direction == SignalDirection.Output
                    || result.Project.Clocks.Any(clock => string.Equals(clock.Name, signal.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    AddWaveformSignal(signal);
                }
            }

            ProjectName = Path.GetFileName(path);
            TopModule = result.Metadata.Name;
            VerilatorVersion = result.VerilatorVersion;
            _currentProjectPath = path;
            _currentProject = result.Project;
            if (!_hasUserLiveReloadDebounceOverride)
            {
                _liveReloadDebounceMs = Math.Clamp(result.Project.LiveReload.DebounceMs, 100, 5000);
                OnPropertyChanged(nameof(LiveReloadDebounceMs));
            }
            // P5-8: notify the "Run CPU" button binding so the IsVisible
            // NotNullConverter can pick up the new project's CpuRuntime.
            OnPropertyChanged(nameof(CpuRuntime));
            // Reset any program override carried over from the previous project.
            CpuProgramOverridePath = null;
            OnPropertyChanged(nameof(CpuProgramDisplayName));
            _currentMetadata = result.Metadata;
            _currentDesign = result.Design;
            _currentAst = result.Ast;
            // P2.9-8: enable View → Schematic Coverage… now that AST is loaded.
            ((RelayCommand)OpenDiagnosticsCommand).RaiseCanExecuteChanged();
            RaiseSynthesisSettingsChanged();
            _currentProjectDirectory = result.ProjectDirectory;
            await RefreshSourceDocumentsAsync(result.Project, result.ProjectDirectory, cancellationToken);
            ElaborationDiagnostics.Clear();
            IsSchematicStale = false;
            LiveReloadStatus = "Live reload idle";
            OnPropertyChanged(nameof(IsLiveReloadActive));
            RebuildPrimitivesByModule();
            SchematicExpandedPaths.Clear();
            OnPropertyChanged(nameof(IsSelectedHierarchyScopeExpanded));

            foreach (string clockName in ResolveAvailableClocks(result.Project, Inputs))
            {
                AvailableClocks.Add(clockName);
            }

            SubscribeToInputs();

            SelectedClockName = AvailableClocks.FirstOrDefault();

            SelectedWaveformLane = WaveformLanes.FirstOrDefault();
            WaveformCursorOrder = _waveformOrder;
            HierarchyRoot = new HierarchyNodeViewModel(result.Design.HierarchyRoot);
            SelectedHierarchyNode = HierarchyRoot;
            await DisposeGateLevelWorkerAsync();
            await DisposeWorkerAsync();
            ConfigureProjectFileWatcher();
            Status = $"Loaded {result.Metadata.Ports.Count} top-level ports.";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException)
        {
            Status = ex.Message;
        }
    }

    private void ConfigureProjectFileWatcher()
    {
        _projectFileWatcher.Stop();
        if (!IsLiveReloadActive
            || _isSubSimActive
            || _currentProject is null
            || _currentProjectPath is null
            || _currentProjectDirectory is null)
        {
            LiveReloadStatus = LiveReloadEnabled ? "Live reload unavailable" : "Live reload disabled";
            return;
        }

        _projectFileWatcher.Start(
            _currentProject,
            _currentProjectPath,
            _currentProjectDirectory,
            _liveReloadDebounceMs);
        LiveReloadStatus = $"Watching HDL files ({_liveReloadDebounceMs} ms debounce)";
    }

    private void OnProjectFilesChanged(object? sender, ProjectFilesChangedEventArgs e) =>
        _projectReloadCoordinator.Queue(e.Paths);

    private async Task ReloadProjectFromChangesAsync(
        IReadOnlyCollection<string> changedPaths,
        CancellationToken cancellationToken)
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await ExecuteSimulationOperationAsync(
                    "Live reload",
                    token => ReloadProjectCoreAsync(changedPaths, token),
                    cancellationToken);
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });
        await completion.Task.WaitAsync(cancellationToken);
    }

    private async Task ReloadProjectCoreAsync(
        IReadOnlyCollection<string> changedPaths,
        CancellationToken cancellationToken)
    {
        if (_currentProjectPath is null || _currentProjectDirectory is null || _currentProject is null)
        {
            return;
        }

        string projectPath = _currentProjectPath;
        string projectDirectory = _currentProjectDirectory;
        DesignAst? previousAst = _currentAst;
        SimulationWorkerClient? workerAtStart = _worker;
        Dictionary<string, string> inputValues = Inputs.ToDictionary(
            static input => input.Name,
            static input => input.Value,
            StringComparer.OrdinalIgnoreCase);
        Stopwatch stopwatch = Stopwatch.StartNew();
        IsLiveReloadBuilding = true;
        LiveReloadStatus = $"Elaborating {changedPaths.Count} changed file(s)…";

        try
        {
            DesignLoadResult result = await _workspace.DesignLoader.LoadAsync(projectPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            AstModuleDiffResult diff = AstModuleDiff.Compare(previousAst, result.Ast!, result.Project.TopModule);
            await RefreshSourceDocumentsAsync(result.Project, result.ProjectDirectory, cancellationToken);

            if (!diff.HasChanges)
            {
                stopwatch.Stop();
                LastLiveReloadElapsedMs = stopwatch.Elapsed.TotalMilliseconds;
                ElaborationDiagnostics.Clear();
                IsSchematicStale = false;
                LiveReloadStatus = $"No semantic HDL change ({stopwatch.Elapsed.TotalMilliseconds:F0} ms)";
                ConfigureProjectFileWatcher();
                return;
            }

            string? selectedHierarchyPath = SelectedHierarchyPath;
            await DisposeGateLevelWorkerAsync();
            ApplyReloadedDesign(result, diff, selectedHierarchyPath, inputValues);
            stopwatch.Stop();
            LastLiveReloadElapsedMs = stopwatch.Elapsed.TotalMilliseconds;
            IsSchematicStale = false;
            ElaborationDiagnostics.Clear();
            LiveReloadStatus =
                $"Schematic refreshed in {stopwatch.Elapsed.TotalMilliseconds:F0} ms; {diff.DirtyModules.Count} module(s) changed";
            ConfigureProjectFileWatcher();

            if (workerAtStart is not null && ReferenceEquals(_worker, workerAtStart))
            {
                try
                {
                    await RebuildAndSwapWorkerAsync(
                        result,
                        workerAtStart,
                        inputValues,
                        diff.TopInterfaceChanged,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is VerilatorInvocationException
                    or IOException
                    or InvalidDataException
                    or InvalidOperationException)
                {
                    ShowWorkerReloadFailure(ex, result.ProjectDirectory);
                    return;
                }
            }
            Status = $"Live reload complete in {stopwatch.Elapsed.TotalMilliseconds:F0} ms.";
        }
        catch (VerilatorInvocationException ex)
        {
            stopwatch.Stop();
            LastLiveReloadElapsedMs = stopwatch.Elapsed.TotalMilliseconds;
            await RefreshChangedSourceDocumentsAsync(changedPaths, projectDirectory, cancellationToken);
            ShowElaborationFailure(
                ElaborationDiagnosticsParser.Parse(ex.StandardError, projectDirectory),
                ex.Message,
                changedPaths);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException)
        {
            stopwatch.Stop();
            LastLiveReloadElapsedMs = stopwatch.Elapsed.TotalMilliseconds;
            ShowElaborationFailure([], ex.Message, changedPaths);
        }
        finally
        {
            IsLiveReloadBuilding = false;
        }
    }

    private void ApplyReloadedDesign(
        DesignLoadResult result,
        AstModuleDiffResult diff,
        string? selectedHierarchyPath,
        IReadOnlyDictionary<string, string> priorInputValues)
    {
        _currentProject = result.Project;
        _currentMetadata = result.Metadata;
        _currentDesign = result.Design;
        _currentAst = result.Ast;
        _currentProjectDirectory = result.ProjectDirectory;
        TopModule = result.Metadata.Name;
        VerilatorVersion = result.VerilatorVersion;
        OnPropertyChanged(nameof(CpuRuntime));
        RaiseSynthesisSettingsChanged();

        if (diff.TopInterfaceChanged)
        {
            RebuildTopPortCollections(result, priorInputValues);
        }

        RebuildPrimitivesByModule();
        HierarchyRoot = new HierarchyNodeViewModel(result.Design.HierarchyRoot);
        SelectedHierarchyNode = HierarchyRoot;
        if (!string.IsNullOrWhiteSpace(selectedHierarchyPath))
        {
            SelectedHierarchyPath = selectedHierarchyPath;
        }
        for (int i = SchematicExpandedPaths.Count - 1; i >= 0; i--)
        {
            if (FindHierarchyNode(HierarchyRoot, SchematicExpandedPaths[i]) is null)
            {
                SchematicExpandedPaths.RemoveAt(i);
            }
        }
        OnPropertyChanged(nameof(IsSelectedHierarchyScopeExpanded));
    }

    private void RebuildTopPortCollections(
        DesignLoadResult result,
        IReadOnlyDictionary<string, string> priorInputValues)
    {
        UnsubscribeFromInputs();
        Inputs.Clear();
        Outputs.Clear();
        AllSignals.Clear();
        TraceSignals.Clear();
        WaveformLanes.Clear();
        AvailableClocks.Clear();
        foreach (SignalPort port in result.Metadata.Ports.OrderBy(static port => port.PinIndex))
        {
            SignalViewModel signal = new(port);
            if (signal.IsInput && priorInputValues.TryGetValue(signal.Name, out string? priorValue))
            {
                signal.Value = priorValue;
            }
            AllSignals.Add(signal);
            if (signal.IsInput) Inputs.Add(signal); else Outputs.Add(signal);
            if (signal.Direction == SignalDirection.Output
                || result.Project.Clocks.Any(clock => string.Equals(clock.Name, signal.Name, StringComparison.OrdinalIgnoreCase)))
            {
                AddWaveformSignal(signal);
            }
        }
        foreach (string clockName in ResolveAvailableClocks(result.Project, Inputs)) AvailableClocks.Add(clockName);
        SelectedClockName = AvailableClocks.FirstOrDefault();
        SubscribeToInputs();
    }

    private async Task RebuildAndSwapWorkerAsync(
        DesignLoadResult result,
        SimulationWorkerClient previousWorker,
        IReadOnlyDictionary<string, string> inputValues,
        bool interfaceChanged,
        CancellationToken cancellationToken)
    {
        IsLiveReloadBuilding = true;
        _hotReloadWorkerSlot = (_hotReloadWorkerSlot + 1) % 2;
        LiveReloadStatus = interfaceChanged
            ? "Port interface changed; building replacement worker…"
            : "Building updated worker while current simulation stays live…";

        PreparedSimulationWorker prepared = await _workerHotSwapService.PrepareAsync(
            result.Project,
            result.Metadata,
            result.Ast,
            result.ProjectDirectory,
            inputValues,
            _hotReloadWorkerSlot,
            cancellationToken);
        SimulationWorkerClient replacement = prepared.Client;
        try
        {
            if (!ReferenceEquals(_worker, previousWorker)) return;

            _liveProbes.AttachWorker(replacement);
            try
            {
                await _liveProbes.RefreshDescriptorsAsync(cancellationToken);
                ApplyFrame(prepared.InitialFrame);
            }
            catch
            {
                _liveProbes.AttachWorker(previousWorker);
                await _liveProbes.RefreshDescriptorsAsync(CancellationToken.None);
                throw;
            }

            _worker = replacement;
            replacement = null!;
            _rtlTraceFilePath = prepared.TraceFilePath;
            _traceFilePath = _rtlTraceFilePath;
            await previousWorker.DisposeAsync();
            LiveReloadStatus = "Live reload ready; updated worker active";
        }
        finally
        {
            if (replacement is not null) await replacement.DisposeAsync();
        }
    }

    private void ShowElaborationFailure(
        IReadOnlyList<ElaborationDiagnostic> parsed,
        string fallbackMessage,
        IReadOnlyCollection<string> changedPaths)
    {
        ElaborationDiagnostics.Clear();
        foreach (ElaborationDiagnostic diagnostic in parsed) ElaborationDiagnostics.Add(diagnostic);
        if (ElaborationDiagnostics.Count == 0)
        {
            string path = changedPaths.FirstOrDefault() ?? _currentProjectPath ?? string.Empty;
            ElaborationDiagnostics.Add(new ElaborationDiagnostic(
                ElaborationDiagnosticSeverity.Error,
                null,
                fallbackMessage.ReplaceLineEndings(" "),
                path,
                1,
                1,
                fallbackMessage));
        }
        IsSchematicStale = true;
        LiveReloadStatus = $"Elaboration failed; showing last good schematic ({LastLiveReloadElapsedMs:F0} ms)";
        Status = ElaborationDiagnostics[0].DisplayText;
    }

    private void ShowWorkerReloadFailure(Exception exception, string projectDirectory)
    {
        ElaborationDiagnostics.Clear();
        if (exception is VerilatorInvocationException invocation)
        {
            foreach (ElaborationDiagnostic diagnostic in ElaborationDiagnosticsParser.Parse(
                invocation.StandardError,
                projectDirectory))
            {
                ElaborationDiagnostics.Add(diagnostic);
            }
        }
        if (ElaborationDiagnostics.Count == 0)
        {
            ElaborationDiagnostics.Add(new ElaborationDiagnostic(
                ElaborationDiagnosticSeverity.Error,
                null,
                exception.Message.ReplaceLineEndings(" "),
                _currentProjectPath ?? string.Empty,
                1,
                1,
                exception.Message));
        }

        IsSchematicStale = false;
        LiveReloadStatus = "Schematic current; worker rebuild failed, previous simulation retained";
        Status = ElaborationDiagnostics[0].DisplayText;
    }

    private async Task RefreshSourceDocumentsAsync(
        ProjectConfiguration project,
        string projectDirectory,
        CancellationToken cancellationToken)
    {
        List<string> paths = ResolveProjectSourcePaths(project, projectDirectory).ToList();
        SourceDocumentSnapshot[] snapshots = await ReadSourceSnapshotsAsync(paths, projectDirectory, cancellationToken);
        ApplySourceSnapshots(snapshots, removeMissing: true);
    }

    private async Task RefreshChangedSourceDocumentsAsync(
        IEnumerable<string> changedPaths,
        string projectDirectory,
        CancellationToken cancellationToken)
    {
        SourceDocumentSnapshot[] snapshots = await ReadSourceSnapshotsAsync(
            changedPaths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase),
            projectDirectory,
            cancellationToken);
        ApplySourceSnapshots(snapshots, removeMissing: false);
    }

    private void ApplySourceSnapshots(IReadOnlyList<SourceDocumentSnapshot> snapshots, bool removeMissing)
    {
        string? selectedPath = SelectedSourceDocument?.FilePath;
        Dictionary<string, SourceDocumentViewModel> existing = SourceDocuments.ToDictionary(
            static document => document.FilePath,
            StringComparer.OrdinalIgnoreCase);
        foreach (SourceDocumentSnapshot snapshot in snapshots)
        {
            if (existing.Remove(snapshot.FilePath, out SourceDocumentViewModel? document))
            {
                if (!document.IsDirty) document.ReplaceFromDisk(snapshot.Text);
            }
            else
            {
                SourceDocuments.Add(new SourceDocumentViewModel(snapshot.FilePath, snapshot.RelativePath, snapshot.Text));
            }
        }
        if (removeMissing)
        {
            foreach (SourceDocumentViewModel obsolete in existing.Values.Where(static document => !document.IsDirty).ToArray())
            {
                SourceDocuments.Remove(obsolete);
            }
        }
        SortSourceDocuments();
        SelectedSourceDocument = SourceDocuments.FirstOrDefault(document =>
            string.Equals(document.FilePath, selectedPath, StringComparison.OrdinalIgnoreCase))
            ?? SourceDocuments.FirstOrDefault();
    }

    private void SortSourceDocuments()
    {
        SourceDocumentViewModel[] sorted = SourceDocuments
            .OrderBy(static document => document.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        for (int target = 0; target < sorted.Length; target++)
        {
            int current = SourceDocuments.IndexOf(sorted[target]);
            if (current != target) SourceDocuments.Move(current, target);
        }
    }

    private IEnumerable<string> ResolveProjectSourcePaths(ProjectConfiguration project, string projectDirectory)
    {
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        if (_currentProjectPath is not null) paths.Add(Path.GetFullPath(_currentProjectPath));
        foreach (string source in project.Sources)
        {
            paths.Add(Path.IsPathRooted(source) ? Path.GetFullPath(source) : Path.GetFullPath(source, projectDirectory));
        }
        foreach (string includeDir in project.IncludeDirs)
        {
            string root = Path.IsPathRooted(includeDir) ? Path.GetFullPath(includeDir) : Path.GetFullPath(includeDir, projectDirectory);
            if (!Directory.Exists(root)) continue;
            foreach (string pattern in new[] { "*.sv", "*.svh", "*.v", "*.vh" })
            {
                foreach (string file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)) paths.Add(file);
            }
        }
        return paths.Where(File.Exists).Order(StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<SourceDocumentSnapshot[]> ReadSourceSnapshotsAsync(
        IEnumerable<string> paths,
        string projectDirectory,
        CancellationToken cancellationToken)
    {
        List<SourceDocumentSnapshot> snapshots = [];
        foreach (string path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fullPath = Path.GetFullPath(path);
            snapshots.Add(new SourceDocumentSnapshot(
                fullPath,
                Path.GetRelativePath(projectDirectory, fullPath),
                await File.ReadAllTextAsync(fullPath, cancellationToken)));
        }
        return snapshots.ToArray();
    }

    private async Task SaveSelectedSourceAsync(CancellationToken cancellationToken)
    {
        SourceDocumentViewModel? document = SelectedSourceDocument;
        if (document is null || !document.IsDirty) return;
        await File.WriteAllTextAsync(document.FilePath, document.Text, cancellationToken);
        document.MarkSaved();
        ((AsyncCommand)SaveSourceCommand).RaiseCanExecuteChanged();
        Status = $"Saved {document.RelativePath}.";
    }

    private void NavigateToSource(string filePath, int line, int column)
    {
        SourceDocumentViewModel? document = SourceDocuments.FirstOrDefault(candidate =>
            string.Equals(candidate.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        if (document is null) return;
        SelectedSourceDocument = document;
        SourceNavigationLine = line;
        SourceNavigationColumn = column;
        SourceNavigationVersion++;
    }

    public void StopLiveReload()
    {
        _projectFileWatcher.Stop();
        _projectReloadCoordinator.Dispose();
    }

    internal void QueueLiveReloadForTest(IEnumerable<string> changedPaths) =>
        _projectReloadCoordinator.Queue(changedPaths);

    internal Task WhenLiveReloadIdleAsync() => _projectReloadCoordinator.WhenIdleAsync();

    private sealed record SourceDocumentSnapshot(string FilePath, string RelativePath, string Text);

    private void LoadSamples()
    {
        string samplesRoot = Path.Combine(FindRepositoryRoot(), "samples");
        if (!Directory.Exists(samplesRoot))
        {
            return;
        }

        foreach (string path in Directory.EnumerateFiles(samplesRoot, "*.bistable.json", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path));
            Samples.Add(new SampleProjectViewModel(
                name,
                path,
                CreateSimulationCommand(
                    $"Load sample {name}",
                    cancellationToken => LoadProjectFromPathAsync(path, cancellationToken))));
        }

    }

    private async Task BuildAsync(CancellationToken cancellationToken)
    {
        if (_currentProjectPath is null || _currentProject is null || _currentMetadata is null || _currentProjectDirectory is null)
        {
            Status = "Open a project first. Use the Samples list or Open Project.";
            return;
        }

        await LoadProjectFromPathAsync(_currentProjectPath, cancellationToken);

        await DisposeWorkerAsync();
        Status = "Building native Verilator worker...";
        Progress<SimulationWorkerBuildProgress> progress = new(report =>
        {
            if (!string.IsNullOrWhiteSpace(report.Message))
            {
                Status = $"Build {report.Stage}: {TrimBuildStatus(report.Message)}";
            }
        });
        SimulationWorkerBuildResult build = await _workspace.WorkerBuilder.BuildAsync(
            _currentProject!,
            _currentMetadata!,
            _currentProjectDirectory!,
            cancellationToken,
            progress,
            _currentAst);

        _worker = await SimulationWorkerClient.StartAsync(build.ExecutablePath, cancellationToken);
        _liveProbes.AttachWorker(_worker); _ = _liveProbes.RefreshDescriptorsAsync(CancellationToken.None);
        _rtlTraceFilePath = build.TraceFilePath;
        _traceFilePath = _rtlTraceFilePath;
        _simulationTarget = SimulationTarget.Rtl;
        RaiseSimulationTargetChanged();
        await PushInputsAsync(cancellationToken);
        SimulationFrame frame = await _worker.StepAsync(new SimulationCommand(SimulationCommandType.Eval), cancellationToken);
        ApplyFrame(frame);
        await RefreshTraceStateAsync(cancellationToken);
        Status = $"Worker ready: {Path.GetFileName(build.ExecutablePath)}";
    }

    private static string TrimBuildStatus(string message)
    {
        string compact = message.ReplaceLineEndings(" ").Trim();
        return compact.Length <= 140 ? compact : compact[..137] + "...";
    }

    private async Task EnterSubSimulationAsync(CancellationToken cancellationToken)
    {
        if (!CanEnterSubSim) return;

        if (_simulationTarget == SimulationTarget.GateLevel)
        {
            SelectedSimulationTarget = SimulationTarget.Rtl;
        }

        string moduleName = SelectedHierarchyNode!.ModuleName;
        if (!_currentDesign!.ModuleCatalog.TryGetValue(moduleName, out ModuleMetadata? subMeta))
        {
            Status = $"Module metadata not found for '{moduleName}'.";
            return;
        }

        SubSimulationConfiguration subSimulation =
            SubSimulationConfigurationResolver.Resolve(_currentProject!, subMeta);
        ProjectConfiguration subConfig = subSimulation.Project;
        DesignAst? subProbeAst = BuildSubSimulationProbeAst(_currentAst, moduleName, subSimulation.BuildTopModule);
        string buildLabel = string.Equals(moduleName, subSimulation.BuildTopModule, StringComparison.Ordinal)
            ? moduleName
            : $"{moduleName} as {subSimulation.BuildTopModule}";
        Status = $"Building isolated simulation for {buildLabel}…";

        SimulationWorkerBuildResult subBuild;
        try
        {
            Progress<SimulationWorkerBuildProgress> progress = new(report =>
            {
                if (!string.IsNullOrWhiteSpace(report.Message))
                    Status = $"Build {report.Stage}: {TrimBuildStatus(report.Message)}";
            });
            subBuild = await _workspace.WorkerBuilder.BuildAsync(
                subConfig, subMeta, _currentProjectDirectory!, cancellationToken, progress, subProbeAst);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Status = $"Sub-simulation build failed: {ex.Message}";
            return;
        }

        // Re-elaborate the sub-module so the hierarchy panel, schematic, and trace lookups
        // operate against the sub-design — without this, those panels keep showing top-level
        // scopes whose signals are no longer driven by the active worker.
        DesignLoadResult subElab;
        try
        {
            subElab = await _workspace.DesignLoader.ElaborateAsync(
                subConfig, _currentProjectDirectory!, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Status = $"Sub-simulation elaboration failed: {ex.Message}";
            return;
        }

        // Save top-level context before swapping
        _topLevelWorker     = _worker;
        _savedTopInputs     = [.. Inputs];
        _savedTopOutputs    = [.. Outputs];
        _savedTopAllSignals = [.. AllSignals];
        _savedTopTraceSignals = [.. TraceSignals];
        _savedTopModule     = TopModule;
        _savedTopTraceFilePath = _traceFilePath;
        _savedTopDesign = _currentDesign;
        _savedTopAst = _currentAst;
        _savedTopHierarchyRoot = HierarchyRoot;
        _savedTopSelectedHierarchyNode = SelectedHierarchyNode;
        _savedTopExpandedPaths = [.. SchematicExpandedPaths];
        _subSimProject = subConfig;

        _worker = await SimulationWorkerClient.StartAsync(subBuild.ExecutablePath, cancellationToken);
        _liveProbes.AttachWorker(_worker); _ = _liveProbes.RefreshDescriptorsAsync(CancellationToken.None);
        _traceFilePath = subBuild.TraceFilePath;
        _currentDesign = subElab.Design;
        _currentAst = subElab.Ast;
        RebuildPrimitivesByModule();

        // Clear the stale schematic selection: the previous path (e.g.
        // "arnicomp_top.acc.d") doesn't exist in the sub-worker's namespace
        // (where it would be "reg_cell.d"). Without this the Live Probe panel
        // would show ghost values from a path the new probe table can't find.
        _selectedSchematicReferenceName = null;
        ForcedPaths.Clear();
        // CRITICAL: detach the live-mode PropertyChanged hook from the OLD
        // (top-level) inputs before clearing the collection. Otherwise the new
        // sub-sim inputs are never observed → ScheduleLiveEvaluation never
        // fires when the user toggles clk → only manual Tick works. This is
        // the source of the "isolated sub-sim posedge doesn't react" bug.
        UnsubscribeFromInputs();
        Inputs.Clear();
        Outputs.Clear();
        AllSignals.Clear();
        TraceSignals.Clear();
        SchematicExpandedPaths.Clear();
        foreach (SignalPort port in subMeta.Ports.OrderBy(static p => p.PinIndex))
        {
            SignalViewModel signal = new(port);
            AllSignals.Add(signal);
            if (signal.IsInput) Inputs.Add(signal);
            else Outputs.Add(signal);
        }
        // Re-subscribe so toggling a sub-sim input triggers the live eval that
        // pushes ALL inputs to the worker (giving the FF its rising-edge transition).
        SubscribeToInputs();

        // Swap the hierarchy view to the sub-design's tree so subscope navigation, signal
        // probing and schematic scope-selection target the sub-module's namespace.
        HierarchyRoot = new HierarchyNodeViewModel(subElab.Design.HierarchyRoot);
        SelectedHierarchyNode = HierarchyRoot;

        SelectedSignal = null;
        TopModule = subElab.Metadata.Name;
        IsSubSimActive = true;

        SimulationFrame frame = await _worker.StepAsync(
            new SimulationCommand(SimulationCommandType.Eval), cancellationToken);
        ApplyFrame(frame);
        await RefreshTraceStateAsync(cancellationToken);

        Status = $"Isolated simulation active: {buildLabel}. Drive inputs, then Eval / Tick.";
    }

    private static DesignAst? BuildSubSimulationProbeAst(
        DesignAst? designAst,
        string selectedModuleName,
        string buildTopModule)
    {
        if (designAst is null) return null;

        ModuleAst? selected = designAst.Modules.FirstOrDefault(m =>
            string.Equals(m.Name, selectedModuleName, StringComparison.OrdinalIgnoreCase));
        if (selected is null) return designAst;

        List<ModuleAst> modules =
        [
            selected with
            {
                Name = buildTopModule,
                IsTop = true,
                OriginalName = null
            }
        ];

        modules.AddRange(designAst.Modules
            .Where(m => !ReferenceEquals(m, selected))
            .Select(static m => m with { IsTop = false }));

        return new DesignAst(modules);
    }

    private void ExitSubSimulation()
    {
        if (!_isSubSimActive) return;

        _ = _worker?.DisposeAsync().AsTask();
        _worker = _topLevelWorker;
        _liveProbes.AttachWorker(_worker); _ = _liveProbes.RefreshDescriptorsAsync(CancellationToken.None);
        _topLevelWorker = null;
        _traceFilePath = _savedTopTraceFilePath;
        _subSimProject = null;
        _currentDesign = _savedTopDesign;
        _currentAst = _savedTopAst;
        RebuildPrimitivesByModule();

        RestoreTopSimulationCollections();
        HierarchyRoot = _savedTopHierarchyRoot;
        SelectedHierarchyNode = _savedTopSelectedHierarchyNode ?? HierarchyRoot;

        _savedTopInputs = _savedTopOutputs = _savedTopAllSignals = _savedTopTraceSignals = null;
        _savedTopDesign = null;
        _savedTopAst = null;
        _savedTopHierarchyRoot = null;
        _savedTopSelectedHierarchyNode = null;
        _savedTopExpandedPaths = null;
        TopModule = _savedTopModule ?? "-";
        _savedTopModule = null;

        SelectedSignal = null;
        // Same rationale as enter — sub-sim's "reg_cell.X" selection no longer
        // exists in the restored top-level namespace.
        _selectedSchematicReferenceName = null;
        ForcedPaths.Clear();
        IsSubSimActive = false;
        Status = "Returned to top-level simulation.";
    }

    private void RestoreTopSimulationCollections()
    {
        // Detach live-mode hooks from the sub-sim inputs before swapping the
        // collection contents — same reasoning as the enter-sub-sim branch.
        // Without unsubscribe + resubscribe, the restored top-level inputs are
        // not observed and toggling them only "works" through manual Tick.
        UnsubscribeFromInputs();
        Inputs.Clear();
        Outputs.Clear();
        AllSignals.Clear();
        TraceSignals.Clear();
        SchematicExpandedPaths.Clear();
        RestoreSaved(AllSignals, _savedTopAllSignals);
        RestoreSaved(Inputs, _savedTopInputs);
        RestoreSaved(Outputs, _savedTopOutputs);
        RestoreSaved(TraceSignals, _savedTopTraceSignals);
        RestoreSaved(SchematicExpandedPaths, _savedTopExpandedPaths);
        SubscribeToInputs();
    }

    private static void RestoreSaved<T>(System.Collections.ObjectModel.ObservableCollection<T> target, IReadOnlyList<T>? saved)
    {
        if (saved is null) return;
        foreach (T item in saved) target.Add(item);
    }

    private async Task EvaluateAsync(CancellationToken cancellationToken)
    {
        if (TopModule == "-")
        {
            Status = "Open a project first. Use the Samples list or Open Project.";
            return;
        }

        SimulationWorkerClient? worker = ActiveSimulationWorker;
        if (worker is not null)
        {
            await PushInputsAsync(cancellationToken);
            SimulationFrame frame = await worker.StepAsync(new SimulationCommand(SimulationCommandType.Eval), cancellationToken);
            ApplyFrame(frame);
            await RefreshTraceStateAsync(cancellationToken);
            Status = $"{ActiveSimulationTargetLabel} eval completed.";
            return;
        }

        PreviewSimulationResult result = _workspace.PreviewSimulation.Evaluate(TopModule, Inputs, Outputs);
        CaptureCurrentOutputValues(Time);
        Status = result.Message;
    }

    private async Task CompareRtlAndGateAsync(CancellationToken cancellationToken)
    {
        if (!CanCompareRtlAndGate || _worker is null || _gateLevelWorker is null)
        {
            Status = "Build the RTL worker and run Synthesize before comparing.";
            return;
        }

        if (!TryGetRunCycles(out long requestedCycles) || requestedCycles > int.MaxValue)
        {
            Status = "Compare cycles must be a positive 32-bit integer.";
            return;
        }

        string? clock = ResolveActiveClockName();
        if (string.IsNullOrWhiteSpace(clock))
        {
            Status = "Select a clock before comparing RTL and gate-level simulation.";
            return;
        }

        List<SimulationCommand> setup = Inputs
            .Select(input => new SimulationCommand(
                SimulationCommandType.SetInput,
                Signal: input.Name,
                Value: input.Value))
            .ToList();

        Status = $"Comparing RTL and Gate for {requestedCycles} cycles...";
        try
        {
            CompareReport report = await _rtlVsGateComparator.CompareProgramAsync(
                _worker,
                _gateLevelWorker,
                new CompareProgram
                {
                    Clock = clock,
                    Cycles = checked((int)requestedCycles),
                    Setup = setup,
                    SignalsToCompare = Outputs.Select(static output => output.Name).ToArray(),
                },
                cancellationToken);

            Status = report.AllMatch
                ? $"RTL/Gate comparison passed for {requestedCycles} cycles."
                : $"RTL/Gate mismatch: {report.FormatSummary(maxLines: 3)}";

            SimulationWorkerClient? activeWorker = ActiveSimulationWorker;
            if (activeWorker is not null)
            {
                SimulationFrame frame = await activeWorker.StepAsync(
                    new SimulationCommand(SimulationCommandType.Eval),
                    cancellationToken);
                ApplyFrame(frame);
                await RefreshTraceStateAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            Status = "RTL/Gate comparison cancelled.";
        }
        catch (Exception ex)
        {
            Status = $"RTL/Gate comparison failed: {ex.Message}";
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        SimulationWorkerClient? worker = ActiveSimulationWorker;
        if (worker is null)
        {
            Time++;
            WaveformCursorOrder = _waveformOrder;
            Status = $"Manual UI tick at t={Time}. Build worker for native ticking.";
            return;
        }

        await PushInputsAsync(cancellationToken);
        string? clock = ResolveActiveClockName();
        SimulationFrame frame = await worker.StepAsync(new SimulationCommand(SimulationCommandType.Tick, Signal: clock), cancellationToken);
        ApplyFrame(frame);
        await RefreshTraceStateAsync(cancellationToken);
        SetInputValueSilently(clock, "0");
        Status = $"{ActiveSimulationTargetLabel} tick pulsed {clock ?? "clock"} 0->1->0 at t={Time}.";
    }

    private async Task RunCyclesAsync(CancellationToken cancellationToken)
    {
        SimulationWorkerClient? worker = ActiveSimulationWorker;
        if (worker is null)
        {
            Time += 10;
            Status = $"Advanced 10 UI cycles to t={Time}. Build worker for native run.";
            return;
        }

        await PushInputsAsync(cancellationToken);
        string? clock = ResolveActiveClockName();
        if (!TryGetRunCycles(out long cycles))
        {
            Status = "Run cycles must be a positive integer.";
            return;
        }

        SimulationFrame? frame = null;
        long remaining = cycles;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long chunk = Math.Min(remaining, SimulationRunChunkSize);
            frame = await worker.StepAsync(
                new SimulationCommand(
                    SimulationCommandType.RunCycles,
                    Signal: clock,
                    Cycles: chunk),
                cancellationToken);
            remaining -= chunk;
        }

        if (frame is null)
        {
            return;
        }

        ApplyFrame(frame);
        await RefreshTraceStateAsync(cancellationToken);
        SetInputValueSilently(clock, "0");
        Status = $"{ActiveSimulationTargetLabel} run pulsed {clock ?? "clock"} for {cycles} cycles; t={Time}.";
    }

    private async Task ResetAsync(CancellationToken cancellationToken)
    {
        Time = 0;
        SimulationWorkerClient? worker = ActiveSimulationWorker;
        if (worker is null)
        {
            ClearWaveformSamples();
            Status = "Session reset.";
            return;
        }

        SimulationFrame frame = await worker.StepAsync(new SimulationCommand(SimulationCommandType.Reset), cancellationToken);
        ClearWaveformSamples();
        ApplyFrame(frame);
        await RefreshTraceStateAsync(cancellationToken);
        string? reset = ActiveProject?.Resets.FirstOrDefault()?.Name;
        if (reset is not null)
        {
            int activeLevel = ActiveProject?.Resets.FirstOrDefault()?.ActiveLevel ?? 0;
            SetInputValueSilently(reset, activeLevel == 0 ? "1" : "0");
        }

        Status = $"{ActiveSimulationTargetLabel} worker reset.";
    }

    private void ToggleInputSignal(string signalName)
    {
        SignalViewModel? input = Inputs.FirstOrDefault(input => string.Equals(input.Name, signalName, StringComparison.OrdinalIgnoreCase));
        if (input is null)
        {
            Status = $"Signal '{signalName}' is not a top-level input.";
            return;
        }

        SelectedSignal = input;
        if (!input.IsBoolean)
        {
            Status = $"Selected {input.Name}. Use the drive editor for {input.WidthLabel} inputs.";
            return;
        }

        input.BooleanValue = !input.BooleanValue;
        SchematicDriveValue = input.Value;
        Status = $"Toggled {input.Name} to {input.Value}.";
    }

    /// <summary>
    /// Apply the value in the Drive textbox to the currently-selected schematic
    /// signal. Top-level inputs go through the legacy `SetInput` pipeline (the
    /// value lands on the SignalViewModel and the next Eval/Tick pushes it).
    /// Internal signals go through the Phase 3 `WriteSignalAsync` probe write —
    /// one-shot, may be overwritten by the next eval if a driver is computing
    /// the same signal. Use Force for sticky writes.
    /// </summary>
    private async Task DriveSelectedSchematicSignalAsync(CancellationToken cancellationToken)
    {
        if (SelectedSignal is { IsInput: true } input)
        {
            if (!TryParseValueForWidth(SchematicDriveValue, input.Width, out _, out string? err))
            {
                Status = $"{input.Name}: {err}";
                return;
            }
            input.Value = SchematicDriveValue.Trim();
            Status = $"Drove {input.Name} to {input.Value}.";
            return;
        }

        HierarchyScopeLocalSignalViewModel? local = SelectedSchematicLocalSignal;
        SimulationWorkerClient? worker = ActiveSimulationWorker;
        if (local?.ResolvedSignalName is { } path && worker is not null)
        {
            if (!TryParseValueForWidth(SchematicDriveValue, local.Width, out _, out string? err))
            {
                Status = $"{path}: {err}";
                return;
            }
            string trimmedValue = SchematicDriveValue.Trim();
            try
            {
                await worker.WriteSignalAsync(path, trimmedValue, cancellationToken);
                _liveProbes.InvalidateAll();
                await _liveProbes.ReadAsync(path, cancellationToken);
                _ = MarkLastSchematicWriteFreshAsync();
                Status = $"Wrote {trimmedValue} to {path} (one-shot; next eval may overwrite).";
            }
            catch (InvalidOperationException ex)
            {
                Status = $"Write failed for {path}: {ex.Message}";
            }
            return;
        }

        Status = "Select a top-level input or internal probe signal first.";
    }

    /// <summary>
    /// Phase 3 force: pin the selected internal signal at the Drive textbox's
    /// value. The worker re-applies it before every eval (including inside
    /// `drive_clock`) so the value survives the FF latch on rising clock edges.
    /// </summary>
    private async Task ForceSelectedSchematicSignalAsync(CancellationToken cancellationToken)
    {
        HierarchyScopeLocalSignalViewModel? local = SelectedSchematicLocalSignal;
        SimulationWorkerClient? worker = ActiveSimulationWorker;
        if (local?.ResolvedSignalName is not { } path || worker is null)
        {
            Status = "Select an internal probe signal first (force is not available for top-level inputs).";
            return;
        }
        if (!TryParseValueForWidth(SchematicDriveValue, local.Width, out _, out string? err))
        {
            Status = $"{path}: {err}";
            return;
        }
        string trimmedValue = SchematicDriveValue.Trim();
        try
        {
            await worker.ForceSignalAsync(path, trimmedValue, cancellationToken);
            AddForcedPath(path);
            OnPropertyChanged(nameof(IsSelectedSchematicSignalForced));
            _liveProbes.InvalidateAll();
            await _liveProbes.ReadAsync(path, cancellationToken);
            Status = $"Forced {path} = {trimmedValue} (sticky across ticks until released).";
        }
        catch (InvalidOperationException ex)
        {
            Status = $"Force failed for {path}: {ex.Message}";
        }
    }

    /// <summary>Phase 3 release: drop a previously-forced signal back to simulation-driven behaviour.</summary>
    private async Task ReleaseSelectedSchematicSignalAsync(CancellationToken cancellationToken)
    {
        SimulationWorkerClient? worker = ActiveSimulationWorker;
        if (SelectedSchematicLocalSignal?.ResolvedSignalName is not { } path || worker is null)
        {
            return;
        }
        try
        {
            await worker.ReleaseSignalAsync(path, cancellationToken);
            RemoveForcedPath(path);
            OnPropertyChanged(nameof(IsSelectedSchematicSignalForced));
            _liveProbes.InvalidateAll();
            await _liveProbes.ReadAsync(path, cancellationToken);
            Status = $"Released {path}; will follow simulation again.";
        }
        catch (InvalidOperationException ex)
        {
            Status = $"Release failed for {path}: {ex.Message}";
        }
    }

    /// <summary>Release a specific path (called from the Forced Signals list's per-row button).</summary>
    private async Task ReleasePathAsync(string path, CancellationToken cancellationToken)
    {
        SimulationWorkerClient? worker = ActiveSimulationWorker;
        if (string.IsNullOrWhiteSpace(path) || worker is null) return;
        try
        {
            await worker.ReleaseSignalAsync(path, cancellationToken);
            RemoveForcedPath(path);
            OnPropertyChanged(nameof(IsSelectedSchematicSignalForced));
            _liveProbes.InvalidateAll();
            await _liveProbes.ReadAsync(path, cancellationToken);
            Status = $"Released {path}.";
        }
        catch (InvalidOperationException ex)
        {
            Status = $"Release failed for {path}: {ex.Message}";
        }
    }

    /// <summary>Release every currently-forced signal in one batch.</summary>
    private async Task ReleaseAllForcedAsync(CancellationToken cancellationToken)
    {
        SimulationWorkerClient? worker = ActiveSimulationWorker;
        if (worker is null || ForcedPaths.Count == 0) return;
        string[] paths = ForcedPaths.ToArray();
        int released = 0;
        foreach (string path in paths)
        {
            try
            {
                await worker.ReleaseSignalAsync(path, cancellationToken);
                RemoveForcedPath(path);
                released++;
            }
            catch (InvalidOperationException) { /* skip and keep going */ }
        }
        OnPropertyChanged(nameof(IsSelectedSchematicSignalForced));
        _liveProbes.InvalidateAll();
        Status = $"Released {released}/{paths.Length} forced signals.";
    }

    private async Task PushInputsAsync(CancellationToken cancellationToken)
    {
        SimulationWorkerClient? worker = ActiveSimulationWorker;
        if (worker is null)
        {
            return;
        }

        foreach (SignalViewModel input in Inputs)
        {
            _ = await worker.StepAsync(
                new SimulationCommand(SimulationCommandType.SetInput, input.Name, input.Value),
                cancellationToken);
        }
    }

    private void ApplyFrame(SimulationFrame frame)
    {
        // P4-1 polish: refresh probe values IN PLACE rather than clearing then
        // re-reading. Clearing-then-refreshing makes the schematic flicker —
        // labels vanish for the ~10-50ms it takes the worker round-trip to
        // re-populate. Old cache values stay visible until ValuesUpdated fires
        // with a fresh value (which only fires when the value actually changed).
        HierarchyScopeLocalSignalViewModel? activeProbe = SelectedSchematicLocalSignal;
        string? selectedScalarPath = null;
        if (activeProbe?.ResolvedSignalName is { } activePath && _liveProbes.HasWorker)
        {
            if (IsSelectedSchematicSignalMemory)
            {
                _liveProbes.InvalidateAll();   // memory snapshot equality needs a clean cache
                KickMemoryRefresh(activePath);
            }
            else
            {
                // Fold the selected probe into the same frame batch. Issuing a
                // separate ReadSignal here would turn a visible frame into two
                // round-trips whenever the probe panel has a scalar selected.
                selectedScalarPath = activePath;
            }
        }
        if (_liveProbes.HasWorker)
        {
            // P4-5: prefer the visible-set if the SchematicPreviewControl has
            // populated it. Falls back to refreshing every scalar (legacy
            // behaviour) when the schematic hasn't rendered yet or when the
            // tracker is unset (e.g. tests without a live UI).
            IReadOnlyCollection<string>? visible = VisibleProbePathsProvider?.Invoke();
            if (visible is { Count: > 0 })
            {
                if (selectedScalarPath is null || visible.Contains(selectedScalarPath, StringComparer.OrdinalIgnoreCase))
                {
                    _ = _liveProbes.RefreshScalarsAsync(visible, CancellationToken.None);
                }
                else
                {
                    HashSet<string> framePaths = new(visible, StringComparer.OrdinalIgnoreCase)
                    {
                        selectedScalarPath
                    };
                    _ = _liveProbes.RefreshScalarsAsync(framePaths, CancellationToken.None);
                }
            }
            else
            {
                _ = _liveProbes.RefreshAllScalarsAsync(CancellationToken.None);
            }
        }

        Time = frame.Time;
        bool useTraceDocument = !string.IsNullOrWhiteSpace(_traceFilePath);
        if (!useTraceDocument && frame.Trace is not null)
        {
            foreach (SignalSample sample in frame.Trace)
            {
                AppendWaveformSample(sample.Signal, sample.Value, sample.Time);
            }
        }

        Dictionary<string, SignalViewModel> outputs = Outputs.ToDictionary(static output => output.Name, StringComparer.OrdinalIgnoreCase);
        foreach (SignalSample sample in frame.Signals)
        {
            if (outputs.TryGetValue(sample.Signal, out SignalViewModel? output))
            {
                string formattedValue = FormatOutputValue(sample.Value, output.Width);
                output.Value = formattedValue;
                if (!useTraceDocument)
                {
                    AppendWaveformSample(sample.Signal, formattedValue, frame.Time);
                }
            }
        }

        if (!useTraceDocument)
        {
            WaveformCursorOrder = _waveformOrder;
        }
    }

    private void AppendWaveformSample(string? signal, string value, ulong time, bool force = false)
    {
        if (string.IsNullOrWhiteSpace(signal))
        {
            return;
        }

        WaveformLaneViewModel? lane = WaveformLanes.FirstOrDefault(lane => string.Equals(lane.Name, signal, StringComparison.OrdinalIgnoreCase));
        if (lane is null)
        {
            return;
        }

        string normalizedValue = NormalizeWaveformValue(lane.Signal, value);
        if (lane.AppendSample(++_waveformOrder, time, normalizedValue, force))
        {
            OnPropertyChanged(nameof(WaveformCursorSummary));
            OnPropertyChanged(nameof(WaveformCursorTime));
        }
    }

    private void AddSelectedWaveformSignal()
    {
        if (SelectedSignal is null)
        {
            Status = "Select a signal first.";
            return;
        }

        AddWaveformSignal(SelectedSignal, selectLane: true);
        Status = $"Added {SelectedSignal.Name} to waveform.";
    }

    private void AddHierarchyScopeSignalsToWaveform()
    {
        if (HierarchyScopeSignals.Count == 0)
        {
            Status = "No traced signals are available for the selected scope.";
            return;
        }

        int before = WaveformLanes.Count;
        foreach (SignalViewModel signal in HierarchyScopeSignals)
        {
            AddWaveformSignal(signal);
        }

        int added = WaveformLanes.Count - before;
        Status = added == 0
            ? $"All signals from {SelectedHierarchyNode?.InstanceName ?? "scope"} are already in the waveform."
            : $"Added {added} scope signals from {SelectedHierarchyNode?.InstanceName ?? "scope"} to waveform.";
    }

    private void RemoveSelectedWaveformSignal()
    {
        WaveformLaneViewModel? lane = SelectedWaveformLane
            ?? (SelectedSignal is null
                ? null
                : WaveformLanes.FirstOrDefault(candidate => string.Equals(candidate.Name, SelectedSignal.Name, StringComparison.OrdinalIgnoreCase)));
        if (lane is null)
        {
            Status = "Select a signal first.";
            return;
        }

        lane.Signal.IsInWaveform = false;
        WaveformLanes.Remove(lane);
        if (ReferenceEquals(SelectedWaveformLane, lane))
        {
            SelectedWaveformLane = WaveformLanes.FirstOrDefault();
        }

        Status = $"Removed {lane.Name} from waveform.";
    }

    private void ClearWaveform()
    {
        foreach (WaveformLaneViewModel lane in WaveformLanes)
        {
            lane.Signal.IsInWaveform = false;
        }

        WaveformLanes.Clear();
        _waveformOrder = 0;
        WaveformCursorOrder = 0;
        SelectedWaveformLane = null;
        Status = "Waveform cleared.";
    }

    private void MoveSelectedWaveformSignalUp()
    {
        if (SelectedSignal is null)
        {
            Status = "Select a signal first.";
            return;
        }

        WaveformLaneViewModel? lane = SelectedWaveformLane
            ?? (SelectedSignal is null
                ? null
                : WaveformLanes.FirstOrDefault(candidate => string.Equals(candidate.Name, SelectedSignal.Name, StringComparison.OrdinalIgnoreCase)));
        if (lane is null)
        {
            Status = "Select a waveform lane first.";
            return;
        }

        int index = WaveformLanes.IndexOf(lane);
        if (index > 0)
        {
            WaveformLanes.Move(index, index - 1);
        }
    }

    private void MoveSelectedWaveformSignalDown()
    {
        if (SelectedSignal is null)
        {
            Status = "Select a signal first.";
            return;
        }

        WaveformLaneViewModel? lane = SelectedWaveformLane
            ?? (SelectedSignal is null
                ? null
                : WaveformLanes.FirstOrDefault(candidate => string.Equals(candidate.Name, SelectedSignal.Name, StringComparison.OrdinalIgnoreCase)));
        if (lane is null)
        {
            Status = "Select a waveform lane first.";
            return;
        }

        int index = WaveformLanes.IndexOf(lane);
        if (index >= 0 && index < WaveformLanes.Count - 1)
        {
            WaveformLanes.Move(index, index + 1);
        }
    }

    private void AddWaveformSignal(SignalViewModel signal, bool selectLane = false)
    {
        signal.IsInWaveform = true;
        WaveformLaneViewModel? lane = WaveformLanes.FirstOrDefault(candidate => string.Equals(candidate.Name, signal.Name, StringComparison.OrdinalIgnoreCase));
        if (lane is null)
        {
            lane = new WaveformLaneViewModel(signal);
            WaveformLanes.Add(lane);
        }

        if (!TryPopulateLaneFromTrace(lane))
        {
            lane.AppendSample(++_waveformOrder, Time, signal.Value, force: true);
        }

        if (selectLane)
        {
            SelectedWaveformLane = lane;
        }
    }

    private void SetInputValueSilently(string? name, string value)
    {
        if (name is null)
        {
            return;
        }

        SignalViewModel? input = Inputs.FirstOrDefault(input => string.Equals(input.Name, name, StringComparison.OrdinalIgnoreCase));
        if (input is not null)
        {
            _suppressInputLiveUpdate = true;
            try
            {
                input.Value = value;
            }
            finally
            {
                _suppressInputLiveUpdate = false;
            }
        }
    }

    private static string FormatOutputValue(string value, int width)
    {
        if (width == 1)
        {
            return value;
        }

        if (!BigInteger.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger parsed))
        {
            return value;
        }

        int digits = Math.Max(1, (width + 3) / 4);
        return "0x" + parsed.ToString("X", CultureInfo.InvariantCulture).PadLeft(digits, '0');
    }

    private async Task DisposeWorkerAsync()
    {
        CancelLiveEvaluation();

        if (_worker is not null)
        {
            await _worker.DisposeAsync();
            _worker = null;
            if (_simulationTarget == SimulationTarget.Rtl)
            {
                _liveProbes.AttachWorker(null);
            }
            _rtlTraceFilePath = null;
            RaiseSimulationTargetChanged();
        }
    }

    private async Task DisposeGateLevelWorkerAsync()
    {
        if (_gateLevelWorker is not null)
        {
            if (_simulationTarget == SimulationTarget.GateLevel)
            {
                _simulationTarget = SimulationTarget.Rtl;
                _traceFilePath = _rtlTraceFilePath;
                _liveProbes.AttachWorker(_worker);
                _ = _liveProbes.RefreshDescriptorsAsync(CancellationToken.None);
            }
            await _gateLevelWorker.DisposeAsync();
            _gateLevelWorker = null;
            _gateLevelTraceFilePath = null;
            RaiseSimulationTargetChanged();
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Bistable.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? Directory.GetCurrentDirectory();
    }

    private async Task LoadLayoutStateAsync()
    {
        LayoutState state = await _workspace.LayoutState.LoadAsync();
        _waveformZoom = state.WaveformZoom;
        _waveformOffset = state.WaveformOffset;
        _leftDockWidth = state.LeftDockWidth > 0 ? state.LeftDockWidth : 260;
        _rightDockWidth = state.RightDockWidth > 0 ? state.RightDockWidth : 320;
        _bottomDockHeight = state.BottomDockHeight > 0 ? state.BottomDockHeight : 280;
        _projectDockZone = state.ProjectDockZone;
        _waveformDockZone = state.WaveformDockZone;
        _schematicDockZone = state.SchematicDockZone;
        _projectLastVisibleZone = _projectDockZone == DockZone.Hidden ? DockZone.Left : _projectDockZone;
        _waveformLastVisibleZone = _waveformDockZone == DockZone.Hidden ? DockZone.Bottom : _waveformDockZone;
        _schematicLastVisibleZone = _schematicDockZone == DockZone.Hidden ? DockZone.Right : _schematicDockZone;
        _projectPanel.Zone = _projectDockZone;
        _waveformPanel.Zone = _waveformDockZone;
        _schematicPanel.Zone = _schematicDockZone;
        RebuildDockCollections();
        OnPropertyChanged(nameof(WaveformZoom));
        OnPropertyChanged(nameof(WaveformOffset));
        OnPropertyChanged(nameof(LeftDockWidth));
        OnPropertyChanged(nameof(RightDockWidth));
        OnPropertyChanged(nameof(BottomDockHeight));
        OnPropertyChanged(nameof(ProjectDockZone));
        OnPropertyChanged(nameof(WaveformDockZone));
        OnPropertyChanged(nameof(SchematicDockZone));
        OnPropertyChanged(nameof(IsProjectPaneVisible));
        OnPropertyChanged(nameof(IsWaveformPaneVisible));
        OnPropertyChanged(nameof(IsSchematicPaneVisible));
    }

    public void UpdateLayoutMetrics(double leftDockWidth, double rightDockWidth, double bottomDockHeight)
    {
        LeftDockWidth = leftDockWidth;
        RightDockWidth = rightDockWidth;
        BottomDockHeight = bottomDockHeight;
    }

    private void ClearWaveformSamples()
    {
        foreach (WaveformLaneViewModel lane in WaveformLanes)
        {
            lane.ClearSamples();
        }

        _waveformOrder = 0;
        WaveformCursorOrder = 0;

        foreach (SignalViewModel input in Inputs.Where(static input => input.IsInWaveform))
        {
            AppendWaveformSample(input.Name, input.Value, Time, force: true);
        }
    }

    private void CaptureCurrentOutputValues(ulong time)
    {
        foreach (SignalViewModel output in Outputs)
        {
            AppendWaveformSample(output.Name, output.Value, time);
        }

        WaveformCursorOrder = _waveformOrder;
    }

    private void SubscribeToInputs()
    {
        foreach (SignalViewModel input in Inputs)
        {
            input.PropertyChanged += OnInputPropertyChanged;
        }
    }

    private void UnsubscribeFromInputs()
    {
        foreach (SignalViewModel input in Inputs)
        {
            input.PropertyChanged -= OnInputPropertyChanged;
        }
    }

    private void OnInputPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_suppressInputLiveUpdate || sender is not SignalViewModel || e.PropertyName != nameof(SignalViewModel.Value))
        {
            return;
        }

        ScheduleLiveEvaluation();
    }

    private void OnSelectedSignalPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SignalViewModel.Value))
        {
            return;
        }

        OnPropertyChanged(nameof(SelectedSchematicSignalValue));
        if (SelectedSignal?.IsInput == true)
        {
            SchematicDriveValue = SelectedSignal.Value;
        }
    }

    private void ScheduleLiveEvaluation()
    {
        if (!LiveModeEnabled || TopModule == "-")
        {
            return;
        }

        if (IsSimulationBusy)
        {
            _liveEvaluationPending = true;
            return;
        }

        if (_isLiveEvaluationInFlight)
        {
            _liveEvaluationPending = true;
            return;
        }

        if (!AreInputsReadyForEvaluation(out string? invalidSignalName))
        {
            if (!string.IsNullOrWhiteSpace(invalidSignalName))
            {
                Status = $"Waiting for a valid value on {invalidSignalName}.";
            }

            return;
        }

        CancelLiveEvaluation();
        CancellationTokenSource cts = new();
        _liveEvaluationCts = cts;
        _ = RunScheduledLiveEvaluationAsync(cts.Token);
    }

    private async Task RunScheduledLiveEvaluationAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_liveEvaluationDelayMs > 0)
            {
                await Task.Delay(_liveEvaluationDelayMs, cancellationToken);
            }

            await ExecuteLiveEvaluationAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ExecuteLiveEvaluationAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || _isLiveEvaluationInFlight || !LiveModeEnabled)
        {
            return;
        }

        if (!AreInputsReadyForEvaluation(out _))
        {
            return;
        }

        _isLiveEvaluationInFlight = true;
        try
        {
            await EvaluateAsync(cancellationToken);
            Status = ActiveSimulationWorker is null
                ? "Live preview updated."
                : $"Live {ActiveSimulationTargetLabel} eval updated.";
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _isLiveEvaluationInFlight = false;
            if (_liveEvaluationPending)
            {
                _liveEvaluationPending = false;
                ScheduleLiveEvaluation();
            }
        }
    }

    private void CancelLiveEvaluation()
    {
        if (_liveEvaluationCts is null)
        {
            return;
        }

        _liveEvaluationCts.Cancel();
        _liveEvaluationCts.Dispose();
        _liveEvaluationCts = null;
        _liveEvaluationPending = false;
    }

    private bool AreInputsReadyForEvaluation(out string? invalidSignalName)
    {
        foreach (SignalViewModel input in Inputs)
        {
            if (!TryParseInputValue(input.Value, out _))
            {
                invalidSignalName = input.Name;
                return false;
            }
        }

        invalidSignalName = null;
        return true;
    }

    private static bool TryParseInputValue(string text, out BigInteger value)
        => SignalValueCodec.TryParse(text, out value);

    /// <summary>
    /// Width-checked parse: returns true only if the parsed numeric fits in
    /// <paramref name="width"/> bits. Used to reject Drive/Force inputs whose
    /// magnitude would overflow the target signal (e.g. <c>0x1FF</c> on an 8b
    /// port). Negative values rejected — sign extension is the caller's job.
    /// </summary>
    private static bool TryParseValueForWidth(string text, int width, out BigInteger value, out string? error)
    {
        error = null;
        if (!SignalValueCodec.TryParse(text, out value))
        {
            error = $"Invalid value '{text}'. Use decimal, 0x, or 0b.";
            return false;
        }
        if (value < BigInteger.Zero)
        {
            error = "Negative values not supported here.";
            return false;
        }
        BigInteger max = (BigInteger.One << Math.Max(1, width)) - 1;
        if (value > max)
        {
            error = $"Value 0x{value:X} exceeds {width}-bit max 0x{max:X}.";
            return false;
        }
        return true;
    }

    private async Task RefreshTraceStateAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_traceFilePath) || string.IsNullOrWhiteSpace(TopModule))
        {
            _traceDocument = VcdTraceDocument.Empty;
            HierarchyScopeSignals.Clear();
            HierarchyTraceScopeSummaries.Clear();
            OnPropertyChanged(nameof(SelectedHierarchyScopeSummary));
            return;
        }

        string traceFilePath = _traceFilePath;
        string topModule = TopModule;
        VcdTraceDocument traceDocument = await Task.Run(
            () => _traceReader.Load(traceFilePath, topModule),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // A target switch can complete while the old trace is being parsed.
        // Never publish an RTL document into a gate-level session (or vice versa).
        if (!string.Equals(_traceFilePath, traceFilePath, StringComparison.Ordinal)
            || !string.Equals(TopModule, topModule, StringComparison.Ordinal))
        {
            return;
        }

        _traceDocument = traceDocument;
        SyncTraceSignalCatalog();
        RebuildHierarchyTraceScopeSummaries();
        RefreshHierarchyScopeSignals();
        RefreshWaveformFromTrace();
    }

    private void SyncTraceSignalCatalog()
    {
        Dictionary<string, SignalViewModel> existing = TraceSignals.ToDictionary(static signal => signal.Name, StringComparer.OrdinalIgnoreCase);
        TraceSignals.Clear();

        foreach (VcdTraceSignal traceSignal in _traceDocument.Signals.Where(static signal => !signal.IsTopLevel))
        {
            if (!existing.TryGetValue(traceSignal.Name, out SignalViewModel? signal))
            {
                signal = new SignalViewModel(
                    traceSignal.Name,
                    traceSignal.ShortName,
                    traceSignal.ScopePath,
                    SignalDirection.Internal,
                    traceSignal.Width,
                    isSigned: false);
            }

            if (_traceDocument.TryGetEvents(traceSignal.Name, out IReadOnlyList<VcdTraceEvent>? events) && events.Count > 0)
            {
                signal.Value = events[^1].Value;
            }

            TraceSignals.Add(signal);
        }
    }

    private void RefreshWaveformFromTrace()
    {
        if (_traceDocument.MaxOrder <= 0)
        {
            return;
        }

        foreach (WaveformLaneViewModel lane in WaveformLanes)
        {
            TryPopulateLaneFromTrace(lane);
        }

        _waveformOrder = _traceDocument.MaxOrder;
        WaveformCursorOrder = _waveformOrder;
        OnPropertyChanged(nameof(WaveformCursorSummary));
        OnPropertyChanged(nameof(WaveformCursorTime));
    }

    private void RefreshHierarchyScopeSignals()
    {
        string? hierarchyPath = SelectedHierarchyNode?.HierarchyPath;
        string? selectedSignalName = SelectedSignal?.Name;
        HierarchyScopeSignals.Clear();

        if (string.IsNullOrWhiteSpace(hierarchyPath))
        {
            OnPropertyChanged(nameof(SelectedHierarchyScopeSummary));
            OnPropertyChanged(nameof(SelectedHierarchyScopeHint));
            return;
        }

        foreach (SignalViewModel signal in TraceSignals
                     .Where(signal => string.Equals(signal.ScopePath, hierarchyPath, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(signal => signal.ShortName, StringComparer.OrdinalIgnoreCase))
        {
            HierarchyScopeSignals.Add(signal);
        }

        if (!string.IsNullOrWhiteSpace(selectedSignalName))
        {
            SignalViewModel? matching = HierarchyScopeSignals.FirstOrDefault(signal =>
                string.Equals(signal.Name, selectedSignalName, StringComparison.OrdinalIgnoreCase));
            if (matching is not null && !ReferenceEquals(SelectedSignal, matching))
            {
                SelectedSignal = matching;
            }
        }

        OnPropertyChanged(nameof(SelectedHierarchyScopeSummary));
        OnPropertyChanged(nameof(SelectedHierarchyScopeHint));
        RefreshSelectedHierarchyLocalSignals();
    }

    private void RefreshSelectedHierarchyNeighborhood()
    {
        SelectedHierarchyChildScopes.Clear();
        SelectedHierarchyChildInstances.Clear();
        if (SelectedHierarchyNode is not null)
        {
            Bistable.Core.Design.DesignModuleDefinition? currentDefinition = ResolveCurrentModuleDefinition();
            Dictionary<string, Bistable.Core.Design.DesignInstanceDefinition> instancesByName = currentDefinition?.Instances
                .ToDictionary(static instance => instance.Name, StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, Bistable.Core.Design.DesignInstanceDefinition>(StringComparer.OrdinalIgnoreCase);

            foreach (HierarchyNodeViewModel child in SelectedHierarchyNode.Children.OrderBy(static child => child.InstanceName, StringComparer.OrdinalIgnoreCase))
            {
                SelectedHierarchyChildScopes.Add(CreateScopeNode(child));
                SelectedHierarchyChildInstances.Add(CreateScopeInstance(child, instancesByName));
            }
        }

        OnPropertyChanged(nameof(SelectedHierarchyParentScope));
        OnPropertyChanged(nameof(SelectedHierarchyScopeHint));
    }

    private void RefreshSelectedHierarchyPorts()
    {
        SelectedHierarchyPorts.Clear();
        SelectedHierarchyContAssigns.Clear();
        SelectedHierarchyPrimitives.Clear();
        if (SelectedHierarchyNode is null || _currentDesign is null)
        {
            return;
        }

        if (!_currentDesign.ModuleCatalog.TryGetValue(SelectedHierarchyNode.ModuleName, out ModuleMetadata? module))
        {
            return;
        }

        foreach (SignalPort port in module.Ports.OrderBy(static port => port.PinIndex))
        {
            SelectedHierarchyPorts.Add(new HierarchyScopePortViewModel(
                port.Name,
                port.Direction,
                port.Width,
                port.IsSigned));
        }

        if (_currentDesign.ModuleDefinitions.TryGetValue(SelectedHierarchyNode.ModuleName, out Bistable.Core.Design.DesignModuleDefinition? definition))
        {
            foreach (Bistable.Core.Design.DesignContAssign assign in definition.ContAssigns)
            {
                SelectedHierarchyContAssigns.Add(assign);
            }
        }

        // Phase 2: decode primitives from the AST when available. The renderer consumes
        // these alongside ContAssigns to draw FF/Mux/etc symbols that the legacy flat
        // model cannot represent.
        if (_currentAst is not null)
        {
            Bistable.Core.Design.Ast.ModuleAst? moduleAst = _currentAst.Modules
                .FirstOrDefault(m => string.Equals(m.Name, SelectedHierarchyNode.ModuleName, StringComparison.OrdinalIgnoreCase));
            if (moduleAst is not null)
            {
                Bistable.Core.Design.Schematic.SchematicPrimitiveList primitives =
                    Bistable.Core.Design.Schematic.SchematicDecoder.Decode(moduleAst);
                foreach (Bistable.Core.Design.Schematic.SchematicPrimitive primitive in primitives.Logic)
                {
                    SelectedHierarchyPrimitives.Add(primitive);
                }
            }
        }
    }

    private void RefreshSelectedHierarchyLocalSignals()
    {
        SelectedHierarchyLocalSignals.Clear();
        Bistable.Core.Design.DesignModuleDefinition? definition = ResolveCurrentModuleDefinition();
        string? hierarchyPath = SelectedHierarchyNode?.HierarchyPath;
        if (definition is null || string.IsNullOrWhiteSpace(hierarchyPath))
        {
            return;
        }

        foreach (Bistable.Core.Design.DesignLocalSignal local in definition.LocalSignals.OrderBy(static local => local.Name, StringComparer.OrdinalIgnoreCase))
        {
            SignalViewModel? traced = HierarchyScopeSignals.FirstOrDefault(signal =>
                string.Equals(signal.ShortName, local.Name, StringComparison.OrdinalIgnoreCase));
            SelectedHierarchyLocalSignals.Add(new HierarchyScopeLocalSignalViewModel(
                local.Name,
                local.Width,
                local.IsSigned,
                traced is not null,
                traced?.Value ?? "-",
                traced?.Name ?? BuildResolvedLocalSignalName(hierarchyPath, local.Name)));
        }

        RaiseSelectedSchematicSignalProperties();
    }

    private void RebuildHierarchyTraceScopeSummaries()
    {
        HierarchyTraceScopeSummaries.Clear();
        if (HierarchyRoot is null)
        {
            return;
        }

        Dictionary<string, List<SignalViewModel>> byExactScope = TraceSignals
            .Where(signal => !string.IsNullOrWhiteSpace(signal.ScopePath))
            .GroupBy(signal => signal.ScopePath!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (HierarchyNodeViewModel node in EnumerateHierarchy(HierarchyRoot))
        {
            int exactCount = byExactScope.TryGetValue(node.HierarchyPath, out List<SignalViewModel>? exactSignals)
                ? exactSignals.Count
                : 0;
            int descendantCount = TraceSignals.Count(signal =>
                !string.IsNullOrWhiteSpace(signal.ScopePath)
                && signal.ScopePath.StartsWith($"{node.HierarchyPath}.", StringComparison.OrdinalIgnoreCase));

            HierarchyTraceScopeSummaries.Add(new HierarchyTraceScopeSummaryViewModel(
                node.HierarchyPath,
                exactCount,
                descendantCount));
        }

        RefreshSelectedHierarchyNeighborhood();
    }

    private bool TryPopulateLaneFromTrace(WaveformLaneViewModel lane)
    {
        if (!_traceDocument.TryGetEvents(lane.Name, out IReadOnlyList<VcdTraceEvent>? events) || events.Count == 0)
        {
            return false;
        }

        lane.ReplaceSamples(events.Select(static traceEvent => new WaveformSampleViewModel(traceEvent.Order, traceEvent.Time, traceEvent.Value)));
        lane.Signal.Value = lane.LatestValue;
        return true;
    }

    private void SyncSelectedWaveformLaneFromSignal()
    {
        if (_selectedSignal is null)
        {
            return;
        }

        WaveformLaneViewModel? lane = WaveformLanes.FirstOrDefault(candidate => string.Equals(candidate.Name, _selectedSignal.Name, StringComparison.OrdinalIgnoreCase));
        if (lane is not null && !ReferenceEquals(lane, SelectedWaveformLane))
        {
            SelectedWaveformLane = lane;
        }
    }

    private void SyncSelectedSignalFromWaveformLane()
    {
        if (_selectedWaveformLane is null)
        {
            return;
        }

        SignalViewModel? signal = FindAnySignalByName(_selectedWaveformLane.Name);
        if (signal is not null && !ReferenceEquals(signal, SelectedSignal))
        {
            SelectedSignal = signal;
        }
    }

    private void SelectHierarchyScope(string hierarchyPath)
    {
        if (string.IsNullOrWhiteSpace(hierarchyPath))
        {
            return;
        }

        SelectedHierarchyPath = hierarchyPath;
    }

    // P2.7-2: history maintenance helpers. PushScopeHistoryIfNeeded delegates
    // to ScopeNavigationHistory; the suppression flag short-circuits the push
    // when Back/Forward themselves triggered the selection change so the stack
    // bookkeeping (already done by GoBack/GoForward) stays consistent.
    private void PushScopeHistoryIfNeeded(string? newPath)
    {
        if (_suppressScopeHistoryPush)
        {
            RaiseScopeHistoryCanExecuteChanged();
            return;
        }
        _scopeHistory.RecordNavigation(newPath);
        RaiseScopeHistoryCanExecuteChanged();
    }

    private void NavigateScopeBack()
    {
        string? previous = _scopeHistory.GoBack();
        if (previous is null) return;
        NavigateWithoutHistoryPush(previous);
    }

    private void NavigateScopeForward()
    {
        string? next = _scopeHistory.GoForward();
        if (next is null) return;
        NavigateWithoutHistoryPush(next);
    }

    private void NavigateWithoutHistoryPush(string hierarchyPath)
    {
        try
        {
            _suppressScopeHistoryPush = true;
            SelectedHierarchyPath = hierarchyPath;
        }
        finally
        {
            _suppressScopeHistoryPush = false;
            RaiseScopeHistoryCanExecuteChanged();
        }
    }

    private void RaiseScopeHistoryCanExecuteChanged()
    {
        if (NavigateScopeBackCommand is RelayCommand back) back.RaiseCanExecuteChanged();
        if (NavigateScopeForwardCommand is RelayCommand fwd) fwd.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanNavigateScopeBack));
        OnPropertyChanged(nameof(CanNavigateScopeForward));
    }

    /// <summary>
    /// Schematic double-click on a sub-instance block: select that hierarchy
    /// node then enter sub-sim for it (provided the user isn't already in one).
    /// Single click already triggers <see cref="SelectHierarchyScopeCommand"/>;
    /// the double click adds the sub-sim build on top.
    /// </summary>
    private async Task EnterSubSimAtPathAsync(string hierarchyPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(hierarchyPath)) return;
        SelectedHierarchyPath = hierarchyPath;
        if (!CanEnterSubSim)
        {
            Status = _isSubSimActive
                ? "Already in a sub-simulation. Exit first."
                : $"Cannot enter sub-sim for '{hierarchyPath}'.";
            return;
        }
        await EnterSubSimulationAsync(cancellationToken);
    }

    private void ToggleSchematicExpansion(string hierarchyPath)
    {
        if (string.IsNullOrWhiteSpace(hierarchyPath))
        {
            return;
        }

        string? existing = SchematicExpandedPaths.FirstOrDefault(path =>
            string.Equals(path, hierarchyPath, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            SchematicExpandedPaths.Add(hierarchyPath);
            Status = $"Expanded schematic scope {hierarchyPath}.";
        }
        else
        {
            SchematicExpandedPaths.Remove(existing);
            Status = $"Collapsed schematic scope {hierarchyPath}.";
        }

        OnPropertyChanged(nameof(IsSelectedHierarchyScopeExpanded));
    }

    private Task PersistLayoutStateAsync() =>
        _workspace.LayoutState.SaveAsync(new LayoutState(
            WaveformZoom,
            WaveformOffset,
            LeftDockWidth,
            RightDockWidth,
            BottomDockHeight,
            ProjectDockZone,
            WaveformDockZone,
            SchematicDockZone));

    private int GetMaxWaveformOffset() => Math.Max(0, (int)_waveformOrder - 1);

    private string? ResolveActiveClockName()
    {
        if (!string.IsNullOrWhiteSpace(SelectedClockName))
        {
            return SelectedClockName;
        }

        return AvailableClocks.FirstOrDefault()
            ?? Inputs.FirstOrDefault(static input => input.Width == 1)?.Name;
    }

    private bool TryGetRunCycles(out long cycles)
    {
        if (long.TryParse(RunCyclesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out cycles)
            && cycles > 0)
        {
            return true;
        }

        cycles = 0;
        return false;
    }

    private AsyncCommand CreateSimulationCommand(
        string operationName,
        Func<CancellationToken, Task> execute,
        Func<bool>? canExecute = null)
    {
        AsyncCommand command = new(
            cancellationToken => ExecuteSimulationOperationAsync(
                operationName,
                execute,
                cancellationToken),
            () => !IsSimulationBusy && (canExecute?.Invoke() ?? true));
        _simulationCommands.Add(command);
        return command;
    }

    private async Task ExecuteSimulationOperationAsync(
        string operationName,
        Func<CancellationToken, Task> execute,
        CancellationToken cancellationToken)
    {
        await _simulationOperationGate.WaitAsync(cancellationToken);
        using CancellationTokenSource linkedCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            CancelLiveEvaluation();
            _activeSimulationCancellation = linkedCancellation;
            SimulationOperationName = operationName;
            IsSimulationBusy = true;
            await execute(linkedCancellation.Token);
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
            Status = $"{operationName} cancelled.";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            Status = $"{operationName} failed: {ex.Message}";
        }
        finally
        {
            _activeSimulationCancellation = null;
            SimulationOperationName = string.Empty;
            IsSimulationBusy = false;
            _simulationOperationGate.Release();

            if (_liveEvaluationPending && LiveModeEnabled)
            {
                _liveEvaluationPending = false;
                ScheduleLiveEvaluation();
            }
        }
    }

    private void CancelActiveSimulationOperation()
    {
        if (_activeSimulationCancellation is null) return;
        Status = $"Cancelling {SimulationOperationName}...";
        _activeSimulationCancellation.Cancel();
    }

    private static IEnumerable<string> ResolveAvailableClocks(ProjectConfiguration project, IEnumerable<SignalViewModel> inputs)
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (ClockHint clock in project.Clocks)
        {
            if (!string.IsNullOrWhiteSpace(clock.Name) && names.Add(clock.Name))
            {
                yield return clock.Name;
            }
        }

        foreach (SignalViewModel input in inputs.Where(static input => input.Width == 1))
        {
            if (names.Add(input.Name))
            {
                yield return input.Name;
            }
        }
    }

    private void MoveDockPanel(DockPanelKind kind, DockZone zone)
    {
        _preferredDockPanelSelection = kind;
        try
        {
            MoveDockPanelCore(kind, zone);
        }
        finally
        {
            _preferredDockPanelSelection = null;
        }

        Status = zone == DockZone.Hidden
            ? $"{kind} pane hidden."
            : $"{kind} pane docked {zone}.";
    }

    private void MoveDockPanelCore(DockPanelKind kind, DockZone zone)
    {
        switch (kind)
        {
            case DockPanelKind.Project:
                ProjectDockZone = zone;
                break;
            case DockPanelKind.Waveform:
                WaveformDockZone = zone;
                break;
            case DockPanelKind.Schematic:
                SchematicDockZone = zone;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    private void RebuildDockCollections()
    {
        RebuildZoneCollection(LeftDockPanels, DockZone.Left);
        RebuildZoneCollection(RightDockPanels, DockZone.Right);
        RebuildZoneCollection(BottomDockPanels, DockZone.Bottom);
        RebuildZoneCollection(CenterDockPanels, DockZone.Center);

        SelectedLeftDockPanel = SelectDockPanelForZone(LeftDockPanels, SelectedLeftDockPanel);
        SelectedRightDockPanel = SelectDockPanelForZone(RightDockPanels, SelectedRightDockPanel);
        SelectedBottomDockPanel = SelectDockPanelForZone(BottomDockPanels, SelectedBottomDockPanel);
        SelectedCenterDockPanel = SelectDockPanelForZone(CenterDockPanels, SelectedCenterDockPanel);
    }

    private DockPanelViewModel? SelectDockPanelForZone(
        IReadOnlyList<DockPanelViewModel> panels,
        DockPanelViewModel? current)
    {
        if (_preferredDockPanelSelection is { } preferred)
        {
            DockPanelViewModel? preferredPanel = panels.FirstOrDefault(panel => panel.Kind == preferred);
            if (preferredPanel is not null)
            {
                return preferredPanel;
            }
        }

        if (current is not null)
        {
            DockPanelViewModel? stillVisible = panels.FirstOrDefault(panel => panel.Kind == current.Kind);
            if (stillVisible is not null)
            {
                return stillVisible;
            }
        }

        return panels.FirstOrDefault();
    }

    private SignalViewModel? FindAnySignalByName(string name) =>
        AllSignals.FirstOrDefault(signal => string.Equals(signal.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? TraceSignals.FirstOrDefault(signal => string.Equals(signal.Name, name, StringComparison.OrdinalIgnoreCase));

    private IReadOnlyList<HierarchyBreadcrumbItemViewModel> BuildHierarchyBreadcrumbs()
    {
        if (HierarchyRoot is null)
        {
            return [];
        }

        List<HierarchyBreadcrumbItemViewModel> breadcrumbs = [];
        string? selectedPath = SelectedHierarchyNode?.HierarchyPath ?? HierarchyRoot.HierarchyPath;
        string[] pathSegments = selectedPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string currentPath = string.Empty;
        HierarchyNodeViewModel? currentNode = HierarchyRoot;

        for (int index = 0; index < pathSegments.Length; index++)
        {
            string segment = pathSegments[index];
            currentPath = index == 0 ? segment : $"{currentPath}.{segment}";
            if (index == 0)
            {
                currentNode = HierarchyRoot;
            }
            else
            {
                currentNode = currentNode?.Children.FirstOrDefault(child =>
                    string.Equals(child.HierarchyPath, currentPath, StringComparison.OrdinalIgnoreCase));
            }

            if (currentNode is null)
            {
                break;
            }

            breadcrumbs.Add(new HierarchyBreadcrumbItemViewModel(
                currentNode.HierarchyPath,
                currentNode.InstanceName,
                currentNode.ModuleName,
                string.Equals(currentNode.HierarchyPath, selectedPath, StringComparison.OrdinalIgnoreCase)));
        }

        return breadcrumbs;
    }

    private HierarchyScopeNodeViewModel CreateScopeNode(HierarchyNodeViewModel node)
    {
        HierarchyTraceScopeSummaryViewModel? summary = HierarchyTraceScopeSummaries.FirstOrDefault(
            candidate => string.Equals(candidate.HierarchyPath, node.HierarchyPath, StringComparison.OrdinalIgnoreCase));
        ModuleMetadata? module = null;
        if (_currentDesign is not null)
        {
            _currentDesign.ModuleCatalog.TryGetValue(node.ModuleName, out module);
        }
        return new HierarchyScopeNodeViewModel(
            node.HierarchyPath,
            node.InstanceName,
            node.ModuleName,
            module?.Inputs.Count ?? 0,
            module?.Outputs.Count ?? 0,
            summary?.ExactSignalCount ?? 0,
            summary?.DescendantSignalCount ?? 0);
    }

    private HierarchyScopeInstanceViewModel CreateScopeInstance(
        HierarchyNodeViewModel node,
        IReadOnlyDictionary<string, Bistable.Core.Design.DesignInstanceDefinition> instancesByName)
    {
        HierarchyTraceScopeSummaryViewModel? summary = HierarchyTraceScopeSummaries.FirstOrDefault(
            candidate => string.Equals(candidate.HierarchyPath, node.HierarchyPath, StringComparison.OrdinalIgnoreCase));
        ModuleMetadata? module = null;
        if (_currentDesign is not null)
        {
            _currentDesign.ModuleCatalog.TryGetValue(node.ModuleName, out module);
        }

        List<HierarchyScopeInstancePortConnectionViewModel> connections = [];
        if (instancesByName.TryGetValue(node.InstanceName, out Bistable.Core.Design.DesignInstanceDefinition? instanceDefinition))
        {
            Dictionary<string, SignalPort> portCatalog = module?.Ports.ToDictionary(static port => port.Name, StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, SignalPort>(StringComparer.OrdinalIgnoreCase);
            foreach (Bistable.Core.Design.DesignInstancePortConnection connection in instanceDefinition.PortConnections)
            {
                int width = portCatalog.TryGetValue(connection.PortName, out SignalPort? port) ? port.Width : 1;
                bool isInput = string.Equals(connection.Direction, "in", StringComparison.OrdinalIgnoreCase);
                connections.Add(new HierarchyScopeInstancePortConnectionViewModel(
                    connection.PortName,
                    connection.SignalName,
                    isInput,
                    width,
                    connection.ConcatParts));
            }
        }

        IReadOnlyList<Bistable.Core.Design.DesignContAssign> instanceContAssigns = [];
        if (_currentDesign is not null
            && _currentDesign.ModuleDefinitions.TryGetValue(node.ModuleName, out Bistable.Core.Design.DesignModuleDefinition? subDefinition))
        {
            instanceContAssigns = subDefinition.ContAssigns;
        }

        return new HierarchyScopeInstanceViewModel(
            node.HierarchyPath,
            node.InstanceName,
            node.ModuleName,
            module?.Inputs.Count ?? 0,
            module?.Outputs.Count ?? 0,
            summary?.ExactSignalCount ?? 0,
            summary?.DescendantSignalCount ?? 0,
            connections,
            CreateScopePorts(module),
            CreateScopeLocalSignals(node.HierarchyPath, node.ModuleName),
            CreateChildScopeInstances(node),
            instanceContAssigns);
    }

    private IReadOnlyList<HierarchyScopePortViewModel> CreateScopePorts(ModuleMetadata? module)
    {
        if (module is null)
        {
            return [];
        }

        return module.Ports
            .OrderBy(static port => port.PinIndex)
            .Select(static port => new HierarchyScopePortViewModel(port.Name, port.Direction, port.Width, port.IsSigned))
            .ToList();
    }

    private IReadOnlyList<HierarchyScopeLocalSignalViewModel> CreateScopeLocalSignals(string hierarchyPath, string moduleName)
    {
        if (_currentDesign is null || !_currentDesign.ModuleDefinitions.TryGetValue(moduleName, out Bistable.Core.Design.DesignModuleDefinition? definition))
        {
            return [];
        }

        List<HierarchyScopeLocalSignalViewModel> signals = [];
        foreach (Bistable.Core.Design.DesignLocalSignal local in definition.LocalSignals.OrderBy(static local => local.Name, StringComparer.OrdinalIgnoreCase))
        {
            SignalViewModel? traced = TraceSignals.FirstOrDefault(signal =>
                string.Equals(signal.ScopePath, hierarchyPath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(signal.ShortName, local.Name, StringComparison.OrdinalIgnoreCase));
            signals.Add(new HierarchyScopeLocalSignalViewModel(
                local.Name,
                local.Width,
                local.IsSigned,
                traced is not null,
                traced?.Value ?? "-",
                traced?.Name ?? BuildResolvedLocalSignalName(hierarchyPath, local.Name)));
        }

        // P3-6 / memory-viewer: memories aren't in DesignLocalSignal (no array
        // dims propagated through the flattener), so reach into the AST and
        // append them. Their hierarchy path matches the worker's probe table key.
        AppendMemoryLocalSignals(signals, hierarchyPath, moduleName);
        return signals;
    }

    private void AppendMemoryLocalSignals(
        List<HierarchyScopeLocalSignalViewModel> signals,
        string hierarchyPath,
        string moduleName)
    {
        if (_currentAst is null) return;
        Bistable.Core.Design.Ast.ModuleAst? module = _currentAst.Modules
            .FirstOrDefault(m => string.Equals(m.Name, moduleName, StringComparison.OrdinalIgnoreCase));
        if (module is null) return;

        foreach (Bistable.Core.Design.Ast.SignalDecl mem in module.LocalSignals
                     .Where(s => s.ArrayDims.Count == 1 && !s.Name.StartsWith("__V", StringComparison.Ordinal))
                     .OrderBy(static s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            int depth = mem.ArrayDims[0].Width;
            signals.Add(new HierarchyScopeLocalSignalViewModel(
                name: mem.Name,
                width: mem.Width,
                isSigned: mem.IsSigned,
                isTraced: false,
                currentValue: "-",
                resolvedSignalName: BuildResolvedLocalSignalName(hierarchyPath, mem.Name),
                memory: new MemoryShape(depth)));
        }
    }

    private static string BuildResolvedLocalSignalName(string hierarchyPath, string localName) =>
        string.IsNullOrWhiteSpace(hierarchyPath) ? localName : $"{hierarchyPath}.{localName}";

    private IReadOnlyList<HierarchyScopeInstanceViewModel> CreateChildScopeInstances(HierarchyNodeViewModel node)
    {
        if (_currentDesign is null || !_currentDesign.ModuleDefinitions.TryGetValue(node.ModuleName, out Bistable.Core.Design.DesignModuleDefinition? definition))
        {
            return [];
        }

        Dictionary<string, Bistable.Core.Design.DesignInstanceDefinition> instancesByName = definition.Instances
            .ToDictionary(static instance => instance.Name, StringComparer.OrdinalIgnoreCase);
        return node.Children
            .OrderBy(static child => child.InstanceName, StringComparer.OrdinalIgnoreCase)
            .Select(child => CreateScopeInstance(child, instancesByName))
            .ToList();
    }

    private Bistable.Core.Design.DesignModuleDefinition? ResolveCurrentModuleDefinition()
    {
        if (SelectedHierarchyNode is null || _currentDesign is null)
        {
            return null;
        }

        return _currentDesign.ModuleDefinitions.TryGetValue(SelectedHierarchyNode.ModuleName, out Bistable.Core.Design.DesignModuleDefinition? definition)
            ? definition
            : null;
    }

    private void RebuildZoneCollection(ObservableCollection<DockPanelViewModel> target, DockZone zone)
    {
        target.Clear();
        foreach (DockPanelViewModel panel in EnumeratePanelsForZone(zone))
        {
            target.Add(panel);
        }
    }

    private IEnumerable<DockPanelViewModel> EnumeratePanelsForZone(DockZone zone)
    {
        if (ProjectDockZone == zone)
        {
            yield return _projectPanel;
        }

        if (WaveformDockZone == zone)
        {
            yield return _waveformPanel;
        }

        if (SchematicDockZone == zone)
        {
            yield return _schematicPanel;
        }
    }

    private static string NormalizeWaveformValue(SignalViewModel signal, string value)
    {
        if (signal.Width == 1)
        {
            return value;
        }

        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        if (!BigInteger.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger parsed))
        {
            return value;
        }

        int digits = Math.Max(1, (signal.Width + 3) / 4);
        return "0x" + parsed.ToString("X", CultureInfo.InvariantCulture).PadLeft(digits, '0');
    }

    private static HierarchyNodeViewModel? FindHierarchyNode(HierarchyNodeViewModel? current, string hierarchyPath)
    {
        if (current is null)
        {
            return null;
        }

        if (string.Equals(current.HierarchyPath, hierarchyPath, StringComparison.Ordinal))
        {
            return current;
        }

        foreach (HierarchyNodeViewModel child in current.Children)
        {
            HierarchyNodeViewModel? match = FindHierarchyNode(child, hierarchyPath);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static HierarchyNodeViewModel? FindHierarchyParentNode(HierarchyNodeViewModel? current, HierarchyNodeViewModel? target)
    {
        if (current is null || target is null)
        {
            return null;
        }

        foreach (HierarchyNodeViewModel child in current.Children)
        {
            if (ReferenceEquals(child, target))
            {
                return current;
            }

            HierarchyNodeViewModel? match = FindHierarchyParentNode(child, target);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static IEnumerable<HierarchyNodeViewModel> EnumerateHierarchy(HierarchyNodeViewModel root)
    {
        yield return root;
        foreach (HierarchyNodeViewModel child in root.Children)
        {
            foreach (HierarchyNodeViewModel descendant in EnumerateHierarchy(child))
            {
                yield return descendant;
            }
        }
    }
}

/// <summary>
/// One memory probe located inside a specific module scope. Surfaced by
/// <see cref="MainWindowViewModel.EnumerateMemoriesAt"/> for the schematic
/// context menu so users can jump straight to "Open Memory Viewer: X" from
/// a right-click on the owning instance.
/// </summary>
public sealed record MemoryLocation(string LocalName, string ResolvedPath, int CellWidth, int Depth);
