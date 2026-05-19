using System.Collections.ObjectModel;
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
    private string _status = "Ready. Open a project to inspect top-level ports.";
    private string _projectName = "No project";
    private string _topModule = "-";
    private string _verilatorVersion = "-";
    private string? _currentProjectPath;
    private ProjectConfiguration? _currentProject;
    private ModuleMetadata? _currentMetadata;
    private string? _currentProjectDirectory;
    private SimulationWorkerClient? _worker;
    private readonly DockPanelViewModel _projectPanel = new(DockPanelKind.Project, "Project");
    private readonly DockPanelViewModel _waveformPanel = new(DockPanelKind.Waveform, "Waveform");
    private SignalViewModel? _selectedSignal;
    private WaveformLaneViewModel? _selectedWaveformLane;
    private DockPanelViewModel? _selectedLeftDockPanel;
    private DockPanelViewModel? _selectedRightDockPanel;
    private DockPanelViewModel? _selectedBottomDockPanel;
    private DockZone _projectDockZone = DockZone.Left;
    private DockZone _waveformDockZone = DockZone.Bottom;
    private DockZone _projectLastVisibleZone = DockZone.Left;
    private DockZone _waveformLastVisibleZone = DockZone.Bottom;
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

    public MainWindowViewModel(BistableWorkspace workspace)
    {
        _workspace = workspace;
        LoadProjectCommand = new AsyncCommand(LoadProjectAsync);
        BuildCommand = new AsyncCommand(BuildAsync);
        EvalCommand = new AsyncCommand(EvaluateAsync);
        TickCommand = new AsyncCommand(TickAsync);
        RunCyclesCommand = new AsyncCommand(RunCyclesAsync);
        ResetCommand = new AsyncCommand(ResetAsync);
        AddSelectedWaveformSignalCommand = new RelayCommand(AddSelectedWaveformSignal);
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
        MoveProjectPaneLeftCommand = new RelayCommand(() => MoveDockPanel(DockPanelKind.Project, DockZone.Left));
        MoveProjectPaneRightCommand = new RelayCommand(() => MoveDockPanel(DockPanelKind.Project, DockZone.Right));
        MoveProjectPaneBottomCommand = new RelayCommand(() => MoveDockPanel(DockPanelKind.Project, DockZone.Bottom));
        HideProjectPaneCommand = new RelayCommand(() => MoveDockPanel(DockPanelKind.Project, DockZone.Hidden));
        MoveWaveformPaneLeftCommand = new RelayCommand(() => MoveDockPanel(DockPanelKind.Waveform, DockZone.Left));
        MoveWaveformPaneRightCommand = new RelayCommand(() => MoveDockPanel(DockPanelKind.Waveform, DockZone.Right));
        MoveWaveformPaneBottomCommand = new RelayCommand(() => MoveDockPanel(DockPanelKind.Waveform, DockZone.Bottom));
        HideWaveformPaneCommand = new RelayCommand(() => MoveDockPanel(DockPanelKind.Waveform, DockZone.Hidden));
        FitWaveformCommand = new RelayCommand(() =>
        {
            WaveformZoom = 1;
            WaveformOffset = 0;
        });
        RebuildDockCollections();
        LoadSamples();
        _ = LoadLayoutStateAsync();
    }

    public ObservableCollection<SignalViewModel> Inputs { get; } = [];

    public ObservableCollection<SignalViewModel> Outputs { get; } = [];

    public ObservableCollection<SignalViewModel> AllSignals { get; } = [];

    public ObservableCollection<SampleProjectViewModel> Samples { get; } = [];

    public ObservableCollection<WaveformLaneViewModel> WaveformLanes { get; } = [];

    public ObservableCollection<string> AvailableClocks { get; } = [];

    public ObservableCollection<DockPanelViewModel> LeftDockPanels { get; } = [];

    public ObservableCollection<DockPanelViewModel> RightDockPanels { get; } = [];

    public ObservableCollection<DockPanelViewModel> BottomDockPanels { get; } = [];

    public ICommand LoadProjectCommand { get; }

    public ICommand BuildCommand { get; }

    public ICommand EvalCommand { get; }

    public ICommand TickCommand { get; }

    public ICommand RunCyclesCommand { get; }

    public ICommand ResetCommand { get; }

    public ICommand AddSelectedWaveformSignalCommand { get; }

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

    public ICommand MoveProjectPaneLeftCommand { get; }

    public ICommand MoveProjectPaneRightCommand { get; }

    public ICommand MoveProjectPaneBottomCommand { get; }

    public ICommand HideProjectPaneCommand { get; }

    public ICommand MoveWaveformPaneLeftCommand { get; }

    public ICommand MoveWaveformPaneRightCommand { get; }

    public ICommand MoveWaveformPaneBottomCommand { get; }

    public ICommand HideWaveformPaneCommand { get; }

    public ICommand FitWaveformCommand { get; }

    public SignalViewModel? SelectedSignal
    {
        get => _selectedSignal;
        set
        {
            if (SetProperty(ref _selectedSignal, value))
            {
                SyncSelectedWaveformLaneFromSignal();
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

    public DockZone ProjectDockZone
    {
        get => _projectDockZone;
        private set
        {
            if (SetProperty(ref _projectDockZone, value))
            {
                if (value != DockZone.Hidden)
                {
                    _projectLastVisibleZone = value;
                }

                _projectPanel.Zone = value;
                RebuildDockCollections();
                OnPropertyChanged(nameof(IsProjectPaneVisible));
                _ = PersistLayoutStateAsync();
            }
        }
    }

    public DockZone WaveformDockZone
    {
        get => _waveformDockZone;
        private set
        {
            if (SetProperty(ref _waveformDockZone, value))
            {
                if (value != DockZone.Hidden)
                {
                    _waveformLastVisibleZone = value;
                }

                _waveformPanel.Zone = value;
                RebuildDockCollections();
                OnPropertyChanged(nameof(IsWaveformPaneVisible));
                _ = PersistLayoutStateAsync();
            }
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
        private set => SetProperty(ref _topModule, value);
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

    public ulong WaveformCursorTime => SelectedWaveformLane?.GetTimeAtOrBefore(WaveformCursorOrder) ?? Time;

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
            WaveformLanes.Clear();
            AvailableClocks.Clear();
            _waveformOrder = 0;
            WaveformOffset = 0;
            WaveformCursorOrder = 0;
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

            foreach (string clockName in ResolveAvailableClocks(result.Project, Inputs))
            {
                AvailableClocks.Add(clockName);
            }

            SelectedClockName = AvailableClocks.FirstOrDefault();

            SelectedWaveformLane = WaveformLanes.FirstOrDefault();
            WaveformCursorOrder = _waveformOrder;

            ProjectName = Path.GetFileName(path);
            TopModule = result.Metadata.Name;
            VerilatorVersion = result.VerilatorVersion;
            _currentProjectPath = path;
            _currentProject = result.Project;
            _currentMetadata = result.Metadata;
            _currentProjectDirectory = result.ProjectDirectory;
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

        Status = "Building native Verilator worker...";
        SimulationWorkerBuildResult build = await _workspace.WorkerBuilder.BuildAsync(
            _currentProject!,
            _currentMetadata!,
            _currentProjectDirectory!,
            cancellationToken);

        await DisposeWorkerAsync();
        _worker = new SimulationWorkerClient(build.ExecutablePath);
        await PushInputsAsync(cancellationToken);
        SimulationSnapshot snapshot = await _worker.SendAsync(new SimulationCommand(SimulationCommandType.Eval), cancellationToken);
        ApplySnapshot(snapshot);
        Status = $"Worker ready: {Path.GetFileName(build.ExecutablePath)}";
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
        string? reset = _currentProject?.Resets.FirstOrDefault()?.Name;
        if (reset is not null)
        {
            int activeLevel = _currentProject?.Resets.FirstOrDefault()?.ActiveLevel ?? 0;
            SetInputValueSilently(reset, activeLevel == 0 ? "1" : "0");
        }

        Status = "Native worker reset.";
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
        if (snapshot.Trace is not null)
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
                AppendWaveformSample(sample.Signal, formattedValue, snapshot.Time);
            }
        }

        WaveformCursorOrder = _waveformOrder;
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

        lane.AppendSample(++_waveformOrder, Time, signal.Value, force: true);
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
            input.Value = value;
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
        _projectLastVisibleZone = _projectDockZone == DockZone.Hidden ? DockZone.Left : _projectDockZone;
        _waveformLastVisibleZone = _waveformDockZone == DockZone.Hidden ? DockZone.Bottom : _waveformDockZone;
        _projectPanel.Zone = _projectDockZone;
        _waveformPanel.Zone = _waveformDockZone;
        RebuildDockCollections();
        OnPropertyChanged(nameof(WaveformZoom));
        OnPropertyChanged(nameof(WaveformOffset));
        OnPropertyChanged(nameof(LeftDockWidth));
        OnPropertyChanged(nameof(RightDockWidth));
        OnPropertyChanged(nameof(BottomDockHeight));
        OnPropertyChanged(nameof(ProjectDockZone));
        OnPropertyChanged(nameof(WaveformDockZone));
        OnPropertyChanged(nameof(IsProjectPaneVisible));
        OnPropertyChanged(nameof(IsWaveformPaneVisible));
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

        SignalViewModel? signal = AllSignals.FirstOrDefault(candidate => string.Equals(candidate.Name, _selectedWaveformLane.Name, StringComparison.OrdinalIgnoreCase));
        if (signal is not null && !ReferenceEquals(signal, SelectedSignal))
        {
            _selectedSignal = signal;
            OnPropertyChanged(nameof(SelectedSignal));
        }
    }

    private Task PersistLayoutStateAsync() =>
        _workspace.LayoutState.SaveAsync(new LayoutState(
            WaveformZoom,
            WaveformOffset,
            LeftDockWidth,
            RightDockWidth,
            BottomDockHeight,
            ProjectDockZone,
            WaveformDockZone));

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
        switch (kind)
        {
            case DockPanelKind.Project:
                ProjectDockZone = zone;
                break;
            case DockPanelKind.Waveform:
                WaveformDockZone = zone;
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

        SelectedLeftDockPanel = LeftDockPanels.FirstOrDefault();
        SelectedRightDockPanel = RightDockPanels.FirstOrDefault();
        SelectedBottomDockPanel = BottomDockPanels.FirstOrDefault();
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
}
