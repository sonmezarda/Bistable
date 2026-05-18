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
        Width = 1280;
        Height = 820;
        MinWidth = 1040;
        MinHeight = 680;
        Background = BackgroundBrush;
        Content = BuildLayout();
    }

    private static Control BuildLayout()
    {
        DockPanel root = new()
        {
            LastChildFill = true
        };

        root.Children.Add(BuildToolbar());
        root.Children.Add(BuildStatusBar());
        root.Children.Add(BuildMainGrid());

        return root;
    }

    private static Control BuildToolbar()
    {
        Border border = PanelBorder();
        DockPanel.SetDock(border, Dock.Top);

        StackPanel row = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(12, 10)
        };

        row.Children.Add(new TextBlock
        {
            Text = "Bistable",
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextBrush,
            Width = 170,
            VerticalAlignment = VerticalAlignment.Center
        });

        row.Children.Add(OpenMenuButton());
        row.Children.Add(ToolbarButton("Build", "BuildCommand"));
        row.Children.Add(ToolbarButton("Eval", "EvalCommand"));
        row.Children.Add(ToolbarButton("Tick", "TickCommand"));
        row.Children.Add(ToolbarButton("Run 10", "RunCyclesCommand"));
        row.Children.Add(ToolbarButton("Reset", "ResetCommand"));

        border.Child = row;
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

    private static Control BuildMainGrid()
    {
        Grid grid = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(260)),
                new ColumnDefinition(new GridLength(4)),
                new ColumnDefinition(GridLength.Star)
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(new GridLength(4)),
                new RowDefinition(new GridLength(280))
            }
        };

        grid.Children.Add(BuildProjectPanel());
        grid.Children.Add(new GridSplitter
        {
            Width = 4,
            Background = StrokeBrush,
            ResizeDirection = GridResizeDirection.Columns,
            [Grid.ColumnProperty] = 1,
            [Grid.RowSpanProperty] = 3
        });
        grid.Children.Add(BuildIoPanel());
        grid.Children.Add(new GridSplitter
        {
            Height = 4,
            Background = StrokeBrush,
            ResizeDirection = GridResizeDirection.Rows,
            [Grid.ColumnProperty] = 2,
            [Grid.RowProperty] = 1
        });
        grid.Children.Add(BuildWaveformPanel());

        return grid;
    }

    private static Control BuildProjectPanel()
    {
        Border border = PanelBorder(new Thickness(12, 12, 6, 6));
        Grid.SetRowSpan(border, 3);

        StackPanel panel = new()
        {
            Spacing = 12,
            Margin = new Thickness(12)
        };

        panel.Children.Add(SectionTitle("Project"));
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

        border.Child = panel;
        return border;
    }

    private static Control BuildIoPanel()
    {
        Grid grid = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            Margin = new Thickness(6, 12, 12, 6),
            ColumnSpacing = 10
        };
        Grid.SetColumn(grid, 2);

        Control inputs = BuildSignalTable("Inputs", "Inputs", true);
        Control outputs = BuildSignalTable("Outputs", "Outputs", false);

        grid.Children.Add(inputs);
        Grid.SetColumn(outputs, 1);
        grid.Children.Add(outputs);

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

    private static Control BuildWaveformPanel()
    {
        Border border = PanelBorder(new Thickness(6, 6, 12, 12));
        Grid.SetColumn(border, 2);
        Grid.SetRow(border, 2);

        Grid waveform = new()
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            },
            Margin = new Thickness(12)
        };

        waveform.Children.Add(BuildWaveformToolbar());
        WaveformPreviewControl preview = new()
        {
            ClipToBounds = true,
            Margin = new Thickness(0, 10, 0, 0),
            [!WaveformPreviewControl.EventsProperty] = new Binding("RecentWaveformEvents"),
            [!WaveformPreviewControl.SignalsProperty] = new Binding("WaveformSignals"),
            [!WaveformPreviewControl.ZoomProperty] = new Binding("WaveformZoom")
        };
        Grid.SetRow(preview, 1);
        waveform.Children.Add(preview);
        border.Child = waveform;
        return border;
    }

    private static Control BuildWaveformToolbar()
    {
        DockPanel toolbar = new()
        {
            LastChildFill = false
        };

        toolbar.Children.Add(SectionTitle("Waveform"));
        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                SmallButton("Up", "MoveWaveformSignalUpCommand"),
                SmallButton("Down", "MoveWaveformSignalDownCommand"),
                SmallButton("Zoom +", "ZoomWaveformInCommand"),
                SmallButton("Zoom -", "ZoomWaveformOutCommand"),
                SmallButton("Fit", "FitWaveformCommand")
            }
        };
        DockPanel.SetDock(actions, Dock.Right);
        toolbar.Children.Add(actions);
        return toolbar;
    }

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

    private static Menu OpenMenuButton()
    {
        Menu menu = new()
        {
            Background = Brushes.Transparent,
            Foreground = TextBrush,
            ItemsSource = new Control[]
            {
                new MenuItem
                {
                    Header = "Open",
                    Background = SurfaceAltBrush,
                    Foreground = TextBrush,
                    BorderBrush = StrokeBrush,
                    ItemsSource = new Control[]
                    {
                        new MenuItem
                        {
                            Header = "Project...",
                            [!MenuItem.CommandProperty] = new Binding("LoadProjectCommand")
                        },
                        new Separator(),
                        new MenuItem
                        {
                            Header = "Samples",
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
                    }
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
}
