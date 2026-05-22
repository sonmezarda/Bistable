using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;

namespace Bistable.App.Views;

public sealed partial class SchematicPreviewControl
{
    private void EnsureViewport(Rect viewportBounds, Size worldSize)
    {
        if (_lastViewportSize != viewportBounds.Size)
        {
            _lastViewportSize = viewportBounds.Size;
            if (!_viewportCustomized)
            {
                _fitPending = true;
            }
        }

        if (!_fitPending || viewportBounds.Width <= 0 || viewportBounds.Height <= 0)
        {
            return;
        }

        double zoomX = Math.Max(0.05, (viewportBounds.Width - FitMargin * 2) / worldSize.Width);
        double zoomY = Math.Max(0.05, (viewportBounds.Height - FitMargin * 2) / worldSize.Height);
        _viewportZoom = Math.Clamp(Math.Min(zoomX, zoomY), 0.2, 3.5);
        double contentWidth = worldSize.Width * _viewportZoom;
        double contentHeight = worldSize.Height * _viewportZoom;
        _viewportPan = new Point(
            (viewportBounds.Width - contentWidth) / 2,
            (viewportBounds.Height - contentHeight) / 2);
        ClampViewportPan(viewportBounds.Size, worldSize);
        _fitPending = false;
        RaiseViewportChanged();
    }

    private void FrameWorldRect(Rect target)
    {
        double paddedWidth = Math.Max(60, target.Width + FitMargin * 1.25);
        double paddedHeight = Math.Max(60, target.Height + FitMargin * 1.25);
        double zoomX = Math.Max(0.05, Bounds.Width / paddedWidth);
        double zoomY = Math.Max(0.05, Bounds.Height / paddedHeight);
        _viewportZoom = Math.Clamp(Math.Min(zoomX, zoomY), 0.25, 4.5);
        _viewportPan = new Point(
            Bounds.Width / 2 - (target.X + target.Width / 2) * _viewportZoom,
            Bounds.Height / 2 - (target.Y + target.Height / 2) * _viewportZoom);
        ClampViewportPan(Bounds.Size, _lastWorldSize);
        _fitPending = false;
        _viewportCustomized = true;
        RaiseViewportChanged();
        InvalidateVisual();
    }

    private void ApplyZoomDelta(double factor, Point viewportPoint)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        Point worldPoint = ViewportToWorld(viewportPoint);
        double nextZoom = Math.Clamp(_viewportZoom * factor, 0.2, 4.5);
        _viewportPan = new Point(
            viewportPoint.X - worldPoint.X * nextZoom,
            viewportPoint.Y - worldPoint.Y * nextZoom);
        _viewportZoom = nextZoom;
        ClampViewportPan(Bounds.Size, _lastWorldSize);
        _fitPending = false;
        _viewportCustomized = true;
        RaiseViewportChanged();
        InvalidateVisual();
    }

    private void ClampViewportPan(Size viewportSize, Size worldSize)
    {
        if (viewportSize.Width <= 0
            || viewportSize.Height <= 0
            || worldSize.Width <= 0
            || worldSize.Height <= 0)
        {
            return;
        }

        double contentWidth = worldSize.Width * _viewportZoom;
        double contentHeight = worldSize.Height * _viewportZoom;
        double horizontalOverscroll = Math.Clamp(viewportSize.Width * 0.18, 48, 180);
        double verticalOverscroll = Math.Clamp(viewportSize.Height * 0.18, 48, 160);

        double clampedX = ClampAxisPan(_viewportPan.X, viewportSize.Width, contentWidth, horizontalOverscroll);
        double clampedY = ClampAxisPan(_viewportPan.Y, viewportSize.Height, contentHeight, verticalOverscroll);
        _viewportPan = new Point(clampedX, clampedY);
    }

    private static double ClampAxisPan(double pan, double viewportLength, double contentLength, double overscroll)
    {
        if (contentLength + overscroll * 2 <= viewportLength)
        {
            return (viewportLength - contentLength) / 2;
        }

        double min = viewportLength - contentLength - overscroll;
        double max = overscroll;
        return Math.Clamp(pan, min, max);
    }

    private Size MeasureWorldSize(
        int inputCount,
        int outputCount,
        int scopeSignalCount,
        int childScopeCount,
        int scopePortCount,
        int localSignalCount,
        bool hasScopeFocus)
    {
        int topLaneCount = Math.Max(inputCount, outputCount);
        double baseWidth = CompactLayout ? 2400 : 3000;
        double baseHeight = CompactLayout ? 1480 : 1800;
        double width = baseWidth + Math.Max(0, childScopeCount - 2) * (CompactLayout ? 150 : 220);
        double height = baseHeight + Math.Max(0, topLaneCount - 4) * (CompactLayout ? 34 : 46);

        if (hasScopeFocus)
        {
            width += Math.Max(0, scopePortCount - 6) * (CompactLayout ? 54 : 72);
            height += Math.Max(0, childScopeCount - 2) * (CompactLayout ? 52 : 70);
            height += Math.Max(0, scopeSignalCount - 4) * (CompactLayout ? 24 : 30);
            height += Math.Max(0, localSignalCount - 2) * (CompactLayout ? 20 : 26);
            int expandedScopeCount = ExpandedScopePaths?.Count() ?? 0;
            width += expandedScopeCount * (CompactLayout ? 360 : 480);
            height += expandedScopeCount * (CompactLayout ? 260 : 360);
        }

        return new Size(
            Math.Clamp(width, CompactLayout ? 2100 : 2600, CompactLayout ? 4200 : 5400),
            Math.Clamp(height, CompactLayout ? 1300 : 1600, CompactLayout ? 3200 : 4200));
    }

    private void RaiseViewportChanged()
    {
        double zoom = _viewportZoom;
        Point pan = _viewportPan;
        Dispatcher.UIThread.Post(
            () => ViewportChanged?.Invoke(this, new ViewportChangedEventArgs(zoom, pan)),
            DispatcherPriority.Background);
    }

    private Point ViewportToWorld(Point point) =>
        new(
            (point.X - _viewportPan.X) / Math.Max(_viewportZoom, 0.0001),
            (point.Y - _viewportPan.Y) / Math.Max(_viewportZoom, 0.0001));
}
