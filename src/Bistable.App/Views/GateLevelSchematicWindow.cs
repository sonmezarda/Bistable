using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Bistable.App.Services;
using Bistable.App.Services.Routing.Elk;
using Bistable.Core.Projects;
using Bistable.Core.Synthesis;

namespace Bistable.App.Views;

// Phase 6.5 Wave 2: hierarchical gate-level schematic viewer. Header strip
// + breadcrumb + canvas. Double-clicking a sub-module instance pushes a new
// scope; clicking a breadcrumb segment pops back to that scope.
public sealed class GateLevelSchematicWindow : Window
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

    public GateLevelSchematicWindow(GateNetlist netlist)
        : this(netlist, RoutingQuality.Balanced)
    {
    }

    public GateLevelSchematicWindow(GateNetlist netlist, RoutingQuality routingQuality)
        : this(netlist, routingQuality, autoDowngradeLargeGraphs: true)
    {
    }

    public GateLevelSchematicWindow(
        GateNetlist netlist,
        RoutingQuality routingQuality,
        bool autoDowngradeLargeGraphs)
    {
        _netlist = netlist;
        _requestedRoutingQuality = routingQuality;
        _autoDowngradeLargeGraphs = autoDowngradeLargeGraphs;
        Title = $"Gate-Level — {netlist.TopModule}";
        Width = 1200;
        Height = 760;
        Background = BackgroundBrush;
        _routingOverlay = BuildRoutingOverlay();
        Content = BuildLayout();
        _scopePath.Add(netlist.TopModule);
        _layoutService.LayoutStillRunning += OnLayoutStillRunning;
        _routingCancelButton.Click += (_, _) => CancelActiveLayout();
        _canvas.SubModuleActivated += OnSubModuleActivated;
        _canvas.SubModuleExpansionToggled += OnSubModuleExpansionToggled;
        _canvas.NetSelected += OnNetSelected;
        _canvas.CellSelected += OnCellSelected;
        _searchBox.TextChanged += (_, _) => RefreshSearchResults();
        _searchResults.SelectionChanged += OnSearchResultSelected;
        Opened += (_, _) =>
        {
            LoadCurrentScope();
            _canvas.Focus();
        };
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

    // ── Scope navigation ──────────────────────────────────────────────────

    private void OnSubModuleActivated(object? sender, string instanceName)
    {
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

        int routableNodeCount = CountRoutableNodes(build.Graph.Children);
        SchematicLayoutDecision decision = SchematicRoutingQualityResolver.Resolve(
            _requestedRoutingQuality,
            _autoDowngradeLargeGraphs,
            routableNodeCount);
        if (decision.AutoDowngraded)
        {
            build.Graph.LayoutOptions = ElkLayoutOptionsFactory.For(decision.EffectiveQuality).ToElkOptions();
        }

        return new PendingScopeLayout(
            build.Graph,
            ResolveScopeModule(scopePath),
            [.. scopePath],
            decision,
            routableNodeCount);
    }

    private void ApplyLaidOutScope(ElkGraph laid, PendingScopeLayout pending)
    {
        GateModule scopeModule = pending.ScopeModule;
        _currentScopeModule = scopeModule;
        _canvas.SetGraph(laid, scopeModule);
        RenderNoSelection();
        _selectionStatus.Text = pending.Decision.AutoDowngraded
            ? $"Auto-switched to Fast preview for large graph ({pending.RoutableNodeCount} nodes)."
            : string.Empty;
        RefreshSearchResults();
        _headerStats.Text = $"{scopeModule.Cells.Count} cells · {scopeModule.Nets.Count} named nets · {scopeModule.Ports.Count} ports";
        Title = $"Gate-Level — {string.Join(" / ", pending.ScopePath)}";
        RebuildBreadcrumb();
    }

    private static int CountRoutableNodes(IReadOnlyList<ElkNode>? nodes)
    {
        if (nodes is null) return 0;

        int count = 0;
        foreach (ElkNode node in nodes)
        {
            if (!node.Id.StartsWith("boundary_", StringComparison.Ordinal))
            {
                count++;
            }
            count += CountRoutableNodes(node.Children);
        }
        return count;
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

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
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
        int RoutableNodeCount);
}
