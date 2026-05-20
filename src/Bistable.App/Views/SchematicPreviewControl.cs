using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
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

    public static readonly StyledProperty<IEnumerable<HierarchyScopeNodeViewModel>?> ScopeChildrenProperty =
        AvaloniaProperty.Register<SchematicPreviewControl, IEnumerable<HierarchyScopeNodeViewModel>?>(nameof(ScopeChildren));

    public static readonly StyledProperty<IEnumerable<HierarchyScopePortViewModel>?> ScopePortsProperty =
        AvaloniaProperty.Register<SchematicPreviewControl, IEnumerable<HierarchyScopePortViewModel>?>(nameof(ScopePorts));

    public static readonly StyledProperty<IEnumerable<SignalViewModel>?> ScopeSignalsProperty =
        AvaloniaProperty.Register<SchematicPreviewControl, IEnumerable<SignalViewModel>?>(nameof(ScopeSignals));

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

    private readonly List<SignalHitTarget> _signalHitTargets = [];
    private readonly List<ScopeHitTarget> _scopeHitTargets = [];
    private INotifyCollectionChanged? _observableSignals;
    private INotifyCollectionChanged? _observableScopeSignals;
    private INotifyCollectionChanged? _observableScopeChildren;
    private INotifyCollectionChanged? _observableScopePorts;

    public event EventHandler<SignalEditorRequestedEventArgs>? SignalEditorRequested;

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

    public IEnumerable<HierarchyScopeNodeViewModel>? ScopeChildren
    {
        get => GetValue(ScopeChildrenProperty);
        set => SetValue(ScopeChildrenProperty, value);
    }

    public IEnumerable<SignalViewModel>? ScopeSignals
    {
        get => GetValue(ScopeSignalsProperty);
        set => SetValue(ScopeSignalsProperty, value);
    }

    public IEnumerable<HierarchyScopePortViewModel>? ScopePorts
    {
        get => GetValue(ScopePortsProperty);
        set => SetValue(ScopePortsProperty, value);
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

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        Rect bounds = Bounds;
        context.FillRectangle(BackgroundBrush, bounds);

        IReadOnlyList<SignalViewModel> inputs = Signals?.Where(static signal => signal.IsInput).ToList() ?? [];
        IReadOnlyList<SignalViewModel> outputs = Signals?.Where(static signal => !signal.IsInput).ToList() ?? [];
        IReadOnlyList<SignalViewModel> scopeSignals = ScopeSignals?.ToList() ?? [];
        IReadOnlyList<HierarchyScopeNodeViewModel> childScopes = ScopeChildren?.ToList() ?? [];
        IReadOnlyList<HierarchyScopePortViewModel> scopePorts = ScopePorts?.ToList() ?? [];
        HierarchyScopeNodeViewModel? parentScope = ScopeParent;

        if (inputs.Count == 0 && outputs.Count == 0)
        {
            DrawText(context, "Load a project to generate a top-level symbol schematic.", 16, 32, MutedBrush, 13);
            return;
        }

        bool hasScopeFocus = HasScopeContext(scopeSignals, childScopes, parentScope);
        int visibleProbeCount = Math.Min(scopeSignals.Count, 8);
        int visibleChildCount = Math.Min(childScopes.Count, 4);
        double reservedBottom = hasScopeFocus
            ? Math.Clamp(220 + visibleProbeCount * 12 + visibleChildCount * 18, 220, 360)
            : 24;
        double diagramHeight = Math.Max(180, bounds.Height - reservedBottom);
        double moduleWidth = Math.Clamp(bounds.Width * 0.36, 280, 420);
        double laneCount = Math.Max(inputs.Count, outputs.Count);
        double laneHeight = Math.Max(24, Math.Min(42, (diagramHeight - 112) / Math.Max(1, laneCount)));
        double moduleHeight = Math.Max(180, 70 + laneCount * laneHeight);
        Rect moduleRect = new(
            (bounds.Width - moduleWidth) / 2,
            Math.Max(28, (diagramHeight - moduleHeight) / 2),
            moduleWidth,
            moduleHeight);

        context.FillRectangle(ModuleFillBrush, moduleRect, 10);
        context.DrawRectangle(new Pen(ModuleStrokeBrush, 1.5), moduleRect, 10);
        DrawText(context, ModuleName, moduleRect.X + 18, moduleRect.Y + 18, TextBrush, 20);
        DrawText(context, "Top-level symbol", moduleRect.X + 18, moduleRect.Y + 46, MutedBrush, 12);
        DrawText(context, "Click pins to drive", moduleRect.X + 18, moduleRect.Bottom - 28, MutedBrush, 11);

        Rect? scopeCard = DrawScopeCard(context, bounds);

        _signalHitTargets.Clear();
        _scopeHitTargets.Clear();
        DrawPins(context, inputs, bounds, moduleRect, leftSide: true, laneHeight);
        DrawPins(context, outputs, bounds, moduleRect, leftSide: false, laneHeight);

        if (hasScopeFocus)
        {
            DrawFocusedScopePanel(context, bounds, moduleRect, scopeCard, scopeSignals, childScopes, parentScope, scopePorts);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        Point point = e.GetPosition(this);
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
        bool interactive = HitTestSignal(e.GetPosition(this)) is not null || HitTestScope(e.GetPosition(this)) is not null;
        Cursor = interactive ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Arrow);
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
        IReadOnlyList<HierarchyScopeNodeViewModel> childScopes,
        HierarchyScopeNodeViewModel? parentScope,
        IReadOnlyList<HierarchyScopePortViewModel> scopePorts)
    {
        int visibleProbeCount = Math.Min(scopeSignals.Count, 8);
        int visibleChildCount = Math.Min(childScopes.Count, 4);
        int visibleLeftPortCount = Math.Min(scopePorts.Count(static port => port.IsInput), 5);
        int visibleRightPortCount = Math.Min(scopePorts.Count(static port => port.IsOutput), 5);
        bool inlineChildren = visibleChildCount > 0 && bounds.Width >= 980;
        int probeColumnCount = bounds.Width >= 900 && visibleProbeCount > 1 ? 2 : 1;
        int probeRowCount = visibleProbeCount == 0 ? 0 : (int)Math.Ceiling(visibleProbeCount / (double)probeColumnCount);
        int childColumnCount = inlineChildren && visibleChildCount > 2 ? 2 : 1;
        int childRowCount = visibleChildCount == 0 ? 0 : (int)Math.Ceiling(visibleChildCount / (double)childColumnCount);
        double panelWidth = Math.Clamp(bounds.Width * (inlineChildren ? 0.72 : 0.58), 360, inlineChildren ? 760 : 620);
        double navigationHeight = 116 + (inlineChildren ? Math.Max(0, childRowCount - 1) * 58 : 0);
        double childrenBlockHeight = inlineChildren || visibleChildCount == 0
            ? 0
            : 62 + Math.Max(0, childRowCount - 1) * 52;
        double probeBlockHeight = visibleProbeCount == 0 ? 44 : 36 + probeRowCount * 30;
        double panelHeight = Math.Clamp(navigationHeight + childrenBlockHeight + probeBlockHeight + 52, 210, 340);
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

        Rect currentNodeRect = DrawCurrentScopeNode(
            context,
            panel,
            inlineChildren,
            parentScope is not null,
            visibleChildCount > 0,
            scopePorts,
            visibleLeftPortCount,
            visibleRightPortCount);
        DrawNavigationNeighborhood(context, panel, currentNodeRect, parentScope, childScopes, visibleChildCount, inlineChildren);

        double probesTop = inlineChildren ? currentNodeRect.Bottom + 18 : currentNodeRect.Bottom + 18 + childrenBlockHeight;
        if (!inlineChildren && visibleChildCount > 0)
        {
            DrawChildRowsBelow(context, panel, currentNodeRect, childScopes, visibleChildCount);
        }

        DrawScopeProbeSection(context, panel, scopeSignals, visibleProbeCount, probesTop);

        string footer = BuildScopeFooter(scopeSignals.Count, visibleProbeCount, childScopes.Count);
        DrawText(context, Ellipsize(footer, 10, panel.Width - 32), panel.X + 16, panel.Bottom - 18, MutedBrush, 10);
    }

    private Rect DrawCurrentScopeNode(
        DrawingContext context,
        Rect panel,
        bool inlineChildren,
        bool hasParent,
        bool hasChildren,
        IReadOnlyList<HierarchyScopePortViewModel> scopePorts,
        int visibleLeftPortCount,
        int visibleRightPortCount)
    {
        double reservedLeft = Math.Max(hasParent ? 152 : 24, visibleLeftPortCount > 0 ? 182 : 24);
        double reservedRight = Math.Max(inlineChildren && hasChildren ? 244 : 24, visibleRightPortCount > 0 ? 182 : 24);
        double availableWidth = panel.Width - reservedLeft - reservedRight;
        double nodeWidth = Math.Clamp(availableWidth, 168, 220);
        double nodeX = panel.X + reservedLeft + Math.Max(0, (availableWidth - nodeWidth) / 2);
        Rect rect = new(nodeX, panel.Y + 82, nodeWidth, 64);

        context.FillRectangle(NodeSelectedFillBrush, rect, 8);
        context.DrawRectangle(new Pen(SelectedBrush, 1.4), rect, 8);
        DrawText(context, Ellipsize(ActiveScopeTitle ?? "Scope", 12, rect.Width - 24), rect.X + 12, rect.Y + 10, TextBrush, 12);
        DrawText(context, Ellipsize(ActiveScopeModuleName ?? "module", 11, rect.Width - 24), rect.X + 12, rect.Y + 30, PinStrokeBrush, 11);
        DrawText(context, "active", rect.X + 12, rect.Bottom - 16, SelectedBrush, 10);

        DrawScopePorts(context, rect, scopePorts);
        return rect;
    }

    private void DrawNavigationNeighborhood(
        DrawingContext context,
        Rect panel,
        Rect currentNodeRect,
        HierarchyScopeNodeViewModel? parentScope,
        IReadOnlyList<HierarchyScopeNodeViewModel> childScopes,
        int visibleChildCount,
        bool inlineChildren)
    {
        if (parentScope is not null)
        {
            Rect parentRect = new(panel.X + 16, currentNodeRect.Y + 8, 128, 48);
            DrawScopeLink(context, new Point(parentRect.Right, parentRect.Center.Y), new Point(currentNodeRect.X, currentNodeRect.Center.Y));
            DrawScopeNodeCard(context, parentScope, parentRect, role: "up");
        }

        if (visibleChildCount == 0 || !inlineChildren)
        {
            return;
        }

        double areaX = currentNodeRect.Right + 26;
        double areaWidth = panel.Right - 16 - areaX;
        int childColumnCount = visibleChildCount > 2 ? 2 : 1;
        int childRowCount = (int)Math.Ceiling(visibleChildCount / (double)childColumnCount);
        double cardWidth = childColumnCount == 1
            ? Math.Max(144, areaWidth)
            : (areaWidth - 10) / 2;

        for (int index = 0; index < visibleChildCount; index++)
        {
            int row = index / childColumnCount;
            int column = index % childColumnCount;
            Rect childRect = new(
                areaX + column * (cardWidth + 10),
                currentNodeRect.Y + row * 58,
                cardWidth,
                48);
            DrawScopeLink(context, new Point(currentNodeRect.Right, currentNodeRect.Center.Y), new Point(childRect.X, childRect.Center.Y));
            DrawScopeNodeCard(context, childScopes[index], childRect, role: "child");
        }
    }

    private void DrawChildRowsBelow(
        DrawingContext context,
        Rect panel,
        Rect currentNodeRect,
        IReadOnlyList<HierarchyScopeNodeViewModel> childScopes,
        int visibleChildCount)
    {
        int columns = panel.Width >= 520 && visibleChildCount > 1 ? 2 : 1;
        int rows = (int)Math.Ceiling(visibleChildCount / (double)columns);
        double availableWidth = panel.Width - 32;
        double cardWidth = columns == 1 ? availableWidth : (availableWidth - 10) / 2;
        double top = currentNodeRect.Bottom + 18;

        for (int index = 0; index < visibleChildCount; index++)
        {
            int row = index / columns;
            int column = index % columns;
            Rect childRect = new(
                panel.X + 16 + column * (cardWidth + 10),
                top + row * 52,
                cardWidth,
                42);
            DrawScopeLink(context, new Point(GetCenterX(currentNodeRect), currentNodeRect.Bottom), new Point(GetCenterX(childRect), childRect.Y));
            DrawScopeNodeCard(context, childScopes[index], childRect, role: "child");
        }
    }

    private void DrawScopeProbeSection(
        DrawingContext context,
        Rect panel,
        IReadOnlyList<SignalViewModel> scopeSignals,
        int visibleProbeCount,
        double top)
    {
        DrawText(context, "Exact-scope probes", panel.X + 16, top, TextBrush, 11);
        DrawText(context, "local traced signals", panel.Right - 116, top, MutedBrush, 10);

        if (visibleProbeCount == 0)
        {
            DrawText(context, "No exact-scope probes are available.", panel.X + 16, top + 22, MutedBrush, 10);
            if (!string.IsNullOrWhiteSpace(ActiveScopeHint))
            {
                DrawText(context, Ellipsize(ActiveScopeHint!, 10, panel.Width - 32), panel.X + 16, top + 40, MutedBrush, 10);
            }

            return;
        }

        int columns = panel.Width >= 900 && visibleProbeCount > 1 ? 2 : 1;
        double columnGap = 14;
        double itemWidth = columns == 1
            ? panel.Width - 32
            : (panel.Width - 32 - columnGap) / 2;

        for (int index = 0; index < visibleProbeCount; index++)
        {
            int row = index / columns;
            int column = index % columns;
            double itemX = panel.X + 16 + column * (itemWidth + columnGap);
            double itemY = top + 20 + row * 30;
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

    private void DrawScopeNodeCard(DrawingContext context, HierarchyScopeNodeViewModel scope, Rect rect, string role)
    {
        bool selected = string.Equals(scope.HierarchyPath, ActiveScopePath, StringComparison.OrdinalIgnoreCase);
        IBrush fill = selected ? NodeSelectedFillBrush : NodeFillBrush;
        IBrush stroke = selected ? SelectedBrush : (scope.HasTraceActivity ? PinStrokeBrush : ModuleStrokeBrush);

        context.FillRectangle(fill, rect, 7);
        context.DrawRectangle(new Pen(stroke, selected ? 1.4 : 1), rect, 7);
        DrawText(context, Ellipsize(scope.InstanceName, 11, rect.Width - 24), rect.X + 10, rect.Y + 8, TextBrush, 11);
        DrawText(context, Ellipsize(scope.ModuleName, 10, rect.Width - 24), rect.X + 10, rect.Y + 24, MutedBrush, 10);
        DrawText(context, role, rect.Right - 26, rect.Y + 8, MutedBrush, 9);
        DrawPortCountStubs(context, rect, scope.InputCount, scope.OutputCount, stroke);
        DrawMiniBadge(context, new Rect(rect.Right - 62, rect.Bottom - 20, 54, 16), scope.ScopeBadgeText, stroke);
        _scopeHitTargets.Add(new ScopeHitTarget(scope.HierarchyPath, rect));
    }

    private void DrawScopePorts(DrawingContext context, Rect nodeRect, IReadOnlyList<HierarchyScopePortViewModel> ports)
    {
        IReadOnlyList<HierarchyScopePortViewModel> inputs = ports.Where(static port => port.IsInput).Take(5).ToList();
        IReadOnlyList<HierarchyScopePortViewModel> outputs = ports.Where(static port => port.IsOutput).Take(5).ToList();
        double leftStep = nodeRect.Height / Math.Max(1, inputs.Count + 1);
        double rightStep = nodeRect.Height / Math.Max(1, outputs.Count + 1);

        for (int index = 0; index < inputs.Count; index++)
        {
            HierarchyScopePortViewModel port = inputs[index];
            double y = nodeRect.Y + leftStep * (index + 1);
            DrawPortStub(context, port, y, nodeRect.X, leftSide: true);
        }

        for (int index = 0; index < outputs.Count; index++)
        {
            HierarchyScopePortViewModel port = outputs[index];
            double y = nodeRect.Y + rightStep * (index + 1);
            DrawPortStub(context, port, y, nodeRect.Right, leftSide: false);
        }
    }

    private void DrawPortStub(DrawingContext context, HierarchyScopePortViewModel port, double y, double edgeX, bool leftSide)
    {
        double lineLength = 26;
        Rect badge = leftSide
            ? new(edgeX - 160, y - 10, 48, 20)
            : new(edgeX + 112, y - 10, 48, 20);
        double lineStartX = leftSide ? edgeX - lineLength : edgeX;
        double lineEndX = leftSide ? edgeX : edgeX + lineLength;
        string label = Ellipsize(port.Name, 10, 78);
        double labelX = leftSide ? badge.Right + 8 : edgeX + 34;

        context.DrawLine(new Pen(PinStrokeBrush, 1.3), new Point(lineStartX, y), new Point(lineEndX, y));
        context.FillRectangle(PinStrokeBrush, new Rect(leftSide ? edgeX - 2 : edgeX - 2, y - 2, 4, 4));
        DrawText(context, label, leftSide ? badge.Right + 8 : labelX, y - 7, MutedBrush, 10);
        DrawMiniBadge(context, badge, port.WidthLabel, PinStrokeBrush);
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
        IReadOnlyCollection<HierarchyScopeNodeViewModel> childScopes,
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

    public sealed class SignalEditorRequestedEventArgs(SignalViewModel signal) : EventArgs
    {
        public SignalViewModel Signal { get; } = signal;
    }
}
