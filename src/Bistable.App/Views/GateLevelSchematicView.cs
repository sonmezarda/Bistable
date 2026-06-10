using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Bistable.App.Services;
using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;
using Bistable.Core.Projects;
using Bistable.Core.Synthesis;

namespace Bistable.App.Views;

// Phase 6.5 Wave 2: hierarchical gate-level schematic viewer. Header strip
// + breadcrumb + canvas. Double-clicking a sub-module instance pushes a new
// scope; clicking a breadcrumb segment pops back to that scope.
public sealed class GateLevelSchematicView : UserControl, IAsyncDisposable
{
    private static readonly IBrush BackgroundBrush = SolidColorBrush.Parse("#0e141c");
    private static readonly IBrush SurfaceBrush    = SolidColorBrush.Parse("#1b2230");
    private static readonly IBrush StrokeBrush     = SolidColorBrush.Parse("#344157");
    private static readonly IBrush AccentBrush     = SolidColorBrush.Parse("#5dbcff");
    private static readonly IBrush TextBrush       = SolidColorBrush.Parse("#d7dde8");
    private static readonly IBrush MutedBrush      = SolidColorBrush.Parse("#8f9aad");
    private static readonly IBrush BreadcrumbActive = SolidColorBrush.Parse("#ffd166");

    private readonly GateNetlist _netlist;
    private readonly RoutingQuality _requestedRoutingQuality;
    private readonly bool _autoDowngradeLargeGraphs;
    private readonly GateSchematicCanvas _canvas = new();
    private readonly SchematicLayoutService _layoutService = new();
    private readonly List<string> _scopePath = new();
    private readonly HashSet<string> _expandedInstancePaths = new(StringComparer.Ordinal);
    private readonly Border _routingOverlay;
    private readonly TextBlock _routingOverlayText = new()
    {
        Text = "Routing schematic...",
        Foreground = TextBrush,
        FontSize = 13,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly Button _routingCancelButton = new()
    {
        Content = "Cancel",
        FontSize = 11,
        Padding = new Thickness(10, 3),
        Background = SurfaceBrush,
        Foreground = TextBrush,
        BorderBrush = BreadcrumbActive,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(3),
    };
    private readonly StackPanel _breadcrumbStrip = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 4,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _headerStats = new()
    {
        Foreground = MutedBrush,
        FontSize = 11,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _selectionStatus = new()
    {
        Foreground = BreadcrumbActive,
        FontSize = 11,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly StackPanel _propertiesStack = new()
    {
        Orientation = Orientation.Vertical,
        Spacing = 6,
    };
    private readonly TextBox _searchBox = new()
    {
        Watermark = "Ctrl+F search cells/nets",
        FontSize = 11,
    };
    private readonly ListBox _searchResults = new()
    {
        Height = 170,
        Background = SolidColorBrush.Parse("#10141b"),
        Foreground = TextBrush,
    };
    private GateModule? _currentScopeModule;
    private CancellationTokenSource? _activeLayoutCts;
    private int _layoutGeneration;
    private bool _isInitialized;
    private bool _isDisposed;
    private MainWindowViewModel? _observedViewModel;
    private GatePinLabelDisplayOptions _pinLabelOptions;
    private readonly ComboBox _pinLabelModeBox = new();
    private readonly CheckBox _groupBusPinLabelsCheckBox = new();
    private readonly NumericUpDown _compactZoomEditor = new();
    private readonly NumericUpDown _detailedZoomEditor = new();
    private bool _updatingPinControls;

    public event EventHandler<GateScopeOpenRequestedEventArgs>? ScopeOpenRequested;

    public event EventHandler<string>? ScopeTitleChanged;

    public GateLevelSchematicView(GateNetlist netlist)
        : this(netlist, new SchematicConfiguration())
    {
    }

    public GateLevelSchematicView(GateNetlist netlist, RoutingQuality routingQuality)
        : this(netlist, new SchematicConfiguration(RoutingQuality: routingQuality))
    {
    }

    public GateLevelSchematicView(
        GateNetlist netlist,
        RoutingQuality routingQuality,
        bool autoDowngradeLargeGraphs,
        IReadOnlyList<string>? initialScopePath = null)
        : this(
            netlist,
            new SchematicConfiguration(
                RoutingQuality: routingQuality,
                AutoDowngradeLargeGraphs: autoDowngradeLargeGraphs),
            initialScopePath)
    {
    }

    public GateLevelSchematicView(
        GateNetlist netlist,
        SchematicConfiguration schematicSettings,
        IReadOnlyList<string>? initialScopePath = null)
    {
        _netlist = netlist;
        _requestedRoutingQuality = schematicSettings.RoutingQuality;
        _autoDowngradeLargeGraphs = schematicSettings.AutoDowngradeLargeGraphs;
        _pinLabelOptions = ToPinLabelOptions(schematicSettings);
        Background = BackgroundBrush;
        _routingOverlay = BuildRoutingOverlay();
        Content = BuildLayout();
        _scopePath.AddRange(initialScopePath is { Count: > 0 }
            ? initialScopePath
            : [netlist.TopModule]);
        _layoutService.LayoutStillRunning += OnLayoutStillRunning;
        _routingCancelButton.Click += (_, _) => CancelActiveLayout();
        _canvas.SubModuleActivated += OnSubModuleActivated;
        _canvas.SubModuleExpansionToggled += OnSubModuleExpansionToggled;
        _canvas.NetSelected += OnNetSelected;
        _canvas.CellSelected += OnCellSelected;
        _canvas.BundleSelected += OnBundleSelected;
        _searchBox.TextChanged += (_, _) => RefreshSearchResults();
        _searchResults.SelectionChanged += OnSearchResultSelected;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DataContextChanged += OnDataContextChanged;
        _canvas.SetPinLabelOptions(_pinLabelOptions);
    }

    private Control BuildLayout()
    {
        Grid shell = new();
        DockPanel root = new() { LastChildFill = true };
        root.Children.Add(BuildHeader());
        root.Children.Add(BuildBreadcrumb());
        root.Children.Add(BuildToolbar());
        root.Children.Add(BuildPropertiesPanel());
        root.Children.Add(_canvas);
        shell.Children.Add(root);
        shell.Children.Add(_routingOverlay);
        return shell;
    }

    private Border BuildRoutingOverlay()
    {
        StackPanel card = new()
        {
            Orientation = Orientation.Vertical,
            Spacing = 10,
            MaxWidth = 360,
        };
        card.Children.Add(new TextBlock
        {
            Text = "Gate-level schematic",
            Foreground = AccentBrush,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
        });
        card.Children.Add(_routingOverlayText);
        card.Children.Add(_routingCancelButton);

        Border cardBorder = new()
        {
            Background = SurfaceBrush,
            BorderBrush = StrokeBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(18, 14),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = card,
        };

        return new Border
        {
            IsVisible = false,
            Background = new SolidColorBrush(Color.FromArgb(150, 14, 20, 28)),
            Child = cardBorder,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(16),
        };
    }

    private Control BuildHeader()
    {
        Border header = new()
        {
            Background = SurfaceBrush,
            BorderBrush = StrokeBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(14, 10),
        };
        DockPanel.SetDock(header, Avalonia.Controls.Dock.Top);
        StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 16 };
        row.Children.Add(new TextBlock
        {
            Text = _netlist.TopModule,
            Foreground = AccentBrush,
            FontWeight = FontWeight.SemiBold,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(_headerStats);
        header.Child = row;
        return header;
    }

    private Control BuildBreadcrumb()
    {
        Border bar = new()
        {
            Background = SurfaceBrush,
            BorderBrush = StrokeBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 4),
        };
        DockPanel.SetDock(bar, Avalonia.Controls.Dock.Top);

        StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new TextBlock
        {
            Text = "Scope:",
            Foreground = MutedBrush,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(_breadcrumbStrip);
        bar.Child = row;
        return bar;
    }

    private Control BuildPropertiesPanel()
    {
        Border panel = new()
        {
            Width = 280,
            Background = SurfaceBrush,
            BorderBrush = StrokeBrush,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Padding = new Thickness(12, 10),
        };
        DockPanel.SetDock(panel, Avalonia.Controls.Dock.Right);

        DockPanel body = new() { LastChildFill = true };
        StackPanel search = new() { Orientation = Orientation.Vertical, Spacing = 6 };
        search.Children.Add(new TextBlock
        {
            Text = "Search",
            Foreground = AccentBrush,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
        });
        search.Children.Add(_searchBox);
        search.Children.Add(_searchResults);
        DockPanel.SetDock(search, Avalonia.Controls.Dock.Top);
        body.Children.Add(search);

        ScrollViewer scroll = new()
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = _propertiesStack,
        };
        body.Children.Add(scroll);
        panel.Child = body;
        RenderNoSelection();
        return panel;
    }

    private Control BuildToolbar()
    {
        Border bar = new()
        {
            Background = SurfaceBrush,
            BorderBrush = StrokeBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 6),
        };
        DockPanel.SetDock(bar, Avalonia.Controls.Dock.Top);

        StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(MiniButton("Fit (F)",   () => _canvas.FitToView()));
        row.Children.Add(MiniButton("Reset (R)", () => _canvas.ResetView()));
        row.Children.Add(MiniButton("Up",        PopScope));
        row.Children.Add(BuildPinSettingsButton());
        row.Children.Add(new TextBlock
        {
            Text = "Simulation:",
            Foreground = MutedBrush,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        });
        row.Children.Add(new ComboBox
        {
            Width = 92,
            MinHeight = 26,
            FontSize = 10,
            Background = SurfaceBrush,
            Foreground = TextBrush,
            BorderBrush = StrokeBrush,
            [!ItemsControl.ItemsSourceProperty] = new Binding("AvailableSimulationTargets"),
            [!SelectingItemsControl.SelectedItemProperty] = new Binding(
                "SelectedSimulationTarget",
                BindingMode.TwoWay),
        });
        row.Children.Add(BoundMiniButton("Eval", "EvalCommand"));
        row.Children.Add(BoundMiniButton("Tick", "TickCommand"));
        row.Children.Add(BoundMiniButton("Reset", "ResetCommand"));
        row.Children.Add(BoundMiniButton("Compare", "CompareRtlAndGateCommand"));
        row.Children.Add(_selectionStatus);
        row.Children.Add(new TextBlock
        {
            Text = "  · + expands in place · double-click drills in · click wire to highlight · middle-drag pan · Ctrl+wheel zoom",
            Foreground = MutedBrush,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
        });
        bar.Child = row;
        return bar;
    }

    private Button BuildPinSettingsButton()
    {
        ConfigurePinSettingsControls();
        StackPanel content = new()
        {
            Orientation = Orientation.Vertical,
            Spacing = 8,
            Width = 290,
            Children =
            {
                new TextBlock
                {
                    Text = "Pin labels",
                    Foreground = AccentBrush,
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 12,
                },
                new TextBlock
                {
                    Text = "Bus grouping affects labels only; bit-level connectivity and net selection stay intact.",
                    Foreground = MutedBrush,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                },
                _pinLabelModeBox,
                _groupBusPinLabelsCheckBox,
                BuildThresholdRow("Compact zoom", _compactZoomEditor),
                BuildThresholdRow("Detailed zoom", _detailedZoomEditor),
            },
        };
        Border flyoutSurface = new()
        {
            Background = SurfaceBrush,
            BorderBrush = StrokeBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(12),
            Child = content,
        };
        Button button = MiniButton("Pins", () => { });
        button.Flyout = new Flyout { Content = flyoutSurface };
        return button;
    }

    private void ConfigurePinSettingsControls()
    {
        _pinLabelModeBox.ItemsSource = Enum.GetValues<GatePinLabelMode>();
        _pinLabelModeBox.SelectedItem = _pinLabelOptions.Mode;
        _pinLabelModeBox.MinHeight = 28;
        _pinLabelModeBox.Background = SurfaceBrush;
        _pinLabelModeBox.Foreground = TextBrush;
        _pinLabelModeBox.BorderBrush = StrokeBrush;
        _pinLabelModeBox.SelectionChanged += (_, _) => ApplyPinSettingsFromControls();

        _groupBusPinLabelsCheckBox.Content = "Group bus labels";
        _groupBusPinLabelsCheckBox.IsChecked = _pinLabelOptions.GroupBusPinLabels;
        _groupBusPinLabelsCheckBox.Foreground = TextBrush;
        _groupBusPinLabelsCheckBox.IsCheckedChanged += (_, _) => ApplyPinSettingsFromControls();

        ConfigureZoomEditor(_compactZoomEditor, _pinLabelOptions.CompactZoom);
        ConfigureZoomEditor(_detailedZoomEditor, _pinLabelOptions.DetailedZoom);
        _compactZoomEditor.ValueChanged += (_, _) => ApplyPinSettingsFromControls();
        _detailedZoomEditor.ValueChanged += (_, _) => ApplyPinSettingsFromControls();
    }

    private static void ConfigureZoomEditor(NumericUpDown editor, double value)
    {
        editor.Minimum = 0.05m;
        editor.Maximum = 8m;
        editor.Increment = 0.05m;
        editor.FormatString = "0.00";
        editor.Value = (decimal)value;
        editor.Width = 90;
        editor.MinHeight = 28;
        editor.Background = SurfaceBrush;
        editor.Foreground = TextBrush;
        editor.BorderBrush = StrokeBrush;
    }

    private static Grid BuildThresholdRow(string label, NumericUpDown editor)
    {
        Grid row = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        row.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = MutedBrush,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(editor, 1);
        row.Children.Add(editor);
        return row;
    }

    private void ApplyPinSettingsFromControls()
    {
        if (_updatingPinControls) return;

        GatePinLabelMode mode = _pinLabelModeBox.SelectedItem is GatePinLabelMode selected
            ? selected
            : GatePinLabelMode.Automatic;
        GatePinLabelDisplayOptions options = new(
            mode,
            _groupBusPinLabelsCheckBox.IsChecked == true,
            (double)(_compactZoomEditor.Value ?? 0.55m),
            (double)(_detailedZoomEditor.Value ?? 0.9m));
        ApplyPinLabelOptions(options);

        if (_observedViewModel is { } viewModel)
        {
            viewModel.GatePinLabelMode = options.Mode;
            viewModel.GateGroupBusPinLabels = options.GroupBusPinLabels;
            viewModel.GatePinLabelCompactZoom = options.CompactZoom;
            viewModel.GatePinLabelDetailedZoom = options.DetailedZoom;
        }
    }

    private void ApplyPinLabelOptions(GatePinLabelDisplayOptions options)
    {
        _pinLabelOptions = options.Normalize();
        _canvas.SetPinLabelOptions(_pinLabelOptions);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _observedViewModel = DataContext as MainWindowViewModel;
        if (_observedViewModel is null) return;
        _observedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ApplySettingsFromViewModel();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.GatePinLabelMode)
            or nameof(MainWindowViewModel.GateGroupBusPinLabels)
            or nameof(MainWindowViewModel.GatePinLabelCompactZoom)
            or nameof(MainWindowViewModel.GatePinLabelDetailedZoom)
            or nameof(MainWindowViewModel.GateSchematicSettings))
        {
            ApplySettingsFromViewModel();
        }
    }

    private void ApplySettingsFromViewModel()
    {
        if (_observedViewModel is null) return;
        ApplyPinLabelOptions(ToPinLabelOptions(_observedViewModel.GateSchematicSettings));
        _updatingPinControls = true;
        try
        {
            _pinLabelModeBox.SelectedItem = _pinLabelOptions.Mode;
            _groupBusPinLabelsCheckBox.IsChecked = _pinLabelOptions.GroupBusPinLabels;
            _compactZoomEditor.Value = (decimal)_pinLabelOptions.CompactZoom;
            _detailedZoomEditor.Value = (decimal)_pinLabelOptions.DetailedZoom;
        }
        finally
        {
            _updatingPinControls = false;
        }
    }

    private static GatePinLabelDisplayOptions ToPinLabelOptions(SchematicConfiguration settings) =>
        new GatePinLabelDisplayOptions(
            settings.GatePinLabelMode,
            settings.GroupGateBusPinLabels,
            settings.GatePinLabelCompactZoom,
            settings.GatePinLabelDetailedZoom).Normalize();

    private Button MiniButton(string content, Action onClick)
    {
        Button b = new()
        {
            Content = content,
            FontSize = 11,
            Padding = new Thickness(8, 2),
            Background = SurfaceBrush,
            Foreground = TextBrush,
            BorderBrush = StrokeBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    private static Button BoundMiniButton(string content, string commandPath) => new()
    {
        Content = content,
        FontSize = 10,
        Padding = new Thickness(7, 2),
        Background = SurfaceBrush,
        Foreground = TextBrush,
        BorderBrush = StrokeBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(3),
        [!Button.CommandProperty] = new Binding(commandPath),
    };

    // ── Scope navigation ──────────────────────────────────────────────────

    private void OnSubModuleActivated(object? sender, string instanceName)
    {
        string[] targetPath =
        [
            .. _scopePath,
            .. instanceName.Split('/', StringSplitOptions.RemoveEmptyEntries),
        ];
        GateScopeOpenRequestedEventArgs request = new(targetPath);
        ScopeOpenRequested?.Invoke(this, request);
        if (request.Handled)
        {
            return;
        }

        foreach (string segment in instanceName.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            _scopePath.Add(segment);
        }
        _expandedInstancePaths.Clear();
        LoadCurrentScope();
    }

    private void OnSubModuleExpansionToggled(object? sender, string instancePath)
    {
        if (!_expandedInstancePaths.Add(instancePath))
        {
            _expandedInstancePaths.Remove(instancePath);
            RemoveDescendantExpansions(instancePath);
        }
        LoadCurrentScope();
    }

    private void RemoveDescendantExpansions(string instancePath)
    {
        string prefix = instancePath + "/";
        _expandedInstancePaths.RemoveWhere(path => path.StartsWith(prefix, StringComparison.Ordinal));
    }

    private void OnNetSelected(object? sender, GateNetSelection? selection)
    {
        _selectionStatus.Text = selection is null
            ? string.Empty
            : $"Selected net: {(string.IsNullOrWhiteSpace(selection.NetName) ? "net" + selection.NetId : selection.NetName)} (net{selection.NetId})";
        if (selection is not null)
        {
            RenderNetSelection(selection);
        }
    }

    private void OnBundleSelected(object? sender, GateBusBundleSelection? selection)
    {
        if (selection is null) return;
        GateBusBundle bundle = selection.Bundle;
        string range = bundle.Msb == bundle.Lsb
            ? bundle.LogicalName
            : $"{bundle.LogicalName}[{bundle.Msb}:{bundle.Lsb}]";
        _selectionStatus.Text = $"Selected bus: {range} ({bundle.Members.Count} bits)";
        RenderBundleSelection(bundle);
    }

    private void OnCellSelected(object? sender, GateCellSelection? selection)
    {
        if (selection is null)
        {
            RenderNoSelection();
            return;
        }

        GateCell cell = selection.Cell;
        _selectionStatus.Text = $"Selected cell: {cell.Name}";
        RenderCellProperties(cell);
    }

    private void PopScope()
    {
        if (_scopePath.Count <= 1) return;
        _scopePath.RemoveAt(_scopePath.Count - 1);
        _expandedInstancePaths.Clear();
        LoadCurrentScope();
    }

    private void JumpToDepth(int depth)
    {
        if (depth < 0 || depth >= _scopePath.Count) return;
        while (_scopePath.Count > depth + 1)
        {
            _scopePath.RemoveAt(_scopePath.Count - 1);
        }
        _expandedInstancePaths.Clear();
        LoadCurrentScope();
    }

    private void RebuildBreadcrumb()
    {
        _breadcrumbStrip.Children.Clear();
        for (int i = 0; i < _scopePath.Count; i++)
        {
            if (i > 0)
            {
                _breadcrumbStrip.Children.Add(new TextBlock
                {
                    Text = "/",
                    Foreground = MutedBrush,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }
            int depth = i;
            bool isLast = i == _scopePath.Count - 1;
            Button seg = new()
            {
                Content = _scopePath[i],
                Foreground = isLast ? BreadcrumbActive : TextBrush,
                Background = isLast ? SurfaceBrush : Brushes.Transparent,
                BorderBrush = StrokeBrush,
                BorderThickness = new Thickness(isLast ? 1 : 0),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 2),
                FontSize = 11,
                FontWeight = isLast ? FontWeight.SemiBold : FontWeight.Normal,
            };
            seg.Click += (_, _) => JumpToDepth(depth);
            _breadcrumbStrip.Children.Add(seg);
        }
    }

    private void LoadCurrentScope()
    {
        CancelActiveLayout();

        _activeLayoutCts?.Dispose();
        _activeLayoutCts = new CancellationTokenSource();
        int generation = ++_layoutGeneration;
        _ = LoadCurrentScopeAsync(generation, _activeLayoutCts);
    }

    private async Task LoadCurrentScopeAsync(int generation, CancellationTokenSource layoutCts)
    {
        using CancellationTokenSource overlayCts =
            CancellationTokenSource.CreateLinkedTokenSource(layoutCts.Token);
        Task overlayTask = ShowRoutingOverlayAfterDelayAsync(generation, overlayCts.Token);

        try
        {
            string[] scopeSnapshot = [.. _scopePath];
            HashSet<string> expandedSnapshot = new(_expandedInstancePaths, StringComparer.Ordinal);
            PendingScopeLayout pending = await Task.Run(
                () => BuildPendingScopeLayout(scopeSnapshot, expandedSnapshot),
                layoutCts.Token);

            ElkGraph laid = await _layoutService.LayoutAsync(pending.Graph, layoutCts.Token);
            if (!IsCurrentLayout(generation) || layoutCts.IsCancellationRequested)
            {
                return;
            }

            ApplyLaidOutScope(laid, pending);
        }
        catch (OperationCanceledException) when (layoutCts.IsCancellationRequested || !IsCurrentLayout(generation))
        {
            // User-triggered cancel or superseded request. Keep the previous
            // successful schematic visible.
        }
        catch (SchematicRoutingException ex)
        {
            if (IsCurrentLayout(generation))
            {
                _selectionStatus.Text = "Layout failed: " + ex.Message;
            }
        }
        catch (System.InvalidOperationException ex)
        {
            if (IsCurrentLayout(generation))
            {
                _selectionStatus.Text = "Scope resolve failed: " + ex.Message;
            }
        }
        finally
        {
            overlayCts.Cancel();
            await ObserveOverlayTaskAsync(overlayTask);
            if (IsCurrentLayout(generation))
            {
                HideRoutingOverlay();
                if (ReferenceEquals(_activeLayoutCts, layoutCts))
                {
                    _activeLayoutCts = null;
                }
            }
            layoutCts.Dispose();
        }
    }

    private PendingScopeLayout BuildPendingScopeLayout(
        IReadOnlyList<string> scopePath,
        IReadOnlySet<string> expandedInstancePaths)
    {
        GateNetlistElkBuildResult build = GateNetlistElkBuilder.BuildScope(
            _netlist,
            scopePath,
            expandedInstancePaths,
            ElkLayoutOptionsFactory.For(_requestedRoutingQuality));

        SchematicGraphMetrics metrics = SchematicGraphMetrics.Measure(build.Graph);
        if (metrics.ExceedsMonolithicRoutingLimit)
        {
            throw new SchematicRoutingException(
                $"Scope is too large for monolithic routing "
                + $"({metrics.NodeCount} nodes, {metrics.PortCount} ports, {metrics.EdgeCount} edges). "
                + "Re-synthesize to regenerate the hierarchy-preserving schematic artifact.");
        }

        SchematicLayoutDecision decision = SchematicRoutingQualityResolver.Resolve(
            _requestedRoutingQuality,
            _autoDowngradeLargeGraphs,
            metrics);
        if (decision.AutoDowngraded)
        {
            build.Graph.LayoutOptions = ElkLayoutOptionsFactory.For(decision.EffectiveQuality).ToElkOptions();
        }

        return new PendingScopeLayout(
            build.Graph,
            ResolveScopeModule(scopePath),
            [.. scopePath],
            decision,
            metrics,
            build.Bundles);
    }

    private void ApplyLaidOutScope(ElkGraph laid, PendingScopeLayout pending)
    {
        GateModule scopeModule = pending.ScopeModule;
        _currentScopeModule = scopeModule;
        _canvas.SetGraph(laid, scopeModule, pending.Bundles);
        RenderNoSelection();
        _selectionStatus.Text = pending.Decision.AutoDowngraded
            ? $"Auto-switched to Fast preview "
                + $"({pending.Metrics.NodeCount} nodes, {pending.Metrics.PortCount} ports, "
                + $"{pending.Metrics.EdgeCount} edges)."
            : string.Empty;
        RefreshSearchResults();
        _headerStats.Text = $"{scopeModule.Cells.Count} cells · {scopeModule.Nets.Count} named nets · {scopeModule.Ports.Count} ports";
        ScopeTitleChanged?.Invoke(this, BuildScopeTitle(pending.ScopePath));
        RebuildBreadcrumb();
    }

    public static string BuildScopeTitle(IReadOnlyList<string> scopePath) =>
        "Gate: " + string.Join(" / ", scopePath);

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_isInitialized || _isDisposed)
        {
            return;
        }

        _isInitialized = true;
        LoadCurrentScope();
        _canvas.Focus();
    }

    private async Task ShowRoutingOverlayAfterDelayAsync(int generation, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            if (IsCurrentLayout(generation) && !cancellationToken.IsCancellationRequested)
            {
                ShowRoutingOverlay("Routing schematic...");
            }
        }
        catch (OperationCanceledException)
        {
            // Fast layouts complete before the overlay threshold; avoid flash.
        }
    }

    private void ShowRoutingOverlay(string message)
    {
        _routingOverlayText.Text = message;
        _routingOverlay.IsVisible = true;
    }

    private void HideRoutingOverlay()
    {
        _routingOverlay.IsVisible = false;
        _routingOverlayText.Text = "Routing schematic...";
    }

    private void CancelActiveLayout()
    {
        _activeLayoutCts?.Cancel();
    }

    private bool IsCurrentLayout(int generation) => generation == _layoutGeneration;

    private void OnLayoutStillRunning(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_activeLayoutCts is null || _activeLayoutCts.IsCancellationRequested || !_routingOverlay.IsVisible)
            {
                return;
            }

            _routingOverlayText.Text = "Routing is taking longer than usual. You can keep waiting or cancel.";
        });
    }

    private static async Task ObserveOverlayTaskAsync(Task overlayTask)
    {
        try
        {
            await overlayTask;
        }
        catch (OperationCanceledException)
        {
            // Expected for quick or cancelled layouts.
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _searchBox.Focus();
            _searchBox.SelectAll();
            e.Handled = true;
        }
    }

    private void RefreshSearchResults()
    {
        string query = (_searchBox.Text ?? string.Empty).Trim();
        if (_currentScopeModule is null || query.Length == 0)
        {
            _searchResults.ItemsSource = Array.Empty<GateSearchResult>();
            return;
        }

        List<GateSearchResult> results = [];
        foreach (GateCell cell in _currentScopeModule.Cells)
        {
            if (cell.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || cell.Type.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(GateSearchResult.ForCell(cell));
            }
        }

        foreach (GateNet net in _currentScopeModule.Nets)
        {
            if (!net.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            GateBit[] netBits = [.. net.Bits.Where(static bit => bit.Kind == BitKind.Net)];
            if (netBits.Length > 0)
            {
                results.Add(GateSearchResult.ForNet(net.Name, netBits[0].NetId));
            }
        }

        _searchResults.ItemsSource = results
            .OrderBy(static r => r.Kind)
            .ThenBy(static r => r.Label, StringComparer.OrdinalIgnoreCase)
            .Take(80)
            .ToList();
    }

    private void OnSearchResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_searchResults.SelectedItem is not GateSearchResult result) return;

        if (result.Cell is { } cell)
        {
            _canvas.HighlightNet(null);
            _canvas.SelectCell(cell.Name);
            _canvas.CenterOnCell(cell.Name);
            RenderCellProperties(cell);
            _selectionStatus.Text = $"Selected cell: {cell.Name}";
            return;
        }

        if (result.NetId is { } netId)
        {
            _canvas.SelectCell(null);
            _canvas.HighlightNet(netId);
            _canvas.CenterOnNet(netId);
            GateNetSelection selection = new(netId, result.Label);
            RenderNetSelection(selection);
            _selectionStatus.Text = $"Selected net: {result.Label} (net{netId})";
        }
    }

    private void RenderNoSelection()
    {
        _propertiesStack.Children.Clear();
        _propertiesStack.Children.Add(PanelTitle("Properties"));
        _propertiesStack.Children.Add(MutedText("Click a gate, sub-module, or wire to inspect it."));
    }

    private void RenderNetSelection(GateNetSelection selection)
    {
        _propertiesStack.Children.Clear();
        _propertiesStack.Children.Add(PanelTitle("Net"));
        AddProperty("ID", "net" + selection.NetId);
        AddProperty("Name", string.IsNullOrWhiteSpace(selection.NetName) ? "(anonymous)" : selection.NetName!);
    }

    private void RenderBundleSelection(GateBusBundle bundle)
    {
        _propertiesStack.Children.Clear();
        _propertiesStack.Children.Add(PanelTitle("Bus"));
        AddProperty("Name", bundle.LogicalName);
        AddProperty("Range", bundle.Msb == bundle.Lsb
            ? $"[{bundle.Msb}]"
            : $"[{bundle.Msb}:{bundle.Lsb}]");
        AddProperty("Width", bundle.Members.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddProperty("From", $"{bundle.SourceNodeId}.{bundle.SourceBaseName}");
        AddProperty("To",   $"{bundle.TargetNodeId}.{bundle.TargetBaseName}");
    }

    private void RenderCellProperties(GateCell cell)
    {
        _propertiesStack.Children.Clear();
        _propertiesStack.Children.Add(PanelTitle("Cell"));
        AddProperty("Name", cell.Name);
        AddProperty("Type", cell.Type);

        if (cell.Attributes.TryGetValue("src", out string? src) && !string.IsNullOrWhiteSpace(src))
        {
            AddProperty("Source", src);
        }

        AddMapSection("Parameters", cell.Parameters);
        AddMapSection("Attributes", cell.Attributes.Where(kv => !string.Equals(kv.Key, "src", StringComparison.Ordinal)));
    }

    private TextBlock PanelTitle(string text) => new()
    {
        Text = text,
        Foreground = AccentBrush,
        FontSize = 13,
        FontWeight = FontWeight.SemiBold,
    };

    private TextBlock MutedText(string text) => new()
    {
        Text = text,
        Foreground = MutedBrush,
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
    };

    private void AddProperty(string name, string value)
    {
        _propertiesStack.Children.Add(new TextBlock
        {
            Text = name,
            Foreground = MutedBrush,
            FontSize = 10,
        });
        _propertiesStack.Children.Add(new TextBlock
        {
            Text = value,
            Foreground = TextBrush,
            FontSize = 11,
            FontFamily = FontFamily.Parse("monospace"),
            TextWrapping = TextWrapping.Wrap,
        });
    }

    private void AddMapSection(string title, IEnumerable<KeyValuePair<string, string>> values)
    {
        var rows = values.ToList();
        if (rows.Count == 0) return;
        _propertiesStack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = AccentBrush,
            FontWeight = FontWeight.SemiBold,
            FontSize = 11,
            Margin = new Thickness(0, 8, 0, 0),
        });
        foreach ((string key, string value) in rows)
        {
            AddProperty(key, value);
        }
    }

    private sealed record GateSearchResult(string Kind, string Label, string Detail, GateCell? Cell, int? NetId)
    {
        public static GateSearchResult ForCell(GateCell cell) =>
            new("cell", cell.Name, cell.Type, cell, null);

        public static GateSearchResult ForNet(string name, int netId) =>
            new("net", name, "net" + netId, null, netId);

        public override string ToString() => $"{Kind}: {Label}  {Detail}";
    }

    private GateModule ResolveScopeModule()
        => ResolveScopeModule(_scopePath);

    private GateModule ResolveScopeModule(IReadOnlyList<string> scopePath)
    {
        // Same path walk BuildScope does, kept local so the window can pull
        // the resolved module out without re-throwing.
        GateModule current = _netlist.Modules[scopePath[0]];
        for (int i = 1; i < scopePath.Count; i++)
        {
            GateCell inst = current.Cells.First(c => c.Name == scopePath[i]);
            current = _netlist.Modules[inst.Type];
        }
        return current;
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        AttachedToVisualTree -= OnAttachedToVisualTree;
        DataContextChanged -= OnDataContextChanged;
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _observedViewModel = null;
        }
        _layoutService.LayoutStillRunning -= OnLayoutStillRunning;
        CancelActiveLayout();
        _activeLayoutCts?.Dispose();
        _activeLayoutCts = null;
        await _layoutService.DisposeAsync();
    }

    private Control BuildErrorBanner(string message)
    {
        Border banner = new()
        {
            Background = SurfaceBrush,
            BorderBrush = StrokeBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(18, 14),
        };
        StackPanel stack = new() { Orientation = Orientation.Vertical, Spacing = 6 };
        stack.Children.Add(new TextBlock { Text = "Gate-level schematic", Foreground = AccentBrush, FontWeight = FontWeight.SemiBold, FontSize = 14 });
        stack.Children.Add(new TextBlock { Text = message, Foreground = MutedBrush, FontSize = 11, TextWrapping = TextWrapping.Wrap });
        banner.Child = stack;
        return banner;
    }

    private sealed record PendingScopeLayout(
        ElkGraph Graph,
        GateModule ScopeModule,
        IReadOnlyList<string> ScopePath,
        SchematicLayoutDecision Decision,
        SchematicGraphMetrics Metrics,
        IReadOnlyList<GateBusBundle> Bundles);
}

public sealed class GateScopeOpenRequestedEventArgs(IReadOnlyList<string> scopePath) : EventArgs
{
    public IReadOnlyList<string> ScopePath { get; } = scopePath;

    public bool Handled { get; set; }
}
