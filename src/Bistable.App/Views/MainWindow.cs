using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Bistable.App.ViewModels;

namespace Bistable.App.Views;

public sealed class MainWindow : Window
{
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
        root.Children.Add(BuildWorkspace());

        return root;
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

        _leftDockPane = BuildDockHost("LeftDockPanels", "SelectedLeftDockPanel");
        grid.Children.Add(_leftDockPane);

        _leftDockSplitter = new GridSplitter
        {
            Width = 4,
            Background = StrokeBrush,
            ResizeDirection = GridResizeDirection.Columns,
            [Grid.ColumnProperty] = 1
        };
        grid.Children.Add(_leftDockSplitter);

        Control inspector = BuildInspectorSurface();
        Grid.SetColumn(inspector, 2);
        grid.Children.Add(inspector);

        _rightDockSplitter = new GridSplitter
        {
            Width = 4,
            Background = StrokeBrush,
            ResizeDirection = GridResizeDirection.Columns,
            [Grid.ColumnProperty] = 3
        };
        grid.Children.Add(_rightDockSplitter);

        _rightDockPane = BuildDockHost("RightDockPanels", "SelectedRightDockPanel");
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

        _bottomDockPane = BuildDockHost("BottomDockPanels", "SelectedBottomDockPanel");
        Grid.SetColumnSpan(_bottomDockPane, 5);
        Grid.SetRow(_bottomDockPane, 2);
        grid.Children.Add(_bottomDockPane);

        return grid;
    }

    private static Control BuildToolbar()
    {
        Border border = PanelBorder();
        DockPanel.SetDock(border, Dock.Top);

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
                    DockMenu("Project Pane",
                        "IsProjectPaneVisible",
                        "ToggleProjectPaneCommand",
                        "MoveProjectPaneLeftCommand",
                        "MoveProjectPaneRightCommand",
                        "MoveProjectPaneBottomCommand",
                        "HideProjectPaneCommand"),
                    DockMenu("Waveform Pane",
                        "IsWaveformPaneVisible",
                        "ToggleWaveformPaneCommand",
                        "MoveWaveformPaneLeftCommand",
                        "MoveWaveformPaneRightCommand",
                        "MoveWaveformPaneBottomCommand",
                        "HideWaveformPaneCommand")
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
        DockPanel.SetDock(border, Dock.Bottom);
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

    private static Border BuildDockHost(string itemsPath, string selectedItemPath)
    {
        Border host = PanelBorder(new Thickness(12, 12, 12, 12));
        TabControl tabControl = new()
        {
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Margin = new Thickness(0),
            [!ItemsControl.ItemsSourceProperty] = new Binding(itemsPath),
            [!SelectingItemsControl.SelectedItemProperty] = new Binding(selectedItemPath, BindingMode.TwoWay),
            ItemTemplate = new FuncDataTemplate<DockPanelViewModel>((panel, _) =>
                panel is null
                    ? new TextBlock()
                    : new TextBlock
                    {
                        Text = panel.Title,
                        Foreground = TextBrush,
                        FontSize = 12
                    }),
            ContentTemplate = new FuncDataTemplate<DockPanelViewModel>((panel, _) =>
                panel is null
                    ? new TextBlock()
                    : BuildPanelSurface(panel.Kind))
        };
        host.Child = tabControl;
        return host;
    }

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

    private static Control BuildProjectPanelContent()
    {
        StackPanel panel = new()
        {
            Spacing = 12,
            Margin = new Thickness(12)
        };

        panel.Children.Add(DockPanelHeader(
            "Project",
            "MoveProjectPaneLeftCommand",
            "MoveProjectPaneRightCommand",
            "MoveProjectPaneBottomCommand",
            "HideProjectPaneCommand"));
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

        return panel;
    }

    private static Control BuildWaveformPanelContent()
    {
        Grid waveform = new()
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            },
            Margin = new Thickness(12)
        };

        waveform.Children.Add(DockPanelHeader(
            "Waveform",
            "MoveWaveformPaneLeftCommand",
            "MoveWaveformPaneRightCommand",
            "MoveWaveformPaneBottomCommand",
            "HideWaveformPaneCommand"));

        Control toolbar = BuildWaveformToolbar();
        Grid.SetRow(toolbar, 1);
        waveform.Children.Add(toolbar);

        WaveformPreviewControl preview = new()
        {
            ClipToBounds = true,
            Margin = new Thickness(0, 10, 0, 0),
            [!WaveformPreviewControl.LanesProperty] = new Binding("WaveformLanes"),
            [!WaveformPreviewControl.ZoomProperty] = new Binding("WaveformZoom"),
            [!WaveformPreviewControl.OffsetProperty] = new Binding("WaveformOffset"),
            [!WaveformPreviewControl.SelectedSignalNameProperty] = new Binding("SelectedWaveformSignalName", BindingMode.TwoWay),
            [!WaveformPreviewControl.CursorOrderProperty] = new Binding("WaveformCursorOrder", BindingMode.TwoWay)
        };
        Grid.SetRow(preview, 2);
        waveform.Children.Add(preview);
        return waveform;
    }

    private static Control BuildWaveformToolbar()
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
                SmallButton("Up", "MoveWaveformSignalUpCommand"),
                SmallButton("Down", "MoveWaveformSignalDownCommand"),
                SmallButton("<", "PanWaveformLeftCommand"),
                SmallButton(">", "PanWaveformRightCommand"),
                SmallButton("Zoom +", "ZoomWaveformInCommand"),
                SmallButton("Zoom -", "ZoomWaveformOutCommand"),
                SmallButton("Fit", "FitWaveformCommand")
            }
        };
        DockPanel.SetDock(actions, Dock.Right);
        toolbar.Children.Add(actions);
        return toolbar;
    }

    private static Control BuildPanelSurface(DockPanelKind kind)
    {
        Border border = PanelBorder(new Thickness(0));
        border.CornerRadius = new CornerRadius(0);
        border.BorderThickness = new Thickness(0);
        border.Child = kind switch
        {
            DockPanelKind.Project => BuildProjectPanelContent(),
            DockPanelKind.Waveform => BuildWaveformPanelContent(),
            _ => new TextBlock { Text = "Unknown panel", Foreground = MutedBrush }
        };
        return border;
    }

    private static Control DockPanelHeader(
        string title,
        string moveLeftCommand,
        string moveRightCommand,
        string moveBottomCommand,
        string hideCommand)
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
                TinyButton("L", moveLeftCommand),
                TinyButton("R", moveRightCommand),
                TinyButton("B", moveBottomCommand),
                TinyButton("X", hideCommand)
            }
        };
        DockPanel.SetDock(actions, Dock.Right);
        row.Children.Add(actions);
        return row;
    }

    private static MenuItem DockMenu(
        string header,
        string visiblePath,
        string toggleCommandPath,
        string moveLeftCommandPath,
        string moveRightCommandPath,
        string moveBottomCommandPath,
        string hideCommandPath) =>
        new()
        {
            Header = header,
            ItemsSource = new Control[]
            {
                new MenuItem
                {
                    Header = "Visible",
                    ToggleType = MenuItemToggleType.CheckBox,
                    [!MenuItem.IsCheckedProperty] = new Binding(visiblePath, BindingMode.TwoWay),
                    [!MenuItem.CommandProperty] = new Binding(toggleCommandPath)
                },
                new Separator(),
                new MenuItem
                {
                    Header = "Dock Left",
                    [!MenuItem.CommandProperty] = new Binding(moveLeftCommandPath)
                },
                new MenuItem
                {
                    Header = "Dock Right",
                    [!MenuItem.CommandProperty] = new Binding(moveRightCommandPath)
                },
                new MenuItem
                {
                    Header = "Dock Bottom",
                    [!MenuItem.CommandProperty] = new Binding(moveBottomCommandPath)
                },
                new Separator(),
                new MenuItem
                {
                    Header = "Hide",
                    [!MenuItem.CommandProperty] = new Binding(hideCommandPath)
                }
            }
        };

    private static IDataTemplate SignalListTemplate() => new FuncDataTemplate<SignalViewModel>((signal, _) =>
        signal is null
            ? new TextBlock()
            : new TextBlock
            {
                Text = $"{signal.DirectionLabel,-6} {signal.Name}[{signal.WidthLabel}]",
                Foreground = TextBrush,
                FontFamily = FontFamily.Parse("monospace"),
                Margin = new Thickness(0, 4)
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

    private static Border PanelBorder(Thickness? margin = null) => new()
    {
        Margin = margin ?? new Thickness(0),
        Background = SurfaceBrush,
        BorderBrush = StrokeBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6)
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
            or nameof(MainWindowViewModel.LeftDockWidth)
            or nameof(MainWindowViewModel.RightDockWidth)
            or nameof(MainWindowViewModel.BottomDockHeight))
        {
            SyncDockLayout(viewModel);
        }
    }

    private void SyncDockLayout(MainWindowViewModel viewModel)
    {
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
    }
}
