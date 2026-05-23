using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Bistable.App.Services;
using Bistable.App.ViewModels;

namespace Bistable.App.Views;

public sealed class SchematicStudioWindow : Window
{
    private static readonly IBrush BackgroundBrush = SolidColorBrush.Parse("#0e1116");
    private static readonly IBrush SurfaceBrush = SolidColorBrush.Parse("#151922");
    private static readonly IBrush SurfaceAltBrush = SolidColorBrush.Parse("#1b202b");
    private static readonly IBrush StrokeBrush = SolidColorBrush.Parse("#2a3241");
    private static readonly IBrush TextBrush = SolidColorBrush.Parse("#d7dde8");
    private static readonly IBrush MutedBrush = SolidColorBrush.Parse("#8f9aad");
    private static readonly IBrush AccentBrush = SolidColorBrush.Parse("#57c7ff");
    private static readonly IBrush GreenBrush = SolidColorBrush.Parse("#65d889");

    public SchematicStudioWindow(MainWindowViewModel viewModel)
    {
        Title = "Bistable Schematic Studio";
        Width = 1680;
        Height = 1040;
        MinWidth = 1200;
        MinHeight = 760;
        Background = BackgroundBrush;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        DataContext = viewModel;
        Content = BuildLayout();
        KeyDown += OnStudioKeyDown;
    }

    private Control BuildLayout()
    {
        SchematicPreviewControl preview = CreateBoundSchematicPreview();

        Grid root = new()
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            },
            Margin = new Thickness(12)
        };

        root.Children.Add(BuildHeader(preview));

        Grid content = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(280)),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(300))
            },
            ColumnSpacing = 10,
            Margin = new Thickness(0, 12, 0, 0),
            [Grid.RowProperty] = 1
        };

        Border scopeBorder = PanelBorder();
        scopeBorder.Child = BuildScopeSidebar();
        content.Children.Add(scopeBorder);

        Grid previewArea = new()
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            }
        };
        previewArea.Children.Add(BuildViewportToolbar(preview));

        Border previewBorder = PanelBorder(new Thickness(0, 10, 0, 0));
        previewBorder.Child = preview;
        Grid.SetRow(previewBorder, 1);
        previewArea.Children.Add(previewBorder);

        Grid.SetColumn(previewArea, 1);
        content.Children.Add(previewArea);

        Border probeBorder = PanelBorder();
        probeBorder.Child = BuildProbePanel();
        Grid.SetColumn(probeBorder, 2);
        content.Children.Add(probeBorder);

        root.Children.Add(content);
        return root;
    }

    private Control BuildHeader(SchematicPreviewControl preview)
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
                    Text = "Schematic Studio",
                    Foreground = AccentBrush,
                    FontSize = 16,
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                },
                BuildBreadcrumbBar(),
                BuildHeaderActions(preview)
            }
        };

        return border;
    }

    private Control BuildHeaderActions(SchematicPreviewControl preview)
    {
        TextBlock zoomText = new()
        {
            Text = "Fit",
            Foreground = MutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 54,
            TextAlignment = TextAlignment.Right
        };
        preview.ViewportChanged += (_, args) => zoomText.Text = $"{args.Zoom * 100:0}%";

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            [Grid.ColumnProperty] = 2
        };
        actions.Children.Add(zoomText);
        actions.Children.Add(BuildRouterComboBox(preview));
        actions.Children.Add(ClickButton("Scope", (_, _) => preview.FrameActiveScope(), 68));
        actions.Children.Add(ClickButton("Fit", (_, _) => preview.FitToView()));
        actions.Children.Add(ClickButton("1:1", (_, _) => preview.ResetView()));
        actions.Children.Add(ClickButton("+", (_, _) => preview.ZoomIn(), 34));
        actions.Children.Add(ClickButton("-", (_, _) => preview.ZoomOut(), 34));
        return actions;
    }

    private static ComboBox BuildRouterComboBox(SchematicPreviewControl preview)
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

    private Control BuildViewportToolbar(SchematicPreviewControl preview)
    {
        DockPanel toolbar = new()
        {
            LastChildFill = false
        };

        toolbar.Children.Add(new TextBlock
        {
            Text = "Pan with middle/right drag. Wheel zooms around the cursor.",
            Foreground = MutedBrush,
            VerticalAlignment = VerticalAlignment.Center
        });

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(actions, Avalonia.Controls.Dock.Right);
        actions.Children.Add(SmallButton("Add Selected", "AddSelectedWaveformSignalCommand"));
        actions.Children.Add(SmallButton("Add Scope", "AddHierarchyScopeSignalsToWaveformCommand"));
        actions.Children.Add(ClickButton("Focus Scope", (_, _) => preview.FrameActiveScope(), 94));
        toolbar.Children.Add(actions);
        return toolbar;
    }

    private Control BuildBreadcrumbBar()
    {
        ItemsControl items = new()
        {
            [!ItemsControl.ItemsSourceProperty] = new Binding("SelectedHierarchyBreadcrumbs")
        };

        items.ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6
        });
        items.ItemTemplate = new FuncDataTemplate<HierarchyBreadcrumbItemViewModel>((item, _) =>
        {
            Border border = new()
            {
                Background = item.IsCurrent ? SurfaceAltBrush : Brushes.Transparent,
                BorderBrush = item.IsCurrent ? AccentBrush : StrokeBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(8, 4)
            };

            Button button = new()
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = new StackPanel
                {
                    Spacing = 0,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = item.Title,
                            Foreground = TextBrush,
                            FontSize = 11,
                            FontWeight = item.IsCurrent ? FontWeight.SemiBold : FontWeight.Medium
                        },
                        new TextBlock
                        {
                            Text = item.ModuleName,
                            Foreground = MutedBrush,
                            FontSize = 9
                        }
                    }
                }
            };
            button.Bind(Button.CommandProperty, new Binding("DataContext.SelectHierarchyScopeCommand")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor)
                {
                    AncestorType = typeof(Window)
                }
            });
            button.CommandParameter = item.HierarchyPath;
            border.Child = button;
            return border;
        });

        return new ScrollViewer
        {
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = items,
            [Grid.ColumnProperty] = 1
        };
    }

    private Control BuildScopeSidebar()
    {
        Grid grid = new()
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(new GridLength(0.42, GridUnitType.Star)),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(new GridLength(0.58, GridUnitType.Star))
            },
            Margin = new Thickness(12)
        };

        grid.Children.Add(new TextBlock
        {
            Text = "Scope",
            Foreground = AccentBrush,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold
        });

        grid.Children.Add(new TextBlock
        {
            Foreground = TextBrush,
            FontSize = 15,
            Margin = new Thickness(0, 10, 0, 0),
            [!TextBlock.TextProperty] = new Binding("SelectedHierarchyScopeTitle"),
            [Grid.RowProperty] = 1
        });

        grid.Children.Add(new TextBlock
        {
            Foreground = MutedBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
            [!TextBlock.TextProperty] = new Binding("SelectedHierarchyScopeSummary"),
            [Grid.RowProperty] = 2
        });

        grid.Children.Add(new ListBox
        {
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = TextBrush,
            Margin = new Thickness(0, 10, 0, 0),
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            ItemTemplate = SignalListTemplate(),
            [!ItemsControl.ItemsSourceProperty] = new Binding("HierarchyScopeSignals"),
            [!SelectingItemsControl.SelectedItemProperty] = new Binding("SelectedSignal", BindingMode.TwoWay),
            [Grid.RowProperty] = 3
        });

        grid.Children.Add(new TextBlock
        {
            Text = "Trace Signals",
            Foreground = AccentBrush,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 10, 0, 0),
            [Grid.RowProperty] = 4
        });

        grid.Children.Add(new ListBox
        {
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = TextBrush,
            Margin = new Thickness(0, 8, 0, 0),
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            ItemTemplate = SignalListTemplate(),
            [!ItemsControl.ItemsSourceProperty] = new Binding("TraceSignals"),
            [!SelectingItemsControl.SelectedItemProperty] = new Binding("SelectedSignal", BindingMode.TwoWay),
            [Grid.RowProperty] = 5
        });

        return grid;
    }

    private static Control BuildProbePanel()
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
            ColumnSpacing = 12,
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

        grid.Children.Add(new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(0, 14, 0, 0),
            Children =
            {
                SmallButton("Add To Waveform", "AddSelectedWaveformSignalCommand"),
                new TextBlock
                {
                    Text = "Single-bit inputs toggle directly on the canvas. Bus inputs open an editor.",
                    Foreground = MutedBrush,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 11
                }
            },
            [Grid.RowProperty] = 6
        });

        return grid;
    }

    private SchematicPreviewControl CreateBoundSchematicPreview()
    {
        SchematicPreviewControl preview = new()
        {
            CompactLayout = false,
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
            [!SchematicPreviewControl.ScopeContAssignsProperty] = new Binding("SelectedHierarchyContAssigns"),
            [!SchematicPreviewControl.ScopePrimitivesProperty] = new Binding("SelectedHierarchyPrimitives")
        };
        preview.SignalEditorRequested += OnSchematicSignalEditorRequested;
        return preview;
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
        CornerRadius = new CornerRadius(6)
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

    private void OnStudioKeyDown(object? sender, KeyEventArgs e)
    {
        if (Content is not Control root)
        {
            return;
        }

        SchematicPreviewControl? preview = FindPreview(root);
        if (preview is null)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.F:
                preview.FitToView();
                e.Handled = true;
                break;
            case Key.S:
                preview.FrameActiveScope();
                e.Handled = true;
                break;
            case Key.D1:
            case Key.NumPad1:
                preview.ResetView();
                e.Handled = true;
                break;
            case Key.OemPlus:
            case Key.Add:
                preview.ZoomIn();
                e.Handled = true;
                break;
            case Key.OemMinus:
            case Key.Subtract:
                preview.ZoomOut();
                e.Handled = true;
                break;
        }
    }

    private static SchematicPreviewControl? FindPreview(Control root)
    {
        if (root is SchematicPreviewControl preview)
        {
            return preview;
        }

        if (root is Panel panel)
        {
            foreach (Control child in panel.Children.OfType<Control>())
            {
                SchematicPreviewControl? nested = FindPreview(child);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        if (root is Decorator decorator && decorator.Child is Control childControl)
        {
            return FindPreview(childControl);
        }

        if (root is ContentControl contentControl && contentControl.Content is Control content)
        {
            return FindPreview(content);
        }

        return null;
    }
}
