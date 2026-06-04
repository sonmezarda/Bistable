using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace Bistable.App.Views;

// Synthesis settings live in a dedicated window opened from Tools → Synthesis
// Settings…, mirroring the PreferencesWindow pattern. They used to share the
// Project panel with signal lists, which made the panel feel crowded and put
// destination-path inputs next to a viewport that doesn't need them. The
// DataContext is the same MainWindowViewModel as the main window, so edits
// propagate live and the existing Save/Synthesize commands keep working.
public sealed class SynthesisSettingsWindow : Window
{
    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(24, 28, 36));
    private static readonly IBrush SurfaceBrush    = new SolidColorBrush(Color.FromRgb(34, 39, 50));
    private static readonly IBrush SurfaceAltBrush = new SolidColorBrush(Color.FromRgb(46, 53, 67));
    private static readonly IBrush StrokeBrush     = new SolidColorBrush(Color.FromRgb(62, 70, 86));
    private static readonly IBrush TextBrush       = Brushes.WhiteSmoke;
    private static readonly IBrush MutedBrush      = new SolidColorBrush(Color.FromRgb(160, 170, 188));
    private static readonly IBrush AccentBrush     = new SolidColorBrush(Color.FromRgb(140, 180, 255));

    public SynthesisSettingsWindow()
    {
        Title = "Synthesis Settings";
        Width = 560;
        Height = 420;
        MinWidth = 440;
        MinHeight = 320;
        Background = BackgroundBrush;
        Content = BuildLayout();
    }

    private Control BuildLayout()
    {
        Border surface = new()
        {
            Background = SurfaceBrush,
            BorderBrush = StrokeBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(16),
            Padding = new Thickness(20),
        };

        StackPanel root = new()
        {
            Orientation = Orientation.Vertical,
            Spacing = 14,
        };

        root.Children.Add(new TextBlock
        {
            Text = "Synthesis",
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextBrush,
        });

        root.Children.Add(new TextBlock
        {
            Text = "Yosys synthesis runs on the loaded project's top module. Edits here are kept in memory; Save writes them back to the project's .bistable.json.",
            FontSize = 11,
            Foreground = MutedBrush,
            TextWrapping = TextWrapping.Wrap,
        });

        root.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 14,
            Children =
            {
                FlagCheckBox("Enabled", "SynthesisEnabled"),
                FlagCheckBox("Generic cells", "SynthesisGenericCells"),
                FlagCheckBox("Flatten", "SynthesisFlatten"),
            }
        });

        root.Children.Add(LabeledTextBox("Top module", "SynthesisTopModule"));
        root.Children.Add(LabeledTextBox("JSON output", "SynthesisOutputJson"));
        root.Children.Add(LabeledTextBox("Verilog output", "SynthesisOutputVerilog"));

        root.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 0, 0),
            Children =
            {
                ActionButton("Save", "SaveSynthesisSettingsCommand"),
                ActionButton("Synthesize", "SynthesizeCommand"),
            }
        });

        surface.Child = root;
        return surface;
    }

    private static CheckBox FlagCheckBox(string label, string bindingPath) => new()
    {
        Content = label,
        Foreground = TextBrush,
        FontSize = 12,
        [!ToggleButton.IsCheckedProperty] = new Binding(bindingPath, BindingMode.TwoWay),
    };

    private static Control LabeledTextBox(string label, string bindingPath)
    {
        Grid rowGrid = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(110)),
                new ColumnDefinition(GridLength.Star),
            },
            ColumnSpacing = 10,
        };
        rowGrid.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = MutedBrush,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        });
        rowGrid.Children.Add(new TextBox
        {
            MinHeight = 30,
            FontSize = 12,
            Background = SurfaceAltBrush,
            Foreground = TextBrush,
            BorderBrush = StrokeBrush,
            FontFamily = FontFamily.Parse("monospace"),
            [!TextBox.TextProperty] = new Binding(bindingPath, BindingMode.TwoWay),
            [Grid.ColumnProperty] = 1,
        });
        return rowGrid;
    }

    private static Button ActionButton(string label, string commandPath) => new()
    {
        Content = label,
        Padding = new Thickness(14, 6),
        FontSize = 12,
        Background = SurfaceAltBrush,
        Foreground = AccentBrush,
        BorderBrush = StrokeBrush,
        [!Button.CommandProperty] = new Binding(commandPath),
    };
}
