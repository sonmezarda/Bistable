using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Bistable.App.Infrastructure;
using Bistable.App.Services;
using Bistable.Protocol;

namespace Bistable.App.ViewModels;

/// <summary>
/// State for the standalone <c>MemoryViewerWindow</c>: an Excel-style grid of
/// memory cells (rows = base address, columns = column offset) with a configurable
/// columns-per-row, address-jump search, and a Reload button. Wraps a
/// <see cref="LiveProbeService"/> and reads from the worker through it.
/// </summary>
public sealed class MemoryViewerWindowViewModel : INotifyPropertyChanged
{
    private readonly LiveProbeService _liveProbes;

    public MemoryViewerWindowViewModel(LiveProbeService liveProbes, string path, int depth, int cellWidth)
    {
        _liveProbes = liveProbes;
        Path = path;
        Depth = depth;
        CellWidth = cellWidth;
        ReloadCommand = new AsyncCommand(ReloadAsync);
        JumpToAddressCommand = new RelayCommand(JumpToAddress);
        // P2.7-mem-load: the dialog itself is opened by the View layer (it needs
        // a Window handle for Avalonia's StorageProvider). The View raises
        // LoadFromFileRequested → user picks a path → View calls back into
        // LoadFromFileAsync. RelayCommand here just routes the click through.
        LoadFromFileCommand = new RelayCommand(() => LoadFromFileRequested?.Invoke(this, EventArgs.Empty));
        _liveProbes.MemoryUpdated += OnMemoryUpdated;
        _ = ReloadAsync(CancellationToken.None);
    }

    public ICommand LoadFromFileCommand { get; }

    public event EventHandler? LoadFromFileRequested;

    /// <summary>
    /// Selectable input format for the file picker. The View binds a combo box
    /// to <see cref="AvailableFormats"/> + <see cref="SelectedFormat"/> so the
    /// user can switch between $readmemh and $readmemb without renaming files.
    /// </summary>
    public IReadOnlyList<MemoryFileLoader.NumeralBase> AvailableFormats { get; } =
        [MemoryFileLoader.NumeralBase.Hex, MemoryFileLoader.NumeralBase.Bin];

    public MemoryFileLoader.NumeralBase SelectedFormat
    {
        get => _selectedFormat;
        set => SetProperty(ref _selectedFormat, value);
    }
    private MemoryFileLoader.NumeralBase _selectedFormat = MemoryFileLoader.NumeralBase.Hex;

    /// <summary>
    /// Parses the file at <paramref name="filePath"/> and writes every cell to
    /// the worker. Errors land in <see cref="Status"/>. Out-of-range addresses
    /// and parse failures are reported as a count so the user can spot issues
    /// without scrolling through diagnostics. Called by the View after the
    /// user picks a file in the open dialog.
    /// </summary>
    public async Task LoadFromFileAsync(string filePath, CancellationToken cancellationToken)
    {
        if (!_liveProbes.HasWorker)
        {
            Status = "No worker attached.";
            return;
        }
        MemoryFileLoader.MemoryImage image;
        try
        {
            image = MemoryFileLoader.LoadFromFile(filePath, CellWidth, Depth, _selectedFormat);
        }
        catch (Exception ex)
        {
            Status = $"Load failed: {ex.Message}";
            return;
        }

        Status = $"Loading {image.CellCount} cells from {System.IO.Path.GetFileName(filePath)}…";
        int written = 0;
        int failed = 0;
        foreach (MemoryFileLoader.MemoryImageCell cell in image.Cells)
        {
            bool ok = await _liveProbes.WriteMemoryCellAsync(Path, cell.Address, cell.HexValue, cancellationToken);
            if (ok) written++; else failed++;
        }
        await _liveProbes.ReadMemoryAsync(Path, 0, Depth, cancellationToken);

        string errorSuffix = image.Errors > 0 ? $", {image.Errors} parse errors" : string.Empty;
        string failedSuffix = failed > 0 ? $", {failed} writes failed" : string.Empty;
        Status = $"Loaded {written} cells from {System.IO.Path.GetFileName(filePath)}{errorSuffix}{failedSuffix}.";
    }

    public string Path { get; }
    public int Depth { get; }
    public int CellWidth { get; }
    public string Title => $"Memory Viewer — {Path}";
    public string Subtitle => $"{Depth} cells × {CellWidth}b";

    // ── Grid layout (Excel-style) ─────────────────────────────────────────
    // Columns per row: configurable 4/8/16/32. Default 16, matches typical hex editors.
    public IReadOnlyList<int> ColumnsPerRowOptions { get; } = [4, 8, 16, 32];

    public int ColumnsPerRow
    {
        get => _columnsPerRow;
        set
        {
            if (SetProperty(ref _columnsPerRow, Math.Max(1, value)))
            {
                RebuildRows();
                OnPropertyChanged(nameof(ColumnHeaders));
            }
        }
    }
    private int _columnsPerRow = 16;

    public ObservableCollection<MemoryRowViewModel> Rows { get; } = [];

    /// <summary>Column header labels "0x00", "0x01"… so the grid header row reads like a hex editor.</summary>
    public IReadOnlyList<string> ColumnHeaders
    {
        get
        {
            string[] labels = new string[_columnsPerRow];
            for (int i = 0; i < _columnsPerRow; i++) labels[i] = $"0x{i:X2}";
            return labels;
        }
    }

    /// <summary>Jump-to-address textbox content (decimal or 0x hex).</summary>
    public string JumpAddressText
    {
        get => _jumpAddressText;
        set => SetProperty(ref _jumpAddressText, value);
    }
    private string _jumpAddressText = string.Empty;

    /// <summary>Currently highlighted address (from Jump).</summary>
    public ulong? HighlightedAddress
    {
        get => _highlightedAddress;
        private set => SetProperty(ref _highlightedAddress, value);
    }
    private ulong? _highlightedAddress;

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }
    private string _status = "Ready.";

    public ICommand ReloadCommand { get; }
    public ICommand JumpToAddressCommand { get; }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        Status = "Reading…";
        if (!_liveProbes.HasWorker)
        {
            Status = "No worker attached.";
            return;
        }
        MemorySnapshot? snap = await _liveProbes.ReadMemoryAsync(Path, 0, Math.Max(1, Depth), cancellationToken);
        if (snap is null)
        {
            Status = "Read failed.";
            return;
        }
        ApplySnapshot(snap);
        Status = $"Loaded {snap.Cells.Count} cells.";
    }

    private void OnMemoryUpdated(object? sender, MemorySnapshotUpdatedEventArgs e)
    {
        if (string.Equals(e.Path, Path, StringComparison.OrdinalIgnoreCase))
        {
            ApplySnapshot(e.Snapshot);
        }
    }

    private void ApplySnapshot(MemorySnapshot snap)
    {
        // Build cell array keyed by absolute address — easier to index when we
        // re-shape rows on ColumnsPerRow changes.
        Dictionary<ulong, string> byAddress = new();
        for (int i = 0; i < snap.Cells.Count; i++)
        {
            byAddress[snap.StartAddress + (ulong)i] = snap.Cells[i];
        }
        _cells = byAddress;
        RebuildRows();
    }

    private Dictionary<ulong, string> _cells = new();

    private void RebuildRows()
    {
        Rows.Clear();
        if (_cells.Count == 0) return;

        // Address range to display = full depth (we always reload the whole memory).
        ulong rowSpan = (ulong)_columnsPerRow;
        ulong totalDepth = (ulong)Depth;
        for (ulong baseAddr = 0; baseAddr < totalDepth; baseAddr += rowSpan)
        {
            List<MemoryCellEditViewModel> cells = new(_columnsPerRow);
            for (int c = 0; c < _columnsPerRow; c++)
            {
                ulong abs = baseAddr + (ulong)c;
                if (abs >= totalDepth) break;
                string value = _cells.TryGetValue(abs, out string? v) ? v : "—";
                cells.Add(new MemoryCellEditViewModel(this, abs, value));
            }
            Rows.Add(new MemoryRowViewModel(baseAddr, cells));
        }
    }

    /// <summary>
    /// Write a single cell back to the worker. Called by
    /// <see cref="MemoryCellEditViewModel.CommitAsync"/> on Enter / focus loss.
    /// Errors surface in the <see cref="Status"/> line.
    /// </summary>
    internal async Task WriteCellAsync(ulong address, string value, CancellationToken cancellationToken)
    {
        if (!_liveProbes.HasWorker)
        {
            Status = "No worker attached.";
            return;
        }
        string trimmed = value.Trim();
        bool ok = await _liveProbes.WriteMemoryCellAsync(Path, address, trimmed, cancellationToken);
        if (!ok)
        {
            Status = $"Write failed at 0x{address:X}.";
            return;
        }
        Status = $"Wrote {trimmed} → 0x{address:X}.";
        await _liveProbes.ReadMemoryAsync(Path, 0, Depth, cancellationToken);
    }

    private void JumpToAddress()
    {
        if (!TryParseAddress(_jumpAddressText, out ulong addr))
        {
            Status = $"Invalid address '{_jumpAddressText}'.";
            return;
        }
        if (addr >= (ulong)Depth)
        {
            Status = $"Address out of range (depth = {Depth}).";
            return;
        }
        HighlightedAddress = addr;
        Status = $"Jumped to 0x{addr:X}.";
    }

    private static bool TryParseAddress(string text, out ulong value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return ulong.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out value);
        }
        return ulong.TryParse(text, out value);
    }

    public void Detach() => _liveProbes.MemoryUpdated -= OnMemoryUpdated;

    public event PropertyChangedEventHandler? PropertyChanged;
    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One row of the memory grid: the base address + the cells in this row.</summary>
public sealed class MemoryRowViewModel
{
    public MemoryRowViewModel(ulong baseAddress, IReadOnlyList<MemoryCellEditViewModel> cells)
    {
        BaseAddress = baseAddress;
        Cells = cells;
    }
    public ulong BaseAddress { get; }
    public string BaseAddressLabel => $"0x{BaseAddress:X4}";
    public IReadOnlyList<MemoryCellEditViewModel> Cells { get; }
}

/// <summary>
/// One editable cell of the memory grid. Two-way bound to its TextBox: when
/// the user commits a new hex value (Enter or focus loss) <see cref="CommitAsync"/>
/// fires <see cref="SimulationWorkerClient.WriteMemoryAsync"/> through the
/// owning <see cref="MemoryViewerWindowViewModel"/>.
/// </summary>
public sealed class MemoryCellEditViewModel : INotifyPropertyChanged
{
    private readonly MemoryViewerWindowViewModel _owner;

    public MemoryCellEditViewModel(MemoryViewerWindowViewModel owner, ulong address, string initialValue)
    {
        _owner = owner;
        Address = address;
        _hexValue = initialValue;
    }

    public ulong Address { get; }

    public string HexValue
    {
        get => _hexValue;
        set
        {
            // Don't fire write on every keystroke — only when the displayed
            // value differs after editing. The view also calls Commit() on
            // Enter / LostFocus, which is the explicit save path.
            if (_hexValue == value) return;
            _hexValue = value;
            OnPropertyChanged();
        }
    }
    private string _hexValue;

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_hexValue)) return;
        await _owner.WriteCellAsync(Address, _hexValue, cancellationToken);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
