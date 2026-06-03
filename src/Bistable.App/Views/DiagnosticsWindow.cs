using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Bistable.Core.Design.Schematic;

namespace Bistable.App.Views;

// P2.9-8: minimal diagnostics surface for the Phase 2.9 coverage report.
//
// The panel is intentionally a flat table view — not a polished UI. Goal is
// for a user who suspects "I think this signal is silently missing" to open
// this window, find the module + endpoint, and immediately see one of:
//   - Routed (no action needed),
//   - IntentionalOmission (we hid it on purpose; see Reason),
//   - Unsupported (we know about it; see Reason),
//   - SilentMiss (the analyzer's red flag — bug, file an issue).
//
// Layout: header row with totals, scrollable list of modules + endpoints, and
// a bottom panel for module-level UnsupportedConstructDiagnostic entries.
public sealed class DiagnosticsWindow : Window
{
    private static readonly IBrush BackgroundBrush = SolidColorBrush.Parse("#10141b");
    private static readonly IBrush SurfaceBrush    = SolidColorBrush.Parse("#1b2230");
    private static readonly IBrush StrokeBrush     = SolidColorBrush.Parse("#344157");
    private static readonly IBrush TextBrush       = SolidColorBrush.Parse("#d7dde8");
    private static readonly IBrush MutedBrush      = SolidColorBrush.Parse("#8f9aad");
    private static readonly IBrush AccentBrush     = SolidColorBrush.Parse("#5dbcff");
    private static readonly IBrush GoodBrush       = SolidColorBrush.Parse("#65d889");
    private static readonly IBrush WarnBrush       = SolidColorBrush.Parse("#FFD166");
    private static readonly IBrush BadBrush        = SolidColorBrush.Parse("#FF6B6B");
    private static readonly FontFamily MonoFont    = FontFamily.Parse("monospace");

    private readonly SchematicCoverageReport _report;

    public DiagnosticsWindow(SchematicCoverageReport report)
    {
        _report = report;
        Title = $"Schematic Coverage — {report.TopModule}";
        Width = 920;
        Height = 640;
        Background = BackgroundBrush;
        Content = BuildLayout();
    }

    private Control BuildLayout()
    {
        DockPanel root = new() { LastChildFill = true };
        root.Children.Add(BuildHeader());
        root.Children.Add(BuildUnsupportedFooter());
        root.Children.Add(BuildMainGrid());
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
        StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 18 };

        int routed = _report.Modules.Sum(m => m.RoutedEndpointCount);
        int omissions = _report.Modules.Sum(m => m.IntentionalOmissionCount);
        int unsupported = _report.Modules.Sum(m => m.UnsupportedEndpointCount);
        int silent = _report.SilentMissCount;

        row.Children.Add(Pill("Routed", routed.ToString(), GoodBrush));
        row.Children.Add(Pill("Intentional", omissions.ToString(), MutedBrush));
        row.Children.Add(Pill("Unsupported", unsupported.ToString(), WarnBrush));
        row.Children.Add(Pill("Silent miss", silent.ToString(), silent == 0 ? GoodBrush : BadBrush));
        row.Children.Add(new TextBlock
        {
            Text = $"   {_report.Modules.Count} modules · {_report.UnsupportedConstructs.Count} construct diagnostics",
            Foreground = MutedBrush,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        });

        header.Child = row;
        DockPanel.SetDock(header, Avalonia.Controls.Dock.Top);
        return header;
    }

    private static Border Pill(string label, string value, IBrush accent)
    {
        StackPanel pill = new() { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        pill.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = MutedBrush,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        });
        pill.Children.Add(new TextBlock
        {
            Text = value,
            Foreground = accent,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return new Border
        {
            Background = SurfaceBrush,
            BorderBrush = StrokeBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 4),
            Child = pill,
        };
    }

    private Control BuildMainGrid()
    {
        Grid grid = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(220)) { MinWidth = 160 },
                new ColumnDefinition(GridLength.Star),
            },
        };

        // Module list on the left, endpoint table on the right. Selecting a
        // module filters the endpoint table.
        ListBox moduleList = new()
        {
            Background = SurfaceBrush,
            BorderBrush = StrokeBrush,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Foreground = TextBrush,
            ItemsSource = _report.Modules.Select(m => new ModuleRow(m)).ToList(),
            ItemTemplate = new FuncDataTemplate<ModuleRow>((row, _) => row is null
                ? new TextBlock()
                : new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Margin = new Thickness(8, 4),
                    Children =
                    {
                        new TextBlock { Text = row.Module.ModuleName, Foreground = TextBrush, FontWeight = FontWeight.SemiBold, FontSize = 12 },
                        new TextBlock { Text = row.Summary, Foreground = MutedBrush, FontSize = 10 },
                    }
                }),
            SelectedIndex = _report.Modules.Count > 0 ? 0 : -1,
        };
        Grid.SetColumn(moduleList, 0);
        grid.Children.Add(moduleList);

        ScrollViewer endpointScroll = new()
        {
            Background = BackgroundBrush,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        StackPanel endpointHost = new() { Orientation = Orientation.Vertical };
        endpointScroll.Content = endpointHost;
        Grid.SetColumn(endpointScroll, 1);
        grid.Children.Add(endpointScroll);

        void Render(ModuleCoverage module) => RenderEndpoints(endpointHost, module);
        if (_report.Modules.Count > 0) Render(_report.Modules[0]);
        moduleList.SelectionChanged += (_, _) =>
        {
            if (moduleList.SelectedItem is ModuleRow row) Render(row.Module);
        };

        return grid;
    }

    private static void RenderEndpoints(StackPanel host, ModuleCoverage module)
    {
        host.Children.Clear();

        // Sticky-ish header.
        Grid headerRow = BuildEndpointRowGrid();
        headerRow.Background = SurfaceBrush;
        headerRow.Children.Add(Cell("Status", 0, AccentBrush, semibold: true));
        headerRow.Children.Add(Cell("Kind", 1, AccentBrush, semibold: true));
        headerRow.Children.Add(Cell("Signal", 2, AccentBrush, semibold: true));
        headerRow.Children.Add(Cell("Endpoint", 3, AccentBrush, semibold: true));
        headerRow.Children.Add(Cell("Reason", 4, AccentBrush, semibold: true));
        host.Children.Add(headerRow);

        foreach (EndpointCoverage endpoint in module.Endpoints)
        {
            Grid row = BuildEndpointRowGrid();
            row.Children.Add(Cell(endpoint.Status.ToString(), 0, StatusBrush(endpoint.Status), semibold: true));
            row.Children.Add(Cell(endpoint.Kind.ToString(), 1, TextBrush));
            row.Children.Add(Cell(endpoint.SignalName, 2, TextBrush, mono: true));
            row.Children.Add(Cell(endpoint.EndpointId, 3, MutedBrush, mono: true));
            row.Children.Add(Cell(endpoint.Reason, 4, MutedBrush));
            host.Children.Add(row);
        }
    }

    private static Grid BuildEndpointRowGrid() => new()
    {
        ColumnDefinitions =
        {
            new ColumnDefinition(new GridLength(110)),
            new ColumnDefinition(new GridLength(140)),
            new ColumnDefinition(new GridLength(160)),
            new ColumnDefinition(new GridLength(200)),
            new ColumnDefinition(GridLength.Star),
        },
        Margin = new Thickness(0, 0, 0, 1),
    };

    private static Control Cell(string text, int column, IBrush brush, bool semibold = false, bool mono = false)
    {
        TextBlock tb = new()
        {
            Text = text,
            Foreground = brush,
            FontSize = 11,
            FontWeight = semibold ? FontWeight.SemiBold : FontWeight.Normal,
            FontFamily = mono ? MonoFont : FontFamily.Default,
            Padding = new Thickness(8, 4),
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(tb, column);
        return tb;
    }

    private static IBrush StatusBrush(EndpointCoverageStatus status) => status switch
    {
        EndpointCoverageStatus.Routed              => GoodBrush,
        EndpointCoverageStatus.IntentionalOmission => MutedBrush,
        EndpointCoverageStatus.Unsupported         => WarnBrush,
        EndpointCoverageStatus.SilentMiss          => BadBrush,
        _                                          => MutedBrush,
    };

    private Control BuildUnsupportedFooter()
    {
        Border footer = new()
        {
            Background = SurfaceBrush,
            BorderBrush = StrokeBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 8),
            Height = 160,
        };
        DockPanel.SetDock(footer, Avalonia.Controls.Dock.Bottom);

        DockPanel body = new() { LastChildFill = true };
        body.Children.Add(new TextBlock
        {
            Text = $"Unsupported construct diagnostics ({_report.UnsupportedConstructs.Count})",
            Foreground = AccentBrush,
            FontWeight = FontWeight.SemiBold,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 4),
            [DockPanel.DockProperty] = Avalonia.Controls.Dock.Top,
        });

        ListBox list = new()
        {
            Background = Brushes.Transparent,
            Foreground = TextBrush,
            ItemsSource = _report.UnsupportedConstructs.ToList(),
            ItemTemplate = new FuncDataTemplate<UnsupportedConstructDiagnostic>((d, _) => d is null
                ? new TextBlock()
                : new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Margin = new Thickness(0, 2),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"[{d.ConstructKind}] {d.ModuleName} · {d.ConstructId}",
                            Foreground = WarnBrush,
                            FontFamily = MonoFont,
                            FontSize = 11,
                        },
                        new TextBlock
                        {
                            Text = d.Reason,
                            Foreground = MutedBrush,
                            FontSize = 10,
                            TextWrapping = TextWrapping.Wrap,
                        }
                    }
                }),
        };
        body.Children.Add(list);

        footer.Child = body;
        return footer;
    }

    private sealed class ModuleRow
    {
        public ModuleCoverage Module { get; }
        public string Summary =>
            $"{Module.RoutedEndpointCount} routed · {Module.UnsupportedEndpointCount} unsupported"
            + (Module.SilentMissCount > 0 ? $" · {Module.SilentMissCount} silent" : string.Empty);
        public ModuleRow(ModuleCoverage module) { Module = module; }
    }
}
