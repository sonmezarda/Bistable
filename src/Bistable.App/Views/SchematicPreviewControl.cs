using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Bistable.App.Services;
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

    public static readonly StyledProperty<ICommand?> ToggleScopeExpansionCommandProperty =
        AvaloniaProperty.Register<SchematicPreviewControl, ICommand?>(nameof(ToggleScopeExpansionCommand));

    public static readonly StyledProperty<bool> IsActiveScopeExpandedProperty =
        AvaloniaProperty.Register<SchematicPreviewControl, bool>(nameof(IsActiveScopeExpanded));

    public static readonly StyledProperty<IEnumerable<string>?> ExpandedScopePathsProperty =
        AvaloniaProperty.Register<SchematicPreviewControl, IEnumerable<string>?>(nameof(ExpandedScopePaths));

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
    private static readonly IBrush LocalNetBrush = SolidColorBrush.Parse("#4fd1b5");
    private static readonly Typeface MonoTypeface = new("monospace");
    private const double FitMargin = 32;
    private static readonly SchematicScopeLayoutEngine ScopeLayoutEngine = new();
    private static readonly SchematicConnectionRouter ConnectionRouter = new();
    private static readonly SchematicNodeCardLayoutEngine NodeCardLayoutEngine = new();

    private readonly List<SignalHitTarget> _signalHitTargets = [];
    private readonly List<SignalReferenceHitTarget> _signalReferenceHitTargets = [];
    private readonly List<ScopeHitTarget> _scopeHitTargets = [];
    private readonly List<ExpansionHitTarget> _expansionHitTargets = [];
    private INotifyCollectionChanged? _observableSignals;
    private INotifyCollectionChanged? _observableScopeSignals;
    private INotifyCollectionChanged? _observableScopeChildren;
    private INotifyCollectionChanged? _observableScopePorts;
    private INotifyCollectionChanged? _observableScopeLocalSignals;
    private INotifyCollectionChanged? _observableExpandedScopePaths;
    private double _viewportZoom = 1;
    private Point _viewportPan;
    private bool _isPanningViewport;
    private Point _lastViewportPointer;
    private bool _fitPending = true;
    private bool _viewportCustomized;
    private Size _lastViewportSize;
    private Size _lastWorldSize;
    private Rect? _lastModuleRect;
    private Rect? _lastFocusedScopePanelRect;

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

    public void FrameActiveScope()
    {
        Rect? target = _lastFocusedScopePanelRect ?? _lastModuleRect;
        if (target is null || Bounds.Width <= 0 || Bounds.Height <= 0 || _lastWorldSize.Width <= 0 || _lastWorldSize.Height <= 0)
        {
            FitToView();
            return;
        }

        FrameWorldRect(target.Value);
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

    public ICommand? ToggleScopeExpansionCommand
    {
        get => GetValue(ToggleScopeExpansionCommandProperty);
        set => SetValue(ToggleScopeExpansionCommandProperty, value);
    }

    public bool IsActiveScopeExpanded
    {
        get => GetValue(IsActiveScopeExpandedProperty);
        set => SetValue(IsActiveScopeExpandedProperty, value);
    }

    public IEnumerable<string>? ExpandedScopePaths
    {
        get => GetValue(ExpandedScopePathsProperty);
        set => SetValue(ExpandedScopePathsProperty, value);
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
        else if (change.Property == ExpandedScopePathsProperty)
        {
            DetachCollection(change.OldValue as INotifyCollectionChanged, ref _observableExpandedScopePaths, OnExpandedScopePathsChanged);
            AttachCollection(change.NewValue as INotifyCollectionChanged, ref _observableExpandedScopePaths, OnExpandedScopePathsChanged);
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
        bool hasScopeFocus = HasScopeContext(scopeSignals, childScopes, parentScope);
        bool expandedScope = IsActiveScopeExpanded && hasScopeFocus;

        if (inputs.Count == 0 && outputs.Count == 0)
        {
            DrawText(context, "Load a project to generate a top-level symbol schematic.", 16, 32, MutedBrush, 13);
            return;
        }

        Size worldSize = MeasureWorldSize(inputs.Count, outputs.Count, scopeSignals.Count, childScopes.Count, scopePorts.Count, localSignals.Count, expandedScope);
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
        _lastModuleRect = moduleRect;
        _lastFocusedScopePanelRect = null;

        using (context.PushTransform(Matrix.CreateTranslation(_viewportPan.X, _viewportPan.Y)))
        using (context.PushTransform(Matrix.CreateScale(_viewportZoom, _viewportZoom)))
        {
            context.FillRectangle(BackgroundBrush, worldBounds);

            Rect? scopeCard = DrawScopeCard(context, worldBounds);

            _signalHitTargets.Clear();
            _signalReferenceHitTargets.Clear();
            _scopeHitTargets.Clear();
            _expansionHitTargets.Clear();

            if (expandedScope)
            {
                DrawExpandedScopePanel(context, worldBounds, moduleRect, scopeCard, scopeSignals, childScopes, scopePorts, localSignals);
            }
            else if (hasScopeFocus && !string.Equals(ActiveScopePath, ModuleName, StringComparison.OrdinalIgnoreCase) && scopePorts.Count > 0)
            {
                DrawCollapsedScopeSymbol(context, moduleRect, scopePorts, canExpand: childScopes.Count > 0);
            }
            else
            {
                DrawCollapsedTopSymbol(context, worldBounds, moduleRect, inputs, outputs, laneHeight, canExpand: childScopes.Count > 0);
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
        ExpansionHitTarget? expansionHit = HitTestExpansion(point);
        if (expansionHit is not null)
        {
            HandleExpansionHit(expansionHit, e);
            return;
        }

        SignalHitTarget? signalHit = HitTestSignal(point);
        if (signalHit is not null)
        {
            HandleSignalHit(signalHit, e);
            return;
        }

        SignalReferenceHitTarget? signalReferenceHit = HitTestSignalReference(point);
        if (signalReferenceHit is not null)
        {
            HandleSignalReferenceHit(signalReferenceHit, e);
            return;
        }

        ScopeHitTarget? scopeHit = HitTestScope(point);
        if (scopeHit is not null)
        {
            SelectedSignalName = null;
            if (e.ClickCount >= 2 && scopeHit.CanExpand)
            {
                ExecuteScopeExpansionToggle(scopeHit.HierarchyPath);
                e.Handled = true;
                return;
            }

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
        bool interactive = HitTestSignal(worldPoint) is not null
            || HitTestSignalReference(worldPoint) is not null
            || HitTestScope(worldPoint) is not null
            || HitTestExpansion(worldPoint) is not null;
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

    private void HandleExpansionHit(ExpansionHitTarget hit, PointerPressedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(hit.HierarchyPath))
        {
            ExecuteScopeExpansionToggle(hit.HierarchyPath);
        }

        e.Handled = true;
    }

    private void ExecuteScopeExpansionToggle(string hierarchyPath)
    {
        ICommand? toggleCommand = ToggleScopeExpansionCommand;
        if (toggleCommand?.CanExecute(hierarchyPath) == true)
        {
            toggleCommand.Execute(hierarchyPath);
        }
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

    private static bool ShouldRefitForProperty(AvaloniaProperty property) =>
        property == CompactLayoutProperty
        || property == ModuleNameProperty
        || property == SignalsProperty
        || property == ScopeSignalsProperty
        || property == ScopeChildrenProperty
        || property == ScopePortsProperty
        || property == ScopeLocalSignalsProperty
        || property == ScopeParentProperty
        || property == IsActiveScopeExpandedProperty
        || property == ExpandedScopePathsProperty
        || property == ActiveScopeTitleProperty
        || property == ActiveScopeModuleNameProperty
        || property == ActiveScopePathProperty
        || property == ActiveScopeSummaryProperty
        || property == ActiveScopeHintProperty;

    private Point ViewportToWorld(Point point) =>
        new(
            (point.X - _viewportPan.X) / Math.Max(_viewportZoom, 0.0001),
            (point.Y - _viewportPan.Y) / Math.Max(_viewportZoom, 0.0001));

    private void DrawCollapsedTopSymbol(
        DrawingContext context,
        Rect worldBounds,
        Rect moduleRect,
        IReadOnlyList<SignalViewModel> inputs,
        IReadOnlyList<SignalViewModel> outputs,
        double laneHeight,
        bool canExpand)
    {
        context.FillRectangle(ModuleFillBrush, moduleRect, 10);
        context.DrawRectangle(new Pen(ModuleStrokeBrush, 1.5), moduleRect, 10);
        DrawText(context, ModuleName, moduleRect.X + 18, moduleRect.Y + 18, TextBrush, 20);
        DrawText(context, "Top-level symbol", moduleRect.X + 18, moduleRect.Y + 46, MutedBrush, 12);
        DrawText(context, "Click pins to drive", moduleRect.X + 18, moduleRect.Bottom - 28, MutedBrush, 11);
        if (canExpand)
        {
            DrawScopeExpansionButton(context, moduleRect, ActiveScopePath, expanded: false);
        }

        DrawPins(context, inputs, worldBounds, moduleRect, leftSide: true, laneHeight);
        DrawPins(context, outputs, worldBounds, moduleRect, leftSide: false, laneHeight);
    }

    private void DrawCollapsedScopeSymbol(
        DrawingContext context,
        Rect moduleRect,
        IReadOnlyList<HierarchyScopePortViewModel> ports,
        bool canExpand)
    {
        context.FillRectangle(ModuleFillBrush, moduleRect, 10);
        context.DrawRectangle(new Pen(ModuleStrokeBrush, 1.5), moduleRect, 10);
        DrawText(context, Ellipsize(ActiveScopeTitle ?? "Scope", 20, moduleRect.Width - 72), moduleRect.X + 18, moduleRect.Y + 18, TextBrush, 20);
        DrawText(context, Ellipsize(ActiveScopeModuleName ?? "module", 12, moduleRect.Width - 72), moduleRect.X + 18, moduleRect.Y + 48, PinStrokeBrush, 12);
        DrawText(context, "Collapsed hierarchy cell", moduleRect.X + 18, moduleRect.Bottom - 28, MutedBrush, 11);
        if (canExpand)
        {
            DrawScopeExpansionButton(context, moduleRect, ActiveScopePath, expanded: false);
        }

        IReadOnlyList<HierarchyScopePortViewModel> inputs = ports.Where(static port => port.IsInput).Take(CompactLayout ? 6 : 9).ToList();
        IReadOnlyList<HierarchyScopePortViewModel> outputs = ports.Where(static port => port.IsOutput).Take(CompactLayout ? 6 : 9).ToList();
        double laneCount = Math.Max(inputs.Count, outputs.Count);
        double laneHeight = Math.Max(24, Math.Min(42, (moduleRect.Height - 112) / Math.Max(1, laneCount)));

        for (int index = 0; index < inputs.Count; index++)
        {
            DrawCollapsedScopePort(context, inputs[index], moduleRect, index, laneHeight, leftSide: true);
        }

        for (int index = 0; index < outputs.Count; index++)
        {
            DrawCollapsedScopePort(context, outputs[index], moduleRect, index, laneHeight, leftSide: false);
        }
    }

    private void DrawCollapsedScopePort(
        DrawingContext context,
        HierarchyScopePortViewModel port,
        Rect moduleRect,
        int index,
        double laneHeight,
        bool leftSide)
    {
        double y = moduleRect.Y + 86 + index * laneHeight;
        double badgeWidth = CompactLayout ? 48 : 58;
        Rect badge = leftSide
            ? new(moduleRect.X + 56, y - 10, badgeWidth, 20)
            : new(moduleRect.Right - 56 - badgeWidth, y - 10, badgeWidth, 20);
        double pinStartX = leftSide ? moduleRect.X : moduleRect.Right - 34;
        double pinEndX = leftSide ? moduleRect.X + 34 : moduleRect.Right;
        IBrush stroke = leftSide ? PinStrokeBrush : OutputValueBrush;

        context.DrawLine(new Pen(stroke, 1.4), new Point(pinStartX, y), new Point(pinEndX, y));
        context.FillRectangle(stroke, new Rect(leftSide ? moduleRect.X - 2 : moduleRect.Right - 2, y - 2, 4, 4));
        DrawMiniBadge(context, badge, port.WidthLabel, stroke);

        string label = Ellipsize(port.Name, 10, CompactLayout ? 82 : 118);
        double labelX = leftSide
            ? badge.Right + 8
            : badge.X - 8 - MeasureWidth(label, 10);
        DrawText(context, label, labelX, y - 7, MutedBrush, 10);
    }

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
                double badgeX = Math.Max(16, pinStartX - badgeWidth - 6);
                Rect badge = new(badgeX, y - 12, badgeWidth, 24);
                double labelRight = badge.X - 10;
                string label = Ellipsize(signal.Name, 12, Math.Max(62, labelRight - 16));
                double labelWidth = MeasureWidth(label, 12);
                double nameX = Math.Max(16, labelRight - labelWidth);
                Rect hit = new(nameX - 8, y - 16, moduleRect.X - nameX + 8, 32);

                _signalHitTargets.Add(new SignalHitTarget(signal, hit));
                context.DrawLine(new Pen(stroke, 2), new Point(pinStartX, y), new Point(pinEndX, y));
                DrawText(context, label, nameX, y - 8, text, 12);
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

    private void HandleSignalReferenceHit(SignalReferenceHitTarget hit, PointerPressedEventArgs e)
    {
        SelectedSignalName = hit.SignalName;
        if (e.ClickCount >= 2)
        {
            ICommand? command = AddSelectedWaveformCommand;
            if (command?.CanExecute(null) == true)
            {
                command.Execute(null);
            }
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

    private void DrawExpandedScopePanel(
        DrawingContext context,
        Rect bounds,
        Rect moduleRect,
        Rect? scopeCard,
        IReadOnlyList<SignalViewModel> scopeSignals,
        IReadOnlyList<HierarchyScopeInstanceViewModel> childScopes,
        IReadOnlyList<HierarchyScopePortViewModel> scopePorts,
        IReadOnlyList<HierarchyScopeLocalSignalViewModel> localSignals)
    {
        int maxChildConnectionRows = childScopes
            .Select(scope => Math.Max(
                scope.PortConnections.Count(static connection => connection.IsInput),
                scope.PortConnections.Count(static connection => connection.IsOutput)))
            .DefaultIfEmpty(0)
            .Max();
        SchematicScopePanelLayout layout = ScopeLayoutEngine.Compute(new SchematicScopeLayoutInput(
            bounds,
            moduleRect,
            scopeCard?.Bottom,
            CompactLayout,
            ParentVisible: false,
            scopeSignals.Count,
            childScopes.Count,
            localSignals.Count,
            scopePorts.Count(static port => port.IsInput),
            scopePorts.Count(static port => port.IsOutput),
            maxChildConnectionRows));
        Rect panel = layout.PanelRect;
        int visibleProbeCount = Math.Min(scopeSignals.Count, CompactLayout ? 8 : 14);
        int visibleChildCount = Math.Min(childScopes.Count, CompactLayout ? 4 : 8);
        int visibleLocalCount = Math.Min(localSignals.Count, CompactLayout ? 4 : 8);
        IReadOnlyList<Rect> effectiveChildRects = BuildEffectiveChildRects(layout, childScopes, visibleChildCount);
        layout = BuildEffectiveScopeLayout(layout, effectiveChildRects, visibleLocalCount, bounds);
        panel = layout.PanelRect;
        _lastFocusedScopePanelRect = panel;

        context.FillRectangle(FocusPanelFillBrush, panel, 8);
        context.DrawRectangle(new Pen(ModuleStrokeBrush, 1.2), panel, 8);

        DrawText(context, string.IsNullOrWhiteSpace(ActiveScopeTitle) ? "Scope" : ActiveScopeTitle!, panel.X + 16, panel.Y + 12, TextBrush, 13);
        DrawText(context, Ellipsize(ActiveScopeModuleName ?? "module", 11, panel.Width - 32), panel.X + 16, panel.Y + 34, PinStrokeBrush, 11);
        if (!string.IsNullOrWhiteSpace(ActiveScopePath))
        {
            DrawText(context, Ellipsize(ActiveScopePath!, 10, panel.Width - 32), panel.X + 16, panel.Y + 52, MutedBrush, 10);
        }

        CurrentPortLayout currentPortLayout = DrawScopeBoundaryPorts(
            context,
            layout,
            scopePorts);
        IReadOnlyList<ChildNodeLayout> childLayouts = DrawNavigationNeighborhood(
            context,
            layout,
            childScopes,
            visibleChildCount);

        double localsTop = layout.LocalSectionRect?.Y ?? (GetLayoutContentBottom(layout) + 24);
        IReadOnlyDictionary<string, LocalSignalAnchor> localSignalAnchors = DrawLocalSignalSection(context, layout, localSignals, visibleLocalCount, localsTop);
        DrawConnectionRoutes(context, currentPortLayout, childLayouts, localSignalAnchors);
        DrawScopeBoundaryPorts(context, layout, scopePorts);
        for (int index = 0; index < childLayouts.Count; index++)
        {
            DrawScopeNodeCard(context, childLayouts[index].Instance, childLayouts[index].Bounds, role: "child");
        }

        DrawLocalSignalSection(context, layout, localSignals, visibleLocalCount, localsTop);

        DrawScopeProbeSection(context, layout, scopeSignals, visibleProbeCount);

        string footer = BuildScopeFooter(scopeSignals.Count, visibleProbeCount, childScopes.Count);
        DrawText(context, Ellipsize(footer, 10, panel.Width - 32), panel.X + 16, panel.Bottom - 18, MutedBrush, 10);
    }

    private CurrentPortLayout DrawScopeBoundaryPorts(
        DrawingContext context,
        SchematicScopePanelLayout layout,
        IReadOnlyList<HierarchyScopePortViewModel> scopePorts)
    {
        Rect rect = layout.CurrentNodeRect;
        DrawText(context, Ellipsize(ActiveScopeTitle ?? "Scope", 12, rect.Width), rect.X, rect.Y + 4, TextBrush, 12);
        DrawText(context, Ellipsize(ActiveScopeModuleName ?? "module", 11, rect.Width), rect.X, rect.Y + 24, PinStrokeBrush, 11);
        DrawScopeExpansionButton(context, new Rect(rect.X, rect.Y, Math.Max(120, rect.Width), 44), ActiveScopePath, expanded: true);

        IReadOnlyDictionary<string, PortAnchor> anchors = DrawScopeBoundaryPortGlyphs(context, layout.PanelRect, rect, scopePorts);
        return new CurrentPortLayout(layout, rect, anchors);
    }

    private IReadOnlyList<ChildNodeLayout> DrawNavigationNeighborhood(
        DrawingContext context,
        SchematicScopePanelLayout layout,
        IReadOnlyList<HierarchyScopeInstanceViewModel> childScopes,
        int visibleChildCount)
    {
        List<ChildNodeLayout> layouts = [];
        if (visibleChildCount == 0)
        {
            return layouts;
        }

        for (int index = 0; index < visibleChildCount; index++)
        {
            Rect childRect = layout.ChildNodeRects[index];
            layouts.Add(DrawScopeNodeCard(context, childScopes[index], childRect, role: "child"));
        }

        return layouts;
    }

    private IReadOnlyList<Rect> BuildEffectiveChildRects(
        SchematicScopePanelLayout layout,
        IReadOnlyList<HierarchyScopeInstanceViewModel> childScopes,
        int visibleChildCount)
    {
        if (visibleChildCount == 0)
        {
            return [];
        }

        List<Rect> rects = [];
        double nextY = double.NegativeInfinity;
        double gap = CompactLayout ? 24 : 32;
        for (int index = 0; index < visibleChildCount; index++)
        {
            Rect rect = layout.ChildNodeRects[index];
            if (rect.Y < nextY)
            {
                rect = new Rect(rect.X, nextY, rect.Width, rect.Height);
            }

            if (IsScopeExpanded(childScopes[index].HierarchyPath) && childScopes[index].ChildInstances.Count > 0)
            {
                rect = ExpandChildRectForNestedScope(rect, childScopes[index]);
            }

            rects.Add(rect);
            nextY = rect.Bottom + gap;
        }

        return rects;
    }

    private Rect ExpandChildRectForNestedScope(Rect rect, HierarchyScopeInstanceViewModel scope)
    {
        int visibleChildCount = Math.Min(scope.ChildInstances.Count, CompactLayout ? 3 : 5);
        int visibleLocalCount = Math.Min(scope.LocalSignals.Count, CompactLayout ? 3 : 5);
        int maxPortRows = Math.Max(
            Math.Max(scope.InputCount, scope.OutputCount),
            scope.ChildInstances
                .Select(child => Math.Max(
                    child.PortConnections.Count(static connection => connection.IsInput),
                    child.PortConnections.Count(static connection => connection.IsOutput)))
                .DefaultIfEmpty(0)
                .Max());
        double width = Math.Max(rect.Width, CompactLayout ? 760 : 980);
        double childHeight = CompactLayout ? 104 : 132;
        double height = Math.Max(
            rect.Height,
            (CompactLayout ? 168 : 204)
            + Math.Max(0, visibleChildCount) * (childHeight + (CompactLayout ? 18 : 24))
            + Math.Max(0, visibleLocalCount) * (CompactLayout ? 22 : 28)
            + Math.Max(0, maxPortRows - 3) * (CompactLayout ? 12 : 16));

        return new Rect(rect.X, rect.Y, width, Math.Clamp(height, CompactLayout ? 320 : 400, CompactLayout ? 780 : 980));
    }

    private SchematicScopePanelLayout BuildEffectiveScopeLayout(
        SchematicScopePanelLayout layout,
        IReadOnlyList<Rect> childRects,
        int visibleLocalCount,
        Rect worldBounds)
    {
        Rect panel = layout.PanelRect;
        double requiredRight = childRects.Count == 0
            ? panel.Right
            : Math.Max(panel.Right, childRects.Max(static rect => rect.Right) + (CompactLayout ? 18 : 24));
        if (requiredRight > panel.Right)
        {
            double nextWidth = Math.Min(requiredRight - panel.X, Math.Max(panel.Width, worldBounds.Right - panel.X - 16));
            panel = new Rect(panel.X, panel.Y, nextWidth, panel.Height);
        }

        double contentBottom = childRects.Count == 0
            ? layout.CurrentNodeRect.Bottom
            : Math.Max(layout.CurrentNodeRect.Bottom, childRects.Max(static rect => rect.Bottom));
        double margin = CompactLayout ? 18 : 22;
        double localsTop = contentBottom + (CompactLayout ? 38 : 46);
        double localHeight = layout.LocalSectionRect?.Height ?? 0;
        Rect? localSection = visibleLocalCount == 0
            ? null
            : new Rect(panel.X + margin, localsTop, panel.Width - margin * 2, localHeight);
        double probeTop = localsTop + localHeight + (localHeight > 0 ? (CompactLayout ? 16 : 20) : 0);
        Rect probeSection = new Rect(panel.X + margin, probeTop, panel.Width - margin * 2, layout.ProbeSectionRect.Height);
        double requiredHeight = probeSection.Bottom + (CompactLayout ? 36 : 44) - panel.Y;
        double maxHeight = Math.Max(panel.Height, panel.Bottom + (CompactLayout ? 340 : 460) - panel.Y);
        panel = new Rect(panel.X, panel.Y, panel.Width, Math.Min(Math.Max(panel.Height, requiredHeight), maxHeight));

        return layout with
        {
            PanelRect = panel,
            ChildNodeRects = childRects,
            LocalSectionRect = localSection,
            ProbeSectionRect = probeSection
        };
    }

    private static double GetLayoutContentBottom(SchematicScopePanelLayout layout) =>
        layout.ChildNodeRects.Count == 0
            ? layout.CurrentNodeRect.Bottom
            : Math.Max(layout.CurrentNodeRect.Bottom, layout.ChildNodeRects.Max(static rect => rect.Bottom));

    private SchematicScopePanelLayout BuildNestedScopeLayout(Rect rect, HierarchyScopeInstanceViewModel scope)
    {
        Rect panel = new(rect.X + 12, rect.Y + 64, rect.Width - 24, Math.Max(120, rect.Height - 92));
        int visibleChildCount = Math.Min(scope.ChildInstances.Count, CompactLayout ? 3 : 5);
        int visibleLocalCount = Math.Min(scope.LocalSignals.Count, CompactLayout ? 3 : 5);
        int visibleInputCount = Math.Min(scope.Ports.Count(static port => port.IsInput), CompactLayout ? 5 : 8);
        int visibleOutputCount = Math.Min(scope.Ports.Count(static port => port.IsOutput), CompactLayout ? 5 : 8);
        double boundaryWidth = Math.Clamp(panel.Width * 0.26, CompactLayout ? 190 : 230, CompactLayout ? 260 : 320);
        double boundaryHeight = Math.Clamp(
            (CompactLayout ? 88 : 112) + Math.Max(visibleInputCount, visibleOutputCount) * (CompactLayout ? 18 : 22),
            CompactLayout ? 138 : 170,
            Math.Max(CompactLayout ? 160 : 210, panel.Height - 64));
        Rect boundaryRect = new(panel.X + 14, panel.Y + 34, boundaryWidth, boundaryHeight);
        double childX = boundaryRect.Right + (CompactLayout ? 96 : 128);
        double childWidth = Math.Max(CompactLayout ? 240 : 300, panel.Right - childX - 16);
        double childHeight = CompactLayout ? 104 : 132;
        double childGap = CompactLayout ? 18 : 24;
        List<Rect> childRects = [];
        for (int index = 0; index < visibleChildCount; index++)
        {
            Rect childRect = new(childX, boundaryRect.Y + index * (childHeight + childGap), childWidth, childHeight);
            if (IsScopeExpanded(scope.ChildInstances[index].HierarchyPath) && scope.ChildInstances[index].ChildInstances.Count > 0)
            {
                childRect = ExpandChildRectForNestedScope(childRect, scope.ChildInstances[index]);
            }

            childRects.Add(childRect);
        }

        double contentBottom = childRects.Count == 0
            ? boundaryRect.Bottom
            : Math.Max(boundaryRect.Bottom, childRects.Max(static child => child.Bottom));
        double localHeight = visibleLocalCount == 0
            ? 0
            : (CompactLayout ? 42 : 54) + Math.Max(0, visibleLocalCount - 1) * (CompactLayout ? 8 : 10);
        Rect? localSection = visibleLocalCount == 0
            ? null
            : new Rect(panel.X + 14, contentBottom + (CompactLayout ? 22 : 28), panel.Width - 28, localHeight);
        Rect probeSection = new(panel.X + 14, (localSection?.Bottom ?? contentBottom) + 12, panel.Width - 28, 20);

        return new SchematicScopePanelLayout(
            panel,
            boundaryRect,
            null,
            childRects,
            localSection,
            probeSection,
            InlineChildren: true,
            RouteCorridorWidth: Math.Max(80, childX - boundaryRect.Right - 24),
            ChildCardWidth: childWidth,
            ChildCardHeight: childHeight,
            TitleBlockHeight: 0,
            LocalColumns: Math.Max(1, Math.Min(3, visibleLocalCount)),
            LocalRowCount: visibleLocalCount,
            ProbeColumns: 1,
            ProbeRowCount: 0);
    }

    private void DrawScopeProbeSection(
        DrawingContext context,
        SchematicScopePanelLayout layout,
        IReadOnlyList<SignalViewModel> scopeSignals,
        int visibleProbeCount)
    {
        Rect panel = layout.PanelRect;
        double top = layout.ProbeSectionRect.Y;
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

        int columns = layout.ProbeColumns;
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

        double labelStart = rect.X + 16;
        double labelEndReserve = badge.X - 12;
        if (signal.IsInWaveform)
        {
            Rect waveformBadge = new(badge.X - 28, rect.Y + 2, 20, 20);
            DrawMiniBadge(context, waveformBadge, "W", OutputValueBrush);
            labelEndReserve = waveformBadge.X - 8;
        }

        string label = Ellipsize(signal.ShortName, 11, Math.Max(48, labelEndReserve - labelStart));
        DrawText(context, label, labelStart, rect.Y + 4, labelBrush, 11);
        DrawValueBadge(context, signal.Value, badge, stroke, ValueFillBrush);

        _signalHitTargets.Add(new SignalHitTarget(signal, rect));
    }

    private ChildNodeLayout DrawScopeNodeCard(DrawingContext context, HierarchyScopeInstanceViewModel scope, Rect rect, string role)
    {
        return IsScopeExpanded(scope.HierarchyPath) && scope.ChildInstances.Count > 0
            ? DrawExpandedScopeNodeCard(context, scope, rect, role)
            : DrawCollapsedScopeNodeCard(context, scope, rect, role);
    }

    private ChildNodeLayout DrawCollapsedScopeNodeCard(DrawingContext context, HierarchyScopeInstanceViewModel scope, Rect rect, string role)
    {
        bool selected = string.Equals(scope.HierarchyPath, ActiveScopePath, StringComparison.OrdinalIgnoreCase);
        IBrush fill = selected ? NodeSelectedFillBrush : NodeFillBrush;
        IBrush stroke = selected ? SelectedBrush : (scope.HasTraceActivity ? PinStrokeBrush : ModuleStrokeBrush);
        IReadOnlyList<HierarchyScopeInstancePortConnectionViewModel> inputConnections = scope.PortConnections.Where(static connection => connection.IsInput).Take(CompactLayout ? 3 : 5).ToList();
        IReadOnlyList<HierarchyScopeInstancePortConnectionViewModel> outputConnections = scope.PortConnections.Where(static connection => connection.IsOutput).Take(CompactLayout ? 3 : 5).ToList();
        SchematicNodeCardLayout layout = NodeCardLayoutEngine.Compute(new SchematicNodeCardLayoutInput(
            rect,
            CompactLayout,
            inputConnections.Count,
            outputConnections.Count,
            scope.InputCount,
            scope.OutputCount));

        context.FillRectangle(fill, rect, 7);
        context.DrawRectangle(new Pen(stroke, selected ? 1.4 : 1), rect, 7);
        context.DrawLine(new Pen(ModuleStrokeBrush, 1), new Point(layout.HeaderRect.X + 8, layout.HeaderRect.Bottom), new Point(layout.HeaderRect.Right - 8, layout.HeaderRect.Bottom));
        context.DrawLine(new Pen(ModuleStrokeBrush, 1), new Point(layout.FooterRect.X + 8, layout.FooterRect.Y), new Point(layout.FooterRect.Right - 8, layout.FooterRect.Y));
        DrawText(context, Ellipsize(scope.InstanceName, 11, rect.Width - 84), rect.X + 10, rect.Y + 8, TextBrush, 11);
        DrawText(context, Ellipsize(scope.ModuleName, 10, rect.Width - 84), rect.X + 10, rect.Y + 24, MutedBrush, 10);
        DrawMiniBadge(context, new Rect(rect.Right - 34, rect.Y + 7, 24, 14), role[..1].ToUpperInvariant(), MutedBrush);
        if (scope.ChildInstances.Count > 0)
        {
            DrawScopeExpansionButton(context, rect, scope.HierarchyPath, expanded: false);
        }

        DrawMiniBadge(context, new Rect(rect.Right - 72, rect.Bottom - 20, 64, 16), scope.ScopeBadgeText, stroke);
        if (inputConnections.Count == 0 && outputConnections.Count == 0)
        {
            DrawPortCountStubs(context, rect, scope.InputCount, scope.OutputCount, stroke);
        }

        _scopeHitTargets.Add(new ScopeHitTarget(scope.HierarchyPath, rect, CanExpand: scope.ChildInstances.Count > 0));
        IReadOnlyDictionary<string, Point> anchors = DrawChildConnectionStubs(context, scope, layout, inputConnections, outputConnections);
        return new ChildNodeLayout(scope, rect, anchors);
    }

    private ChildNodeLayout DrawExpandedScopeNodeCard(DrawingContext context, HierarchyScopeInstanceViewModel scope, Rect rect, string role)
    {
        bool selected = string.Equals(scope.HierarchyPath, ActiveScopePath, StringComparison.OrdinalIgnoreCase);
        IBrush stroke = selected ? SelectedBrush : PinStrokeBrush;
        context.FillRectangle(NodeFillBrush, rect, 8);
        context.DrawRectangle(new Pen(stroke, selected ? 1.5 : 1.1), rect, 8);

        Rect header = new(rect.X + 12, rect.Y + 10, rect.Width - 24, 44);
        DrawText(context, Ellipsize(scope.InstanceName, 12, header.Width - 92), header.X, header.Y + 1, TextBrush, 12);
        DrawText(context, Ellipsize(scope.ModuleName, 10, header.Width - 92), header.X, header.Y + 22, PinStrokeBrush, 10);
        DrawMiniBadge(context, new Rect(rect.Right - 72, rect.Y + 13, 34, 16), role[..1].ToUpperInvariant(), MutedBrush);
        DrawScopeExpansionButton(context, rect, scope.HierarchyPath, expanded: true);
        context.DrawLine(new Pen(ModuleStrokeBrush, 1), new Point(rect.X + 12, rect.Y + 58), new Point(rect.Right - 12, rect.Y + 58));

        SchematicScopePanelLayout nestedLayout = BuildNestedScopeLayout(rect, scope);
        CurrentPortLayout boundaryLayout = DrawNestedScopeBoundaryPorts(context, nestedLayout, scope);
        IReadOnlyList<ChildNodeLayout> childLayouts = [];
        int visibleChildCount = Math.Min(scope.ChildInstances.Count, CompactLayout ? 3 : 5);
        if (visibleChildCount > 0)
        {
            List<ChildNodeLayout> layouts = [];
            for (int index = 0; index < visibleChildCount; index++)
            {
                layouts.Add(DrawScopeNodeCard(context, scope.ChildInstances[index], nestedLayout.ChildNodeRects[index], role: "child"));
            }

            childLayouts = layouts;
        }

        int visibleLocalCount = Math.Min(scope.LocalSignals.Count, CompactLayout ? 3 : 5);
        double localsTop = nestedLayout.LocalSectionRect?.Y ?? (GetLayoutContentBottom(nestedLayout) + 18);
        IReadOnlyDictionary<string, LocalSignalAnchor> localAnchors = DrawNestedLocalSignalSection(context, nestedLayout, scope.LocalSignals, visibleLocalCount, localsTop);
        DrawConnectionRoutes(context, boundaryLayout, childLayouts, localAnchors);

        boundaryLayout = DrawNestedScopeBoundaryPorts(context, nestedLayout, scope);
        for (int index = 0; index < childLayouts.Count; index++)
        {
            DrawScopeNodeCard(context, childLayouts[index].Instance, childLayouts[index].Bounds, role: "child");
        }

        DrawNestedLocalSignalSection(context, nestedLayout, scope.LocalSignals, visibleLocalCount, localsTop);

        if (scope.ChildInstances.Count > visibleChildCount)
        {
            DrawText(
                context,
                $"+{scope.ChildInstances.Count - visibleChildCount} child instances hidden at this zoom.",
                rect.X + 14,
                rect.Bottom - 18,
                MutedBrush,
                9);
        }

        _scopeHitTargets.Add(new ScopeHitTarget(scope.HierarchyPath, rect, CanExpand: true));
        Dictionary<string, Point> externalAnchors = boundaryLayout.PortAnchors.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.ExternalPoint,
            StringComparer.OrdinalIgnoreCase);
        return new ChildNodeLayout(scope, rect, externalAnchors);
    }

    private CurrentPortLayout DrawNestedScopeBoundaryPorts(
        DrawingContext context,
        SchematicScopePanelLayout layout,
        HierarchyScopeInstanceViewModel scope)
    {
        Rect rect = layout.CurrentNodeRect;
        DrawText(context, Ellipsize(scope.InstanceName, 10, rect.Width), rect.X, rect.Y - 20, TextBrush, 10);
        IReadOnlyDictionary<string, PortAnchor> anchors = DrawScopeBoundaryPortGlyphs(context, layout.PanelRect, rect, scope.Ports);
        return new CurrentPortLayout(layout, rect, anchors);
    }

    private IReadOnlyDictionary<string, LocalSignalAnchor> DrawNestedLocalSignalSection(
        DrawingContext context,
        SchematicScopePanelLayout layout,
        IReadOnlyList<HierarchyScopeLocalSignalViewModel> localSignals,
        int visibleLocalCount,
        double top)
    {
        Dictionary<string, LocalSignalAnchor> anchors = new(StringComparer.OrdinalIgnoreCase);
        if (visibleLocalCount == 0)
        {
            return anchors;
        }

        Rect panel = layout.PanelRect;
        DrawText(context, "Local nets", panel.X + 14, top, MutedBrush, 9);
        double chipWidth = CompactLayout ? 132 : 164;
        double chipHeight = CompactLayout ? 16 : 18;
        double gap = CompactLayout ? 8 : 10;
        int columns = Math.Max(1, Math.Min(3, (int)((panel.Width - 28 + gap) / (chipWidth + gap))));
        for (int index = 0; index < visibleLocalCount; index++)
        {
            int row = index / columns;
            int column = index % columns;
            HierarchyScopeLocalSignalViewModel signal = localSignals[index];
            Rect chip = new(
                panel.X + 14 + column * (chipWidth + gap),
                top + 16 + row * (chipHeight + 6),
                chipWidth,
                chipHeight);
            IBrush stroke = signal.IsTraced ? PinStrokeBrush : ModuleStrokeBrush;
            context.FillRectangle(ValueFillBrush, chip, 4);
            context.DrawRectangle(new Pen(stroke, 1), chip, 4);
            DrawText(context, Ellipsize(signal.Name, 9, chip.Width - 48), chip.X + 6, chip.Y + 2, TextBrush, 9);
            DrawText(context, signal.WidthLabel, chip.Right - 28, chip.Y + 2, stroke, 9);
            if (!string.IsNullOrWhiteSpace(signal.ResolvedSignalName))
            {
                _signalReferenceHitTargets.Add(new SignalReferenceHitTarget(signal.ResolvedSignalName!, chip, null));
            }

            anchors[signal.Name] = new LocalSignalAnchor(new Point(chip.Right, chip.Y + chip.Height / 2), signal.ResolvedSignalName);
        }

        return anchors;
    }

    private IReadOnlyDictionary<string, PortAnchor> DrawScopeBoundaryPortGlyphs(
        DrawingContext context,
        Rect panelRect,
        Rect nodeRect,
        IReadOnlyList<HierarchyScopePortViewModel> ports)
    {
        Dictionary<string, PortAnchor> anchors = new(StringComparer.OrdinalIgnoreCase);
        int maxPorts = CompactLayout ? 5 : 8;
        IReadOnlyList<HierarchyScopePortViewModel> inputs = ports.Where(static port => port.IsInput).Take(maxPorts).ToList();
        IReadOnlyList<HierarchyScopePortViewModel> outputs = ports.Where(static port => port.IsOutput).Take(maxPorts).ToList();
        double topInset = CompactLayout ? 68 : 78;
        double bottomInset = CompactLayout ? 20 : 24;
        double usableHeight = Math.Max(24, nodeRect.Height - topInset - bottomInset);
        double leftStep = usableHeight / Math.Max(1, inputs.Count + 1);
        double rightStep = usableHeight / Math.Max(1, outputs.Count + 1);
        double leftX = nodeRect.X;
        double rightX = Math.Min(panelRect.Right - (CompactLayout ? 150 : 190), nodeRect.Right + layoutSafeGap(nodeRect.Width));

        for (int index = 0; index < inputs.Count; index++)
        {
            HierarchyScopePortViewModel port = inputs[index];
            double y = nodeRect.Y + topInset + leftStep * (index + 1);
            anchors[port.Name] = DrawBoundaryPortGlyph(context, port, new Point(leftX, y), isInput: true);
        }

        for (int index = 0; index < outputs.Count; index++)
        {
            HierarchyScopePortViewModel port = outputs[index];
            double y = nodeRect.Y + topInset + rightStep * (index + 1);
            anchors[port.Name] = DrawBoundaryPortGlyph(context, port, new Point(rightX, y), isInput: false);
        }

        return anchors;

        static double layoutSafeGap(double width) => Math.Max(220, width + 120);
    }

    private PortAnchor DrawBoundaryPortGlyph(DrawingContext context, HierarchyScopePortViewModel port, Point origin, bool isInput)
    {
        double y = origin.Y;
        double glyphWidth = CompactLayout ? 18 : 22;
        double glyphHeight = CompactLayout ? 12 : 14;
        double badgeWidth = CompactLayout ? 48 : 58;
        double labelWidth = CompactLayout ? 78 : 112;
        Rect badge;
        Rect labelRect;
        Point anchor;
        Point externalAnchor;
        Point[] points;
        if (isInput)
        {
            points =
            [
                new Point(origin.X, y - glyphHeight / 2),
                new Point(origin.X + glyphWidth * 0.68, y - glyphHeight / 2),
                new Point(origin.X + glyphWidth, y),
                new Point(origin.X + glyphWidth * 0.68, y + glyphHeight / 2),
                new Point(origin.X, y + glyphHeight / 2)
            ];
            badge = new Rect(origin.X + glyphWidth + 8, y - 10, badgeWidth, 20);
            labelRect = new Rect(badge.Right + 8, y - 8, labelWidth, 18);
            anchor = new Point(labelRect.Right + 8, y);
            externalAnchor = new Point(origin.X, y);
            context.DrawLine(new Pen(PinStrokeBrush, 1.2), new Point(origin.X + glyphWidth, y), anchor);
        }
        else
        {
            points =
            [
                new Point(origin.X + glyphWidth, y - glyphHeight / 2),
                new Point(origin.X + glyphWidth * 0.32, y - glyphHeight / 2),
                new Point(origin.X, y),
                new Point(origin.X + glyphWidth * 0.32, y + glyphHeight / 2),
                new Point(origin.X + glyphWidth, y + glyphHeight / 2)
            ];
            labelRect = new Rect(origin.X - labelWidth - badgeWidth - 18, y - 8, labelWidth, 18);
            badge = new Rect(labelRect.Right + 8, y - 10, badgeWidth, 20);
            anchor = new Point(labelRect.X - 8, y);
            externalAnchor = new Point(origin.X + glyphWidth, y);
            context.DrawLine(new Pen(OutputValueBrush, 1.2), anchor, new Point(origin.X, y));
        }

        StreamGeometry geometry = new();
        using (StreamGeometryContext geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(points[0], isFilled: true);
            for (int index = 1; index < points.Length; index++)
            {
                geometryContext.LineTo(points[index]);
            }

            geometryContext.EndFigure(isClosed: true);
        }

        IBrush stroke = isInput ? PinStrokeBrush : OutputValueBrush;
        context.DrawGeometry(ValueFillBrush, new Pen(stroke, 1.1), geometry);
        DrawMiniBadge(context, badge, port.WidthLabel, PinStrokeBrush);
        DrawText(context, Ellipsize(port.Name, 10, labelRect.Width), labelRect.X, labelRect.Y + 2, MutedBrush, 10);
        context.FillRectangle(stroke, new Rect(anchor.X - 2, anchor.Y - 2, 4, 4));
        context.FillRectangle(stroke, new Rect(externalAnchor.X - 2, externalAnchor.Y - 2, 4, 4));
        return new PortAnchor(port.Name, anchor, port.IsInput, externalAnchor);
    }

    private IReadOnlyDictionary<string, Point> DrawChildConnectionStubs(
        DrawingContext context,
        HierarchyScopeInstanceViewModel scope,
        SchematicNodeCardLayout layout,
        IReadOnlyList<HierarchyScopeInstancePortConnectionViewModel> inputs,
        IReadOnlyList<HierarchyScopeInstancePortConnectionViewModel> outputs)
    {
        Dictionary<string, Point> anchors = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < inputs.Count && index < layout.InputRows.Count; index++)
        {
            HierarchyScopeInstancePortConnectionViewModel connection = inputs[index];
            SchematicNodeCardRowLayout row = layout.InputRows[index];
            DrawNodeConnectionRow(context, row, connection.PortName, connection.WidthLabel, PinStrokeBrush, TextBrush);
            anchors[connection.PortName] = row.RouteAnchor;
        }

        for (int index = 0; index < outputs.Count && index < layout.OutputRows.Count; index++)
        {
            HierarchyScopeInstancePortConnectionViewModel connection = outputs[index];
            SchematicNodeCardRowLayout row = layout.OutputRows[index];
            DrawNodeConnectionRow(context, row, connection.PortName, connection.WidthLabel, OutputValueBrush, TextBrush);
            anchors[connection.PortName] = row.RouteAnchor;
        }

        if (layout.HiddenInputCount > 0)
        {
            DrawText(context, $"+{layout.HiddenInputCount} in", layout.FooterRect.X + 8, layout.FooterRect.Y + 3, MutedBrush, 9);
        }

        if (layout.HiddenOutputCount > 0)
        {
            string hiddenText = $"+{layout.HiddenOutputCount} out";
            DrawText(
                context,
                hiddenText,
                layout.FooterRect.Right - MeasureWidth(hiddenText, 9) - 8,
                layout.FooterRect.Y + 3,
                MutedBrush,
                9);
        }

        return anchors;
    }

    private void DrawNodeConnectionRow(
        DrawingContext context,
        SchematicNodeCardRowLayout row,
        string label,
        string widthLabel,
        IBrush stroke,
        IBrush textBrush)
    {
        context.DrawLine(new Pen(stroke, 1.15), row.StubStart, row.StubEnd);
        context.FillRectangle(stroke, new Rect(row.RouteAnchor.X - 2, row.RouteAnchor.Y - 2, 4, 4));
        DrawMiniBadge(context, row.WidthBadgeRect, widthLabel, stroke);
        DrawText(
            context,
            Ellipsize(label, 9, row.LabelRect.Width),
            row.LabelRect.X,
            row.LabelRect.Y + 1,
            textBrush,
            9);
    }

    private IReadOnlyDictionary<string, LocalSignalAnchor> DrawLocalSignalSection(
        DrawingContext context,
        SchematicScopePanelLayout layout,
        IReadOnlyList<HierarchyScopeLocalSignalViewModel> localSignals,
        int visibleLocalCount,
        double top)
    {
        Rect panel = layout.PanelRect;
        Dictionary<string, LocalSignalAnchor> anchors = new(StringComparer.OrdinalIgnoreCase);
        if (visibleLocalCount == 0)
        {
            return anchors;
        }

        DrawText(context, "Local nets", panel.X + 16, top, TextBrush, 11);
        double chipY = top + 18;
        double chipWidth = panel.Width >= 760 && visibleLocalCount > 2 ? (CompactLayout ? 150 : 190) : (CompactLayout ? 170 : 220);
        int columns = Math.Max(1, layout.LocalColumns);
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
            if (!string.IsNullOrWhiteSpace(signal.ResolvedSignalName))
            {
                bool selected = string.Equals(SelectedSignalName, signal.ResolvedSignalName, StringComparison.OrdinalIgnoreCase);
                if (selected)
                {
                    context.DrawRectangle(new Pen(SelectedBrush, 1.2), chip.Inflate(1), 4);
                }

                _signalReferenceHitTargets.Add(new SignalReferenceHitTarget(signal.ResolvedSignalName!, chip, null));
            }

            anchors[signal.Name] = new LocalSignalAnchor(new Point(chip.Right, chip.Y + chip.Height / 2), signal.ResolvedSignalName);
        }

        return anchors;
    }

    private void DrawConnectionRoutes(
        DrawingContext context,
        CurrentPortLayout currentPortLayout,
        IReadOnlyList<ChildNodeLayout> childLayouts,
        IReadOnlyDictionary<string, LocalSignalAnchor> localSignalAnchors)
    {
        List<SchematicConnectionRouteRequest> requests = [];
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
                else if (localSignalAnchors.TryGetValue(connection.SignalName, out LocalSignalAnchor? localAnchor))
                {
                    source = localAnchor.Point;
                }

                if (source is null)
                {
                    continue;
                }

                string? selectionSignalName = currentPortLayout.PortAnchors.TryGetValue(connection.SignalName, out PortAnchor? portAnchor) && portAnchor is not null
                    ? connection.SignalName
                    : localSignalAnchors.TryGetValue(connection.SignalName, out LocalSignalAnchor? localSelection)
                        ? localSelection.ResolvedSignalName
                        : null;

                requests.Add(new SchematicConnectionRouteRequest(
                    $"{child.Instance.HierarchyPath}:{connection.PortName}:{connection.SignalName}:{(connection.IsInput ? "i" : "o")}",
                    connection.SignalName,
                    selectionSignalName,
                    connection.Width,
                    source.Value,
                    childAnchor,
                    SourceFromLocalSignal: localSignalAnchors.ContainsKey(connection.SignalName),
                    TargetIsInput: connection.IsInput));
            }
        }

        if (requests.Count == 0)
        {
            return;
        }

        Dictionary<string, SchematicConnectionRouteRequest> requestIndex = requests.ToDictionary(static request => request.Id, StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<SchematicConnectionRoute> routes = ConnectionRouter.Compute(
            new SchematicConnectionRoutingInput(
                currentPortLayout.Layout,
                CompactLayout,
                requests,
                BuildRoutingObstacles(currentPortLayout.Layout)));

        HashSet<string> drawnSegments = new(StringComparer.OrdinalIgnoreCase);

        foreach (SchematicConnectionRoute route in routes)
        {
            SchematicConnectionRouteRequest request = requestIndex[route.Id];
            bool selected = !string.IsNullOrWhiteSpace(request.SelectionSignalName)
                && string.Equals(SelectedSignalName, request.SelectionSignalName, StringComparison.OrdinalIgnoreCase);
            DrawScopedConnectionRoute(
                context,
                route.Points,
                ResolveRouteBrush(request),
                selected,
                request.LabelWidth,
                drawnSegments);
            bool shouldDrawLabel = selected
                || request.LabelWidth > 1 && route.IsBundlePrimary;
            if (shouldDrawLabel)
            {
                DrawConnectionRouteLabel(context, request, route, selected, route.BundleSize);
            }

            if (!string.IsNullOrWhiteSpace(request.SelectionSignalName))
            {
                _signalReferenceHitTargets.Add(new SignalReferenceHitTarget(request.SelectionSignalName!, route.LabelBounds, route.Points));
            }
        }
    }

    private static IBrush ResolveRouteBrush(SchematicConnectionRouteRequest request)
    {
        if (request.SourceFromLocalSignal)
        {
            return LocalNetBrush;
        }

        return request.TargetIsInput ? PinStrokeBrush : OutputValueBrush;
    }

    private static IReadOnlyList<Rect> BuildRoutingObstacles(SchematicScopePanelLayout layout)
    {
        List<Rect> obstacles = [layout.CurrentNodeRect];
        obstacles.AddRange(layout.ChildNodeRects);
        if (layout.LocalSectionRect is Rect localSection)
        {
            obstacles.Add(localSection);
        }

        return obstacles;
    }

    private void DrawScopedConnectionRoute(
        DrawingContext context,
        IReadOnlyList<Point> points,
        IBrush brush,
        bool selected,
        int width,
        HashSet<string>? drawnSegments = null)
    {
        if (selected)
        {
            Pen highlight = new(SelectedBrush, CompactLayout ? 2.8 : 3.2);
            for (int index = 0; index < points.Count - 1; index++)
            {
                context.DrawLine(highlight, points[index], points[index + 1]);
            }
        }

        double thickness = width > 1 ? (CompactLayout ? 1.8 : 2.1) : 1.1;
        Pen pen = new(brush, selected ? Math.Max(thickness, 2.1) : thickness);
        for (int index = 0; index < points.Count - 1; index++)
        {
            if (!selected && drawnSegments is not null && !drawnSegments.Add(BuildRouteSegmentKey(points[index], points[index + 1], width)))
            {
                continue;
            }

            context.DrawLine(pen, points[index], points[index + 1]);
        }
    }

    private static string BuildRouteSegmentKey(Point start, Point end, int width)
    {
        double x1 = Math.Round(start.X, 1);
        double y1 = Math.Round(start.Y, 1);
        double x2 = Math.Round(end.X, 1);
        double y2 = Math.Round(end.Y, 1);
        if (x2 < x1 || Math.Abs(x1 - x2) < 0.01 && y2 < y1)
        {
            (x1, x2) = (x2, x1);
            (y1, y2) = (y2, y1);
        }

        return $"{(width > 1 ? "b" : "s")}:{x1:F1},{y1:F1}:{x2:F1},{y2:F1}";
    }

    private void DrawConnectionRouteLabel(
        DrawingContext context,
        SchematicConnectionRouteRequest request,
        SchematicConnectionRoute route,
        bool selected,
        int routeCount)
    {
        string text = request.LabelWidth <= 1
            ? request.SignalName
            : routeCount > 1
                ? $"{request.SignalName} [{request.LabelWidth}b] x{routeCount}"
                : $"{request.SignalName} [{request.LabelWidth}b]";
        string label = Ellipsize(text, 9, route.LabelBounds.Width - 8);
        IBrush stroke = selected ? SelectedBrush : (request.TargetIsInput ? PinStrokeBrush : OutputValueBrush);
        context.FillRectangle(ValueFillBrush, route.LabelBounds, 4);
        context.DrawRectangle(new Pen(stroke, selected ? 1.2 : 1), route.LabelBounds, 4);
        DrawText(context, label, route.LabelBounds.X + 4, route.LabelBounds.Y + 3, stroke, 9);
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

    private void DrawScopeExpansionButton(DrawingContext context, Rect ownerRect, string? hierarchyPath, bool expanded)
    {
        if (string.IsNullOrWhiteSpace(hierarchyPath))
        {
            return;
        }

        Rect button = new(ownerRect.Right - 28, ownerRect.Y + 10, 18, 18);
        IBrush stroke = expanded ? SelectedBrush : PinStrokeBrush;
        context.FillRectangle(ValueFillBrush, button, 4);
        context.DrawRectangle(new Pen(stroke, 1.1), button, 4);
        DrawText(context, expanded ? "-" : "+", button.X + 5, button.Y + 1, stroke, 13);
        _expansionHitTargets.Add(new ExpansionHitTarget(hierarchyPath, button.Inflate(4)));
    }

    private SignalHitTarget? HitTestSignal(Point point) => _signalHitTargets.FirstOrDefault(hit => hit.Bounds.Contains(point));

    private SignalReferenceHitTarget? HitTestSignalReference(Point point) =>
        _signalReferenceHitTargets.FirstOrDefault(hit => hit.Contains(point, CompactLayout ? 5 : 6));

    private ScopeHitTarget? HitTestScope(Point point) => _scopeHitTargets.FirstOrDefault(hit => hit.Bounds.Contains(point));

    private ExpansionHitTarget? HitTestExpansion(Point point) => _expansionHitTargets.FirstOrDefault(hit => hit.Bounds.Contains(point));

    private void OnSignalsChanged(object? sender, NotifyCollectionChangedEventArgs e) => OnSignalCollectionChanged(e);

    private void OnScopeSignalsChanged(object? sender, NotifyCollectionChangedEventArgs e) => OnSignalCollectionChanged(e);

    private void OnScopeChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    private void OnScopePortsChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    private void OnScopeLocalSignalsChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    private void OnExpandedScopePathsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_viewportCustomized)
        {
            _fitPending = true;
        }

        InvalidateVisual();
    }

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

    private bool IsScopeExpanded(string hierarchyPath) =>
        ExpandedScopePaths?.Any(path => string.Equals(path, hierarchyPath, StringComparison.OrdinalIgnoreCase)) == true;

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

    private sealed record SignalReferenceHitTarget(string SignalName, Rect? Bounds, IReadOnlyList<Point>? RoutePoints)
    {
        public bool Contains(Point point, double tolerance)
        {
            if (Bounds is Rect bounds && bounds.Contains(point))
            {
                return true;
            }

            if (RoutePoints is null || RoutePoints.Count < 2)
            {
                return false;
            }

            for (int index = 0; index < RoutePoints.Count - 1; index++)
            {
                if (DistanceToSegment(point, RoutePoints[index], RoutePoints[index + 1]) <= tolerance)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private sealed record ScopeHitTarget(string HierarchyPath, Rect Bounds, bool CanExpand);

    private sealed record ExpansionHitTarget(string HierarchyPath, Rect Bounds);

    private sealed record PortAnchor(string Name, Point Point, bool IsInput, Point ExternalPoint);

    private sealed record LocalSignalAnchor(Point Point, string? ResolvedSignalName);

    private sealed record CurrentPortLayout(
        SchematicScopePanelLayout Layout,
        Rect Bounds,
        IReadOnlyDictionary<string, PortAnchor> PortAnchors);

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

    private static double DistanceToSegment(Point point, Point start, Point end)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        if (Math.Abs(dx) < double.Epsilon && Math.Abs(dy) < double.Epsilon)
        {
            return Math.Sqrt(Math.Pow(point.X - start.X, 2) + Math.Pow(point.Y - start.Y, 2));
        }

        double t = ((point.X - start.X) * dx + (point.Y - start.Y) * dy) / (dx * dx + dy * dy);
        t = Math.Clamp(t, 0, 1);
        double projectionX = start.X + t * dx;
        double projectionY = start.Y + t * dy;
        double distanceX = point.X - projectionX;
        double distanceY = point.Y - projectionY;
        return Math.Sqrt(distanceX * distanceX + distanceY * distanceY);
    }
}
