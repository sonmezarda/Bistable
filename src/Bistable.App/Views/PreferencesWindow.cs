using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Bistable.App.Services;
using Bistable.App.ViewModels;
using Bistable.Core.Projects;

namespace Bistable.App.Views;

// P2.7-9 follow-up: VS Code / JetBrains-style preferences window. Categories
// down the left, the focused category's form on the right. Currently hosts
// just the Schematic category (theme + routing engine) but every future
// preference (P2.7-3 mini-map defaults, P2.7-5 hover behaviour, P2.7-7 layout
// overrides, P2.7-8 view-state retention, P2.7-10 export defaults) lands here.
//
// The window's DataContext is the same MainWindowViewModel as the main window,
// so editing a preference here propagates instantly to the schematic preview
// through the existing property bindings.
public sealed class PreferencesWindow : Window
{
    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(24, 28, 36));
    private static readonly IBrush SurfaceBrush    = new SolidColorBrush(Color.FromRgb(34, 39, 50));
    private static readonly IBrush SurfaceAltBrush = new SolidColorBrush(Color.FromRgb(46, 53, 67));
    private static readonly IBrush StrokeBrush     = new SolidColorBrush(Color.FromRgb(62, 70, 86));
    private static readonly IBrush TextBrush       = Brushes.WhiteSmoke;
    private static readonly IBrush MutedBrush      = new SolidColorBrush(Color.FromRgb(160, 170, 188));

    public PreferencesWindow()
    {
        Title = "Preferences";
        Width = 720;
        Height = 480;
        MinWidth = 520;
        MinHeight = 360;
        Background = BackgroundBrush;
        Content = BuildLayout();
    }

    private Control BuildLayout()
    {
        Grid grid = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(180)) { MinWidth = 140, MaxWidth = 240 },
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
            }
        };

        // Category sidebar — single item for now, more land here in future
        // phases (Schematic / Hover / Export / Keymap / ...).
        ListBox sidebar = new()
        {
            Background = SurfaceBrush,
            BorderBrush = StrokeBrush,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Foreground = TextBrush,
            Padding = new Thickness(8),
            ItemsSource = new[] { "Schematic" },
            SelectedIndex = 0,
        };
        Grid.SetColumn(sidebar, 0);
        grid.Children.Add(sidebar);

        Border formHost = new()
        {
            Background = BackgroundBrush,
            Padding = new Thickness(24, 20),
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = BuildSchematicForm(),
            },
        };
        Grid.SetColumn(formHost, 1);
        grid.Children.Add(formHost);
        return grid;
    }

    private Control BuildSchematicForm()
    {
        StackPanel root = new()
        {
            Orientation = Orientation.Vertical,
            Spacing = 18,
        };

        root.Children.Add(new TextBlock
        {
            Text = "Schematic",
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextBrush,
            Margin = new Thickness(0, 0, 0, 6),
        });

        root.Children.Add(BuildField(
            "Color theme",
            "Choose the palette used to render the schematic. Print-friendly is monochrome for printer-safe screenshots.",
            BuildEnumComboBox<SchematicThemePreset>("AvailableSchematicThemes", "SchematicThemePreset",
                preset => SchematicThemePresets.DisplayName(preset))));

        root.Children.Add(BuildField(
            "Routing engine",
            "Algorithm used to lay out wires. ELK (default) handles deeply hierarchical schematics best; Internal is a lightweight maze router.",
            BuildEnumComboBox<SchematicRoutingEngine>("AvailableSchematicRouters", "SchematicRouter",
                engine => engine.ToString())));

        root.Children.Add(BuildField(
            "Gate routing quality",
            "Project-scoped ELK quality preset for synthesized gate-level schematics. Fast preview is intended for large RISC-V-scale designs.",
            BuildRoutingQualityEditor()));

        root.Children.Add(BuildField(
            "Gate pin labels",
            "Controls hierarchical and primitive pin names. Automatic mode uses zoom-based LOD; bus grouping changes labels only and preserves bit-level connectivity.",
            BuildGatePinLabelEditor()));

        root.Children.Add(BuildField(
            "Gate bus wires",
            "Controls whether multi-bit connections use one routed trunk with endpoint fan-out or remain as individual bit wires. Automatic switches by zoom.",
            BuildGateBusWireEditor()));

        root.Children.Add(new TextBlock
        {
            Text = "Live development",
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextBrush,
            Margin = new Thickness(0, 14, 0, 0),
        });
        root.Children.Add(BuildField(
            "Automatic HDL reload",
            "Watch project HDL files, re-elaborate after saves, and keep the last good schematic visible when Verilator reports an error.",
            BuildLiveReloadEditor()));

        return root;
    }

    private Control BuildLiveReloadEditor()
    {
        StackPanel editor = new()
        {
            Orientation = Orientation.Vertical,
            Spacing = 8,
            Margin = new Thickness(0, 6, 0, 0)
        };
        editor.Children.Add(new CheckBox
        {
            Content = "Enable live reload",
            Foreground = TextBrush,
            [!ToggleButton.IsCheckedProperty] = new Binding("LiveReloadEnabled", BindingMode.TwoWay)
        });
        Grid debounce = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(new GridLength(110))
            },
            ColumnSpacing = 10
        };
        debounce.Children.Add(ThresholdLabel("Save debounce"));
        NumericUpDown value = new()
        {
            Minimum = 100,
            Maximum = 5000,
            Increment = 50,
            FormatString = "0 ms",
            Background = SurfaceAltBrush,
            Foreground = TextBrush,
            BorderBrush = StrokeBrush,
            [!NumericUpDown.ValueProperty] = new Binding("LiveReloadDebounceMs", BindingMode.TwoWay),
            [Grid.ColumnProperty] = 1
        };
        debounce.Children.Add(value);
        editor.Children.Add(debounce);
        return editor;
    }

    private Control BuildField(string label, string description, Control editor)
    {
        StackPanel field = new()
        {
            Orientation = Orientation.Vertical,
            Spacing = 4,
        };
        field.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextBrush,
        });
        field.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 11,
            Foreground = MutedBrush,
            TextWrapping = TextWrapping.Wrap,
        });
        field.Children.Add(editor);
        return field;
    }

    private ComboBox BuildEnumComboBox<TEnum>(string itemsSourcePath, string selectedItemPath, Func<TEnum, string> displayName)
        where TEnum : struct, Enum
    {
        ComboBox box = new()
        {
            Width = 240,
            MinHeight = 30,
            Margin = new Thickness(0, 6, 0, 0),
            Background = SurfaceAltBrush,
            Foreground = TextBrush,
            BorderBrush = StrokeBrush,
            [!ItemsControl.ItemsSourceProperty] = new Binding(itemsSourcePath),
            [!SelectingItemsControl.SelectedItemProperty] = new Binding(selectedItemPath, BindingMode.TwoWay),
            ItemTemplate = new FuncDataTemplate<TEnum>(
                (value, _) => new TextBlock
                {
                    Text = displayName(value),
                    Foreground = TextBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                supportsRecycling: true),
        };
        return box;
    }

    private Control BuildRoutingQualityEditor()
    {
        StackPanel row = new()
        {
            Orientation = Orientation.Vertical,
            Spacing = 8,
            Margin = new Thickness(0, 6, 0, 0),
        };
        row.Children.Add(BuildEnumComboBox<RoutingQuality>(
            "AvailableRoutingQualities",
            "GateRoutingQuality",
            RoutingQualityDisplayName));
        row.Children.Add(new CheckBox
        {
            Content = "Auto fast preview for large graphs",
            Foreground = TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            [!ToggleButton.IsCheckedProperty] = new Binding("GateAutoDowngradeLargeGraphs", BindingMode.TwoWay),
        });
        row.Children.Add(new Button
        {
            Content = "Save to project",
            MinHeight = 30,
            Padding = new Thickness(10, 2),
            Background = SurfaceAltBrush,
            Foreground = TextBrush,
            BorderBrush = StrokeBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            [!Button.CommandProperty] = new Binding("SaveProjectSettingsCommand"),
        });
        return row;
    }

    private Control BuildGatePinLabelEditor()
    {
        Grid thresholds = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(new GridLength(90)),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(new GridLength(90)),
            },
            ColumnSpacing = 8,
        };
        thresholds.Children.Add(ThresholdLabel("Compact at"));
        thresholds.Children.Add(ThresholdEditor("GatePinLabelCompactZoom", 1));
        thresholds.Children.Add(ThresholdLabel("Detailed at", 2));
        thresholds.Children.Add(ThresholdEditor("GatePinLabelDetailedZoom", 3));

        StackPanel editor = new()
        {
            Orientation = Orientation.Vertical,
            Spacing = 8,
            Margin = new Thickness(0, 6, 0, 0),
        };
        editor.Children.Add(BuildEnumComboBox<GatePinLabelMode>(
            "AvailableGatePinLabelModes",
            "GatePinLabelMode",
            GatePinLabelModeDisplayName));
        editor.Children.Add(BuildEnumComboBox<GatePinVisibilityMode>(
            "AvailableGatePinVisibilityModes",
            "GatePinVisibilityMode",
            GatePinVisibilityModeDisplayName));
        editor.Children.Add(new CheckBox
        {
            Content = "Group bus pin labels, for example data[31:0]",
            Foreground = TextBrush,
            [!ToggleButton.IsCheckedProperty] =
                new Binding("GateGroupBusPinLabels", BindingMode.TwoWay),
        });
        editor.Children.Add(thresholds);
        return editor;
    }

    private Control BuildGateBusWireEditor()
    {
        StackPanel editor = new()
        {
            Orientation = Orientation.Vertical,
            Spacing = 8,
            Margin = new Thickness(0, 6, 0, 0),
        };
        editor.Children.Add(BuildEnumComboBox<GateBusVisualizationMode>(
            "AvailableGateBusVisualizationModes",
            "GateBusVisualizationMode",
            GateBusVisualizationModeDisplayName));

        Grid threshold = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(new GridLength(90)),
            },
            ColumnSpacing = 8,
        };
        threshold.Children.Add(ThresholdLabel("Use trunks below zoom"));
        threshold.Children.Add(ThresholdEditor("GateBusTrunkMaxZoom", 1));
        editor.Children.Add(threshold);
        return editor;
    }

    private static TextBlock ThresholdLabel(string text, int column = 0) => new()
    {
        Text = text,
        Foreground = MutedBrush,
        FontSize = 11,
        VerticalAlignment = VerticalAlignment.Center,
        [Grid.ColumnProperty] = column,
    };

    private static NumericUpDown ThresholdEditor(string bindingPath, int column) => new()
    {
        Minimum = 0.05m,
        Maximum = 8m,
        Increment = 0.05m,
        FormatString = "0.00",
        MinHeight = 28,
        Background = SurfaceAltBrush,
        Foreground = TextBrush,
        BorderBrush = StrokeBrush,
        [!NumericUpDown.ValueProperty] = new Binding(bindingPath, BindingMode.TwoWay),
        [Grid.ColumnProperty] = column,
    };

    private static string RoutingQualityDisplayName(RoutingQuality quality) => quality switch
    {
        RoutingQuality.FastPreview => "Fast preview",
        RoutingQuality.Balanced => "Balanced",
        RoutingQuality.Production => "Production",
        _ => quality.ToString(),
    };

    private static string GatePinLabelModeDisplayName(GatePinLabelMode mode) => mode switch
    {
        GatePinLabelMode.Automatic => "Automatic (zoom LOD)",
        GatePinLabelMode.Always => "Always show",
        GatePinLabelMode.Hidden => "Hidden",
        _ => mode.ToString(),
    };

    private static string GatePinVisibilityModeDisplayName(GatePinVisibilityMode mode) => mode switch
    {
        GatePinVisibilityMode.ConnectedOnly => "Connected pins only",
        GatePinVisibilityMode.All => "All pins",
        _ => mode.ToString(),
    };

    private static string GateBusVisualizationModeDisplayName(GateBusVisualizationMode mode) => mode switch
    {
        GateBusVisualizationMode.Automatic => "Automatic (zoom LOD)",
        GateBusVisualizationMode.Bundled => "Bundled trunks",
        GateBusVisualizationMode.Individual => "Individual bit wires",
        _ => mode.ToString(),
    };
}
