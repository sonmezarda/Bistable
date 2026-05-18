using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Bistable.App.ViewModels;

namespace Bistable.App.Views;

public sealed class WaveformPreviewControl : Control
{
    public static readonly StyledProperty<IEnumerable<WaveformEventViewModel>?> EventsProperty =
        AvaloniaProperty.Register<WaveformPreviewControl, IEnumerable<WaveformEventViewModel>?>(nameof(Events));

    public static readonly StyledProperty<IEnumerable<SignalViewModel>?> SignalsProperty =
        AvaloniaProperty.Register<WaveformPreviewControl, IEnumerable<SignalViewModel>?>(nameof(Signals));

    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<WaveformPreviewControl, double>(nameof(Zoom), 1);

    private static readonly IBrush BackgroundBrush = SolidColorBrush.Parse("#10141b");
    private static readonly IPen GridPen = new Pen(SolidColorBrush.Parse("#242c3a"), 1);
    private static readonly IPen TracePen = new Pen(SolidColorBrush.Parse("#57c7ff"), 2);
    private static readonly IBrush TextBrush = SolidColorBrush.Parse("#d7dde8");
    private static readonly IBrush MutedBrush = SolidColorBrush.Parse("#8f9aad");
    private static readonly Typeface MonoTypeface = new("monospace");

    private INotifyCollectionChanged? _observableEvents;
    private INotifyCollectionChanged? _observableSignals;

    public IEnumerable<WaveformEventViewModel>? Events
    {
        get => GetValue(EventsProperty);
        set => SetValue(EventsProperty, value);
    }

    public IEnumerable<SignalViewModel>? Signals
    {
        get => GetValue(SignalsProperty);
        set => SetValue(SignalsProperty, value);
    }

    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == EventsProperty)
        {
            if (_observableEvents is not null)
            {
                _observableEvents.CollectionChanged -= OnEventsChanged;
            }

            _observableEvents = change.NewValue as INotifyCollectionChanged;
            if (_observableEvents is not null)
            {
                _observableEvents.CollectionChanged += OnEventsChanged;
            }

            InvalidateVisual();
        }

        if (change.Property == SignalsProperty)
        {
            if (_observableSignals is not null)
            {
                _observableSignals.CollectionChanged -= OnEventsChanged;
            }

            _observableSignals = change.NewValue as INotifyCollectionChanged;
            if (_observableSignals is not null)
            {
                _observableSignals.CollectionChanged += OnEventsChanged;
            }

            InvalidateVisual();
        }

        if (change.Property == ZoomProperty)
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        Rect bounds = Bounds;
        context.FillRectangle(BackgroundBrush, bounds);

        int visibleEventCount = Math.Max(20, (int)(400 / Math.Max(1, Zoom)));
        List<WaveformEventViewModel> events = Events?
            .Take(visibleEventCount)
            .Reverse()
            .OrderBy(static sample => sample.Order)
            .ToList() ?? [];
        if (events.Count == 0)
        {
            DrawText(context, "Build a worker, then Eval/Tick/Run to capture waveform samples.", 14, 42, MutedBrush, 13);
            return;
        }

        List<SignalViewModel> signals = Signals?
            .Take(6)
            .ToList() ?? [];

        if (signals.Count == 0)
        {
            DrawText(context, "Select signals with the Wave checkbox to add lanes.", 14, 42, MutedBrush, 13);
            return;
        }

        double labelWidth = 178;
        double valueColumnX = 104;
        double top = 30;
        double laneHeight = Math.Max(24, (bounds.Height - 40) / Math.Max(1, signals.Count));
        double plotLeft = labelWidth;
        double plotRight = Math.Max(plotLeft + 40, bounds.Width - 16);
        long minOrder = events.Min(static sample => sample.Order);
        long maxOrder = events.Max(static sample => sample.Order);

        DrawText(context, "Name", 12, 8, MutedBrush, 11);
        DrawText(context, "Value", valueColumnX, 8, MutedBrush, 11);
        DrawText(context, $"Zoom {Zoom:0.0}x", plotRight - 76, 8, MutedBrush, 11);
        context.DrawLine(GridPen, new Point(valueColumnX - 10, 4), new Point(valueColumnX - 10, bounds.Height - 8));
        context.DrawLine(GridPen, new Point(plotLeft - 10, 4), new Point(plotLeft - 10, bounds.Height - 8));

        for (int lane = 0; lane < signals.Count; lane++)
        {
            SignalViewModel signal = signals[lane];
            string signalName = signal.Name;
            double yMid = top + lane * laneHeight + laneHeight / 2;
            double yHigh = yMid - 7;
            double yLow = yMid + 7;

            DrawText(context, signalName, 12, yMid - 8, TextBrush, 12);
            DrawText(context, signal.Value, valueColumnX, yMid - 8, TextBrush, 12);
            context.DrawLine(GridPen, new Point(plotLeft, yLow), new Point(plotRight, yLow));

            List<WaveformEventViewModel> laneEvents = events
                .Where(sample => string.Equals(sample.Signal, signalName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (laneEvents.Count == 0)
            {
                continue;
            }

            if (!signal.IsBoolean)
            {
                DrawBusLane(context, laneEvents, yMid, plotLeft, plotRight, minOrder, maxOrder);
                continue;
            }

            double previousX = ToX(laneEvents[0].Order, minOrder, maxOrder, plotLeft, plotRight);
            double previousY = ToBitLevel(laneEvents[0].Value, yHigh, yLow);
            context.DrawLine(TracePen, new Point(plotLeft, previousY), new Point(previousX, previousY));

            for (int i = 1; i < laneEvents.Count; i++)
            {
                double x = ToX(laneEvents[i].Order, minOrder, maxOrder, plotLeft, plotRight);
                double y = ToBitLevel(laneEvents[i].Value, yHigh, yLow);
                context.DrawLine(TracePen, new Point(previousX, previousY), new Point(x, previousY));
                if (Math.Abs(y - previousY) > 0.1)
                {
                    context.DrawLine(TracePen, new Point(x, previousY), new Point(x, y));
                }

                previousX = x;
                previousY = y;
            }

            context.DrawLine(TracePen, new Point(previousX, previousY), new Point(plotRight, previousY));
        }
    }

    private static void DrawBusLane(
        DrawingContext context,
        IReadOnlyList<WaveformEventViewModel> laneEvents,
        double y,
        double plotLeft,
        double plotRight,
        long minOrder,
        long maxOrder)
    {
        context.DrawLine(TracePen, new Point(plotLeft, y), new Point(plotRight, y));

        string? previousValue = null;
        for (int i = 0; i < laneEvents.Count; i++)
        {
            WaveformEventViewModel sample = laneEvents[i];
            if (string.Equals(previousValue, sample.Value, StringComparison.Ordinal))
            {
                continue;
            }

            double x = Math.Min(plotRight - 42, ToX(sample.Order, minOrder, maxOrder, plotLeft, plotRight) + 4);
            context.DrawLine(GridPen, new Point(x, y - 9), new Point(x, y + 9));
            DrawText(context, sample.Value, x + 4, y - 18, TextBrush, 11);
            previousValue = sample.Value;
        }
    }

    private static double ToX(long order, long minOrder, long maxOrder, double left, double right)
    {
        if (maxOrder <= minOrder)
        {
            return left;
        }

        double ratio = (double)(order - minOrder) / (maxOrder - minOrder);
        return left + ratio * (right - left);
    }

    private static double ToBitLevel(string value, double high, double low)
    {
        if (value is "0" or "0x0" or "0x00" or "0x000")
        {
            return low;
        }

        return high;
    }

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

    private void OnEventsChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
}
