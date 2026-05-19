using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Bistable.App.ViewModels;

namespace Bistable.App.Views;

public sealed class SchematicPreviewControl : Control
{
    public static readonly StyledProperty<string> ModuleNameProperty =
        AvaloniaProperty.Register<SchematicPreviewControl, string>(nameof(ModuleName), "module");

    public static readonly StyledProperty<IEnumerable<SignalViewModel>?> SignalsProperty =
        AvaloniaProperty.Register<SchematicPreviewControl, IEnumerable<SignalViewModel>?>(nameof(Signals));

    public static readonly StyledProperty<string?> SelectedSignalNameProperty =
        AvaloniaProperty.Register<SchematicPreviewControl, string?>(nameof(SelectedSignalName));

    private static readonly IBrush BackgroundBrush = SolidColorBrush.Parse("#10141b");
    private static readonly IBrush ModuleFillBrush = SolidColorBrush.Parse("#1b2230");
    private static readonly IBrush ModuleStrokeBrush = SolidColorBrush.Parse("#344157");
    private static readonly IBrush PinStrokeBrush = SolidColorBrush.Parse("#57c7ff");
    private static readonly IBrush SelectedBrush = SolidColorBrush.Parse("#ffd166");
    private static readonly IBrush TextBrush = SolidColorBrush.Parse("#d7dde8");
    private static readonly IBrush MutedBrush = SolidColorBrush.Parse("#8f9aad");
    private static readonly Typeface MonoTypeface = new("monospace");
    private INotifyCollectionChanged? _observableSignals;

    public string ModuleName
    {
        get => GetValue(ModuleNameProperty);
        set => SetValue(ModuleNameProperty, value);
    }

    public IEnumerable<SignalViewModel>? Signals
    {
        get => GetValue(SignalsProperty);
        set => SetValue(SignalsProperty, value);
    }

    public string? SelectedSignalName
    {
        get => GetValue(SelectedSignalNameProperty);
        set => SetValue(SelectedSignalNameProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SignalsProperty)
        {
            if (_observableSignals is not null)
            {
                _observableSignals.CollectionChanged -= OnSignalsChanged;
            }

            _observableSignals = change.NewValue as INotifyCollectionChanged;
            if (_observableSignals is not null)
            {
                _observableSignals.CollectionChanged += OnSignalsChanged;
            }
        }

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        Rect bounds = Bounds;
        context.FillRectangle(BackgroundBrush, bounds);

        IReadOnlyList<SignalViewModel> inputs = Signals?.Where(static signal => signal.IsInput).ToList() ?? [];
        IReadOnlyList<SignalViewModel> outputs = Signals?.Where(static signal => !signal.IsInput).ToList() ?? [];
        if (inputs.Count == 0 && outputs.Count == 0)
        {
            DrawText(context, "Load a project to generate a top-level symbol schematic.", 16, 32, MutedBrush, 13);
            return;
        }

        double moduleWidth = Math.Clamp(bounds.Width * 0.36, 280, 420);
        double laneCount = Math.Max(inputs.Count, outputs.Count);
        double laneHeight = Math.Max(24, Math.Min(42, (bounds.Height - 110) / Math.Max(1, laneCount)));
        double moduleHeight = Math.Max(180, 70 + laneCount * laneHeight);
        Rect moduleRect = new(
            (bounds.Width - moduleWidth) / 2,
            Math.Max(28, (bounds.Height - moduleHeight) / 2),
            moduleWidth,
            moduleHeight);

        context.FillRectangle(ModuleFillBrush, moduleRect, 10);
        context.DrawRectangle(new Pen(ModuleStrokeBrush, 1.5), moduleRect, 10);
        DrawText(context, ModuleName, moduleRect.X + 18, moduleRect.Y + 18, TextBrush, 20);
        DrawText(context, "Top-level symbol", moduleRect.X + 18, moduleRect.Y + 46, MutedBrush, 12);

        DrawPins(context, inputs, moduleRect, true, laneHeight);
        DrawPins(context, outputs, moduleRect, false, laneHeight);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        Point point = e.GetPosition(this);
        string? signalName = HitTestSignal(point);
        if (signalName is not null)
        {
            SelectedSignalName = signalName;
            e.Handled = true;
        }
    }

    private void DrawPins(DrawingContext context, IReadOnlyList<SignalViewModel> signals, Rect moduleRect, bool leftSide, double laneHeight)
    {
        for (int index = 0; index < signals.Count; index++)
        {
            SignalViewModel signal = signals[index];
            double y = moduleRect.Y + 86 + index * laneHeight;
            bool isSelected = string.Equals(signal.Name, SelectedSignalName, StringComparison.OrdinalIgnoreCase);
            IBrush stroke = isSelected ? SelectedBrush : PinStrokeBrush;
            IBrush text = isSelected ? SelectedBrush : TextBrush;

            if (leftSide)
            {
                double pinStartX = moduleRect.X - 44;
                double pinEndX = moduleRect.X;
                context.DrawLine(new Pen(stroke, 2), new Point(pinStartX, y), new Point(pinEndX, y));
                DrawText(context, signal.Name, pinStartX - 6 - MeasureWidth(signal.Name, 12), y - 8, text, 12);
                DrawText(context, signal.WidthLabel, pinStartX - 6 - MeasureWidth(signal.WidthLabel, 11), y + 7, MutedBrush, 11);
            }
            else
            {
                double pinStartX = moduleRect.Right;
                double pinEndX = moduleRect.Right + 44;
                context.DrawLine(new Pen(stroke, 2), new Point(pinStartX, y), new Point(pinEndX, y));
                DrawText(context, signal.Name, pinEndX + 6, y - 8, text, 12);
                DrawText(context, signal.Value, pinEndX + 6, y + 7, MutedBrush, 11);
            }

            context.FillRectangle(stroke, new Rect(leftSide ? moduleRect.X - 3 : moduleRect.Right - 3, y - 3, 6, 6));
        }
    }

    private string? HitTestSignal(Point point)
    {
        IReadOnlyList<SignalViewModel> inputs = Signals?.Where(static signal => signal.IsInput).ToList() ?? [];
        IReadOnlyList<SignalViewModel> outputs = Signals?.Where(static signal => !signal.IsInput).ToList() ?? [];
        double moduleWidth = Math.Clamp(Bounds.Width * 0.36, 280, 420);
        double laneCount = Math.Max(inputs.Count, outputs.Count);
        double laneHeight = Math.Max(24, Math.Min(42, (Bounds.Height - 110) / Math.Max(1, laneCount)));
        Rect moduleRect = new(
            (Bounds.Width - moduleWidth) / 2,
            Math.Max(28, (Bounds.Height - Math.Max(180, 70 + laneCount * laneHeight)) / 2),
            moduleWidth,
            Math.Max(180, 70 + laneCount * laneHeight));

        for (int index = 0; index < inputs.Count; index++)
        {
            SignalViewModel signal = inputs[index];
            double y = moduleRect.Y + 86 + index * laneHeight;
            Rect hit = new(moduleRect.X - 180, y - 12, 180, 24);
            if (hit.Contains(point))
            {
                return signal.Name;
            }
        }

        for (int index = 0; index < outputs.Count; index++)
        {
            SignalViewModel signal = outputs[index];
            double y = moduleRect.Y + 86 + index * laneHeight;
            Rect hit = new(moduleRect.Right, y - 12, 180, 24);
            if (hit.Contains(point))
            {
                return signal.Name;
            }
        }

        return null;
    }

    private void OnSignalsChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    private static void DrawText(DrawingContext context, string text, double x, double y, IBrush brush, double size)
    {
        FormattedText formatted = new(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            MonoTypeface,
            size,
            brush);
        context.DrawText(formatted, new Point(x, y));
    }

    private static double MeasureWidth(string text, double size)
    {
        FormattedText formatted = new(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            MonoTypeface,
            size,
            TextBrush);
        return formatted.Width;
    }
}
