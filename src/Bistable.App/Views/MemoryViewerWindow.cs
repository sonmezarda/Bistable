using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Bistable.App.Services;
using Bistable.App.ViewModels;

namespace Bistable.App.Views;

/// <summary>
/// Standalone hex-editor-style memory viewer. Excel-like grid with one row
/// per N cells (configurable), an address column on the left, and a hex value
/// per column. Re-reads automatically when the worker's
/// <see cref="Services.LiveProbeService.MemoryUpdated"/> fires after a Tick/Eval.
/// </summary>
public sealed class MemoryViewerWindow : Window
{
    private static readonly IBrush BackgroundBrush = SolidColorBrush.Parse("#0e141c");
    private static readonly IBrush SurfaceBrush = SolidColorBrush.Parse("#15202c");
    private static readonly IBrush TextBrush = SolidColorBrush.Parse("#d6e1f0");
    private static readonly IBrush MutedBrush = SolidColorBrush.Parse("#7f8da5");
    private static readonly IBrush GreenBrush = SolidColorBrush.Parse("#65d889");
    private static readonly IBrush AccentBrush = SolidColorBrush.Parse("#5dbcff");
    private static readonly IBrush StrokeBrush = SolidColorBrush.Parse("#243345");
    private static readonly FontFamily MonoFont = FontFamily.Parse("monospace");

    private readonly MemoryViewerWindowViewModel _viewModel;

    public MemoryViewerWindow(MemoryViewerWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        Title = viewModel.Title;
        Width = 920;
        Height = 600;
        Background = BackgroundBrush;
        Content = BuildLayout();
        Closed += (_, _) => _viewModel.Detach();
        // P2.7-mem-load: dialog stays in the view so we can reach the window
        // handle for StorageProvider. ViewModel raises the request; we open the
        // picker, then call back into LoadFromFileAsync with the chosen path.
        viewModel.LoadFromFileRequested += async (_, _) => await OnLoadFromFileRequestedAsync();
    }

    private async Task OnLoadFromFileRequestedAsync()
    {
        IStorageProvider? storage = StorageProvider;
        if (storage is null) return;
        FilePickerOpenOptions options = new()
        {
            Title = "Load memory image",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Memory image (*.hex; *.bin; *.mem; *.txt)")
                {
                    Patterns = new[] { "*.hex", "*.bin", "*.mem", "*.txt" }
                },
                new FilePickerFileType("All files") { Patterns = new[] { "*" } }
            }
        };
        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(options);
        if (files.Count == 0) return;
        string? path = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;
        await _viewModel.LoadFromFileAsync(path, CancellationToken.None);
    }

    private Control BuildLayout()
    {
        DockPanel root = new() { LastChildFill = true };
        root.Children.Add(BuildToolbar());
        root.Children.Add(BuildStatusBar());
        root.Children.Add(BuildGrid());
        return root;
    }

    private static Control BuildToolbar()
    {
        Border toolbar = new()
        {
            Background = SurfaceBrush,
            BorderBrush = StrokeBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 8, 12, 8)
        };
        StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 10 };

        row.Children.Add(new TextBlock
        {
            Foreground = AccentBrush,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            [!TextBlock.TextProperty] = new Binding("Subtitle")
        });

        row.Children.Add(new TextBlock
        {
            Text = "  Columns/row:",
            Foreground = MutedBrush,
            VerticalAlignment = VerticalAlignment.Center
        });
        ComboBox columnsBox = new()
        {
            Width = 70,
            [!ComboBox.ItemsSourceProperty] = new Binding("ColumnsPerRowOptions"),
            [!ComboBox.SelectedItemProperty] = new Binding("ColumnsPerRow", BindingMode.TwoWay)
        };
        row.Children.Add(columnsBox);

        row.Children.Add(new TextBlock
        {
            Text = "  Jump:",
            Foreground = MutedBrush,
            VerticalAlignment = VerticalAlignment.Center
        });
        TextBox jumpBox = new()
        {
            Width = 110,
            MinHeight = 30,
            FontFamily = MonoFont,
            Foreground = TextBrush,
            Background = BackgroundBrush,
            BorderBrush = StrokeBrush,
            Watermark = "0x000",
            [!TextBox.TextProperty] = new Binding("JumpAddressText", BindingMode.TwoWay)
        };
        row.Children.Add(jumpBox);
        Button jumpButton = new()
        {
            Content = "Go",
            Padding = new Thickness(10, 4, 10, 4),
            [!Button.CommandProperty] = new Binding("JumpToAddressCommand")
        };
        row.Children.Add(jumpButton);

        Button reload = new()
        {
            Content = "Reload",
            Padding = new Thickness(10, 4, 10, 4),
            [!Button.CommandProperty] = new Binding("ReloadCommand")
        };
        row.Children.Add(reload);

        // P2.7-mem-load: format combo + Load File button. Format combo lets the
        // user pick between $readmemh and $readmemb without renaming files.
        row.Children.Add(new TextBlock
        {
            Text = "  Format:",
            Foreground = MutedBrush,
            VerticalAlignment = VerticalAlignment.Center
        });
        ComboBox formatBox = new()
        {
            Width = 70,
            [!ComboBox.ItemsSourceProperty] = new Binding(nameof(MemoryViewerWindowViewModel.AvailableFormats)),
            [!ComboBox.SelectedItemProperty] = new Binding(nameof(MemoryViewerWindowViewModel.SelectedFormat), BindingMode.TwoWay)
        };
        row.Children.Add(formatBox);

        Button loadFile = new()
        {
            Content = "Load File…",
            Padding = new Thickness(10, 4, 10, 4),
            [!Button.CommandProperty] = new Binding(nameof(MemoryViewerWindowViewModel.LoadFromFileCommand))
        };
        row.Children.Add(loadFile);

        toolbar.Child = row;
        DockPanel.SetDock(toolbar, Avalonia.Controls.Dock.Top);
        return toolbar;
    }

    private static Control BuildStatusBar()
    {
        Border bar = new()
        {
            Background = SurfaceBrush,
            BorderBrush = StrokeBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 4, 12, 4)
        };
        bar.Child = new TextBlock
        {
            Foreground = MutedBrush,
            FontSize = 11,
            [!TextBlock.TextProperty] = new Binding("Status")
        };
        DockPanel.SetDock(bar, Avalonia.Controls.Dock.Bottom);
        return bar;
    }

    private Control BuildGrid()
    {
        // Single ScrollViewer wraps both the column header AND the rows so the
        // address column stays aligned. We achieve column alignment by using a
        // monospace font + fixed-width address column + a shared column-template
        // builder for the cells.
        ScrollViewer scroll = new()
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Padding = new Thickness(4, 4, 4, 4)
        };

        StackPanel column = new() { Orientation = Orientation.Vertical };

        // Column header — re-bind so it rebuilds when ColumnsPerRow changes.
        // Trick: use ItemsControl + ColumnHeaders collection.
        column.Children.Add(BuildHeaderRow());
        column.Children.Add(BuildRowsList());

        scroll.Content = column;
        return scroll;
    }

    private static Control BuildHeaderRow()
    {
        StackPanel header = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 0,
            Margin = new Thickness(0, 0, 0, 4)
        };
        header.Children.Add(new TextBlock
        {
            Text = "Address",
            Width = 80,
            Foreground = AccentBrush,
            FontFamily = MonoFont,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Padding = new Thickness(4, 2, 4, 2)
        });
        ItemsControl headerCols = new()
        {
            ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel { Orientation = Orientation.Horizontal }),
            [!ItemsControl.ItemsSourceProperty] = new Binding("ColumnHeaders"),
            ItemTemplate = new FuncDataTemplate<string>((label, _) => new TextBlock
            {
                Text = label ?? string.Empty,
                Width = 56,
                Foreground = AccentBrush,
                FontFamily = MonoFont,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Padding = new Thickness(4, 2, 4, 2),
                TextAlignment = TextAlignment.Right
            })
        };
        header.Children.Add(headerCols);
        return header;
    }

    private static Control BuildRowsList()
    {
        ItemsControl rows = new()
        {
            [!ItemsControl.ItemsSourceProperty] = new Binding("Rows"),
            ItemTemplate = new FuncDataTemplate<MemoryRowViewModel>((row, _) => BuildRow(row))
        };
        return rows;
    }

    private static Control BuildRow(MemoryRowViewModel? row)
    {
        if (row is null) return new TextBlock();
        StackPanel panel = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 0,
            Margin = new Thickness(0, 1, 0, 1)
        };
        panel.Children.Add(new TextBlock
        {
            Text = row.BaseAddressLabel,
            Width = 80,
            Foreground = MutedBrush,
            FontFamily = MonoFont,
            FontSize = 11,
            Padding = new Thickness(4, 2, 4, 2)
        });
        ItemsControl cellsList = new()
        {
            ItemsSource = row.Cells,
            ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel { Orientation = Orientation.Horizontal }),
            ItemTemplate = new FuncDataTemplate<MemoryCellEditViewModel>(BuildCellEditor)
        };
        panel.Children.Add(cellsList);
        return panel;
    }

    /// <summary>
    /// Editable cell — hex value bound two-way. Enter commits the write via
    /// the cell VM's <see cref="MemoryCellEditViewModel.CommitAsync"/>.
    /// Focus loss also commits so the user can tab through cells.
    /// </summary>
    private static Control BuildCellEditor(MemoryCellEditViewModel? cell, Avalonia.Controls.INameScope _)
    {
        if (cell is null) return new TextBlock();
        TextBox box = new()
        {
            Width = 56,
            MinHeight = 22,
            Padding = new Thickness(4, 1, 4, 1),
            Foreground = GreenBrush,
            FontFamily = MonoFont,
            FontSize = 11,
            TextAlignment = TextAlignment.Right,
            Background = SolidColorBrush.Parse("#0e141c"),
            BorderBrush = StrokeBrush,
            BorderThickness = new Thickness(1),
            [!TextBox.TextProperty] = new Binding(nameof(MemoryCellEditViewModel.HexValue), BindingMode.TwoWay)
            {
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            }
        };
        box.KeyDown += async (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Return || e.Key == Avalonia.Input.Key.Enter)
            {
                e.Handled = true;
                await cell.CommitAsync(CancellationToken.None);
            }
        };
        box.LostFocus += async (_, _) => await cell.CommitAsync(CancellationToken.None);
        return box;
    }
}
