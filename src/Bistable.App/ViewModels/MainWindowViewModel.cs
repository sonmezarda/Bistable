using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Bistable.App.Infrastructure;
using Bistable.App.Services;
using Bistable.Core.Design;
using Bistable.Core.Projects;
using Bistable.Protocol;
using Bistable.Verilator;

namespace Bistable.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
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
    private string? _currentProjectDirectory;
    private SimulationWorkerClient? _worker;
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
    private bool _liveModeEnabled = true;
    private bool _suppressInputLiveUpdate;
    private CancellationTokenSource? _liveEvaluationCts;
    private bool _isLiveEvaluationInFlight;
    private bool _liveEvaluationPending;

    public MainWindowViewModel(BistableWorkspace workspace, bool loadPersistedLayout = true, int liveEvaluationDelayMs = 120)
    {
        _workspace = workspace;
        _liveEvaluationDelayMs = Math.Max(0, liveEvaluationDelayMs);
        LoadProjectCommand = new AsyncCommand(LoadProjectAsync);
        BuildCommand = new AsyncCommand(BuildAsync);
        EvalCommand = new AsyncCommand(EvaluateAsync);
        TickCommand = new AsyncCommand(TickAsync);
        RunCyclesCommand = new AsyncCommand(RunCyclesAsync);
        ResetCommand = new AsyncCommand(ResetAsync);
        AddSelectedWaveformSignalCommand = new RelayCommand(AddSelectedWaveformSignal);
        AddHierarchyScopeSignalsToWaveformCommand = new RelayCommand(AddHierarchyScopeSignalsToWaveform);
        SelectHierarchyScopeCommand = new ParameterizedRelayCommand<string>(SelectHierarchyScope);
        ToggleSchematicExpansionCommand = new ParameterizedRelayCommand<string>(ToggleSchematicExpansion);
        ToggleInputSignalCommand = new ParameterizedRelayCommand<string>(ToggleInputSignal);
        DriveSelectedSchematicInputCommand = new RelayCommand(DriveSelectedSchematicInput);
        RemoveSelectedWaveformSignalCommand = new RelayCommand(RemoveSelectedWaveformSignal);
        ClearWaveformCommand = new RelayCommand(ClearWaveform);
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
        FitWaveformCommand = new RelayCommand(() =>
        {
            WaveformZoom = 1;
            WaveformOffset = 0;
        });
        RebuildDockCollections();
        LoadSamples();
        if (loadPersistedLayout)
        {
            _ = LoadLayoutStateAsync();
        }
    }

    public ObservableCollection<SignalViewModel> Inputs { get; } = [];

    public ObservableCollection<SignalViewModel> Outputs { get; } = [];

    public ObservableCollection<SignalViewModel> AllSignals { get; } = [];

    public ObservableCollection<SignalViewModel> TraceSignals { get; } = [];

    public ObservableCollection<SignalViewModel> HierarchyScopeSignals { get; } = [];

    public ObservableCollection<HierarchyTraceScopeSummaryViewModel> HierarchyTraceScopeSummaries { get; } = [];

    public ObservableCollection<HierarchyScopeNodeViewModel> SelectedHierarchyChildScopes { get; } = [];

    public ObservableCollection<HierarchyScopeInstanceViewModel> SelectedHierarchyChildInstances { get; } = [];

    public ObservableCollection<HierarchyScopePortViewModel> SelectedHierarchyPorts { get; } = [];

    public ObservableCollection<HierarchyScopeLocalSignalViewModel> SelectedHierarchyLocalSignals { get; } = [];

    public ObservableCollection<Bistable.Core.Design.DesignContAssign> SelectedHierarchyContAssigns { get; } = [];

    public ObservableCollection<string> SchematicExpandedPaths { get; } = [];

    public ObservableCollection<SampleProjectViewModel> Samples { get; } = [];

    public ObservableCollection<WaveformLaneViewModel> WaveformLanes { get; } = [];

    public ObservableCollection<string> AvailableClocks { get; } = [];

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
            }
        }
    }

    public ICommand LoadProjectCommand { get; }

    public ICommand BuildCommand { get; }

    public ICommand EvalCommand { get; }

    public ICommand TickCommand { get; }

    public ICommand RunCyclesCommand { get; }

    public ICommand ResetCommand { get; }

    public ICommand AddSelectedWaveformSignalCommand { get; }

    public ICommand AddHierarchyScopeSignalsToWaveformCommand { get; }

    public ICommand SelectHierarchyScopeCommand { get; }

    public ICommand ToggleSchematicExpansionCommand { get; }

    public ICommand ToggleInputSignalCommand { get; }

    public ICommand DriveSelectedSchematicInputCommand { get; }

    public ICommand RemoveSelectedWaveformSignalCommand { get; }

    public ICommand ClearWaveformCommand { get; }

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
                    : FindAnySignalByName(value);
            }
            finally
            {
                _settingSelectedSchematicReference = false;
            }

            RaiseSelectedSchematicSignalProperties();
        }
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
        private set => SetProperty(ref _status, value);
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

    public bool CanDriveSelectedSchematicInput => SelectedSignal?.IsInput == true;

    public bool CanToggleSelectedSchematicInput => SelectedSignal?.IsInput == true && SelectedSignal.IsBoolean;

    public string SelectedSchematicSignalDisplayName =>
        SelectedSignal?.DisplayName
        ?? SelectedSchematicLocalSignal?.Name
        ?? _selectedSchematicReferenceName
        ?? "No signal selected";

    public string SelectedSchematicSignalDirection => SelectedSignal?.DirectionLabel ?? (SelectedSchematicLocalSignal is null ? "-" : "INTERNAL");

    public string SelectedSchematicSignalWidth => SelectedSignal?.WidthLabel ?? SelectedSchematicLocalSignal?.WidthLabel ?? "-";

    public string SelectedSchematicSignalValue => SelectedSignal?.Value ?? SelectedSchematicLocalSignal?.CurrentValue ?? "-";

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
        OnPropertyChanged(nameof(SelectedSchematicSignalName));
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
            _currentMetadata = result.Metadata;
            _currentDesign = result.Design;
            _currentProjectDirectory = result.ProjectDirectory;
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
            await DisposeWorkerAsync();
            Status = $"Loaded {result.Metadata.Ports.Count} top-level ports.";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException)
        {
            Status = ex.Message;
        }
    }

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
                new AsyncCommand(cancellationToken => LoadProjectFromPathAsync(path, cancellationToken))));
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
            progress);

        _worker = new SimulationWorkerClient(build.ExecutablePath);
        _traceFilePath = build.TraceFilePath;
        await PushInputsAsync(cancellationToken);
        SimulationSnapshot snapshot = await _worker.SendAsync(new SimulationCommand(SimulationCommandType.Eval), cancellationToken);
        ApplySnapshot(snapshot);
        RefreshTraceState();
        Status = $"Worker ready: {Path.GetFileName(build.ExecutablePath)}";
    }

    private static string TrimBuildStatus(string message)
    {
        string compact = message.ReplaceLineEndings(" ").Trim();
        return compact.Length <= 140 ? compact : compact[..137] + "...";
    }

    private async Task EvaluateAsync(CancellationToken cancellationToken)
    {
        if (TopModule == "-")
        {
            Status = "Open a project first. Use the Samples list or Open Project.";
            return;
        }

        if (_worker is not null)
        {
            await PushInputsAsync(cancellationToken);
            SimulationSnapshot snapshot = await _worker.SendAsync(new SimulationCommand(SimulationCommandType.Eval), cancellationToken);
            ApplySnapshot(snapshot);
            RefreshTraceState();
            Status = "Native eval completed.";
            return;
        }

        PreviewSimulationResult result = _workspace.PreviewSimulation.Evaluate(TopModule, Inputs, Outputs);
        CaptureCurrentOutputValues(Time);
        Status = result.Message;
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        if (_worker is null)
        {
            Time++;
            WaveformCursorOrder = _waveformOrder;
            Status = $"Manual UI tick at t={Time}. Build worker for native ticking.";
            return;
        }

        await PushInputsAsync(cancellationToken);
        string? clock = ResolveActiveClockName();
        SimulationSnapshot snapshot = await _worker.SendAsync(new SimulationCommand(SimulationCommandType.Tick, Signal: clock), cancellationToken);
        ApplySnapshot(snapshot);
        RefreshTraceState();
        SetInputValueSilently(clock, "0");
        Status = $"Native tick pulsed {clock ?? "clock"} 0->1->0 at t={Time}.";
    }

    private async Task RunCyclesAsync(CancellationToken cancellationToken)
    {
        if (_worker is null)
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

        SimulationSnapshot snapshot = await _worker.SendAsync(new SimulationCommand(SimulationCommandType.RunCycles, Signal: clock, Cycles: cycles), cancellationToken);
        ApplySnapshot(snapshot);
        RefreshTraceState();
        SetInputValueSilently(clock, "0");
        Status = $"Native run pulsed {clock ?? "clock"} for {cycles} cycles; t={Time}.";
    }

    private async Task ResetAsync(CancellationToken cancellationToken)
    {
        Time = 0;
        if (_worker is null)
        {
            ClearWaveformSamples();
            Status = "Session reset.";
            return;
        }

        SimulationSnapshot snapshot = await _worker.SendAsync(new SimulationCommand(SimulationCommandType.Reset), cancellationToken);
        ClearWaveformSamples();
        ApplySnapshot(snapshot);
        RefreshTraceState();
        string? reset = _currentProject?.Resets.FirstOrDefault()?.Name;
        if (reset is not null)
        {
            int activeLevel = _currentProject?.Resets.FirstOrDefault()?.ActiveLevel ?? 0;
            SetInputValueSilently(reset, activeLevel == 0 ? "1" : "0");
        }

        Status = "Native worker reset.";
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

    private void DriveSelectedSchematicInput()
    {
        if (SelectedSignal is null || !SelectedSignal.IsInput)
        {
            Status = "Select an input signal first.";
            return;
        }

        if (!TryParseInputValue(SchematicDriveValue, out _))
        {
            Status = $"Invalid drive value for {SelectedSignal.Name}. Use decimal, 0x, or 0b.";
            return;
        }

        SelectedSignal.Value = SchematicDriveValue.Trim();
        Status = $"Drove {SelectedSignal.Name} to {SelectedSignal.Value}.";
    }

    private async Task PushInputsAsync(CancellationToken cancellationToken)
    {
        if (_worker is null)
        {
            return;
        }

        foreach (SignalViewModel input in Inputs)
        {
            SimulationSnapshot snapshot = await _worker.SendAsync(
                new SimulationCommand(SimulationCommandType.SetInput, input.Name, input.Value),
                cancellationToken);
            ApplySnapshot(snapshot);
        }
    }

    private void ApplySnapshot(SimulationSnapshot snapshot)
    {
        Time = snapshot.Time;
        bool useTraceDocument = !string.IsNullOrWhiteSpace(_traceFilePath);
        if (!useTraceDocument && snapshot.Trace is not null)
        {
            foreach (SignalSample sample in snapshot.Trace)
            {
                AppendWaveformSample(sample.Signal, sample.Value, sample.Time);
            }
        }

        Dictionary<string, SignalViewModel> outputs = Outputs.ToDictionary(static output => output.Name, StringComparer.OrdinalIgnoreCase);
        foreach (SignalSample sample in snapshot.Signals)
        {
            if (outputs.TryGetValue(sample.Signal, out SignalViewModel? output))
            {
                string formattedValue = FormatOutputValue(sample.Value, output.Width);
                output.Value = formattedValue;
                if (!useTraceDocument)
                {
                    AppendWaveformSample(sample.Signal, formattedValue, snapshot.Time);
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
            Status = _worker is null
                ? "Live preview updated."
                : "Live native eval updated.";
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

    private void RefreshTraceState()
    {
        if (string.IsNullOrWhiteSpace(_traceFilePath) || string.IsNullOrWhiteSpace(TopModule))
        {
            _traceDocument = VcdTraceDocument.Empty;
            HierarchyScopeSignals.Clear();
            HierarchyTraceScopeSummaries.Clear();
            OnPropertyChanged(nameof(SelectedHierarchyScopeSummary));
            return;
        }

        _traceDocument = _traceReader.Load(_traceFilePath, TopModule);
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
                    width));
            }
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
            CreateChildScopeInstances(node));
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

        return signals;
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
