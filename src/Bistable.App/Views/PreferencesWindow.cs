using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Bistable.App.Services;
using Bistable.App.ViewModels;

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
            Child = BuildSchematicForm(),
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

        return root;
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
}
