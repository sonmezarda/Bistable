using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Bistable.App.ViewModels;

namespace Bistable.App.Views;

public sealed class SignalValueEditorWindow : Window
{
    private static readonly IBrush BackgroundBrush = SolidColorBrush.Parse("#151922");
    private static readonly IBrush SurfaceAltBrush = SolidColorBrush.Parse("#1b202b");
    private static readonly IBrush StrokeBrush = SolidColorBrush.Parse("#2a3241");
    private static readonly IBrush TextBrush = SolidColorBrush.Parse("#d7dde8");
    private static readonly IBrush MutedBrush = SolidColorBrush.Parse("#8f9aad");
    private static readonly IBrush AccentBrush = SolidColorBrush.Parse("#57c7ff");
    private static readonly IBrush ActiveBitBrush = SolidColorBrush.Parse("#57c7ff");
    private static readonly IBrush InactiveBitBrush = SolidColorBrush.Parse("#121924");

    private readonly SignalValueEditorViewModel _viewModel;
    private readonly Action<string> _applyValue;

    public SignalValueEditorWindow(SignalValueEditorViewModel viewModel, Action<string> applyValue)
    {
        _viewModel = viewModel;
        _applyValue = applyValue;

        Title = $"Drive {_viewModel.SignalName}";
        Width = Math.Clamp(420 + (_viewModel.Width / 8.0) * 18, 460, 880);
        Height = Math.Clamp(320 + (_viewModel.Width / 16.0) * 28, 360, 680);
        MinWidth = 440;
        MinHeight = 340;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = BackgroundBrush;
        Content = BuildLayout();
        DataContext = _viewModel;
    }

    private Control BuildLayout()
    {
        Grid root = new()
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            },
            Margin = new Thickness(16)
        };

        root.Children.Add(new TextBlock
        {
            Foreground = AccentBrush,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            [!TextBlock.TextProperty] = new Binding("SignalName")
        });

        root.Children.Add(new TextBlock
        {
            Foreground = MutedBrush,
            Margin = new Thickness(0, 6, 0, 0),
            [!TextBlock.TextProperty] = new Binding("WidthLabel"),
            [Grid.RowProperty] = 1
        });

        Grid editorGrid = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(160))
            },
            ColumnSpacing = 10,
            Margin = new Thickness(0, 14, 0, 0),
            [Grid.RowProperty] = 2
        };

        TextBox valueBox = new()
        {
            MinHeight = 34,
            Background = SurfaceAltBrush,
            Foreground = TextBrush,
            BorderBrush = StrokeBrush,
            FontFamily = FontFamily.Parse("monospace"),
            [!TextBox.TextProperty] = new Binding("ValueText", BindingMode.TwoWay)
        };
        editorGrid.Children.Add(valueBox);

        ComboBox formatBox = new()
        {
            MinHeight = 34,
            Background = SurfaceAltBrush,
            Foreground = TextBrush,
            BorderBrush = StrokeBrush,
            [!ItemsControl.ItemsSourceProperty] = new Binding("AvailableFormats"),
            [!SelectingItemsControl.SelectedItemProperty] = new Binding("SelectedFormat", BindingMode.TwoWay),
            [Grid.ColumnProperty] = 1
        };
        editorGrid.Children.Add(formatBox);
        root.Children.Add(editorGrid);

        root.Children.Add(new TextBlock
        {
            Foreground = MutedBrush,
            FontSize = 11,
            Margin = new Thickness(0, 8, 0, 0),
            [!TextBlock.TextProperty] = new Binding("ErrorMessage"),
            [Grid.RowProperty] = 3
        });

        Border bitsBorder = new()
        {
            Background = SurfaceAltBrush,
            BorderBrush = StrokeBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 14, 0, 0),
            [Grid.RowProperty] = 4
        };
        bitsBorder.Child = BuildBitsPane();
        root.Children.Add(bitsBorder);

        Grid footer = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Margin = new Thickness(0, 14, 0, 0),
            [Grid.RowProperty] = 5
        };

        footer.Children.Add(new TextBlock
        {
            Foreground = MutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            [!TextBlock.TextProperty] = new Binding("CanonicalValue")
        });

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                Button("Apply", ApplyAndKeepOpen),
                Button("OK", ApplyAndClose),
                Button("Cancel", (_, _) => Close())
            },
            [Grid.ColumnProperty] = 1
        };
        footer.Children.Add(buttons);
        root.Children.Add(footer);

        return root;
    }

    private Control BuildBitsPane()
    {
        Grid grid = new()
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            },
            Margin = new Thickness(12)
        };

        grid.Children.Add(new TextBlock
        {
            Text = "Bits",
            Foreground = AccentBrush,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold
        });

        ItemsControl bits = new()
        {
            Margin = new Thickness(0, 10, 0, 0),
            ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                ItemWidth = 40,
                ItemHeight = 48
            }),
            ItemTemplate = new FuncDataTemplate<SignalBitViewModel>((bit, _) =>
            {
                if (bit is null)
                {
                    return new TextBlock();
                }

                StackPanel panel = new()
                {
                    Spacing = 4
                };

                ToggleButton toggle = new()
                {
                    Width = 32,
                    Height = 24,
                    Padding = new Thickness(0),
                    Foreground = TextBrush,
                    BorderBrush = StrokeBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    [!ToggleButton.IsCheckedProperty] = new Binding(nameof(SignalBitViewModel.IsSet), BindingMode.TwoWay)
                };
                toggle.Bind(TemplatedControl.BackgroundProperty, new Binding(nameof(SignalBitViewModel.IsSet))
                {
                    Converter = new BitBackgroundConverter()
                });
                toggle.Bind(ContentControl.ContentProperty, new Binding(nameof(SignalBitViewModel.IsSet))
                {
                    Converter = new BitContentConverter()
                });
                panel.Children.Add(toggle);

                panel.Children.Add(new TextBlock
                {
                    Text = bit.Label,
                    Foreground = MutedBrush,
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center
                });

                return panel;
            }),
            [!ItemsControl.ItemsSourceProperty] = new Binding("Bits")
        };
        Grid.SetRow(bits, 1);
        grid.Children.Add(bits);

        grid.Children.Add(new TextBlock
        {
            Foreground = MutedBrush,
            FontSize = 11,
            Margin = new Thickness(0, 10, 0, 0),
            Text = "Click bit cells directly or edit the numeric value above.",
            [Grid.RowProperty] = 2
        });

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = grid
        };
    }

    private void ApplyAndKeepOpen(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel.TryApplyText())
        {
            _applyValue(_viewModel.CanonicalValue);
        }
    }

    private void ApplyAndClose(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel.TryApplyText())
        {
            _applyValue(_viewModel.CanonicalValue);
            Close();
        }
    }

    private static Button Button(string text, EventHandler<Avalonia.Interactivity.RoutedEventArgs> onClick)
    {
        Button button = new()
        {
            Content = text,
            MinWidth = 78,
            Height = 32,
            Background = SurfaceAltBrush,
            Foreground = TextBrush,
            BorderBrush = StrokeBrush
        };
        button.Click += onClick;
        return button;
    }

    private sealed class BitBackgroundConverter : Avalonia.Data.Converters.IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is true ? ActiveBitBrush : InactiveBitBrush;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class BitContentConverter : Avalonia.Data.Converters.IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is true ? "1" : "0";

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
