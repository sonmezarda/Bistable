using System.ComponentModel;
using Bistable.App.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Bistable.App.Infrastructure;
using Bistable.App.ViewModels;
using Dock.Avalonia.Controls;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using DockCore = Dock.Model.Core;
using DockModelOrientation = Dock.Model.Core.Orientation;

namespace Bistable.App.Views;

public sealed class MainWindow : Window
{
    private DockControl? _dockWorkspaceControl;
    private RootDock? _dockRoot;
    private DocumentDock? _documentDock;
    private ToolDock? _leftToolDock;
    private BistableToolDockable? _projectDockable;
    private BistableDocumentDockable? _inspectorDockable;
    private BistableDocumentDockable? _waveformDockable;
    private BistableDocumentDockable? _schematicDockable;
    private readonly ColumnDefinition _leftDockColumn = new(new GridLength(260));
    private readonly ColumnDefinition _leftDockSplitterColumn = new(new GridLength(4));
    private readonly ColumnDefinition _rightDockSplitterColumn = new(new GridLength(4));
    private readonly ColumnDefinition _rightDockColumn = new(new GridLength(320));
    private readonly RowDefinition _bottomDockSplitterRow = new(new GridLength(4));
    private readonly RowDefinition _bottomDockRow = new(new GridLength(280));
    private Border? _leftDockPane;
    private Border? _rightDockPane;
    private Border? _bottomDockPane;
    private GridSplitter? _leftDockSplitter;
    private GridSplitter? _rightDockSplitter;
    private GridSplitter? _bottomDockSplitter;
    private TabControl? _leftDockTabs;
    private TabControl? _rightDockTabs;
    private TabControl? _bottomDockTabs;
    private Border? _centerWorkspacePane;
    private TabControl? _centerWorkspaceTabs;
    private SchematicStudioWindow? _schematicStudioWindow;
    private WaveformStudioWindow? _waveformStudioWindow;
    private readonly Dictionary<DockPanelKind, ToolPanelWindow> _floatingToolWindows = [];

    private static readonly IBrush BackgroundBrush = SolidColorBrush.Parse("#0e1116");
    private static readonly IBrush SurfaceBrush = SolidColorBrush.Parse("#151922");
    private static readonly IBrush SurfaceAltBrush = SolidColorBrush.Parse("#1b202b");
    private static readonly IBrush StrokeBrush = SolidColorBrush.Parse("#2a3241");
    private static readonly IBrush TextBrush = SolidColorBrush.Parse("#d7dde8");
    private static readonly IBrush MutedBrush = SolidColorBrush.Parse("#8f9aad");
    private static readonly IBrush AccentBrush = SolidColorBrush.Parse("#57c7ff");
    private static readonly IBrush GreenBrush = SolidColorBrush.Parse("#65d889");

    public MainWindow()
    {
        Title = "Bistable";
        Width = 1420;
        Height = 900;
        MinWidth = 1120;
        MinHeight = 720;
        Background = BackgroundBrush;
        Content = BuildLayout();
        DataContextChanged += OnDataContextChanged;
        Closing += OnClosing;
    }

    private Control BuildLayout()
    {
        DockPanel root = new()
        {
            LastChildFill = true
        };

        root.Children.Add(BuildToolbar());
        root.Children.Add(BuildStatusBar());
        root.Children.Add(BuildDockWorkspace());

        return root;
    }

    private Control BuildDockWorkspace()
    {
        _dockWorkspaceControl = new DockControl
        {
            Margin = new Thickness(10, 12, 10, 8),
            IsDockingEnabled = true,
            AutoCreateDataTemplates = true
        };

        _dockWorkspaceControl.DataTemplates.Add(CreateDockableTemplate<BistableToolDockable>());
        _dockWorkspaceControl.DataTemplates.Add(CreateDockableTemplate<BistableDocumentDockable>());

        InitializeDockWorkspaceModel();
        return _dockWorkspaceControl;
    }

    private FuncDataTemplate<TDockable> CreateDockableTemplate<TDockable>()
        where TDockable : class
    {
        return new FuncDataTemplate<TDockable>((dockable, _) =>
        {
            if (dockable is null)
            {
                return new TextBlock();
            }

            Control? content = dockable switch
            {
                BistableToolDockable tool => tool.GetOrCreateContent(),
                BistableDocumentDockable document => document.GetOrCreateContent(),
                _ => null
            };

            if (content is null)
            {
                return new TextBlock();
            }

            content.DataContext = DataContext;
            return content;
        });
    }

    private void InitializeDockWorkspaceModel()
    {
        if (_dockWorkspaceControl is null)
        {
            return;
        }

        Factory factory = new();

        _projectDockable = new BistableToolDockable(
            DockPanelKind.Project,
            "project",
            "Project",
            () => WrapDockContent(BuildProjectPanelContent(showHeader: false)));
        _inspectorDockable = new BistableDocumentDockable(
            DockPanelKind.Project,
            "inspector",
            "Inspector",
            () => WrapDockContent(BuildInspectorSurface()));
        _waveformDockable = new BistableDocumentDockable(
            DockPanelKind.Waveform,
            "waveform",
            "Waveform",
            () => WrapDockContent(BuildWaveformPanelContent(showHeader: false)));
        _schematicDockable = new BistableDocumentDockable(
            DockPanelKind.Schematic,
            "schematic",
            "Schematic",
            () => WrapDockContent(BuildSchematicPanelContent(showHeader: false)));

        _leftToolDock = new ToolDock
        {
            Id = "tools-left",
            Title = "Tools",
            Proportion = 0.22,
            ActiveDockable = _projectDockable,
            DefaultDockable = _projectDockable,
            VisibleDockables = new List<DockCore.IDockable> { _projectDockable }
        };

        _documentDock = new DocumentDock
        {
            Id = "documents",
            Title = "Workspace",
            Proportion = 0.78,
            ActiveDockable = _inspectorDockable,
            DefaultDockable = _inspectorDockable,
            VisibleDockables = new List<DockCore.IDockable>
            {
                _inspectorDockable,
                _waveformDockable,
                _schematicDockable
            }
        };

        ProportionalDock main = new()
        {
            Id = "main",
            Orientation = DockModelOrientation.Horizontal,
            ActiveDockable = _documentDock,
            DefaultDockable = _documentDock,
            VisibleDockables = new List<DockCore.IDockable>
            {
                _leftToolDock,
                new ProportionalDockSplitter(),
                _documentDock
            }
        };

        _dockRoot = new RootDock
        {
            Id = "root",
            IsFocusableRoot = true,
            EnableAdaptiveGlobalDockTargets = true,
            ActiveDockable = main,
            DefaultDockable = main,
            VisibleDockables = new List<DockCore.IDockable> { main },
            HiddenDockables = new List<DockCore.IDockable>(),
            LeftPinnedDockables = new List<DockCore.IDockable>(),
            RightPinnedDockables = new List<DockCore.IDockable>(),
            TopPinnedDockables = new List<DockCore.IDockable>(),
            BottomPinnedDockables = new List<DockCore.IDockable>(),
            Windows = new List<DockCore.IDockWindow>()
        };

        _dockWorkspaceControl.Factory = factory;
        _dockWorkspaceControl.Layout = _dockRoot;
    }

    private static Control WrapDockContent(Control control)
    {
        Border border = new()
        {
            Background = SurfaceBrush,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            ClipToBounds = true,
            Padding = new Thickness(0),
            Child = control
        };
        return border;
    }

    private Control BuildWorkspace()
    {
        Grid grid = new()
        {
            ColumnDefinitions =
            {
                _leftDockColumn,
                _leftDockSplitterColumn,
                new ColumnDefinition(GridLength.Star),
                _rightDockSplitterColumn,
                _rightDockColumn
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                _bottomDockSplitterRow,
                _bottomDockRow
            }
        };

        _leftDockPane = BuildDockHost();
        grid.Children.Add(_leftDockPane);

        _leftDockSplitter = new GridSplitter
        {
            Width = 4,
            Background = StrokeBrush,
            ResizeDirection = GridResizeDirection.Columns,
            [Grid.ColumnProperty] = 1
        };
        grid.Children.Add(_leftDockSplitter);

        _centerWorkspacePane = BuildDockHost();
        Grid.SetColumn(_centerWorkspacePane, 2);
        grid.Children.Add(_centerWorkspacePane);

        _rightDockSplitter = new GridSplitter
        {
            Width = 4,
            Background = StrokeBrush,
            ResizeDirection = GridResizeDirection.Columns,
            [Grid.ColumnProperty] = 3
        };
        grid.Children.Add(_rightDockSplitter);

        _rightDockPane = BuildDockHost();
        Grid.SetColumn(_rightDockPane, 4);
        grid.Children.Add(_rightDockPane);

        _bottomDockSplitter = new GridSplitter
        {
            Height = 4,
            Background = StrokeBrush,
            ResizeDirection = GridResizeDirection.Rows,
            [Grid.ColumnProperty] = 0,
            [Grid.ColumnSpanProperty] = 5,
            [Grid.RowProperty] = 1
        };
        grid.Children.Add(_bottomDockSplitter);

        _bottomDockPane = BuildDockHost();
        Grid.SetColumnSpan(_bottomDockPane, 5);
        Grid.SetRow(_bottomDockPane, 2);
        grid.Children.Add(_bottomDockPane);

        return grid;
    }

    private Control BuildToolbar()
    {
        Border border = PanelBorder();
        DockPanel.SetDock(border, Avalonia.Controls.Dock.Top);

        Grid grid = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            Margin = new Thickness(12, 8, 12, 10)
        };

        StackPanel menus = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                TopMenu("File", new Control[]
                {
                    new MenuItem
                    {
                        Header = "Open Project...",
                        [!MenuItem.CommandProperty] = new Binding("LoadProjectCommand")
                    },
                    new Separator(),
                    new MenuItem
                    {
                        Header = "Open Sample",
                        [!ItemsControl.ItemsSourceProperty] = new Binding("Samples"),
                        ItemTemplate = new FuncDataTemplate<SampleProjectViewModel>((sample, _) =>
                            sample is null
                                ? new MenuItem()
                                : new MenuItem
                                {
                                    Header = sample.Name,
                                    Command = sample.OpenCommand
                                })
                    }
                }),
                TopMenu("View", new Control[]
                {
                    new MenuItem
                    {
                        Header = "Project",
                        Command = new RelayCommand(() => ActivateDockable(DockPanelKind.Project))
                    },
                    new MenuItem
                    {
                        Header = "Inspector",
                        Command = new RelayCommand(ActivateInspectorDocument)
                    },
                    new MenuItem
                    {
                        Header = "Waveform",
                        Command = new RelayCommand(() => ActivateDockable(DockPanelKind.Waveform))
                    },
                    new MenuItem
                    {
                        Header = "Schematic",
                        Command = new RelayCommand(() => ActivateDockable(DockPanelKind.Schematic))
                    },
                    new Separator(),
                    new MenuItem
                    {
                        Header = "Open Schematic Studio",
                        Command = new RelayCommand(OpenSchematicStudio)
                    },
                    new MenuItem
                    {
                        Header = "Open Waveform Studio",
                        Command = new RelayCommand(OpenWaveformStudio)
                    },
                    new Separator(),
                    new MenuItem
                    {
                        Header = "Reset Tool Layout",
                        Command = new RelayCommand(ResetToolLayout)
                    }
                })
            }
        };
        grid.Children.Add(menus);

        TextBlock title = new()
        {
            Text = "Bistable",
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextBrush,
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                ToolbarButton("Build", "BuildCommand"),
                ToolbarButton("Eval", "EvalCommand"),
                ToolbarButton("Tick", "TickCommand"),
                ToolbarCheckBox("Live", "LiveModeEnabled"),
                ToolbarLabel("Clock"),
                ToolbarComboBox("AvailableClocks", "SelectedClockName", 110),
                ToolbarLabel("Cycles"),
                ToolbarTextBox("RunCyclesText", 72),
                ToolbarButton("Run", "RunCyclesCommand"),
                ToolbarButton("Reset", "ResetCommand")
            }
        };
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        border.Child = grid;
        return border;
    }

    private static Control BuildStatusBar()
    {
        Border border = PanelBorder();
        DockPanel.SetDock(border, Avalonia.Controls.Dock.Bottom);
        border.Child = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Margin = new Thickness(12, 8),
            Children =
            {
                new TextBlock
                {
                    Foreground = MutedBrush,
                    [!TextBlock.TextProperty] = new Binding("Status")
                },
                new TextBlock
                {
                    Foreground = MutedBrush,
                    [!TextBlock.TextProperty] = new Binding("Time") { StringFormat = "t={0}" },
                    [Grid.ColumnProperty] = 1
                }
            }
        };

        return border;
    }

    private static Border BuildDockHost() => PanelBorder(new Thickness(12, 12, 12, 12));

    private static Control BuildInspectorSurface()
    {
        Grid grid = new()
        {
            Margin = new Thickness(6, 12, 6, 6),
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            }
        };

        Border header = PanelBorder();
        header.Child = new DockPanel
        {
            Margin = new Thickness(12, 10),
            Children =
            {
                new TextBlock
                {
                    Text = "Inspector",
                    Foreground = AccentBrush,
                    FontSize = 13,
                    FontWeight = FontWeight.SemiBold
                }
            }
        };
        grid.Children.Add(header);

        Grid contentGrid = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 10,
            Margin = new Thickness(0, 10, 0, 0)
        };

        Control inputs = BuildSignalTable("Inputs", "Inputs", true);
        Control outputs = BuildSignalTable("Outputs", "Outputs", false);
        contentGrid.Children.Add(inputs);
        Grid.SetColumn(outputs, 1);
        contentGrid.Children.Add(outputs);

        Grid.SetRow(contentGrid, 1);
        grid.Children.Add(contentGrid);
        Grid.SetColumn(grid, 2);
        return grid;
    }

    private static Control BuildSignalTable(string title, string sourcePath, bool editable)
    {
        Border border = PanelBorder();
        StackPanel panel = new()
        {
            Spacing = 10,
            Margin = new Thickness(12)
        };
        panel.Children.Add(SectionTitle(title));
        panel.Children.Add(new ListBox
        {
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            ItemTemplate = SignalRowTemplate(editable),
            [!ItemsControl.ItemsSourceProperty] = new Binding(sourcePath)
        });

        border.Child = panel;
        return border;
    }

    private Control BuildProjectPanelContent(bool showHeader = true)
    {
        StackPanel panel = new()
        {
            Spacing = 12,
            Margin = new Thickness(12)
        };

        if (showHeader)
        {
            panel.Children.Add(DockPanelHeader(
                "Project",
                DockPanelKind.Project));
        }
        panel.Children.Add(BoundLabel("ProjectName", 15, TextBrush));
        panel.Children.Add(MetadataLine("Top", "TopModule"));
        panel.Children.Add(MetadataLine("Tool", "VerilatorVersion"));
        panel.Children.Add(SectionTitle("Signals"));
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                SmallButton("Add", "AddSelectedWaveformSignalCommand"),
                SmallButton("Remove", "RemoveSelectedWaveformSignalCommand"),
                SmallButton("Clear", "ClearWaveformCommand")
            }
        });
        panel.Children.Add(new ListBox
        {
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = TextBrush,
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            ItemTemplate = SignalListTemplate(),
            [!ItemsControl.ItemsSourceProperty] = new Binding("AllSignals"),
            [!SelectingItemsControl.SelectedItemProperty] = new Binding("SelectedSignal", BindingMode.TwoWay)
        });
        panel.Children.Add(SectionTitle("Trace Signals"));
        panel.Children.Add(new ListBox
        {
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = TextBrush,
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            ItemTemplate = SignalListTemplate(),
            [!ItemsControl.ItemsSourceProperty] = new Binding("TraceSignals"),
            [!SelectingItemsControl.SelectedItemProperty] = new Binding("SelectedSignal", BindingMode.TwoWay)
        });

        return panel;
    }

    private Control BuildWaveformPanelContent(bool showHeader = true)
    {
        Grid waveform = new()
        {
            RowDefinitions =
            {
                new RowDefinition(showHeader ? GridLength.Auto : new GridLength(0)),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            },
            Margin = new Thickness(12)
        };

        if (showHeader)
        {
            waveform.Children.Add(DockPanelHeader(
                "Waveform",
                DockPanelKind.Waveform));
        }

        Control toolbar = BuildWaveformToolbar();
        Grid.SetRow(toolbar, 1);
        waveform.Children.Add(toolbar);

        Grid content = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(300)),
                new ColumnDefinition(new GridLength(8)),
                new ColumnDefinition(GridLength.Star)
            },
            Margin = new Thickness(0, 10, 0, 0)
        };

        Border lanesBorder = PanelBorder();
        lanesBorder.Child = BuildWaveformLaneList();
        content.Children.Add(lanesBorder);

        GridSplitter laneSplitter = new()
        {
            Width = 8,
            Background = Brushes.Transparent,
            ResizeDirection = GridResizeDirection.Columns,
            [Grid.ColumnProperty] = 1
        };
        content.Children.Add(laneSplitter);

        WaveformPreviewControl preview = new()
        {
            ClipToBounds = true,
            [!WaveformPreviewControl.LanesProperty] = new Binding("WaveformLanes"),
            [!WaveformPreviewControl.ZoomProperty] = new Binding("WaveformZoom"),
            [!WaveformPreviewControl.OffsetProperty] = new Binding("WaveformOffset"),
            [!WaveformPreviewControl.SelectedSignalNameProperty] = new Binding("SelectedWaveformSignalName", BindingMode.TwoWay),
            [!WaveformPreviewControl.CursorOrderProperty] = new Binding("WaveformCursorOrder", BindingMode.TwoWay)
        };
        Grid.SetColumn(preview, 2);
        content.Children.Add(preview);

        Grid.SetRow(content, 2);
        waveform.Children.Add(content);
        return waveform;
    }

    private static Control BuildWaveformLaneList()
    {
        Grid grid = new()
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            },
            Margin = new Thickness(10)
        };

        grid.Children.Add(new TextBlock
        {
            Text = "Waveform Signals",
            Foreground = AccentBrush,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold
        });

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 10, 0, 10),
            Children =
            {
                SmallButton("Up", "MoveWaveformSignalUpCommand"),
                SmallButton("Down", "MoveWaveformSignalDownCommand"),
                SmallButton("Remove", "RemoveSelectedWaveformSignalCommand"),
                SmallButton("Clear", "ClearWaveformCommand")
            }
        };
        Grid.SetRow(actions, 1);
        grid.Children.Add(actions);

        ListBox list = new()
        {
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = TextBrush,
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            ItemTemplate = WaveformLaneTemplate(),
            [!ItemsControl.ItemsSourceProperty] = new Binding("WaveformLanes"),
            [!SelectingItemsControl.SelectedItemProperty] = new Binding("SelectedWaveformLane", BindingMode.TwoWay)
        };
        Grid.SetRow(list, 2);
        grid.Children.Add(list);

        return grid;
    }

    private Control BuildSchematicPanelContent(bool showHeader = true)
    {
        Grid grid = new()
        {
            RowDefinitions =
            {
                new RowDefinition(showHeader ? GridLength.Auto : new GridLength(0)),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(new GridLength(0.56, GridUnitType.Star)) { MinHeight = 220 },
                new RowDefinition(new GridLength(5)),
                new RowDefinition(new GridLength(0.44, GridUnitType.Star)) { MinHeight = 180 }
            },
            Margin = new Thickness(12)
        };

        if (showHeader)
        {
            grid.Children.Add(DockPanelHeader(
                "Schematic",
                DockPanelKind.Schematic));
        }

        Grid previewGrid = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(5)),
                new ColumnDefinition(new GridLength(300)) { MinWidth = 240, MaxWidth = 460 }
            },
            Margin = new Thickness(0, 12, 0, 0)
        };

        SchematicPreviewControl preview = CreateBoundSchematicPreview(compactLayout: true);
        previewGrid.Children.Add(preview);

        Control toolbar = BuildSchematicViewportToolbar(preview, includeStudioButton: true);
        Grid.SetRow(toolbar, 1);
        grid.Children.Add(toolbar);

        GridSplitter probeSplitter = new()
        {
            Width = 5,
            Background = StrokeBrush,
            ResizeDirection = GridResizeDirection.Columns,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetColumn(probeSplitter, 1);
        previewGrid.Children.Add(probeSplitter);

        Border liveProbeBorder = PanelBorder();
        liveProbeBorder.Child = BuildSchematicProbePanel();
        Grid.SetColumn(liveProbeBorder, 2);
        previewGrid.Children.Add(liveProbeBorder);

        Grid.SetRow(previewGrid, 2);
        grid.Children.Add(previewGrid);

        GridSplitter verticalSplitter = new()
        {
            Height = 5,
            Background = StrokeBrush,
            ResizeDirection = GridResizeDirection.Rows,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, 10, 0, 0)
        };
        Grid.SetRow(verticalSplitter, 3);
        grid.Children.Add(verticalSplitter);

        Grid hierarchyGrid = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(260)) { MinWidth = 220, MaxWidth = 420 },
                new ColumnDefinition(new GridLength(5)),
                new ColumnDefinition(GridLength.Star)
            },
            Margin = new Thickness(0, 12, 0, 0)
        };

        Border treeBorder = PanelBorder();
        Grid treePanel = new()
        {
            Margin = new Thickness(10),
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(new GridLength(0.48, GridUnitType.Star)),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(new GridLength(0.52, GridUnitType.Star))
            }
        };
        treePanel.Children.Add(new TextBlock
        {
            Text = "Hierarchy",
            Foreground = AccentBrush,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold
        });

        treePanel.Children.Add(new TextBlock
        {
            Foreground = MutedBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
            [!TextBlock.TextProperty] = new Binding("SelectedHierarchySummary"),
            [Grid.RowProperty] = 1
        });

        treePanel.Children.Add(new TreeView
        {
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Margin = new Thickness(0, 8, 0, 0),
            [!ItemsControl.ItemsSourceProperty] = new Binding("HierarchyRoot.Children"),
            [!SelectingItemsControl.SelectedItemProperty] = new Binding("SelectedHierarchyNode", BindingMode.TwoWay),
            ItemTemplate = new FuncTreeDataTemplate<HierarchyNodeViewModel>(
                (node, _) =>
                    node is null
                        ? new TextBlock()
                        : new TextBlock
                        {
                            Text = node.DisplayLabel,
                            Foreground = TextBrush,
                            FontFamily = FontFamily.Parse("monospace")
                        },
                static node => node.Children),
            [Grid.RowProperty] = 2
        });

        treePanel.Children.Add(new TextBlock
        {
            Foreground = AccentBrush,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 10, 0, 0),
            [!TextBlock.TextProperty] = new Binding("SelectedHierarchyScopeTitle"),
            [Grid.RowProperty] = 3
        });

        treePanel.Children.Add(new TextBlock
        {
            Foreground = MutedBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
            [!TextBlock.TextProperty] = new Binding("SelectedHierarchyScopeSummary"),
            [Grid.RowProperty] = 4
        });

        treePanel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 8, 0, 0),
            Children =
            {
                SmallButton("Add Selected", "AddSelectedWaveformSignalCommand"),
                SmallButton("Add Scope", "AddHierarchyScopeSignalsToWaveformCommand")
            },
            [Grid.RowProperty] = 5
        });

        treePanel.Children.Add(new ListBox
        {
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = TextBrush,
            Margin = new Thickness(0, 8, 0, 0),
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            ItemTemplate = SignalListTemplate(),
            [!ItemsControl.ItemsSourceProperty] = new Binding("HierarchyScopeSignals"),
            [!SelectingItemsControl.SelectedItemProperty] = new Binding("SelectedSignal", BindingMode.TwoWay),
            [Grid.RowProperty] = 6
        });

        treeBorder.Child = treePanel;
        hierarchyGrid.Children.Add(treeBorder);

        GridSplitter hierarchySplitter = new()
        {
            Width = 5,
            Background = StrokeBrush,
            ResizeDirection = GridResizeDirection.Columns,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetColumn(hierarchySplitter, 1);
        hierarchyGrid.Children.Add(hierarchySplitter);

        Border graphBorder = PanelBorder();
        HierarchyGraphControl graph = new()
        {
            [!HierarchyGraphControl.RootProperty] = new Binding("HierarchyRoot"),
            [!HierarchyGraphControl.SelectedPathProperty] = new Binding("SelectedHierarchyPath", BindingMode.TwoWay),
            [!HierarchyGraphControl.ScopeSummariesProperty] = new Binding("HierarchyTraceScopeSummaries")
        };
        graphBorder.Child = graph;
        Grid.SetColumn(graphBorder, 2);
        hierarchyGrid.Children.Add(graphBorder);

        Grid.SetRow(hierarchyGrid, 4);
        grid.Children.Add(hierarchyGrid);
        return grid;
    }

    private static Control BuildSchematicProbePanel()
    {
        Grid grid = new()
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            },
            Margin = new Thickness(12)
        };

        grid.Children.Add(new TextBlock
        {
            Text = "Live Probe",
            Foreground = AccentBrush,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold
        });

        grid.Children.Add(new TextBlock
        {
            Foreground = TextBrush,
            FontFamily = FontFamily.Parse("monospace"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
            [!TextBlock.TextProperty] = new Binding("SelectedSchematicSignalDisplayName"),
            [Grid.RowProperty] = 1
        });

        Grid metadata = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(78)),
                new ColumnDefinition(GridLength.Star)
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            RowSpacing = 6,
            Margin = new Thickness(0, 10, 0, 0),
            [Grid.RowProperty] = 2
        };

        metadata.Children.Add(MetadataCaption("Dir"));
        metadata.Children.Add(MetadataValue("SelectedSchematicSignalDirection", 0));
        metadata.Children.Add(MetadataCaption("Width", 1));
        metadata.Children.Add(MetadataValue("SelectedSchematicSignalWidth", 1));
        metadata.Children.Add(MetadataCaption("Value", 2));
        metadata.Children.Add(MetadataValue("SelectedSchematicSignalValue", 2, GreenBrush));
        grid.Children.Add(metadata);

        grid.Children.Add(new TextBlock
        {
            Text = "Drive",
            Foreground = MutedBrush,
            Margin = new Thickness(0, 12, 0, 0),
            [Grid.RowProperty] = 3
        });

        TextBox driveBox = new()
        {
            MinHeight = 32,
            Background = SurfaceAltBrush,
            Foreground = TextBrush,
            BorderBrush = StrokeBrush,
            FontFamily = FontFamily.Parse("monospace"),
            Margin = new Thickness(0, 8, 0, 0),
            [!Control.IsEnabledProperty] = new Binding("CanDriveSelectedSchematicInput"),
            [!TextBox.TextProperty] = new Binding("SchematicDriveValue", BindingMode.TwoWay),
            [Grid.RowProperty] = 4
        };
        grid.Children.Add(driveBox);

        Button applyButton = SmallButton("Apply", "DriveSelectedSchematicInputCommand");
        applyButton.Bind(Control.IsEnabledProperty, new Binding("CanDriveSelectedSchematicInput"));

        Button toggleButton = SmallButton("Toggle", "ToggleInputSignalCommand");
        toggleButton.Bind(Button.CommandParameterProperty, new Binding("SelectedSchematicSignalName"));
        toggleButton.Bind(Control.IsEnabledProperty, new Binding("CanToggleSelectedSchematicInput"));

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 10, 0, 0),
            [Grid.RowProperty] = 5
        };
        actions.Children.Add(applyButton);
        actions.Children.Add(toggleButton);
        grid.Children.Add(actions);

        Button addWaveformButton = SmallButton("Add To Waveform", "AddSelectedWaveformSignalCommand");

        grid.Children.Add(new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(0, 14, 0, 0),
            Children =
            {
                addWaveformButton,
                new TextBlock
                {
                    Text = "1-bit inputs toggle directly. Bus inputs open an editor.",
                    Foreground = MutedBrush,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 11
                }
            },
            [Grid.RowProperty] = 6
        });

        return grid;
    }

    private Control BuildWaveformToolbar()
    {
        DockPanel toolbar = new()
        {
            LastChildFill = false,
            Margin = new Thickness(0, 12, 0, 0)
        };

        TextBlock summary = new()
        {
            Foreground = MutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            [!TextBlock.TextProperty] = new Binding("WaveformCursorSummary")
        };
        toolbar.Children.Add(summary);

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                ClickButton("Studio", (_, _) => OpenWaveformStudio(), 68),
                SmallButton("Up", "MoveWaveformSignalUpCommand"),
                SmallButton("Down", "MoveWaveformSignalDownCommand"),
                SmallButton("<", "PanWaveformLeftCommand"),
                SmallButton(">", "PanWaveformRightCommand"),
                SmallButton("Zoom +", "ZoomWaveformInCommand"),
                SmallButton("Zoom -", "ZoomWaveformOutCommand"),
                SmallButton("Fit", "FitWaveformCommand")
            }
        };
        DockPanel.SetDock(actions, Avalonia.Controls.Dock.Right);
        toolbar.Children.Add(actions);
        return toolbar;
    }

    private Control BuildSchematicViewportToolbar(SchematicPreviewControl preview, bool includeStudioButton)
    {
        TextBlock zoomText = new()
        {
            Text = "Fit",
            Foreground = MutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 52,
            TextAlignment = TextAlignment.Right
        };
        preview.ViewportChanged += (_, args) => zoomText.Text = $"{args.Zoom * 100:0}%";

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 0, 0),
            Children =
            {
                zoomText,
                BuildSchematicRouterComboBox(preview),
                ClickButton("Fit", (_, _) => preview.FitToView()),
                ClickButton("1:1", (_, _) => preview.ResetView()),
                ClickButton("+", (_, _) => preview.ZoomIn(), 34),
                ClickButton("-", (_, _) => preview.ZoomOut(), 34)
            }
        };

        if (includeStudioButton)
        {
            buttons.Children.Insert(1, ClickButton("Studio", (_, _) => OpenSchematicStudio(), 68));
        }

        return buttons;
    }

    private static ComboBox BuildSchematicRouterComboBox(SchematicPreviewControl preview)
    {
        ComboBox routerBox = new()
        {
            Width = 136,
            MinHeight = 30,
            Background = SurfaceAltBrush,
            Foreground = TextBrush,
            BorderBrush = StrokeBrush,
            ItemsSource = Enum.GetValues<SchematicRoutingEngine>(),
            SelectedItem = preview.RoutingEngine
        };
        routerBox.SelectionChanged += (_, _) =>
        {
            if (routerBox.SelectedItem is SchematicRoutingEngine engine)
            {
                preview.RoutingEngine = engine;
            }
        };
        return routerBox;
    }

    private Control BuildPanelSurface(DockPanelKind kind)
    {
        Border border = PanelBorder(new Thickness(0));
        border.CornerRadius = new CornerRadius(0);
        border.BorderThickness = new Thickness(0);
        border.Child = kind switch
        {
            DockPanelKind.Project => BuildProjectPanelContent(),
            DockPanelKind.Waveform => BuildWaveformPanelContent(),
            DockPanelKind.Schematic => BuildSchematicPanelContent(),
            _ => new TextBlock { Text = "Unknown panel", Foreground = MutedBrush }
        };
        return border;
    }

    private Control DockPanelHeader(string title, DockPanelKind panelKind)
    {
        DockPanel row = new()
        {
            LastChildFill = false
        };

        row.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = AccentBrush,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                DockButton("L", panelKind, DockZone.Left),
                DockButton("R", panelKind, DockZone.Right),
                DockButton("B", panelKind, DockZone.Bottom),
                DockButton("C", panelKind, DockZone.Center),
                DockButton("F", panelKind, DockZone.Floating),
                DockButton("X", panelKind, DockZone.Hidden)
            }
        };
        DockPanel.SetDock(actions, Avalonia.Controls.Dock.Right);
        row.Children.Add(actions);
        return row;
    }

    private MenuItem DockMenu(
        string header,
        string visiblePath,
        DockPanelKind panelKind) =>
        new()
        {
            Header = header,
            ItemsSource = new Control[]
            {
                new MenuItem
                {
                    Header = "Visible",
                    ToggleType = MenuItemToggleType.CheckBox,
                    [!MenuItem.IsCheckedProperty] = new Binding(visiblePath, BindingMode.TwoWay)
                },
                new Separator(),
                new MenuItem
                {
                    Header = "Dock Left",
                    Command = CreateDockCommand(panelKind, DockZone.Left)
                },
                new MenuItem
                {
                    Header = "Dock Right",
                    Command = CreateDockCommand(panelKind, DockZone.Right)
                },
                new MenuItem
                {
                    Header = "Dock Bottom",
                    Command = CreateDockCommand(panelKind, DockZone.Bottom)
                },
                new MenuItem
                {
                    Header = "Dock Center",
                    Command = CreateDockCommand(panelKind, DockZone.Center)
                },
                new MenuItem
                {
                    Header = "Float",
                    Command = CreateDockCommand(panelKind, DockZone.Floating)
                },
                new Separator(),
                new MenuItem
                {
                    Header = "Hide",
                    Command = CreateDockCommand(panelKind, DockZone.Hidden)
                }
            }
        };

    private static IDataTemplate SignalListTemplate() => new FuncDataTemplate<SignalViewModel>((signal, _) =>
        signal is null
            ? new TextBlock()
            : new TextBlock
            {
                Text = signal.BrowseLabel,
                Foreground = TextBrush,
                FontFamily = FontFamily.Parse("monospace"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 4)
            });

    private static IDataTemplate WaveformLaneTemplate() => new FuncDataTemplate<WaveformLaneViewModel>((lane, _) =>
    {
        if (lane is null)
        {
            return new TextBlock();
        }

        Grid row = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(88))
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            Margin = new Thickness(0, 4),
            ColumnSpacing = 8
        };

        row.Children.Add(new TextBlock
        {
            Text = lane.DisplayName,
            Foreground = TextBrush,
            FontFamily = FontFamily.Parse("monospace"),
            FontSize = 12
        });

        row.Children.Add(new TextBlock
        {
            Text = lane.LatestValue,
            Foreground = GreenBrush,
            FontFamily = FontFamily.Parse("monospace"),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            [Grid.ColumnProperty] = 1
        });

        row.Children.Add(new TextBlock
        {
            Text = lane.ScopeLabel,
            Foreground = MutedBrush,
            FontSize = 11,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            [Grid.RowProperty] = 1
        });

        row.Children.Add(new TextBlock
        {
            Text = lane.WidthLabel,
            Foreground = MutedBrush,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right,
            [Grid.ColumnProperty] = 1,
            [Grid.RowProperty] = 1
        });

        return row;
    });

    private static IDataTemplate SignalRowTemplate(bool editable) => new FuncDataTemplate<SignalViewModel>((signal, _) =>
    {
        if (signal is null)
        {
            return new TextBlock();
        }

        Grid row = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(90)),
                new ColumnDefinition(new GridLength(120))
            },
            Margin = new Thickness(0, 4),
            ColumnSpacing = 8
        };

        row.Children.Add(new TextBlock
        {
            Text = signal.Name,
            Foreground = TextBrush,
            FontFamily = FontFamily.Parse("monospace"),
            VerticalAlignment = VerticalAlignment.Center
        });

        row.Children.Add(new TextBlock
        {
            Text = signal.WidthLabel,
            Foreground = MutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            [Grid.ColumnProperty] = 1
        });

        Control valueControl = CreateValueControl(signal, editable);

        Grid.SetColumn(valueControl, 2);
        row.Children.Add(valueControl);
        return row;
    });

    private static Control CreateValueControl(SignalViewModel signal, bool editable)
    {
        if (!editable)
        {
            return new TextBlock
            {
                [!TextBlock.TextProperty] = new Binding(nameof(SignalViewModel.Value)),
                Foreground = GreenBrush,
                FontFamily = FontFamily.Parse("monospace"),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        if (signal.IsBoolean)
        {
            return new CheckBox
            {
                VerticalAlignment = VerticalAlignment.Center,
                [!ContentControl.ContentProperty] = new Binding(nameof(SignalViewModel.Value)),
                [!ToggleButton.IsCheckedProperty] = new Binding(nameof(SignalViewModel.BooleanValue), BindingMode.TwoWay)
            };
        }

        return new TextBox
        {
            [!TextBox.TextProperty] = new Binding(nameof(SignalViewModel.Value), BindingMode.TwoWay),
            Background = SurfaceAltBrush,
            Foreground = TextBrush,
            BorderBrush = StrokeBrush,
            MinHeight = 30
        };
    }

    private static Menu TopMenu(string header, IEnumerable<object> items)
    {
        Menu menu = new()
        {
            Background = Brushes.Transparent,
            Foreground = TextBrush,
            ItemsSource = new Control[]
            {
                new MenuItem
                {
                    Header = header,
                    Background = SurfaceAltBrush,
                    Foreground = TextBrush,
                    BorderBrush = StrokeBrush,
                    ItemsSource = items.ToArray()
                }
            }
        };

        return menu;
    }

    private static Button ToolbarButton(string text, string commandPath) => new()
    {
        Content = text,
        MinWidth = 78,
        Height = 34,
        Background = SurfaceAltBrush,
        Foreground = TextBrush,
        BorderBrush = StrokeBrush,
        [!Button.CommandProperty] = new Binding(commandPath)
    };

    private static TextBlock ToolbarLabel(string text) => new()
    {
        Text = text,
        Foreground = MutedBrush,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static ComboBox ToolbarComboBox(string itemsPath, string selectedPath, double width) => new()
    {
        Width = width,
        MinHeight = 34,
        Background = SurfaceAltBrush,
        Foreground = TextBrush,
        BorderBrush = StrokeBrush,
        [!ItemsControl.ItemsSourceProperty] = new Binding(itemsPath),
        [!SelectingItemsControl.SelectedItemProperty] = new Binding(selectedPath, BindingMode.TwoWay)
    };

    private static TextBox ToolbarTextBox(string path, double width) => new()
    {
        Width = width,
        MinHeight = 34,
        Background = SurfaceAltBrush,
        Foreground = TextBrush,
        BorderBrush = StrokeBrush,
        HorizontalContentAlignment = HorizontalAlignment.Right,
        [!TextBox.TextProperty] = new Binding(path, BindingMode.TwoWay)
    };

    private static CheckBox ToolbarCheckBox(string text, string path) => new()
    {
        Content = text,
        MinHeight = 34,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = TextBrush,
        [!ToggleButton.IsCheckedProperty] = new Binding(path, BindingMode.TwoWay)
    };

    private static Button SmallButton(string text, string commandPath) => new()
    {
        Content = text,
        MinWidth = 56,
        Height = 28,
        FontSize = 12,
        Background = SurfaceAltBrush,
        Foreground = TextBrush,
        BorderBrush = StrokeBrush,
        [!Button.CommandProperty] = new Binding(commandPath)
    };

    private static Button ClickButton(string text, EventHandler<RoutedEventArgs> onClick, double minWidth = 56)
    {
        Button button = new()
        {
            Content = text,
            MinWidth = minWidth,
            Height = 28,
            FontSize = 12,
            Background = SurfaceAltBrush,
            Foreground = TextBrush,
            BorderBrush = StrokeBrush
        };
        button.Click += onClick;
        return button;
    }

    private SchematicPreviewControl CreateBoundSchematicPreview(bool compactLayout)
    {
        SchematicPreviewControl preview = new()
        {
            CompactLayout = compactLayout,
            [!SchematicPreviewControl.ModuleNameProperty] = new Binding("SchematicModuleName"),
            [!SchematicPreviewControl.SignalsProperty] = new Binding("AllSignals"),
            [!SchematicPreviewControl.ScopeSignalsProperty] = new Binding("HierarchyScopeSignals"),
            [!SchematicPreviewControl.SelectedSignalNameProperty] = new Binding("SelectedSchematicSignalName", BindingMode.TwoWay),
            [!SchematicPreviewControl.ToggleInputCommandProperty] = new Binding("ToggleInputSignalCommand"),
            [!SchematicPreviewControl.AddSelectedWaveformCommandProperty] = new Binding("AddSelectedWaveformSignalCommand"),
            [!SchematicPreviewControl.SelectScopeCommandProperty] = new Binding("SelectHierarchyScopeCommand"),
            [!SchematicPreviewControl.ToggleScopeExpansionCommandProperty] = new Binding("ToggleSchematicExpansionCommand"),
            [!SchematicPreviewControl.IsActiveScopeExpandedProperty] = new Binding("IsSelectedHierarchyScopeExpanded"),
            [!SchematicPreviewControl.ExpandedScopePathsProperty] = new Binding("SchematicExpandedPaths"),
            [!SchematicPreviewControl.ActiveScopeTitleProperty] = new Binding("SelectedHierarchyScopeTitle"),
            [!SchematicPreviewControl.ActiveScopeModuleNameProperty] = new Binding("SelectedHierarchyScopeModuleName"),
            [!SchematicPreviewControl.ActiveScopePathProperty] = new Binding("SelectedHierarchyScopePath"),
            [!SchematicPreviewControl.ActiveScopeSummaryProperty] = new Binding("SelectedHierarchyScopeSummary"),
            [!SchematicPreviewControl.ActiveScopeHintProperty] = new Binding("SelectedHierarchyScopeHint"),
            [!SchematicPreviewControl.ScopeParentProperty] = new Binding("SelectedHierarchyParentScope"),
            [!SchematicPreviewControl.ScopeChildrenProperty] = new Binding("SelectedHierarchyChildInstances"),
            [!SchematicPreviewControl.ScopePortsProperty] = new Binding("SelectedHierarchyPorts"),
            [!SchematicPreviewControl.ScopeLocalSignalsProperty] = new Binding("SelectedHierarchyLocalSignals"),
            [!SchematicPreviewControl.ScopeContAssignsProperty] = new Binding("SelectedHierarchyContAssigns")
        };
        preview.SignalEditorRequested += OnSchematicSignalEditorRequested;
        return preview;
    }

    private static Button TinyButton(string text, string commandPath) => new()
    {
        Content = text,
        Width = 24,
        Height = 24,
        FontSize = 11,
        Padding = new Thickness(0),
        Background = SurfaceAltBrush,
        Foreground = TextBrush,
        BorderBrush = StrokeBrush,
        [!Button.CommandProperty] = new Binding(commandPath)
    };

    private RelayCommand CreateDockCommand(DockPanelKind panelKind, DockZone zone) =>
        new(() => ExecuteDockCommand(panelKind, zone));

    private void ExecuteDockCommand(DockPanelKind panelKind, DockZone zone)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        DockCommandParameter parameter = new(panelKind, zone);
        if (viewModel.DockPanelCommand.CanExecute(parameter))
        {
            viewModel.DockPanelCommand.Execute(parameter);
        }
    }

    private void ActivateInspectorDocument()
    {
        if (_inspectorDockable is null || _documentDock is null)
        {
            return;
        }

        _documentDock.ActiveDockable = _inspectorDockable;
        _documentDock.DefaultDockable = _inspectorDockable;
        _dockWorkspaceControl?.InvalidateVisual();
    }

    private void ActivateDockable(DockPanelKind kind)
    {
        switch (kind)
        {
            case DockPanelKind.Project:
                if (_projectDockable is not null && _leftToolDock is not null)
                {
                    _leftToolDock.ActiveDockable = _projectDockable;
                }

                break;
            case DockPanelKind.Waveform:
                if (_waveformDockable is not null && _documentDock is not null)
                {
                    _documentDock.ActiveDockable = _waveformDockable;
                }

                break;
            case DockPanelKind.Schematic:
                if (_schematicDockable is not null && _documentDock is not null)
                {
                    _documentDock.ActiveDockable = _schematicDockable;
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        _dockWorkspaceControl?.InvalidateVisual();
    }

    private void ResetToolLayout()
    {
        ExecuteDockCommand(DockPanelKind.Project, DockZone.Left);
        ExecuteDockCommand(DockPanelKind.Waveform, DockZone.Bottom);
        ExecuteDockCommand(DockPanelKind.Schematic, DockZone.Right);
    }

    private void OpenSchematicStudio()
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (_schematicStudioWindow is { IsVisible: true } window)
        {
            window.Activate();
            return;
        }

        _schematicStudioWindow = new SchematicStudioWindow(viewModel);
        _schematicStudioWindow.Closed += OnSchematicStudioClosed;
        _schematicStudioWindow.Show(this);
    }

    private void OpenWaveformStudio()
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (_waveformStudioWindow is { IsVisible: true } window)
        {
            window.Activate();
            return;
        }

        _waveformStudioWindow = new WaveformStudioWindow(viewModel);
        _waveformStudioWindow.Closed += OnWaveformStudioClosed;
        _waveformStudioWindow.Show(this);
    }

    private void OnSchematicStudioClosed(object? sender, EventArgs e)
    {
        if (_schematicStudioWindow is not null)
        {
            _schematicStudioWindow.Closed -= OnSchematicStudioClosed;
            _schematicStudioWindow = null;
        }
    }

    private void OnWaveformStudioClosed(object? sender, EventArgs e)
    {
        if (_waveformStudioWindow is not null)
        {
            _waveformStudioWindow.Closed -= OnWaveformStudioClosed;
            _waveformStudioWindow = null;
        }
    }

    private async void OnSchematicSignalEditorRequested(object? sender, SchematicPreviewControl.SignalEditorRequestedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        SignalViewModel signal = e.Signal;
        viewModel.SelectedSignal = signal;
        SignalValueEditorViewModel editorViewModel = new(signal.Name, signal.Width, signal.Value);
        SignalValueEditorWindow dialog = new(editorViewModel, canonicalValue =>
        {
            viewModel.SelectedSchematicSignalName = signal.Name;
            viewModel.SchematicDriveValue = canonicalValue;
            viewModel.DriveSelectedSchematicInputCommand.Execute(null);
        });

        await dialog.ShowDialog(this);
    }

    private Button DockButton(string text, DockPanelKind panelKind, DockZone zone) => new()
    {
        Content = text,
        Width = 24,
        Height = 24,
        FontSize = 11,
        Padding = new Thickness(0),
        Background = SurfaceAltBrush,
        Foreground = TextBrush,
        BorderBrush = StrokeBrush,
        Command = CreateDockCommand(panelKind, zone)
    };

    private static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        Foreground = AccentBrush,
        FontSize = 13,
        FontWeight = FontWeight.SemiBold
    };

    private static TextBlock BoundLabel(string path, double size, IBrush brush) => new()
    {
        Foreground = brush,
        FontSize = size,
        TextWrapping = TextWrapping.Wrap,
        [!TextBlock.TextProperty] = new Binding(path)
    };

    private static Control MetadataLine(string label, string path)
    {
        StackPanel row = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        row.Children.Add(new TextBlock
        {
            Text = label,
            Width = 38,
            Foreground = MutedBrush
        });
        row.Children.Add(BoundLabel(path, 12, TextBrush));
        return row;
    }

    private static TextBlock MetadataCaption(string text, int row = 0) => new()
    {
        Text = text,
        Foreground = MutedBrush,
        [Grid.RowProperty] = row
    };

    private static TextBlock MetadataValue(string path, int row, IBrush? brush = null) => new()
    {
        Foreground = brush ?? TextBrush,
        FontFamily = FontFamily.Parse("monospace"),
        [!TextBlock.TextProperty] = new Binding(path),
        [Grid.ColumnProperty] = 1,
        [Grid.RowProperty] = row
    };

    private static Border PanelBorder(Thickness? margin = null) => new()
    {
        Margin = margin ?? new Thickness(0),
        Background = SurfaceBrush,
        BorderBrush = StrokeBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        ClipToBounds = true
    };

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (sender is not MainWindow window)
        {
            return;
        }

        if (e is AvaloniaPropertyChangedEventArgs args
            && args.OldValue is MainWindowViewModel previousViewModel)
        {
            previousViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (window.DataContext is MainWindowViewModel viewModel)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            SyncDockLayout(viewModel);
            if (_schematicStudioWindow is not null)
            {
                _schematicStudioWindow.DataContext = viewModel;
            }

            if (_waveformStudioWindow is not null)
            {
                _waveformStudioWindow.DataContext = viewModel;
            }
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (e.PropertyName is nameof(MainWindowViewModel.ProjectDockZone)
            or nameof(MainWindowViewModel.WaveformDockZone)
            or nameof(MainWindowViewModel.SchematicDockZone)
            or nameof(MainWindowViewModel.SelectedCenterDockPanel)
            or nameof(MainWindowViewModel.LeftDockWidth)
            or nameof(MainWindowViewModel.RightDockWidth)
            or nameof(MainWindowViewModel.BottomDockHeight))
        {
            SyncDockLayout(viewModel);
        }
    }

    private void SyncDockLayout(MainWindowViewModel viewModel)
    {
        RefreshDockHosts(viewModel);
        RefreshCenterWorkspace(viewModel);
        RefreshFloatingToolWindows(viewModel);

        bool hasLeftDock = viewModel.LeftDockPanels.Count > 0;
        bool hasRightDock = viewModel.RightDockPanels.Count > 0;
        bool hasBottomDock = viewModel.BottomDockPanels.Count > 0;

        _leftDockColumn.Width = hasLeftDock ? new GridLength(viewModel.LeftDockWidth) : new GridLength(0);
        _leftDockSplitterColumn.Width = hasLeftDock ? new GridLength(4) : new GridLength(0);
        _rightDockSplitterColumn.Width = hasRightDock ? new GridLength(4) : new GridLength(0);
        _rightDockColumn.Width = hasRightDock ? new GridLength(viewModel.RightDockWidth) : new GridLength(0);
        _bottomDockSplitterRow.Height = hasBottomDock ? new GridLength(4) : new GridLength(0);
        _bottomDockRow.Height = hasBottomDock ? new GridLength(viewModel.BottomDockHeight) : new GridLength(0);

        if (_leftDockPane is not null)
        {
            _leftDockPane.IsVisible = hasLeftDock;
        }

        if (_leftDockSplitter is not null)
        {
            _leftDockSplitter.IsVisible = hasLeftDock;
        }

        if (_rightDockPane is not null)
        {
            _rightDockPane.IsVisible = hasRightDock;
        }

        if (_rightDockSplitter is not null)
        {
            _rightDockSplitter.IsVisible = hasRightDock;
        }

        if (_bottomDockPane is not null)
        {
            _bottomDockPane.IsVisible = hasBottomDock;
        }

        if (_bottomDockSplitter is not null)
        {
            _bottomDockSplitter.IsVisible = hasBottomDock;
        }
    }

    private void RefreshDockHosts(MainWindowViewModel viewModel)
    {
        _leftDockTabs = PopulateDockHost(_leftDockPane, viewModel.LeftDockPanels, viewModel.SelectedLeftDockPanel);
        _rightDockTabs = PopulateDockHost(_rightDockPane, viewModel.RightDockPanels, viewModel.SelectedRightDockPanel);
        _bottomDockTabs = PopulateDockHost(_bottomDockPane, viewModel.BottomDockPanels, viewModel.SelectedBottomDockPanel);
    }

    private void RefreshCenterWorkspace(MainWindowViewModel viewModel)
    {
        if (_centerWorkspacePane is null)
        {
            return;
        }

        if (viewModel.CenterDockPanels.Count == 0)
        {
            Control inspector = BuildInspectorSurface();
            inspector.DataContext = DataContext;
            _centerWorkspacePane.Child = inspector;
            _centerWorkspaceTabs = null;
            return;
        }

        TabControl tabControl = new()
        {
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Margin = new Thickness(0)
        };

        List<TabItem> items =
        [
            new()
            {
                Header = new TextBlock
                {
                    Text = "Inspector",
                    Foreground = TextBrush,
                    FontSize = 12
                },
                Content = BindCenterContent(BuildInspectorSurface())
            }
        ];

        foreach (DockPanelViewModel panel in viewModel.CenterDockPanels)
        {
            TabItem item = new()
            {
                Header = new TextBlock
                {
                    Text = panel.Title,
                    Foreground = TextBrush,
                    FontSize = 12
                },
                Content = BindCenterContent(BuildPanelSurface(panel.Kind)),
                DataContext = panel
            };
            items.Add(item);

            if (viewModel.SelectedCenterDockPanel?.Kind == panel.Kind)
            {
                tabControl.SelectedItem = item;
            }
        }

        if (tabControl.SelectedItem is null)
        {
            tabControl.SelectedIndex = Math.Clamp(items.Count - 1, 0, items.Count - 1);
        }

        tabControl.SelectionChanged += (_, _) =>
        {
            if (DataContext is not MainWindowViewModel currentViewModel)
            {
                return;
            }

            currentViewModel.SelectedCenterDockPanel = (tabControl.SelectedItem as TabItem)?.DataContext as DockPanelViewModel;
        };

        tabControl.ItemsSource = items;
        _centerWorkspacePane.Child = tabControl;
        _centerWorkspaceTabs = tabControl;
    }

    private Control BindCenterContent(Control control)
    {
        control.DataContext = DataContext;
        return control;
    }

    private void RefreshFloatingToolWindows(MainWindowViewModel viewModel)
    {
        DockPanelKind[] allPanels = [DockPanelKind.Project, DockPanelKind.Waveform, DockPanelKind.Schematic];
        foreach (DockPanelKind kind in allPanels)
        {
            DockZone zone = GetDockZone(viewModel, kind);
            if (zone == DockZone.Floating)
            {
                EnsureFloatingToolWindow(viewModel, kind);
            }
            else if (_floatingToolWindows.TryGetValue(kind, out ToolPanelWindow? window))
            {
                _floatingToolWindows.Remove(kind);
                window.Close();
            }
        }
    }

    private void EnsureFloatingToolWindow(MainWindowViewModel viewModel, DockPanelKind kind)
    {
        if (_floatingToolWindows.TryGetValue(kind, out ToolPanelWindow? existing))
        {
            existing.DataContext = DataContext;
            if (!existing.IsVisible)
            {
                existing.Show(this);
            }

            return;
        }

        Control content = BuildPanelSurface(kind);
        content.DataContext = DataContext;
        ToolPanelWindow window = new(kind, GetPanelTitle(kind), content)
        {
            DataContext = DataContext
        };
        window.Closed += OnFloatingToolWindowClosed;
        _floatingToolWindows[kind] = window;
        window.Show(this);
    }

    private static string GetPanelTitle(DockPanelKind kind) =>
        kind switch
        {
            DockPanelKind.Project => "Project",
            DockPanelKind.Waveform => "Waveform",
            DockPanelKind.Schematic => "Schematic",
            _ => kind.ToString()
        };

    private void OnFloatingToolWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not ToolPanelWindow window)
        {
            return;
        }

        window.Closed -= OnFloatingToolWindowClosed;
        _floatingToolWindows.Remove(window.PanelKind);

        if (DataContext is MainWindowViewModel viewModel && GetDockZone(viewModel, window.PanelKind) == DockZone.Floating)
        {
            ExecuteDockCommand(window.PanelKind, DockZone.Hidden);
        }
    }

    private static DockZone GetDockZone(MainWindowViewModel viewModel, DockPanelKind kind) =>
        kind switch
        {
            DockPanelKind.Project => viewModel.ProjectDockZone,
            DockPanelKind.Waveform => viewModel.WaveformDockZone,
            DockPanelKind.Schematic => viewModel.SchematicDockZone,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

    private TabControl? PopulateDockHost(Border? host, IReadOnlyList<DockPanelViewModel> panels, DockPanelViewModel? selectedPanel)
    {
        if (host is null)
        {
            return null;
        }

        if (panels.Count == 0)
        {
            host.Child = null;
            return null;
        }

        TabControl tabControl = new()
        {
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Margin = new Thickness(0)
        };

        List<TabItem> items = [];
        foreach (DockPanelViewModel panel in panels)
        {
            Control content = BuildPanelSurface(panel.Kind);
            content.DataContext = DataContext;
            TabItem item = new()
            {
                Header = new TextBlock
                {
                    Text = panel.Title,
                    Foreground = TextBrush,
                    FontSize = 12
                },
                Content = content,
                DataContext = panel
            };
            items.Add(item);
            if (selectedPanel is not null && selectedPanel.Kind == panel.Kind)
            {
                tabControl.SelectedItem = item;
            }
        }

        if (tabControl.SelectedItem is null && items.Count > 0)
        {
            tabControl.SelectedIndex = 0;
        }

        tabControl.ItemsSource = items;
        host.Child = tabControl;
        return tabControl;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        double leftWidth = _leftDockPane?.Bounds.Width > 0 ? _leftDockPane.Bounds.Width : viewModel.LeftDockWidth;
        double rightWidth = _rightDockPane?.Bounds.Width > 0 ? _rightDockPane.Bounds.Width : viewModel.RightDockWidth;
        double bottomHeight = _bottomDockPane?.Bounds.Height > 0 ? _bottomDockPane.Bounds.Height : viewModel.BottomDockHeight;
        viewModel.UpdateLayoutMetrics(leftWidth, rightWidth, bottomHeight);
        foreach (ToolPanelWindow floatingWindow in _floatingToolWindows.Values.ToArray())
        {
            floatingWindow.Close();
        }

        _floatingToolWindows.Clear();
        _schematicStudioWindow?.Close();
        _waveformStudioWindow?.Close();
    }
}
