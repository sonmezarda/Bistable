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
    private readonly Dictionary<string, string> _lastWaveformValues = new(StringComparer.OrdinalIgnoreCase);
    private SignalViewModel? _selectedSignal;
    private double _waveformZoom = 1;
    private long _waveformOrder;
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
        FitWaveformCommand = new RelayCommand(() => WaveformZoom = 1);
        LoadSamples();
    }

    public ObservableCollection<SignalViewModel> Inputs { get; } = [];

    public ObservableCollection<SignalViewModel> Outputs { get; } = [];

    public ObservableCollection<SignalViewModel> AllSignals { get; } = [];

    public ObservableCollection<SampleProjectViewModel> Samples { get; } = [];

    public ObservableCollection<WaveformEventViewModel> RecentWaveformEvents { get; } = [];

    public ObservableCollection<SignalViewModel> WaveformSignals { get; } = [];

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

    public ICommand FitWaveformCommand { get; }

    public SignalViewModel? SelectedSignal
    {
        get => _selectedSignal;
        set => SetProperty(ref _selectedSignal, value);
    }

    public double WaveformZoom
    {
        get => _waveformZoom;
        private set => SetProperty(ref _waveformZoom, value);
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
            RecentWaveformEvents.Clear();
            WaveformSignals.Clear();
            _lastWaveformValues.Clear();
            _waveformOrder = 0;
            SelectedSignal = null;

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
        Status = result.Message;
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        if (_worker is null)
        {
            Time++;
            Status = $"Manual UI tick at t={Time}. Build worker for native ticking.";
            return;
        }

        await PushInputsAsync(cancellationToken);
        string? clock = _currentProject?.Clocks.FirstOrDefault()?.Name ?? Inputs.FirstOrDefault(static input => input.Width == 1)?.Name;
        AddWaveformEvent(Time, clock, "1", force: true);
        SimulationSnapshot snapshot = await _worker.SendAsync(new SimulationCommand(SimulationCommandType.Tick, Signal: clock), cancellationToken);
        ApplySnapshot(snapshot);
        SetInputValue(clock, "0", forceWaveformEvent: true);
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
        string? clock = _currentProject?.Clocks.FirstOrDefault()?.Name ?? Inputs.FirstOrDefault(static input => input.Width == 1)?.Name;
        AddWaveformEvent(Time, clock, "1", force: true);
        SimulationSnapshot snapshot = await _worker.SendAsync(new SimulationCommand(SimulationCommandType.RunCycles, Signal: clock, Cycles: 10), cancellationToken);
        ApplySnapshot(snapshot);
        SetInputValue(clock, "0", forceWaveformEvent: true);
        Status = $"Native run pulsed {clock ?? "clock"} for 10 cycles; t={Time}.";
    }

    private async Task ResetAsync(CancellationToken cancellationToken)
    {
        Time = 0;
        if (_worker is null)
        {
            Status = "Session reset.";
            return;
        }

        SimulationSnapshot snapshot = await _worker.SendAsync(new SimulationCommand(SimulationCommandType.Reset), cancellationToken);
        ApplySnapshot(snapshot);
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
            AddWaveformEvent(Time, input.Name, input.Value);
            ApplySnapshot(snapshot);
        }
    }

    private void ApplySnapshot(SimulationSnapshot snapshot)
    {
        Time = snapshot.Time;
        Dictionary<string, SignalViewModel> outputs = Outputs.ToDictionary(static output => output.Name, StringComparer.OrdinalIgnoreCase);
        foreach (SignalSample sample in snapshot.Signals)
        {
            if (outputs.TryGetValue(sample.Signal, out SignalViewModel? output))
            {
                string formattedValue = FormatOutputValue(sample.Value, output.Width);
                output.Value = formattedValue;
                AddWaveformEvent(snapshot.Time, sample.Signal, formattedValue);
            }
        }
    }

    private void AddWaveformEvent(ulong time, string? signal, string value, bool force = false)
    {
        if (string.IsNullOrWhiteSpace(signal))
        {
            return;
        }

        if (!force
            && _lastWaveformValues.TryGetValue(signal, out string? previous)
            && string.Equals(previous, value, StringComparison.Ordinal))
        {
            return;
        }

        _lastWaveformValues[signal] = value;
        RecentWaveformEvents.Insert(0, new WaveformEventViewModel(++_waveformOrder, time, signal, value));
        while (RecentWaveformEvents.Count > 200)
        {
            RecentWaveformEvents.RemoveAt(RecentWaveformEvents.Count - 1);
        }
    }

    private void AddSelectedWaveformSignal()
    {
        if (SelectedSignal is null)
        {
            Status = "Select a signal first.";
            return;
        }

        AddWaveformSignal(SelectedSignal);
        Status = $"Added {SelectedSignal.Name} to waveform.";
    }

    private void RemoveSelectedWaveformSignal()
    {
        if (SelectedSignal is null)
        {
            Status = "Select a signal first.";
            return;
        }

        SelectedSignal.IsInWaveform = false;
        WaveformSignals.Remove(SelectedSignal);
        Status = $"Removed {SelectedSignal.Name} from waveform.";
    }

    private void ClearWaveform()
    {
        foreach (SignalViewModel signal in WaveformSignals)
        {
            signal.IsInWaveform = false;
        }

        WaveformSignals.Clear();
        RecentWaveformEvents.Clear();
        _lastWaveformValues.Clear();
        Status = "Waveform cleared.";
    }

    private void MoveSelectedWaveformSignalUp()
    {
        if (SelectedSignal is null)
        {
            Status = "Select a signal first.";
            return;
        }

        int index = WaveformSignals.IndexOf(SelectedSignal);
        if (index > 0)
        {
            WaveformSignals.Move(index, index - 1);
        }
    }

    private void MoveSelectedWaveformSignalDown()
    {
        if (SelectedSignal is null)
        {
            Status = "Select a signal first.";
            return;
        }

        int index = WaveformSignals.IndexOf(SelectedSignal);
        if (index >= 0 && index < WaveformSignals.Count - 1)
        {
            WaveformSignals.Move(index, index + 1);
        }
    }

    private void AddWaveformSignal(SignalViewModel signal)
    {
        signal.IsInWaveform = true;
        if (!WaveformSignals.Contains(signal))
        {
            WaveformSignals.Add(signal);
        }

        AddWaveformEvent(Time, signal.Name, signal.Value, force: true);
    }

    private void SetInputValue(string? name, string value, bool forceWaveformEvent = false)
    {
        if (name is null)
        {
            return;
        }

        SignalViewModel? input = Inputs.FirstOrDefault(input => string.Equals(input.Name, name, StringComparison.OrdinalIgnoreCase));
        if (input is not null)
        {
            input.Value = value;
            AddWaveformEvent(Time, input.Name, value, forceWaveformEvent);
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
}
