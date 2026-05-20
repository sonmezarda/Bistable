using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Bistable.App.ViewModels;

namespace Bistable.App.Views;

public sealed class WaveformStudioWindow : Window
{
    private static readonly IBrush BackgroundBrush = SolidColorBrush.Parse("#0e1116");
    private static readonly IBrush SurfaceBrush = SolidColorBrush.Parse("#151922");
    private static readonly IBrush SurfaceAltBrush = SolidColorBrush.Parse("#1b202b");
    private static readonly IBrush StrokeBrush = SolidColorBrush.Parse("#2a3241");
    private static readonly IBrush TextBrush = SolidColorBrush.Parse("#d7dde8");
    private static readonly IBrush MutedBrush = SolidColorBrush.Parse("#8f9aad");
    private static readonly IBrush AccentBrush = SolidColorBrush.Parse("#57c7ff");
    private static readonly IBrush GreenBrush = SolidColorBrush.Parse("#65d889");

    public WaveformStudioWindow(MainWindowViewModel viewModel)
    {
        Title = "Bistable Waveform Studio";
        Width = 1640;
        Height = 980;
        MinWidth = 1180;
        MinHeight = 720;
        Background = BackgroundBrush;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        DataContext = viewModel;
        Content = BuildLayout();
    }

    private Control BuildLayout()
    {
        Grid root = new()
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            },
            Margin = new Thickness(12)
        };

        root.Children.Add(BuildHeader());

        Grid content = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(320)),
                new ColumnDefinition(new GridLength(8)),
                new ColumnDefinition(GridLength.Star)
            },
            Margin = new Thickness(0, 12, 0, 0),
            [Grid.RowProperty] = 1
        };

        Border lanesBorder = PanelBorder();
        lanesBorder.Child = BuildLaneList();
        content.Children.Add(lanesBorder);

        GridSplitter splitter = new()
        {
            Width = 8,
            Background = Brushes.Transparent,
            ResizeDirection = GridResizeDirection.Columns,
            [Grid.ColumnProperty] = 1
        };
        content.Children.Add(splitter);

        Border previewBorder = PanelBorder();
        previewBorder.Child = BuildPreview();
        Grid.SetColumn(previewBorder, 2);
        content.Children.Add(previewBorder);

        root.Children.Add(content);
        return root;
    }

    private Control BuildHeader()
    {
        Border border = PanelBorder();
        border.Child = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Margin = new Thickness(12, 10),
            Children =
            {
                new TextBlock
                {
                    Text = "Waveform Studio",
                    Foreground = AccentBrush,
                    FontSize = 16,
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                },
                new TextBlock
                {
                    Foreground = MutedBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(16, 0, 0, 0),
                    [!TextBlock.TextProperty] = new Binding("WaveformCursorSummary"),
                    [Grid.ColumnProperty] = 1
                },
                new StackPanel
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
                    },
                    [Grid.ColumnProperty] = 2
                }
            }
        };

        return border;
    }

    private static Control BuildLaneList()
    {
        Grid grid = new()
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            },
            Margin = new Thickness(12)
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

    private static Control BuildPreview()
    {
        Grid grid = new()
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            },
            Margin = new Thickness(12)
        };

        grid.Children.Add(new TextBlock
        {
            Text = "Wheel zooms. Shift + wheel pans. Drag to scrub.",
            Foreground = MutedBrush
        });

        WaveformPreviewControl preview = new()
        {
            ClipToBounds = true,
            Margin = new Thickness(0, 12, 0, 0),
            [!WaveformPreviewControl.LanesProperty] = new Binding("WaveformLanes"),
            [!WaveformPreviewControl.ZoomProperty] = new Binding("WaveformZoom"),
            [!WaveformPreviewControl.OffsetProperty] = new Binding("WaveformOffset"),
            [!WaveformPreviewControl.SelectedSignalNameProperty] = new Binding("SelectedWaveformSignalName", BindingMode.TwoWay),
            [!WaveformPreviewControl.CursorOrderProperty] = new Binding("WaveformCursorOrder", BindingMode.TwoWay)
        };
        Grid.SetRow(preview, 1);
        grid.Children.Add(preview);

        return grid;
    }

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

    private static Border PanelBorder(Thickness? margin = null) => new()
    {
        Margin = margin ?? new Thickness(0),
        Background = SurfaceBrush,
        BorderBrush = StrokeBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        ClipToBounds = true
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
}
