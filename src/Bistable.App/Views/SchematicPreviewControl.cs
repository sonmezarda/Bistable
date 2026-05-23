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
using Bistable.Core.Design;

namespace Bistable.App.Views;

public sealed partial class SchematicPreviewControl : Control
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

    public static readonly StyledProperty<IEnumerable<DesignContAssign>?> ScopeContAssignsProperty =
        AvaloniaProperty.Register<SchematicPreviewControl, IEnumerable<DesignContAssign>?>(nameof(ScopeContAssigns));

    public static readonly StyledProperty<IEnumerable<Bistable.Core.Design.Schematic.SchematicPrimitive>?> ScopePrimitivesProperty =
        AvaloniaProperty.Register<SchematicPreviewControl, IEnumerable<Bistable.Core.Design.Schematic.SchematicPrimitive>?>(nameof(ScopePrimitives));

    public static readonly StyledProperty<IEnumerable<SignalViewModel>?> ScopeSignalsProperty =
        AvaloniaProperty.Register<SchematicPreviewControl, IEnumerable<SignalViewModel>?>(nameof(ScopeSignals));

    public static readonly StyledProperty<bool> CompactLayoutProperty =
        AvaloniaProperty.Register<SchematicPreviewControl, bool>(nameof(CompactLayout), true);

    public static readonly StyledProperty<SchematicTheme> PaletteProperty =
        AvaloniaProperty.Register<SchematicPreviewControl, SchematicTheme>(nameof(Palette), SchematicTheme.Dark);

    public static readonly StyledProperty<SchematicRoutingEngine> RoutingEngineProperty =
        AvaloniaProperty.Register<SchematicPreviewControl, SchematicRoutingEngine>(
            nameof(RoutingEngine),
            SchematicRoutingEngine.Elk);

    private SchematicTheme Palette => GetValue(PaletteProperty);

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
    private INotifyCollectionChanged? _observableScopeContAssigns;
    private INotifyCollectionChanged? _observableExpandedScopePaths;
    private double _viewportZoom = 1;
    private Point _viewportPan;
    private bool _isPanningViewport;
    private Point _lastViewportPointer;
    private string? _hoveredSignalName;
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

    public IEnumerable<DesignContAssign>? ScopeContAssigns
    {
        get => GetValue(ScopeContAssignsProperty);
        set => SetValue(ScopeContAssignsProperty, value);
    }

    public IEnumerable<Bistable.Core.Design.Schematic.SchematicPrimitive>? ScopePrimitives
    {
        get => GetValue(ScopePrimitivesProperty);
        set => SetValue(ScopePrimitivesProperty, value);
    }

    public SchematicRoutingEngine RoutingEngine
    {
        get => GetValue(RoutingEngineProperty);
        set => SetValue(RoutingEngineProperty, value);
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
        else if (change.Property == ScopeContAssignsProperty)
        {
            DetachCollection(change.OldValue as INotifyCollectionChanged, ref _observableScopeContAssigns, OnScopeContAssignsChanged);
            AttachCollection(change.NewValue as INotifyCollectionChanged, ref _observableScopeContAssigns, OnScopeContAssignsChanged);
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
        context.FillRectangle(Palette.Background, viewportBounds);

        IReadOnlyList<SignalViewModel> inputs = Signals?.Where(static signal => signal.IsInput).ToList() ?? [];
        IReadOnlyList<SignalViewModel> outputs = Signals?.Where(static signal => !signal.IsInput).ToList() ?? [];
        IReadOnlyList<SignalViewModel> scopeSignals = ScopeSignals?.ToList() ?? [];
        IReadOnlyList<HierarchyScopeInstanceViewModel> childScopes = ScopeChildren?.ToList() ?? [];
        IReadOnlyList<HierarchyScopePortViewModel> scopePorts = ScopePorts?.ToList() ?? [];
        IReadOnlyList<HierarchyScopeLocalSignalViewModel> localSignals = ScopeLocalSignals?.ToList() ?? [];
        IReadOnlyList<DesignContAssign> contAssigns = ScopeContAssigns?.ToList() ?? [];
        IReadOnlyList<Bistable.Core.Design.Schematic.SchematicPrimitive> scopePrimitives =
            ScopePrimitives?.ToList() ?? [];
        HierarchyScopeNodeViewModel? parentScope = ScopeParent;
        bool hasScopeFocus = HasScopeContext(scopeSignals, childScopes, parentScope);
        bool expandedScope = IsActiveScopeExpanded && hasScopeFocus;

        if (inputs.Count == 0 && outputs.Count == 0)
        {
            DrawText(context, "Load a project to generate a top-level symbol schematic.", 16, 32, Palette.Muted, 13);
            return;
        }

        Size worldSize = MeasureWorldSize(inputs.Count, outputs.Count, scopeSignals.Count, childScopes.Count, scopePorts.Count, localSignals.Count, expandedScope);
        _lastWorldSize = worldSize;
        EnsureViewport(viewportBounds, worldSize);

        int visibleProbeCount = Math.Min(scopeSignals.Count, CompactLayout ? 10 : 18);
        int visibleChildCount = Math.Min(childScopes.Count, CompactLayout ? 6 : 10);
        int visibleLocalCount = Math.Min(localSignals.Count, CompactLayout ? 8 : 14);
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
            context.FillRectangle(Palette.Background, worldBounds);

            Rect? scopeCard = DrawScopeCard(context, worldBounds);

            _signalHitTargets.Clear();
            _signalReferenceHitTargets.Clear();
            _scopeHitTargets.Clear();
            _expansionHitTargets.Clear();

            if (expandedScope)
            {
                DrawExpandedScopePanel(context, worldBounds, moduleRect, scopeCard, scopeSignals, childScopes, scopePorts, localSignals, contAssigns, scopePrimitives);
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
            e.Handled = true;
            return;
        }

        SignalHitTarget? signalHit = HitTestSignal(point);
        if (signalHit is not null)
        {
            HandleSignalHit(signalHit, e);
            e.Handled = true;
            return;
        }

        SignalReferenceHitTarget? signalReferenceHit = HitTestSignalReference(point);
        if (signalReferenceHit is not null)
        {
            HandleSignalReferenceHit(signalReferenceHit, e);
            e.Handled = true;
            return;
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
        SignalReferenceHitTarget? routeHover = HitTestSignalReference(worldPoint);
        string? newHoveredSignal = routeHover?.SignalName;
        if (!string.Equals(newHoveredSignal, _hoveredSignalName, StringComparison.OrdinalIgnoreCase))
        {
            _hoveredSignalName = newHoveredSignal;
            InvalidateVisual();
        }

        bool interactive = HitTestSignal(worldPoint) is not null
            || routeHover is not null
            || HitTestExpansion(worldPoint) is not null;
        Cursor = interactive ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Arrow);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoveredSignalName is not null)
        {
            _hoveredSignalName = null;
            InvalidateVisual();
        }
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

    private static bool ShouldRefitForProperty(AvaloniaProperty property) =>
        property == CompactLayoutProperty
        || property == ModuleNameProperty
        || property == SignalsProperty
        || property == ScopeSignalsProperty
        || property == ScopeChildrenProperty
        || property == ScopePortsProperty
        || property == ScopeLocalSignalsProperty
        || property == ScopeContAssignsProperty
        || property == ScopeParentProperty
        || property == IsActiveScopeExpandedProperty
        || property == ExpandedScopePathsProperty
        || property == ActiveScopeTitleProperty
        || property == ActiveScopeModuleNameProperty
        || property == ActiveScopePathProperty
        || property == ActiveScopeSummaryProperty
        || property == ActiveScopeHintProperty;

    private void OnSignalsChanged(object? sender, NotifyCollectionChangedEventArgs e) => OnSignalCollectionChanged(e);

    private void OnScopeSignalsChanged(object? sender, NotifyCollectionChangedEventArgs e) => OnSignalCollectionChanged(e);

    private void OnScopeChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    private void OnScopePortsChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    private void OnScopeLocalSignalsChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    private void OnScopeContAssignsChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

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

    private sealed record LocalSignalAnchor(Point Point, string? ResolvedSignalName, string CurrentValue);

    private sealed record CurrentPortLayout(
        SchematicScopePanelLayout Layout,
        Rect Bounds,
        IReadOnlyDictionary<string, PortAnchor> PortAnchors);

    private sealed record ChildNodeLayout(
        HierarchyScopeInstanceViewModel Instance,
        Rect Bounds,
        IReadOnlyDictionary<string, Point> PortAnchors);

    private sealed record PendingLocalConnection(
        ChildNodeLayout Child,
        HierarchyScopeInstancePortConnectionViewModel Connection,
        Point ChildAnchor,
        LocalSignalAnchor? LocalAnchor);

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
