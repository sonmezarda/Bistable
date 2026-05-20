using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
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

    public static readonly StyledProperty<ICommand?> ToggleInputCommandProperty =
        AvaloniaProperty.Register<SchematicPreviewControl, ICommand?>(nameof(ToggleInputCommand));

    public static readonly StyledProperty<ICommand?> AddSelectedWaveformCommandProperty =
        AvaloniaProperty.Register<SchematicPreviewControl, ICommand?>(nameof(AddSelectedWaveformCommand));

    public static readonly StyledProperty<ICommand?> SelectScopeCommandProperty =
        AvaloniaProperty.Register<SchematicPreviewControl, ICommand?>(nameof(SelectScopeCommand));

    public static readonly StyledProperty<string?> ActiveScopeTitleProperty =
        AvaloniaProperty.Register<SchematicPreviewControl, string?>(nameof(ActiveScopeTitle));

    public static readonly StyledProperty<string?> ActiveScopeModuleNameProperty =
        AvaloniaProperty.Register<SchematicPreviewControl, string?>(nameof(ActiveScopeModuleName));

    public static readonly StyledProperty<string?> ActiveScopePathProperty =
        AvaloniaProperty.Register<SchematicPreviewControl, string?>(nameof(ActiveScopePath));

    public static readonly StyledProperty<string?> ActiveScopeSummaryProperty =
        AvaloniaProperty.Register<SchematicPreviewControl, string?>(nameof(ActiveScopeSummary));

    public static readonly StyledProperty<string?> ActiveScopeHintProperty =
        AvaloniaProperty.Register<SchematicPreviewControl, string?>(nameof(ActiveScopeHint));

    public static readonly StyledProperty<HierarchyScopeNodeViewModel?> ScopeParentProperty =
        AvaloniaProperty.Register<SchematicPreviewControl, HierarchyScopeNodeViewModel?>(nameof(ScopeParent));

    public static readonly StyledProperty<IEnumerable<HierarchyScopeInstanceViewModel>?> ScopeChildrenProperty =
        AvaloniaProperty.Register<SchematicPreviewControl, IEnumerable<HierarchyScopeInstanceViewModel>?>(nameof(ScopeChildren));

    public static readonly StyledProperty<IEnumerable<HierarchyScopePortViewModel>?> ScopePortsProperty =
        AvaloniaProperty.Register<SchematicPreviewControl, IEnumerable<HierarchyScopePortViewModel>?>(nameof(ScopePorts));

    public static readonly StyledProperty<IEnumerable<HierarchyScopeLocalSignalViewModel>?> ScopeLocalSignalsProperty =
        AvaloniaProperty.Register<SchematicPreviewControl, IEnumerable<HierarchyScopeLocalSignalViewModel>?>(nameof(ScopeLocalSignals));

    public static readonly StyledProperty<IEnumerable<SignalViewModel>?> ScopeSignalsProperty =
        AvaloniaProperty.Register<SchematicPreviewControl, IEnumerable<SignalViewModel>?>(nameof(ScopeSignals));

    public static readonly StyledProperty<bool> CompactLayoutProperty =
        AvaloniaProperty.Register<SchematicPreviewControl, bool>(nameof(CompactLayout), true);

    private static readonly IBrush BackgroundBrush = SolidColorBrush.Parse("#10141b");
    private static readonly IBrush ModuleFillBrush = SolidColorBrush.Parse("#1b2230");
    private static readonly IBrush ModuleStrokeBrush = SolidColorBrush.Parse("#344157");
    private static readonly IBrush PinStrokeBrush = SolidColorBrush.Parse("#57c7ff");
    private static readonly IBrush SelectedBrush = SolidColorBrush.Parse("#ffd166");
    private static readonly IBrush ValueFillBrush = SolidColorBrush.Parse("#121924");
    private static readonly IBrush InputValueBrush = SolidColorBrush.Parse("#7fd6ff");
    private static readonly IBrush OutputValueBrush = SolidColorBrush.Parse("#65d889");
    private static readonly IBrush TextBrush = SolidColorBrush.Parse("#d7dde8");
    private static readonly IBrush MutedBrush = SolidColorBrush.Parse("#8f9aad");
    private static readonly IBrush FocusPanelFillBrush = SolidColorBrush.Parse("#141b26");
    private static readonly IBrush ScopeHighlightBrush = SolidColorBrush.Parse("#2a3a52");
    private static readonly IBrush NodeFillBrush = SolidColorBrush.Parse("#192232");
    private static readonly IBrush NodeSelectedFillBrush = SolidColorBrush.Parse("#25344a");
    private static readonly IBrush ConnectorBrush = SolidColorBrush.Parse("#4f6487");
    private static readonly Typeface MonoTypeface = new("monospace");
    private const double FitMargin = 32;

    private readonly List<SignalHitTarget> _signalHitTargets = [];
    private readonly List<ScopeHitTarget> _scopeHitTargets = [];
    private INotifyCollectionChanged? _observableSignals;
    private INotifyCollectionChanged? _observableScopeSignals;
    private INotifyCollectionChanged? _observableScopeChildren;
    private INotifyCollectionChanged? _observableScopePorts;
    private INotifyCollectionChanged? _observableScopeLocalSignals;
    private double _viewportZoom = 1;
    private Point _viewportPan;
    private bool _isPanningViewport;
    private Point _lastViewportPointer;
    private bool _fitPending = true;
    private bool _viewportCustomized;
    private Size _lastViewportSize;
    private Size _lastWorldSize;

    public SchematicPreviewControl()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public event EventHandler<SignalEditorRequestedEventArgs>? SignalEditorRequested;

    public event EventHandler<ViewportChangedEventArgs>? ViewportChanged;

    public void ZoomIn() => ApplyZoomDelta(1.18, new Point(Bounds.Width / 2, Bounds.Height / 2));

    public void ZoomOut() => ApplyZoomDelta(1 / 1.18, new Point(Bounds.Width / 2, Bounds.Height / 2));

    public void FitToView()
    {
        _fitPending = true;
        _viewportCustomized = false;
        InvalidateVisual();
    }

    public void ResetView()
    {
        _viewportZoom = 1;
        _viewportPan = new Point(FitMargin, FitMargin);
        _fitPending = false;
        _viewportCustomized = true;
        RaiseViewportChanged();
        InvalidateVisual();
    }

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

    public ICommand? ToggleInputCommand
    {
        get => GetValue(ToggleInputCommandProperty);
        set => SetValue(ToggleInputCommandProperty, value);
    }

    public ICommand? AddSelectedWaveformCommand
    {
        get => GetValue(AddSelectedWaveformCommandProperty);
        set => SetValue(AddSelectedWaveformCommandProperty, value);
    }

    public ICommand? SelectScopeCommand
    {
        get => GetValue(SelectScopeCommandProperty);
        set => SetValue(SelectScopeCommandProperty, value);
    }

    public string? ActiveScopeTitle
    {
        get => GetValue(ActiveScopeTitleProperty);
        set => SetValue(ActiveScopeTitleProperty, value);
    }

    public string? ActiveScopeModuleName
    {
        get => GetValue(ActiveScopeModuleNameProperty);
        set => SetValue(ActiveScopeModuleNameProperty, value);
    }

    public string? ActiveScopePath
    {
        get => GetValue(ActiveScopePathProperty);
        set => SetValue(ActiveScopePathProperty, value);
    }

    public string? ActiveScopeSummary
    {
        get => GetValue(ActiveScopeSummaryProperty);
        set => SetValue(ActiveScopeSummaryProperty, value);
    }

    public string? ActiveScopeHint
    {
        get => GetValue(ActiveScopeHintProperty);
        set => SetValue(ActiveScopeHintProperty, value);
    }

    public HierarchyScopeNodeViewModel? ScopeParent
    {
        get => GetValue(ScopeParentProperty);
        set => SetValue(ScopeParentProperty, value);
    }

    public IEnumerable<HierarchyScopeInstanceViewModel>? ScopeChildren
    {
        get => GetValue(ScopeChildrenProperty);
        set => SetValue(ScopeChildrenProperty, value);
    }

    public IEnumerable<SignalViewModel>? ScopeSignals
    {
        get => GetValue(ScopeSignalsProperty);
        set => SetValue(ScopeSignalsProperty, value);
    }

    public bool CompactLayout
    {
        get => GetValue(CompactLayoutProperty);
        set => SetValue(CompactLayoutProperty, value);
    }

    public IEnumerable<HierarchyScopePortViewModel>? ScopePorts
    {
        get => GetValue(ScopePortsProperty);
        set => SetValue(ScopePortsProperty, value);
    }

    public IEnumerable<HierarchyScopeLocalSignalViewModel>? ScopeLocalSignals
    {
        get => GetValue(ScopeLocalSignalsProperty);
        set => SetValue(ScopeLocalSignalsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SignalsProperty)
        {
            DetachSignalSource(change.OldValue as IEnumerable<SignalViewModel>, ref _observableSignals, OnSignalsChanged);
            AttachSignalSource(change.NewValue as IEnumerable<SignalViewModel>, ref _observableSignals, OnSignalsChanged);
        }
        else if (change.Property == ScopeSignalsProperty)
        {
            DetachSignalSource(change.OldValue as IEnumerable<SignalViewModel>, ref _observableScopeSignals, OnScopeSignalsChanged);
            AttachSignalSource(change.NewValue as IEnumerable<SignalViewModel>, ref _observableScopeSignals, OnScopeSignalsChanged);
        }
        else if (change.Property == ScopeChildrenProperty)
        {
            DetachCollection(change.OldValue as INotifyCollectionChanged, ref _observableScopeChildren, OnScopeChildrenChanged);
            AttachCollection(change.NewValue as INotifyCollectionChanged, ref _observableScopeChildren, OnScopeChildrenChanged);
        }
        else if (change.Property == ScopePortsProperty)
        {
            DetachCollection(change.OldValue as INotifyCollectionChanged, ref _observableScopePorts, OnScopePortsChanged);
            AttachCollection(change.NewValue as INotifyCollectionChanged, ref _observableScopePorts, OnScopePortsChanged);
        }
        else if (change.Property == ScopeLocalSignalsProperty)
        {
            DetachCollection(change.OldValue as INotifyCollectionChanged, ref _observableScopeLocalSignals, OnScopeLocalSignalsChanged);
            AttachCollection(change.NewValue as INotifyCollectionChanged, ref _observableScopeLocalSignals, OnScopeLocalSignalsChanged);
        }

        if (ShouldRefitForProperty(change.Property))
        {
            if (change.Property == CompactLayoutProperty)
            {
                _viewportCustomized = false;
                _fitPending = true;
            }
            else if (!_viewportCustomized)
            {
                _fitPending = true;
            }
        }

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        Rect viewportBounds = Bounds;
        using IDisposable? clip = context.PushClip(viewportBounds);
        context.FillRectangle(BackgroundBrush, viewportBounds);

        IReadOnlyList<SignalViewModel> inputs = Signals?.Where(static signal => signal.IsInput).ToList() ?? [];
        IReadOnlyList<SignalViewModel> outputs = Signals?.Where(static signal => !signal.IsInput).ToList() ?? [];
        IReadOnlyList<SignalViewModel> scopeSignals = ScopeSignals?.ToList() ?? [];
        IReadOnlyList<HierarchyScopeInstanceViewModel> childScopes = ScopeChildren?.ToList() ?? [];
        IReadOnlyList<HierarchyScopePortViewModel> scopePorts = ScopePorts?.ToList() ?? [];
        IReadOnlyList<HierarchyScopeLocalSignalViewModel> localSignals = ScopeLocalSignals?.ToList() ?? [];
        HierarchyScopeNodeViewModel? parentScope = ScopeParent;

        if (inputs.Count == 0 && outputs.Count == 0)
        {
            DrawText(context, "Load a project to generate a top-level symbol schematic.", 16, 32, MutedBrush, 13);
            return;
        }

        bool hasScopeFocus = HasScopeContext(scopeSignals, childScopes, parentScope);
        Size worldSize = MeasureWorldSize(inputs.Count, outputs.Count, scopeSignals.Count, childScopes.Count, scopePorts.Count, localSignals.Count, hasScopeFocus);
        _lastWorldSize = worldSize;
        EnsureViewport(viewportBounds, worldSize);

        int visibleProbeCount = Math.Min(scopeSignals.Count, CompactLayout ? 8 : 14);
        int visibleChildCount = Math.Min(childScopes.Count, CompactLayout ? 4 : 8);
        int visibleLocalCount = Math.Min(localSignals.Count, CompactLayout ? 4 : 8);
        double reservedBottom = hasScopeFocus
            ? Math.Clamp(
                (CompactLayout ? 236 : 340)
                + visibleProbeCount * (CompactLayout ? 12 : 18)
                + visibleChildCount * (CompactLayout ? 18 : 28)
                + visibleLocalCount * (CompactLayout ? 14 : 18),
                CompactLayout ? 236 : 340,
                CompactLayout ? 390 : 620)
            : 24;
        Rect worldBounds = new(0, 0, worldSize.Width, worldSize.Height);
        double diagramHeight = Math.Max(180, worldBounds.Height - reservedBottom);
        double moduleWidth = Math.Clamp(worldBounds.Width * 0.36, 280, 520);
        double laneCount = Math.Max(inputs.Count, outputs.Count);
        double laneHeight = Math.Max(24, Math.Min(42, (diagramHeight - 112) / Math.Max(1, laneCount)));
        double moduleHeight = Math.Max(180, 70 + laneCount * laneHeight);
        Rect moduleRect = new(
            (worldBounds.Width - moduleWidth) / 2,
            Math.Max(28, (diagramHeight - moduleHeight) / 2),
            moduleWidth,
            moduleHeight);

        using (context.PushTransform(Matrix.CreateTranslation(_viewportPan.X, _viewportPan.Y)))
        using (context.PushTransform(Matrix.CreateScale(_viewportZoom, _viewportZoom)))
        {
            context.FillRectangle(BackgroundBrush, worldBounds);

            context.FillRectangle(ModuleFillBrush, moduleRect, 10);
            context.DrawRectangle(new Pen(ModuleStrokeBrush, 1.5), moduleRect, 10);
            DrawText(context, ModuleName, moduleRect.X + 18, moduleRect.Y + 18, TextBrush, 20);
            DrawText(context, "Top-level symbol", moduleRect.X + 18, moduleRect.Y + 46, MutedBrush, 12);
            DrawText(context, "Click pins to drive", moduleRect.X + 18, moduleRect.Bottom - 28, MutedBrush, 11);

            Rect? scopeCard = DrawScopeCard(context, worldBounds);

            _signalHitTargets.Clear();
            _scopeHitTargets.Clear();
            DrawPins(context, inputs, worldBounds, moduleRect, leftSide: true, laneHeight);
            DrawPins(context, outputs, worldBounds, moduleRect, leftSide: false, laneHeight);

            if (hasScopeFocus)
            {
                DrawFocusedScopePanel(context, worldBounds, moduleRect, scopeCard, scopeSignals, childScopes, parentScope, scopePorts, localSignals);
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        PointerPointProperties props = e.GetCurrentPoint(this).Properties;
        if (props.IsMiddleButtonPressed || props.IsRightButtonPressed)
        {
            _isPanningViewport = true;
            _lastViewportPointer = e.GetPosition(this);
            e.Handled = true;
            return;
        }

        Point point = ViewportToWorld(e.GetPosition(this));
        SignalHitTarget? signalHit = HitTestSignal(point);
        if (signalHit is not null)
        {
            HandleSignalHit(signalHit, e);
            return;
        }

        ScopeHitTarget? scopeHit = HitTestScope(point);
        if (scopeHit is not null)
        {
            SelectedSignalName = null;
            ICommand? command = SelectScopeCommand;
            if (command?.CanExecute(scopeHit.HierarchyPath) == true)
            {
                command.Execute(scopeHit.HierarchyPath);
                e.Handled = true;
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point viewportPoint = e.GetPosition(this);
        if (_isPanningViewport)
        {
            Vector delta = viewportPoint - _lastViewportPointer;
            _viewportPan = new Point(_viewportPan.X + delta.X, _viewportPan.Y + delta.Y);
            ClampViewportPan(Bounds.Size, _lastWorldSize);
            _lastViewportPointer = viewportPoint;
            _viewportCustomized = true;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        Point worldPoint = ViewportToWorld(viewportPoint);
        bool interactive = HitTestSignal(worldPoint) is not null || HitTestScope(worldPoint) is not null;
        Cursor = interactive ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Arrow);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isPanningViewport)
        {
            _isPanningViewport = false;
            e.Handled = true;
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        double delta = e.Delta.Y;
        if (Math.Abs(delta) < double.Epsilon)
        {
            return;
        }

        ApplyZoomDelta(delta > 0 ? 1.12 : 1 / 1.12, e.GetPosition(this));
        e.Handled = true;
    }

    private void HandleSignalHit(SignalHitTarget hit, PointerPressedEventArgs e)
    {
        SelectedSignalName = hit.Signal.Name;
        if (hit.Signal.IsInput && hit.Signal.IsBoolean)
        {
            ICommand? command = ToggleInputCommand;
            if (command?.CanExecute(hit.Signal.Name) == true)
            {
                command.Execute(hit.Signal.Name);
            }
            else
            {
                hit.Signal.BooleanValue = !hit.Signal.BooleanValue;
            }
        }
        else if (hit.Signal.IsInput)
        {
            SignalEditorRequested?.Invoke(this, new SignalEditorRequestedEventArgs(hit.Signal));
        }
        else if (e.ClickCount >= 2)
        {
            ICommand? command = AddSelectedWaveformCommand;
            if (command?.CanExecute(null) == true)
            {
                command.Execute(null);
            }
        }

        e.Handled = true;
    }

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
        double baseWidth = CompactLayout ? 1800 : 2500;
        double baseHeight = CompactLayout ? 1200 : 1560;
        double width = baseWidth + Math.Max(0, childScopeCount - 2) * (CompactLayout ? 84 : 160);
        double height = baseHeight + Math.Max(0, topLaneCount - 4) * (CompactLayout ? 26 : 38);

        if (hasScopeFocus)
        {
            width += Math.Max(0, scopePortCount - 6) * (CompactLayout ? 30 : 56);
            height += Math.Max(0, childScopeCount - 2) * (CompactLayout ? 34 : 56);
            height += Math.Max(0, scopeSignalCount - 4) * (CompactLayout ? 18 : 24);
            height += Math.Max(0, localSignalCount - 2) * (CompactLayout ? 16 : 22);
        }

        return new Size(
            Math.Clamp(width, CompactLayout ? 1500 : 2100, CompactLayout ? 2400 : 3600),
            Math.Clamp(height, CompactLayout ? 1000 : 1300, CompactLayout ? 1800 : 2600));
    }

    private void RaiseViewportChanged()
    {
        double zoom = _viewportZoom;
        Point pan = _viewportPan;
        Dispatcher.UIThread.Post(
            () => ViewportChanged?.Invoke(this, new ViewportChangedEventArgs(zoom, pan)),
            DispatcherPriority.Background);
    }

    private static bool ShouldRefitForProperty(AvaloniaProperty property) =>
        property == CompactLayoutProperty
        || property == ModuleNameProperty
        || property == SignalsProperty
        || property == ScopeSignalsProperty
        || property == ScopeChildrenProperty
        || property == ScopePortsProperty
        || property == ScopeLocalSignalsProperty
        || property == ScopeParentProperty
        || property == ActiveScopeTitleProperty
        || property == ActiveScopeModuleNameProperty
        || property == ActiveScopePathProperty
        || property == ActiveScopeSummaryProperty
        || property == ActiveScopeHintProperty;

    private Point ViewportToWorld(Point point) =>
        new(
            (point.X - _viewportPan.X) / Math.Max(_viewportZoom, 0.0001),
            (point.Y - _viewportPan.Y) / Math.Max(_viewportZoom, 0.0001));

    private void DrawPins(
        DrawingContext context,
        IReadOnlyList<SignalViewModel> signals,
        Rect bounds,
        Rect moduleRect,
        bool leftSide,
        double laneHeight)
    {
        for (int index = 0; index < signals.Count; index++)
        {
            SignalViewModel signal = signals[index];
            double y = moduleRect.Y + 86 + index * laneHeight;
            bool isSelected = string.Equals(signal.Name, SelectedSignalName, StringComparison.OrdinalIgnoreCase);
            IBrush stroke = isSelected ? SelectedBrush : PinStrokeBrush;
            IBrush text = isSelected ? SelectedBrush : TextBrush;
            double badgeWidth = GetValueBadgeWidth(signal.Value, 62, 98);

            if (leftSide)
            {
                double pinStartX = moduleRect.X - 44;
                double pinEndX = moduleRect.X;
                double badgeX = Math.Max(16, moduleRect.X - 58 - badgeWidth);
                Rect badge = new(badgeX, y - 12, badgeWidth, 24);
                double labelRight = badge.X - 12;
                string label = Ellipsize(signal.Name, 12, Math.Max(90, labelRight - 16));
                double labelWidth = MeasureWidth(label, 12);
                double nameX = Math.Max(16, labelRight - labelWidth);
                string widthLabel = Ellipsize(signal.WidthLabel, 11, Math.Max(70, labelRight - 16));
                double widthX = Math.Max(16, labelRight - MeasureWidth(widthLabel, 11));
                Rect hit = new(nameX - 8, y - 16, moduleRect.X - nameX + 8, 32);

                _signalHitTargets.Add(new SignalHitTarget(signal, hit));
                context.DrawLine(new Pen(stroke, 2), new Point(pinStartX, y), new Point(pinEndX, y));
                DrawText(context, label, nameX, y - 8, text, 12);
                DrawText(context, widthLabel, widthX, y + 7, MutedBrush, 11);
                DrawValueBadge(
                    context,
                    signal.Value,
                    badge,
                    signal.IsBoolean && signal.Value == "1" ? SelectedBrush : InputValueBrush,
                    ValueFillBrush);
            }
            else
            {
                double pinStartX = moduleRect.Right;
                double pinEndX = moduleRect.Right + 44;
                double badgeX = Math.Min(bounds.Right - badgeWidth - 16, pinEndX + 64);
                Rect badge = new(badgeX, y - 12, badgeWidth, 24);
                double labelX = pinEndX + 10;
                string label = Ellipsize(signal.Name, 12, Math.Max(90, badge.X - labelX - 10));
                Rect hit = new(moduleRect.Right - 8, y - 16, badge.Right - moduleRect.Right + 8, 32);

                _signalHitTargets.Add(new SignalHitTarget(signal, hit));
                context.DrawLine(new Pen(stroke, 2), new Point(pinStartX, y), new Point(pinEndX, y));
                DrawText(context, label, labelX, y - 8, text, 12);
                DrawValueBadge(context, signal.Value, badge, isSelected ? SelectedBrush : OutputValueBrush, ValueFillBrush);
            }

            context.FillRectangle(stroke, new Rect(leftSide ? moduleRect.X - 3 : moduleRect.Right - 3, y - 3, 6, 6));
        }
    }

    private Rect? DrawScopeCard(DrawingContext context, Rect bounds)
    {
        if (!HasScopeCardContent())
        {
            return null;
        }

        string title = string.IsNullOrWhiteSpace(ActiveScopeTitle) ? "Scope" : ActiveScopeTitle!;
        string moduleName = string.IsNullOrWhiteSpace(ActiveScopeModuleName) ? "module" : ActiveScopeModuleName!;
        string path = string.IsNullOrWhiteSpace(ActiveScopePath) ? string.Empty : ActiveScopePath!;
        string summary = string.IsNullOrWhiteSpace(ActiveScopeSummary) ? string.Empty : ActiveScopeSummary!;

        double contentWidth = new[]
        {
            MeasureWidth(title, 12),
            MeasureWidth(moduleName, 11),
            path.Length == 0 ? 0 : MeasureWidth(path, 10),
            summary.Length == 0 ? 0 : MeasureWidth(summary, 10)
        }.Max();
        double cardWidth = Math.Clamp(contentWidth + 28, 210, 320);
        double cardHeight = 72 + (summary.Length == 0 ? 0 : 16);
        Rect card = new(bounds.Right - cardWidth - 16, 16, cardWidth, cardHeight);

        context.FillRectangle(ValueFillBrush, card, 6);
        context.DrawRectangle(new Pen(PinStrokeBrush, 1), card, 6);
        DrawText(context, title, card.X + 12, card.Y + 8, TextBrush, 12);
        DrawText(context, Ellipsize(moduleName, 11, card.Width - 24), card.X + 12, card.Y + 28, PinStrokeBrush, 11);
        if (path.Length > 0)
        {
            DrawText(context, Ellipsize(path, 10, card.Width - 24), card.X + 12, card.Y + 46, MutedBrush, 10);
        }

        if (summary.Length > 0)
        {
            DrawText(context, Ellipsize(summary, 10, card.Width - 24), card.X + 12, card.Bottom - 16, MutedBrush, 10);
        }

        return card;
    }

    private void DrawFocusedScopePanel(
        DrawingContext context,
        Rect bounds,
        Rect moduleRect,
        Rect? scopeCard,
        IReadOnlyList<SignalViewModel> scopeSignals,
        IReadOnlyList<HierarchyScopeInstanceViewModel> childScopes,
        HierarchyScopeNodeViewModel? parentScope,
        IReadOnlyList<HierarchyScopePortViewModel> scopePorts,
        IReadOnlyList<HierarchyScopeLocalSignalViewModel> localSignals)
    {
        int visibleProbeCount = Math.Min(scopeSignals.Count, CompactLayout ? 8 : 14);
        int visibleChildCount = Math.Min(childScopes.Count, CompactLayout ? 4 : 8);
        int visibleLocalCount = Math.Min(localSignals.Count, CompactLayout ? 4 : 8);
        int visibleLeftPortCount = Math.Min(scopePorts.Count(static port => port.IsInput), CompactLayout ? 5 : 8);
        int visibleRightPortCount = Math.Min(scopePorts.Count(static port => port.IsOutput), CompactLayout ? 5 : 8);
        bool inlineChildren = visibleChildCount > 0 && bounds.Width >= (CompactLayout ? 980 : 1400);
        int probeColumnCount = bounds.Width >= (CompactLayout ? 900 : 1200) && visibleProbeCount > 1
            ? (CompactLayout ? 2 : Math.Min(3, visibleProbeCount))
            : 1;
        int probeRowCount = visibleProbeCount == 0 ? 0 : (int)Math.Ceiling(visibleProbeCount / (double)probeColumnCount);
        int childColumnCount = inlineChildren && visibleChildCount > 2 ? 2 : 1;
        int childRowCount = visibleChildCount == 0 ? 0 : (int)Math.Ceiling(visibleChildCount / (double)childColumnCount);
        double panelWidth = Math.Clamp(
            bounds.Width * (inlineChildren ? (CompactLayout ? 0.72 : 0.78) : (CompactLayout ? 0.58 : 0.66)),
            CompactLayout ? 360 : 620,
            inlineChildren ? (CompactLayout ? 760 : 1240) : (CompactLayout ? 620 : 980));
        double childCardHeight = GetChildCardHeight(childScopes.Take(visibleChildCount));
        double childRowPitch = childCardHeight + (CompactLayout ? 10 : 16);
        double navigationHeight = (CompactLayout ? 116 : 148) + (inlineChildren ? Math.Max(0, childRowCount - 1) * childRowPitch : 0);
        double childrenBlockHeight = inlineChildren || visibleChildCount == 0
            ? 0
            : (CompactLayout ? 62 : 76) + Math.Max(0, childRowCount - 1) * childRowPitch;
        int localColumns = !CompactLayout && panelWidth >= 900 && visibleLocalCount > 2 ? 2 : 1;
        int localRowCount = visibleLocalCount == 0 ? 0 : (int)Math.Ceiling(visibleLocalCount / (double)localColumns);
        double localRowPitch = CompactLayout ? 20 : 24;
        double localBlockHeight = visibleLocalCount == 0 ? 0 : (CompactLayout ? 46 : 54) + Math.Max(0, localRowCount - 1) * localRowPitch;
        double probeBlockHeight = visibleProbeCount == 0 ? 44 : (CompactLayout ? 36 : 42) + probeRowCount * (CompactLayout ? 30 : 34);
        double panelHeight = Math.Clamp(
            navigationHeight + childrenBlockHeight + localBlockHeight + probeBlockHeight + (CompactLayout ? 60 : 82),
            CompactLayout ? 230 : 340,
            CompactLayout ? 390 : 620);
        double panelX = Math.Clamp(GetCenterX(moduleRect) - panelWidth / 2, 16, bounds.Right - panelWidth - 16);
        double minimumTop = scopeCard is null ? 18 : scopeCard.Value.Bottom + 12;
        double targetY = moduleRect.Bottom + 38;
        double panelY = Math.Max(minimumTop, Math.Min(bounds.Bottom - panelHeight - 16, targetY));
        Rect panel = new(panelX, panelY, panelWidth, panelHeight);

        DrawScopeConnector(context, moduleRect, panel);

        context.FillRectangle(FocusPanelFillBrush, panel, 8);
        context.DrawRectangle(new Pen(ModuleStrokeBrush, 1.2), panel, 8);

        DrawText(context, string.IsNullOrWhiteSpace(ActiveScopeTitle) ? "Scope" : ActiveScopeTitle!, panel.X + 16, panel.Y + 12, TextBrush, 13);
        DrawText(context, Ellipsize(ActiveScopeModuleName ?? "module", 11, panel.Width - 32), panel.X + 16, panel.Y + 34, PinStrokeBrush, 11);
        if (!string.IsNullOrWhiteSpace(ActiveScopePath))
        {
            DrawText(context, Ellipsize(ActiveScopePath!, 10, panel.Width - 32), panel.X + 16, panel.Y + 52, MutedBrush, 10);
        }

        CurrentPortLayout currentPortLayout = DrawCurrentScopeNode(
            context,
            panel,
            inlineChildren,
            parentScope is not null,
            visibleChildCount > 0,
            scopePorts,
            visibleLeftPortCount,
            visibleRightPortCount);
        IReadOnlyList<ChildNodeLayout> childLayouts = DrawNavigationNeighborhood(
            context,
            panel,
            currentPortLayout.Bounds,
            parentScope,
            childScopes,
            visibleChildCount,
            inlineChildren);

        if (!inlineChildren && visibleChildCount > 0)
        {
            childLayouts = DrawChildRowsBelow(context, panel, currentPortLayout.Bounds, childScopes, visibleChildCount);
        }

        double localsTop = inlineChildren
            ? currentPortLayout.Bounds.Bottom + 18
            : currentPortLayout.Bounds.Bottom + 18 + childrenBlockHeight;
        IReadOnlyDictionary<string, Point> localSignalAnchors = DrawLocalSignalSection(context, panel, localSignals, visibleLocalCount, localsTop);
        DrawConnectionRoutes(context, currentPortLayout, childLayouts, localSignalAnchors);

        double probesTop = localsTop + localBlockHeight + (localBlockHeight > 0 ? (CompactLayout ? 8 : 12) : 0);
        DrawScopeProbeSection(context, panel, scopeSignals, visibleProbeCount, probesTop);

        string footer = BuildScopeFooter(scopeSignals.Count, visibleProbeCount, childScopes.Count);
        DrawText(context, Ellipsize(footer, 10, panel.Width - 32), panel.X + 16, panel.Bottom - 18, MutedBrush, 10);
    }

    private CurrentPortLayout DrawCurrentScopeNode(
        DrawingContext context,
        Rect panel,
        bool inlineChildren,
        bool hasParent,
        bool hasChildren,
        IReadOnlyList<HierarchyScopePortViewModel> scopePorts,
        int visibleLeftPortCount,
        int visibleRightPortCount)
    {
        double reservedLeft = Math.Max(hasParent ? (CompactLayout ? 152 : 212) : 24, visibleLeftPortCount > 0 ? (CompactLayout ? 182 : 232) : 24);
        double reservedRight = Math.Max(inlineChildren && hasChildren ? (CompactLayout ? 244 : 360) : 24, visibleRightPortCount > 0 ? (CompactLayout ? 182 : 232) : 24);
        double availableWidth = panel.Width - reservedLeft - reservedRight;
        double nodeWidth = Math.Clamp(availableWidth, CompactLayout ? 168 : 240, CompactLayout ? 220 : 320);
        double nodeHeight = GetCurrentNodeHeight(visibleLeftPortCount, visibleRightPortCount);
        double nodeX = panel.X + reservedLeft + Math.Max(0, (availableWidth - nodeWidth) / 2);
        Rect rect = new(nodeX, panel.Y + (CompactLayout ? 82 : 98), nodeWidth, nodeHeight);

        context.FillRectangle(NodeSelectedFillBrush, rect, 8);
        context.DrawRectangle(new Pen(SelectedBrush, 1.4), rect, 8);
        DrawText(context, Ellipsize(ActiveScopeTitle ?? "Scope", 12, rect.Width - 24), rect.X + 12, rect.Y + 10, TextBrush, 12);
        DrawText(context, Ellipsize(ActiveScopeModuleName ?? "module", 11, rect.Width - 24), rect.X + 12, rect.Y + 30, PinStrokeBrush, 11);
        DrawText(context, "active", rect.X + 12, rect.Bottom - 16, SelectedBrush, 10);

        IReadOnlyDictionary<string, PortAnchor> anchors = DrawScopePorts(context, rect, scopePorts);
        return new CurrentPortLayout(rect, anchors);
    }

    private IReadOnlyList<ChildNodeLayout> DrawNavigationNeighborhood(
        DrawingContext context,
        Rect panel,
        Rect currentNodeRect,
        HierarchyScopeNodeViewModel? parentScope,
        IReadOnlyList<HierarchyScopeInstanceViewModel> childScopes,
        int visibleChildCount,
        bool inlineChildren)
    {
        List<ChildNodeLayout> layouts = [];
        if (parentScope is not null)
        {
            Rect parentRect = new(panel.X + 16, currentNodeRect.Y + 8, 128, 48);
            DrawScopeLink(context, new Point(parentRect.Right, parentRect.Center.Y), new Point(currentNodeRect.X, currentNodeRect.Center.Y));
            DrawParentScopeNodeCard(context, parentScope, parentRect);
        }

        if (visibleChildCount == 0 || !inlineChildren)
        {
            return layouts;
        }

        double areaX = currentNodeRect.Right + (CompactLayout ? 26 : 34);
        double areaWidth = panel.Right - 16 - areaX;
        int childColumnCount = visibleChildCount > 2 ? 2 : 1;
        int childRowCount = (int)Math.Ceiling(visibleChildCount / (double)childColumnCount);
        double cardWidth = childColumnCount == 1
            ? Math.Max(144, areaWidth)
            : (areaWidth - (CompactLayout ? 10 : 18)) / 2;
        double cardHeight = GetChildCardHeight(childScopes.Take(visibleChildCount));
        double rowPitch = cardHeight + (CompactLayout ? 10 : 16);

        for (int index = 0; index < visibleChildCount; index++)
        {
            int row = index / childColumnCount;
            int column = index % childColumnCount;
            Rect childRect = new(
                areaX + column * (cardWidth + (CompactLayout ? 10 : 18)),
                currentNodeRect.Y + row * rowPitch,
                cardWidth,
                cardHeight);
            DrawScopeLink(context, new Point(currentNodeRect.Right, currentNodeRect.Center.Y), new Point(childRect.X, childRect.Center.Y));
            layouts.Add(DrawScopeNodeCard(context, childScopes[index], childRect, role: "child"));
        }

        return layouts;
    }

    private IReadOnlyList<ChildNodeLayout> DrawChildRowsBelow(
        DrawingContext context,
        Rect panel,
        Rect currentNodeRect,
        IReadOnlyList<HierarchyScopeInstanceViewModel> childScopes,
        int visibleChildCount)
    {
        List<ChildNodeLayout> layouts = [];
        int columns = panel.Width >= 520 && visibleChildCount > 1 ? 2 : 1;
        double availableWidth = panel.Width - 32;
        double cardWidth = columns == 1 ? availableWidth : (availableWidth - (CompactLayout ? 10 : 18)) / 2;
        double cardHeight = GetChildCardHeight(childScopes.Take(visibleChildCount));
        double top = currentNodeRect.Bottom + (CompactLayout ? 18 : 28);
        double rowPitch = cardHeight + (CompactLayout ? 10 : 16);

        for (int index = 0; index < visibleChildCount; index++)
        {
            int row = index / columns;
            int column = index % columns;
            Rect childRect = new(
                panel.X + 16 + column * (cardWidth + (CompactLayout ? 10 : 18)),
                top + row * rowPitch,
                cardWidth,
                cardHeight);
            DrawScopeLink(context, new Point(GetCenterX(currentNodeRect), currentNodeRect.Bottom), new Point(GetCenterX(childRect), childRect.Y));
            layouts.Add(DrawScopeNodeCard(context, childScopes[index], childRect, role: "child"));
        }

        return layouts;
    }

    private void DrawScopeProbeSection(
        DrawingContext context,
        Rect panel,
        IReadOnlyList<SignalViewModel> scopeSignals,
        int visibleProbeCount,
        double top)
    {
        DrawText(context, "Exact-scope probes", panel.X + 16, top, TextBrush, 11);
        DrawText(context, "local traced signals", panel.Right - (CompactLayout ? 116 : 134), top, MutedBrush, 10);

        if (visibleProbeCount == 0)
        {
            DrawText(context, "No exact-scope probes are available.", panel.X + 16, top + 22, MutedBrush, 10);
            if (!string.IsNullOrWhiteSpace(ActiveScopeHint))
            {
                DrawText(context, Ellipsize(ActiveScopeHint!, 10, panel.Width - 32), panel.X + 16, top + 40, MutedBrush, 10);
            }

            return;
        }

        int columns = panel.Width >= (CompactLayout ? 900 : 1200) && visibleProbeCount > 1
            ? (CompactLayout ? 2 : Math.Min(3, visibleProbeCount))
            : 1;
        double columnGap = CompactLayout ? 14 : 18;
        double itemWidth = columns == 1
            ? panel.Width - 32
            : (panel.Width - 32 - columnGap) / 2;
        if (!CompactLayout && columns == 3)
        {
            itemWidth = (panel.Width - 32 - columnGap * 2) / 3;
        }

        for (int index = 0; index < visibleProbeCount; index++)
        {
            int row = index / columns;
            int column = index % columns;
            double itemX = panel.X + 16 + column * (itemWidth + columnGap);
            double itemY = top + 20 + row * (CompactLayout ? 30 : 34);
            DrawScopeProbe(context, scopeSignals[index], new Rect(itemX, itemY, itemWidth, 24));
        }
    }

    private void DrawScopeProbe(DrawingContext context, SignalViewModel signal, Rect rect)
    {
        bool isSelected = string.Equals(signal.Name, SelectedSignalName, StringComparison.OrdinalIgnoreCase);
        IBrush stroke = isSelected ? SelectedBrush : PinStrokeBrush;
        IBrush labelBrush = isSelected ? SelectedBrush : TextBrush;
        double badgeWidth = GetValueBadgeWidth(signal.Value, 54, 92);
        Rect badge = new(rect.Right - badgeWidth, rect.Y + 2, badgeWidth, 20);
        double centerY = rect.Y + rect.Height / 2;
        Rect lineRect = new(rect.X + 4, centerY - 3, 6, 6);

        if (isSelected)
        {
            context.FillRectangle(ScopeHighlightBrush, rect, 5);
        }

        context.DrawRectangle(new Pen(isSelected ? SelectedBrush : ModuleStrokeBrush, isSelected ? 1.2 : 1), rect, 5);
        context.FillRectangle(stroke, lineRect);
        context.DrawLine(new Pen(stroke, 1.4), new Point(lineRect.Right + 2, centerY), new Point(badge.X - 8, centerY));

        double labelStart = rect.X + 16;
        if (signal.IsInWaveform)
        {
            Rect waveformBadge = new(badge.X - 28, rect.Y + 2, 20, 20);
            DrawMiniBadge(context, waveformBadge, "W", OutputValueBrush);
        }

        string label = Ellipsize(signal.ShortName, 11, Math.Max(60, badge.X - labelStart - 32));
        DrawText(context, label, labelStart, rect.Y + 4, labelBrush, 11);
        DrawValueBadge(context, signal.Value, badge, stroke, ValueFillBrush);

        _signalHitTargets.Add(new SignalHitTarget(signal, rect));
    }

    private ChildNodeLayout DrawScopeNodeCard(DrawingContext context, HierarchyScopeInstanceViewModel scope, Rect rect, string role)
    {
        bool selected = string.Equals(scope.HierarchyPath, ActiveScopePath, StringComparison.OrdinalIgnoreCase);
        IBrush fill = selected ? NodeSelectedFillBrush : NodeFillBrush;
        IBrush stroke = selected ? SelectedBrush : (scope.HasTraceActivity ? PinStrokeBrush : ModuleStrokeBrush);

        context.FillRectangle(fill, rect, 7);
        context.DrawRectangle(new Pen(stroke, selected ? 1.4 : 1), rect, 7);
        DrawText(context, Ellipsize(scope.InstanceName, 11, rect.Width - 24), rect.X + 10, rect.Y + 8, TextBrush, 11);
        DrawText(context, Ellipsize(scope.ModuleName, 10, rect.Width - 24), rect.X + 10, rect.Y + 24, MutedBrush, 10);
        DrawText(context, role, rect.Right - 34, rect.Y + 8, MutedBrush, 9);
        DrawPortCountStubs(context, rect, scope.InputCount, scope.OutputCount, stroke);
        DrawMiniBadge(context, new Rect(rect.Right - 62, rect.Bottom - 20, 54, 16), scope.ScopeBadgeText, stroke);
        _scopeHitTargets.Add(new ScopeHitTarget(scope.HierarchyPath, rect));
        IReadOnlyDictionary<string, Point> anchors = DrawChildConnectionStubs(context, rect, scope.PortConnections);
        return new ChildNodeLayout(scope, rect, anchors);
    }

    private void DrawParentScopeNodeCard(DrawingContext context, HierarchyScopeNodeViewModel scope, Rect rect)
    {
        bool selected = string.Equals(scope.HierarchyPath, ActiveScopePath, StringComparison.OrdinalIgnoreCase);
        IBrush fill = selected ? NodeSelectedFillBrush : NodeFillBrush;
        IBrush stroke = selected ? SelectedBrush : ModuleStrokeBrush;

        context.FillRectangle(fill, rect, 7);
        context.DrawRectangle(new Pen(stroke, selected ? 1.4 : 1), rect, 7);
        DrawText(context, Ellipsize(scope.InstanceName, 11, rect.Width - 24), rect.X + 10, rect.Y + 8, TextBrush, 11);
        DrawText(context, Ellipsize(scope.ModuleName, 10, rect.Width - 24), rect.X + 10, rect.Y + 24, MutedBrush, 10);
        DrawText(context, "up", rect.Right - 18, rect.Y + 8, MutedBrush, 9);
        _scopeHitTargets.Add(new ScopeHitTarget(scope.HierarchyPath, rect));
    }

    private IReadOnlyDictionary<string, PortAnchor> DrawScopePorts(DrawingContext context, Rect nodeRect, IReadOnlyList<HierarchyScopePortViewModel> ports)
    {
        Dictionary<string, PortAnchor> anchors = new(StringComparer.OrdinalIgnoreCase);
        int maxPorts = CompactLayout ? 5 : 8;
        IReadOnlyList<HierarchyScopePortViewModel> inputs = ports.Where(static port => port.IsInput).Take(maxPorts).ToList();
        IReadOnlyList<HierarchyScopePortViewModel> outputs = ports.Where(static port => port.IsOutput).Take(maxPorts).ToList();
        double topInset = CompactLayout ? 14 : 18;
        double bottomInset = CompactLayout ? 14 : 18;
        double usableHeight = Math.Max(24, nodeRect.Height - topInset - bottomInset);
        double leftStep = usableHeight / Math.Max(1, inputs.Count + 1);
        double rightStep = usableHeight / Math.Max(1, outputs.Count + 1);

        for (int index = 0; index < inputs.Count; index++)
        {
            HierarchyScopePortViewModel port = inputs[index];
            double y = nodeRect.Y + topInset + leftStep * (index + 1);
            anchors[port.Name] = DrawPortStub(context, port, y, nodeRect.X, leftSide: true);
        }

        for (int index = 0; index < outputs.Count; index++)
        {
            HierarchyScopePortViewModel port = outputs[index];
            double y = nodeRect.Y + topInset + rightStep * (index + 1);
            anchors[port.Name] = DrawPortStub(context, port, y, nodeRect.Right, leftSide: false);
        }

        return anchors;
    }

    private PortAnchor DrawPortStub(DrawingContext context, HierarchyScopePortViewModel port, double y, double edgeX, bool leftSide)
    {
        double lineLength = CompactLayout ? 26 : 34;
        Rect badge = leftSide
            ? new(edgeX - (CompactLayout ? 160 : 194), y - 10, CompactLayout ? 48 : 58, 20)
            : new(edgeX + (CompactLayout ? 112 : 136), y - 10, CompactLayout ? 48 : 58, 20);
        double lineStartX = leftSide ? edgeX - lineLength : edgeX;
        double lineEndX = leftSide ? edgeX : edgeX + lineLength;
        string label = Ellipsize(port.Name, 10, CompactLayout ? 78 : 104);
        double labelX = leftSide ? badge.Right + 8 : edgeX + (CompactLayout ? 34 : 42);

        context.DrawLine(new Pen(PinStrokeBrush, 1.3), new Point(lineStartX, y), new Point(lineEndX, y));
        context.FillRectangle(PinStrokeBrush, new Rect(leftSide ? edgeX - 2 : edgeX - 2, y - 2, 4, 4));
        DrawText(context, label, leftSide ? badge.Right + 8 : labelX, y - 7, MutedBrush, 10);
        DrawMiniBadge(context, badge, port.WidthLabel, PinStrokeBrush);
        Point anchor = new(leftSide ? lineStartX : lineEndX, y);
        return new PortAnchor(port.Name, anchor, leftSide);
    }

    private IReadOnlyDictionary<string, Point> DrawChildConnectionStubs(
        DrawingContext context,
        Rect rect,
        IReadOnlyList<HierarchyScopeInstancePortConnectionViewModel> connections)
    {
        Dictionary<string, Point> anchors = new(StringComparer.OrdinalIgnoreCase);
        int maxConnections = CompactLayout ? 3 : 5;
        IReadOnlyList<HierarchyScopeInstancePortConnectionViewModel> inputs = connections.Where(static connection => connection.IsInput).Take(maxConnections).ToList();
        IReadOnlyList<HierarchyScopeInstancePortConnectionViewModel> outputs = connections.Where(static connection => connection.IsOutput).Take(maxConnections).ToList();
        double topInset = CompactLayout ? 16 : 22;
        double bottomInset = CompactLayout ? 14 : 18;
        double usableHeight = Math.Max(20, rect.Height - topInset - bottomInset);
        double leftStep = usableHeight / Math.Max(1, inputs.Count + 1);
        double rightStep = usableHeight / Math.Max(1, outputs.Count + 1);

        for (int index = 0; index < inputs.Count; index++)
        {
            HierarchyScopeInstancePortConnectionViewModel connection = inputs[index];
            double y = rect.Y + topInset + leftStep * (index + 1);
            double stubLength = CompactLayout ? 10 : 14;
            context.DrawLine(new Pen(PinStrokeBrush, 1.1), new Point(rect.X - stubLength, y), new Point(rect.X, y));
            DrawText(context, Ellipsize(connection.PortName, 9, CompactLayout ? 44 : 64), rect.X + 4, y - 6, MutedBrush, 9);
            anchors[connection.PortName] = new Point(rect.X - stubLength, y);
        }

        for (int index = 0; index < outputs.Count; index++)
        {
            HierarchyScopeInstancePortConnectionViewModel connection = outputs[index];
            double y = rect.Y + topInset + rightStep * (index + 1);
            double stubLength = CompactLayout ? 10 : 14;
            context.DrawLine(new Pen(OutputValueBrush, 1.1), new Point(rect.Right, y), new Point(rect.Right + stubLength, y));
            double labelWidth = CompactLayout ? 44 : 64;
            double labelX = rect.Right - labelWidth - 8;
            DrawText(context, Ellipsize(connection.PortName, 9, labelWidth), labelX, y - 6, MutedBrush, 9);
            anchors[connection.PortName] = new Point(rect.Right + stubLength, y);
        }

        return anchors;
    }

    private IReadOnlyDictionary<string, Point> DrawLocalSignalSection(
        DrawingContext context,
        Rect panel,
        IReadOnlyList<HierarchyScopeLocalSignalViewModel> localSignals,
        int visibleLocalCount,
        double top)
    {
        Dictionary<string, Point> anchors = new(StringComparer.OrdinalIgnoreCase);
        if (visibleLocalCount == 0)
        {
            return anchors;
        }

        DrawText(context, "Local nets", panel.X + 16, top, TextBrush, 11);
        double chipY = top + 18;
        double chipWidth = panel.Width >= 760 && visibleLocalCount > 2 ? (CompactLayout ? 150 : 190) : (CompactLayout ? 170 : 220);
        int columns = panel.Width >= 760 && visibleLocalCount > 2 ? (!CompactLayout && visibleLocalCount > 4 ? 3 : 2) : 1;
        for (int index = 0; index < visibleLocalCount; index++)
        {
            int row = index / columns;
            int column = index % columns;
            HierarchyScopeLocalSignalViewModel signal = localSignals[index];
            Rect chip = new(
                panel.X + 16 + column * (chipWidth + (CompactLayout ? 12 : 14)),
                chipY + row * (CompactLayout ? 20 : 24),
                chipWidth,
                CompactLayout ? 16 : 18);
            IBrush stroke = signal.IsTraced ? PinStrokeBrush : ModuleStrokeBrush;
            context.FillRectangle(ValueFillBrush, chip, 4);
            context.DrawRectangle(new Pen(stroke, 1), chip, 4);
            DrawText(context, Ellipsize(signal.Name, 10, chip.Width - 62), chip.X + 6, chip.Y + 2, TextBrush, 10);
            DrawText(context, signal.WidthLabel, chip.Right - 28, chip.Y + 2, stroke, 10);
            anchors[signal.Name] = new Point(chip.Right, chip.Y + chip.Height / 2);
        }

        return anchors;
    }

    private void DrawConnectionRoutes(
        DrawingContext context,
        CurrentPortLayout currentPortLayout,
        IReadOnlyList<ChildNodeLayout> childLayouts,
        IReadOnlyDictionary<string, Point> localSignalAnchors)
    {
        foreach (ChildNodeLayout child in childLayouts)
        {
            foreach (HierarchyScopeInstancePortConnectionViewModel connection in child.Instance.PortConnections)
            {
                if (!child.PortAnchors.TryGetValue(connection.PortName, out Point childAnchor))
                {
                    continue;
                }

                Point? source = null;
                if (currentPortLayout.PortAnchors.TryGetValue(connection.SignalName, out PortAnchor? currentPort) && currentPort is not null)
                {
                    source = currentPort.Point;
                }
                else if (localSignalAnchors.TryGetValue(connection.SignalName, out Point localAnchor))
                {
                    source = localAnchor;
                }

                if (source is null)
                {
                    continue;
                }

                DrawScopedConnectionRoute(
                    context,
                    source.Value,
                    childAnchor,
                    connection.IsInput ? ConnectorBrush : OutputValueBrush);
            }
        }
    }

    private void DrawScopedConnectionRoute(DrawingContext context, Point start, Point end, IBrush brush)
    {
        double midX = start.X + (end.X - start.X) / 2;
        Pen pen = new(brush, 1.1);
        context.DrawLine(pen, start, new Point(midX, start.Y));
        context.DrawLine(pen, new Point(midX, start.Y), new Point(midX, end.Y));
        context.DrawLine(pen, new Point(midX, end.Y), end);
    }

    private void DrawPortCountStubs(DrawingContext context, Rect rect, int inputCount, int outputCount, IBrush stroke)
    {
        double leftCount = Math.Min(3, inputCount);
        double rightCount = Math.Min(3, outputCount);
        for (int index = 0; index < leftCount; index++)
        {
            double y = rect.Y + 12 + index * 10;
            context.DrawLine(new Pen(stroke, 1.1), new Point(rect.X - 8, y), new Point(rect.X, y));
        }

        for (int index = 0; index < rightCount; index++)
        {
            double y = rect.Y + 12 + index * 10;
            context.DrawLine(new Pen(stroke, 1.1), new Point(rect.Right, y), new Point(rect.Right + 8, y));
        }
    }

    private void DrawScopeConnector(DrawingContext context, Rect moduleRect, Rect panelRect)
    {
        DrawScopeLink(
            context,
            new Point(GetCenterX(moduleRect), moduleRect.Bottom),
            new Point(GetCenterX(panelRect), panelRect.Y));
    }

    private void DrawScopeLink(DrawingContext context, Point start, Point end)
    {
        double midY = start.Y + (end.Y - start.Y) / 2;
        Pen pen = new(ConnectorBrush, 1.2);
        context.DrawLine(pen, start, new Point(start.X, midY));
        context.DrawLine(pen, new Point(start.X, midY), new Point(end.X, midY));
        context.DrawLine(pen, new Point(end.X, midY), end);
    }

    private SignalHitTarget? HitTestSignal(Point point) => _signalHitTargets.FirstOrDefault(hit => hit.Bounds.Contains(point));

    private ScopeHitTarget? HitTestScope(Point point) => _scopeHitTargets.FirstOrDefault(hit => hit.Bounds.Contains(point));

    private void OnSignalsChanged(object? sender, NotifyCollectionChangedEventArgs e) => OnSignalCollectionChanged(e);

    private void OnScopeSignalsChanged(object? sender, NotifyCollectionChangedEventArgs e) => OnSignalCollectionChanged(e);

    private void OnScopeChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    private void OnScopePortsChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    private void OnScopeLocalSignalsChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    private void OnSignalCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (SignalViewModel signal in e.OldItems.OfType<SignalViewModel>())
            {
                signal.PropertyChanged -= OnSignalPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (SignalViewModel signal in e.NewItems.OfType<SignalViewModel>())
            {
                signal.PropertyChanged += OnSignalPropertyChanged;
            }
        }

        InvalidateVisual();
    }

    private void OnSignalPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SignalViewModel.Value) or nameof(SignalViewModel.IsInWaveform))
        {
            InvalidateVisual();
        }
    }

    private void AttachSignalSource(
        IEnumerable<SignalViewModel>? signals,
        ref INotifyCollectionChanged? observableSignals,
        NotifyCollectionChangedEventHandler handler)
    {
        if (observableSignals is not null)
        {
            observableSignals.CollectionChanged -= handler;
        }

        observableSignals = signals as INotifyCollectionChanged;
        if (observableSignals is not null)
        {
            observableSignals.CollectionChanged += handler;
        }

        if (signals is null)
        {
            return;
        }

        foreach (SignalViewModel signal in signals)
        {
            signal.PropertyChanged += OnSignalPropertyChanged;
        }
    }

    private void DetachSignalSource(
        IEnumerable<SignalViewModel>? signals,
        ref INotifyCollectionChanged? observableSignals,
        NotifyCollectionChangedEventHandler handler)
    {
        if (signals is not null)
        {
            foreach (SignalViewModel signal in signals)
            {
                signal.PropertyChanged -= OnSignalPropertyChanged;
            }
        }

        if (observableSignals is not null)
        {
            observableSignals.CollectionChanged -= handler;
            observableSignals = null;
        }
    }

    private void AttachCollection(
        INotifyCollectionChanged? source,
        ref INotifyCollectionChanged? field,
        NotifyCollectionChangedEventHandler handler)
    {
        if (field is not null)
        {
            field.CollectionChanged -= handler;
        }

        field = source;
        if (field is not null)
        {
            field.CollectionChanged += handler;
        }
    }

    private static void DetachCollection(
        INotifyCollectionChanged? source,
        ref INotifyCollectionChanged? field,
        NotifyCollectionChangedEventHandler handler)
    {
        if (source is not null)
        {
            source.CollectionChanged -= handler;
        }

        field = null;
    }

    private bool HasScopeContext(
        IReadOnlyCollection<SignalViewModel> scopeSignals,
        IReadOnlyCollection<HierarchyScopeInstanceViewModel> childScopes,
        HierarchyScopeNodeViewModel? parentScope)
    {
        if (scopeSignals.Count > 0 || childScopes.Count > 0 || parentScope is not null)
        {
            return true;
        }

        string? scopePath = ActiveScopePath;
        return !string.IsNullOrWhiteSpace(scopePath)
            && !string.Equals(scopePath, ModuleName, StringComparison.OrdinalIgnoreCase);
    }

    private bool HasScopeCardContent() =>
        !string.IsNullOrWhiteSpace(ActiveScopePath)
        && !string.Equals(ActiveScopePath, ModuleName, StringComparison.OrdinalIgnoreCase);

    private string BuildScopeFooter(int totalProbeCount, int visibleProbeCount, int childScopeCount)
    {
        if (totalProbeCount > visibleProbeCount)
        {
            return $"+{totalProbeCount - visibleProbeCount} more exact-scope probes in the hierarchy list.";
        }

        if (childScopeCount > 4)
        {
            return $"+{childScopeCount - 4} more child instances in hierarchy.";
        }

        if (!string.IsNullOrWhiteSpace(ActiveScopeHint))
        {
            return ActiveScopeHint!;
        }

        return ActiveScopeSummary ?? string.Empty;
    }

    private double GetCurrentNodeHeight(int inputCount, int outputCount)
    {
        int portCount = Math.Max(inputCount, outputCount);
        return CompactLayout
            ? Math.Clamp(58 + portCount * 9, 64, 108)
            : Math.Clamp(72 + portCount * 12, 84, 156);
    }

    private double GetChildCardHeight(IEnumerable<HierarchyScopeInstanceViewModel> scopes)
    {
        int maxRows = scopes
            .Select(scope => Math.Max(
                scope.PortConnections.Count(static connection => connection.IsInput),
                scope.PortConnections.Count(static connection => connection.IsOutput)))
            .DefaultIfEmpty(0)
            .Max();

        return CompactLayout
            ? Math.Clamp(48 + maxRows * 10, 52, 90)
            : Math.Clamp(64 + maxRows * 14, 76, 144);
    }

    private static void DrawValueBadge(DrawingContext context, string value, Rect rect, IBrush strokeBrush, IBrush fillBrush)
    {
        context.FillRectangle(fillBrush, rect, 5);
        context.DrawRectangle(new Pen(strokeBrush, 1), rect, 5);
        DrawText(
            context,
            value,
            rect.X + Math.Max(8, (rect.Width - MeasureWidth(value, 11)) / 2),
            rect.Y + 5,
            strokeBrush,
            11);
    }

    private static void DrawMiniBadge(DrawingContext context, Rect rect, string text, IBrush strokeBrush)
    {
        context.FillRectangle(ValueFillBrush, rect, 4);
        context.DrawRectangle(new Pen(strokeBrush, 1), rect, 4);
        DrawText(context, Ellipsize(text, 10, rect.Width - 8), rect.X + 4, rect.Y + 2, strokeBrush, 10);
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

    private static string Ellipsize(string text, double size, double maxWidth)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 0)
        {
            return string.Empty;
        }

        if (MeasureWidth(text, size) <= maxWidth)
        {
            return text;
        }

        const string ellipsis = "...";
        int length = text.Length;
        while (length > 1)
        {
            string candidate = text[..length] + ellipsis;
            if (MeasureWidth(candidate, size) <= maxWidth)
            {
                return candidate;
            }

            length--;
        }

        return ellipsis;
    }

    private static double GetValueBadgeWidth(string value, double minWidth, double maxWidth) =>
        Math.Clamp(MeasureWidth(value, 11) + 18, minWidth, maxWidth);

    private static double GetCenterX(Rect rect) => rect.X + rect.Width / 2;

    private sealed record SignalHitTarget(SignalViewModel Signal, Rect Bounds);

    private sealed record ScopeHitTarget(string HierarchyPath, Rect Bounds);

    private sealed record PortAnchor(string Name, Point Point, bool IsInput);

    private sealed record CurrentPortLayout(Rect Bounds, IReadOnlyDictionary<string, PortAnchor> PortAnchors);

    private sealed record ChildNodeLayout(
        HierarchyScopeInstanceViewModel Instance,
        Rect Bounds,
        IReadOnlyDictionary<string, Point> PortAnchors);

    public sealed class SignalEditorRequestedEventArgs(SignalViewModel signal) : EventArgs
    {
        public SignalViewModel Signal { get; } = signal;
    }

    public sealed class ViewportChangedEventArgs(double zoom, Point pan) : EventArgs
    {
        public double Zoom { get; } = zoom;

        public Point Pan { get; } = pan;
    }
}
