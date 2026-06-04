using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Bistable.App.Services;
using Bistable.App.Services.Routing.Elk;
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
    private readonly GateSchematicCanvas _canvas = new();
    private readonly List<string> _scopePath = new();
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

    public GateLevelSchematicWindow(GateNetlist netlist)
    {
        _netlist = netlist;
        Title = $"Gate-Level — {netlist.TopModule}";
        Width = 1200;
        Height = 760;
        Background = BackgroundBrush;
        Content = BuildLayout();
        _scopePath.Add(netlist.TopModule);
        _canvas.SubModuleActivated += OnSubModuleActivated;
        Opened += (_, _) =>
        {
            LoadCurrentScope();
            _canvas.Focus();
        };
    }

    private Control BuildLayout()
    {
        DockPanel root = new() { LastChildFill = true };
        root.Children.Add(BuildHeader());
        root.Children.Add(BuildBreadcrumb());
        root.Children.Add(BuildToolbar());
        root.Children.Add(_canvas);
        return root;
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
        row.Children.Add(new TextBlock
        {
            Text = "  · middle-drag pan · Ctrl+wheel zoom · double-click instance to drill in",
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
        _scopePath.Add(instanceName);
        LoadCurrentScope();
    }

    private void PopScope()
    {
        if (_scopePath.Count <= 1) return;
        _scopePath.RemoveAt(_scopePath.Count - 1);
        LoadCurrentScope();
    }

    private void JumpToDepth(int depth)
    {
        if (depth < 0 || depth >= _scopePath.Count) return;
        while (_scopePath.Count > depth + 1)
        {
            _scopePath.RemoveAt(_scopePath.Count - 1);
        }
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
        try
        {
            GateNetlistElkBuildResult build = GateNetlistElkBuilder.BuildScope(_netlist, _scopePath);
            ElkGraph laid = new ElkRunner().Layout(build.Graph);
            GateModule scopeModule = ResolveScopeModule();
            _canvas.SetGraph(laid, scopeModule);
            _headerStats.Text = $"{scopeModule.Cells.Count} cells · {scopeModule.Nets.Count} named nets · {scopeModule.Ports.Count} ports";
            Title = $"Gate-Level — {string.Join(" / ", _scopePath)}";
            RebuildBreadcrumb();
        }
        catch (SchematicRoutingException ex)
        {
            Content = BuildErrorBanner("Layout failed: " + ex.Message);
        }
        catch (System.InvalidOperationException ex)
        {
            Content = BuildErrorBanner("Scope resolve failed: " + ex.Message);
        }
    }

    private GateModule ResolveScopeModule()
    {
        // Same path walk BuildScope does, kept local so the window can pull
        // the resolved module out without re-throwing.
        GateModule current = _netlist.Modules[_scopePath[0]];
        for (int i = 1; i < _scopePath.Count; i++)
        {
            GateCell inst = current.Cells.First(c => c.Name == _scopePath[i]);
            current = _netlist.Modules[inst.Type];
        }
        return current;
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
}
