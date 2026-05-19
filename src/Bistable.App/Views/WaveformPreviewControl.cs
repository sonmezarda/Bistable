using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Bistable.App.ViewModels;

namespace Bistable.App.Views;

public sealed class WaveformPreviewControl : Control
{
    public static readonly StyledProperty<IEnumerable<WaveformLaneViewModel>?> LanesProperty =
        AvaloniaProperty.Register<WaveformPreviewControl, IEnumerable<WaveformLaneViewModel>?>(nameof(Lanes));

    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<WaveformPreviewControl, double>(nameof(Zoom), 1);

    public static readonly StyledProperty<int> OffsetProperty =
        AvaloniaProperty.Register<WaveformPreviewControl, int>(nameof(Offset));

    public static readonly StyledProperty<string?> SelectedSignalNameProperty =
        AvaloniaProperty.Register<WaveformPreviewControl, string?>(nameof(SelectedSignalName));

    public static readonly StyledProperty<long> CursorOrderProperty =
        AvaloniaProperty.Register<WaveformPreviewControl, long>(nameof(CursorOrder));

    private static readonly IBrush BackgroundBrush = SolidColorBrush.Parse("#10141b");
    private static readonly IBrush SelectedLaneBrush = SolidColorBrush.Parse("#162536");
    private static readonly IBrush CursorBrush = SolidColorBrush.Parse("#ffd166");
    private static readonly IBrush BusFillBrush = SolidColorBrush.Parse("#15212f");
    private static readonly IBrush TextBrush = SolidColorBrush.Parse("#d7dde8");
    private static readonly IBrush MutedBrush = SolidColorBrush.Parse("#8f9aad");
    private static readonly IPen GridPen = new Pen(SolidColorBrush.Parse("#242c3a"), 1);
    private static readonly IPen TracePen = new Pen(SolidColorBrush.Parse("#57c7ff"), 2);
    private static readonly IPen CursorPen = new Pen(CursorBrush, 1);
    private static readonly Typeface MonoTypeface = new("monospace");

    private INotifyCollectionChanged? _observableLanes;
    private readonly Dictionary<WaveformLaneViewModel, INotifyCollectionChanged> _laneSampleSources = [];
    private bool _isScrubbing;

    public IEnumerable<WaveformLaneViewModel>? Lanes
    {
        get => GetValue(LanesProperty);
        set => SetValue(LanesProperty, value);
    }

    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public int Offset
    {
        get => GetValue(OffsetProperty);
        set => SetValue(OffsetProperty, value);
    }

    public string? SelectedSignalName
    {
        get => GetValue(SelectedSignalNameProperty);
        set => SetValue(SelectedSignalNameProperty, value);
    }

    public long CursorOrder
    {
        get => GetValue(CursorOrderProperty);
        set => SetValue(CursorOrderProperty, Math.Max(0, value));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == LanesProperty)
        {
            UnsubscribeFromLanes();

            _observableLanes = change.NewValue as INotifyCollectionChanged;
            if (_observableLanes is not null)
            {
                _observableLanes.CollectionChanged += OnLaneCollectionChanged;
            }

            SubscribeToLaneSamples();
            InvalidateVisual();
            return;
        }

        if (change.Property == ZoomProperty
            || change.Property == OffsetProperty
            || change.Property == SelectedSignalNameProperty
            || change.Property == CursorOrderProperty)
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        Rect bounds = Bounds;
        context.FillRectangle(BackgroundBrush, bounds);

        IReadOnlyList<WaveformLaneViewModel> lanes = Lanes?.ToList() ?? [];
        if (lanes.Count == 0)
        {
            DrawText(context, "Add signals to the waveform to inspect their history.", 14, 42, MutedBrush, 13);
            return;
        }

        long maxOrder = lanes
            .SelectMany(static lane => lane.Samples)
            .Select(static sample => sample.Order)
            .DefaultIfEmpty(0)
            .Max();

        if (maxOrder == 0)
        {
            DrawText(context, "Build a worker, then Eval/Tick/Run to capture waveform samples.", 14, 42, MutedBrush, 13);
            return;
        }

        double plotLeft = 10;
        double plotRight = Math.Max(plotLeft + 60, bounds.Width - 10);
        double axisTop = 10;
        double laneTop = 34;
        double laneHeight = Math.Max(28, (bounds.Height - laneTop - 12) / Math.Max(1, lanes.Count));
        long visibleOrderCount = GetVisibleOrderCount(plotRight - plotLeft, Zoom);
        long windowEnd = Math.Max(1, maxOrder - Offset);
        long windowStart = Math.Max(1, windowEnd - visibleOrderCount + 1);
        long cursorOrder = Math.Clamp(CursorOrder <= 0 ? maxOrder : CursorOrder, windowStart, windowEnd);

        DrawAxis(context, lanes, plotLeft, plotRight, axisTop, windowStart, windowEnd);
        context.DrawLine(GridPen, new Point(plotLeft, 4), new Point(plotLeft, bounds.Height - 8));

        for (int laneIndex = 0; laneIndex < lanes.Count; laneIndex++)
        {
            WaveformLaneViewModel lane = lanes[laneIndex];
            double laneY = laneTop + laneIndex * laneHeight;
            double bitHigh = laneY + 8;
            double bitLow = laneY + laneHeight - 8;
            bool isSelected = string.Equals(lane.Name, SelectedSignalName, StringComparison.OrdinalIgnoreCase);

            if (isSelected)
            {
                context.FillRectangle(SelectedLaneBrush, new Rect(0, laneY - 2, bounds.Width, laneHeight));
            }

            context.DrawLine(GridPen, new Point(plotLeft, laneY + laneHeight - 2), new Point(plotRight, laneY + laneHeight - 2));

            if (lane.IsBoolean)
            {
                DrawBooleanLane(context, lane, plotLeft, plotRight, bitHigh, bitLow, windowStart, windowEnd);
            }
            else
            {
                DrawBusLane(context, lane, plotLeft, plotRight, laneY + 6, laneHeight - 12, windowStart, windowEnd);
            }
        }

        double cursorX = ToX(cursorOrder, windowStart, windowEnd, plotLeft, plotRight);
        context.DrawLine(CursorPen, new Point(cursorX, axisTop + 12), new Point(cursorX, bounds.Height - 8));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        Point point = e.GetPosition(this);
        UpdateInteractionState(point);
        _isScrubbing = true;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!_isScrubbing)
        {
            return;
        }

        UpdateInteractionState(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _isScrubbing = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            Offset = Math.Max(0, Offset + (e.Delta.Y < 0 ? 12 : -12));
        }
        else
        {
            double nextZoom = e.Delta.Y > 0 ? Zoom * 1.2 : Zoom / 1.2;
            Zoom = Math.Clamp(nextZoom, 1, 12);
        }

        e.Handled = true;
    }

    private void UpdateInteractionState(Point point)
    {
        IReadOnlyList<WaveformLaneViewModel> lanes = Lanes?.ToList() ?? [];
        if (lanes.Count == 0)
        {
            return;
        }

        const double plotLeft = 10;
        double plotRight = Math.Max(plotLeft + 60, Bounds.Width - 16);
        double laneTop = 34;
        double laneHeight = Math.Max(28, (Bounds.Height - laneTop - 12) / Math.Max(1, lanes.Count));
        long maxOrder = lanes
            .SelectMany(static lane => lane.Samples)
            .Select(static sample => sample.Order)
            .DefaultIfEmpty(0)
            .Max();

        if (maxOrder <= 0)
        {
            return;
        }

        if (point.Y >= laneTop)
        {
            int laneIndex = Math.Clamp((int)((point.Y - laneTop) / laneHeight), 0, lanes.Count - 1);
            SelectedSignalName = lanes[laneIndex].Name;
        }

        if (point.X >= plotLeft)
        {
            long visibleOrderCount = GetVisibleOrderCount(plotRight - plotLeft, Zoom);
            long windowEnd = Math.Max(1, maxOrder - Offset);
            long windowStart = Math.Max(1, windowEnd - visibleOrderCount + 1);
            double ratio = (point.X - plotLeft) / Math.Max(1, plotRight - plotLeft);
            long order = windowStart + (long)Math.Round(Math.Clamp(ratio, 0, 1) * (windowEnd - windowStart));
            CursorOrder = order;
        }
    }

    private void DrawAxis(
        DrawingContext context,
        IReadOnlyList<WaveformLaneViewModel> lanes,
        double plotLeft,
        double plotRight,
        double axisTop,
        long windowStart,
        long windowEnd)
    {
        const int divisions = 6;
        for (int index = 0; index < divisions; index++)
        {
            double ratio = divisions == 1 ? 0 : index / (double)(divisions - 1);
            long order = windowStart + (long)Math.Round((windowEnd - windowStart) * ratio);
            double x = plotLeft + (plotRight - plotLeft) * ratio;
            context.DrawLine(GridPen, new Point(x, axisTop + 14), new Point(x, Bounds.Height - 8));
            ulong time = ResolveTimeAtOrBefore(lanes, order);
            DrawText(context, $"t={time}", x + 4, axisTop, MutedBrush, 11);
        }
    }

    private static void DrawBooleanLane(
        DrawingContext context,
        WaveformLaneViewModel lane,
        double plotLeft,
        double plotRight,
        double bitHigh,
        double bitLow,
        long windowStart,
        long windowEnd)
    {
        IReadOnlyList<WaveformSampleViewModel> samples = GetVisibleSamples(lane, windowStart, windowEnd);
        if (samples.Count == 0)
        {
            return;
        }

        double previousX = plotLeft;
        double previousY = ToBitLevel(samples[0].Value, bitHigh, bitLow);
        for (int index = 0; index < samples.Count; index++)
        {
            WaveformSampleViewModel sample = samples[index];
            double x = ToX(sample.Order, windowStart, windowEnd, plotLeft, plotRight);
            context.DrawLine(TracePen, new Point(previousX, previousY), new Point(x, previousY));

            double nextY = ToBitLevel(sample.Value, bitHigh, bitLow);
            if (Math.Abs(nextY - previousY) > 0.1)
            {
                context.DrawLine(TracePen, new Point(x, previousY), new Point(x, nextY));
            }

            previousX = x;
            previousY = nextY;
        }

        context.DrawLine(TracePen, new Point(previousX, previousY), new Point(plotRight, previousY));
    }

    private static void DrawBusLane(
        DrawingContext context,
        WaveformLaneViewModel lane,
        double plotLeft,
        double plotRight,
        double laneTop,
        double laneHeight,
        long windowStart,
        long windowEnd)
    {
        IReadOnlyList<WaveformSampleViewModel> samples = GetVisibleSamples(lane, windowStart, windowEnd);
        if (samples.Count == 0)
        {
            return;
        }

        double segmentLeft = plotLeft;
        for (int index = 0; index < samples.Count; index++)
        {
            WaveformSampleViewModel current = samples[index];
            double segmentRight = index < samples.Count - 1
                ? ToX(samples[index + 1].Order, windowStart, windowEnd, plotLeft, plotRight)
                : plotRight;

            Rect rect = new(segmentLeft, laneTop, Math.Max(2, segmentRight - segmentLeft), laneHeight);
            context.FillRectangle(BusFillBrush, rect);
            context.DrawRectangle(null, GridPen, rect);
            if (rect.Width > 28)
            {
                DrawText(context, current.Value, rect.X + 6, rect.Y + 4, TextBrush, 11);
            }

            segmentLeft = segmentRight;
        }
    }

    private static IReadOnlyList<WaveformSampleViewModel> GetVisibleSamples(WaveformLaneViewModel lane, long windowStart, long windowEnd)
    {
        if (lane.Samples.Count == 0)
        {
            return [];
        }

        List<WaveformSampleViewModel> visible = [];
        WaveformSampleViewModel? previous = null;
        for (int index = 0; index < lane.Samples.Count; index++)
        {
            WaveformSampleViewModel sample = lane.Samples[index];
            if (sample.Order < windowStart)
            {
                previous = sample;
                continue;
            }

            if (sample.Order > windowEnd)
            {
                break;
            }

            if (visible.Count == 0 && previous is not null)
            {
                visible.Add(previous);
            }

            visible.Add(sample);
        }

        if (visible.Count == 0 && previous is not null)
        {
            visible.Add(previous);
        }

        return visible;
    }

    private static long GetVisibleOrderCount(double plotWidth, double zoom)
    {
        double baseline = Math.Max(40, plotWidth / 11);
        return Math.Max(12, (long)Math.Round(baseline / Math.Max(1, zoom)));
    }

    private static double ToX(long order, long windowStart, long windowEnd, double left, double right)
    {
        if (windowEnd <= windowStart)
        {
            return left;
        }

        double ratio = (double)(order - windowStart) / (windowEnd - windowStart);
        return left + ratio * (right - left);
    }

    private static double ToBitLevel(string value, double high, double low) =>
        value is "0" or "0x0" or "0x00" or "0x000" ? low : high;

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

    private ulong ResolveTimeAtOrBefore(IReadOnlyList<WaveformLaneViewModel> lanes, long order)
    {
        ulong time = 0;
        foreach (WaveformLaneViewModel lane in lanes)
        {
            if (lane.Samples.Count == 0)
            {
                continue;
            }

            time = Math.Max(time, lane.GetTimeAtOrBefore(order));
        }

        return time;
    }

    private void SubscribeToLaneSamples()
    {
        foreach (WaveformLaneViewModel lane in Lanes ?? [])
        {
            if (lane.Samples is INotifyCollectionChanged observable)
            {
                _laneSampleSources[lane] = observable;
                observable.CollectionChanged += OnLaneSamplesChanged;
            }
        }
    }

    private void UnsubscribeFromLanes()
    {
        if (_observableLanes is not null)
        {
            _observableLanes.CollectionChanged -= OnLaneCollectionChanged;
            _observableLanes = null;
        }

        foreach ((_, INotifyCollectionChanged observable) in _laneSampleSources)
        {
            observable.CollectionChanged -= OnLaneSamplesChanged;
        }

        _laneSampleSources.Clear();
    }

    private void OnLaneCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UnsubscribeFromLanes();
        _observableLanes = sender as INotifyCollectionChanged;
        if (_observableLanes is not null)
        {
            _observableLanes.CollectionChanged += OnLaneCollectionChanged;
        }

        SubscribeToLaneSamples();
        InvalidateVisual();
    }

    private void OnLaneSamplesChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
}
