using System.Windows.Input;
using Avalonia;
using Avalonia.Input;
using Bistable.App.ViewModels;

namespace Bistable.App.Views;

public sealed partial class SchematicPreviewControl
{
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

    /// <summary>
    /// Single click on a sub-instance scope body: select its hierarchy node.
    /// Double click additionally enters sub-sim for that module — turns the
    /// "click to drill in" expectation into the actual isolation flow.
    /// </summary>
    private void HandleScopeHit(ScopeHitTarget hit, PointerPressedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(hit.HierarchyPath)) return;

        ICommand? select = SelectScopeCommand;
        if (select?.CanExecute(hit.HierarchyPath) == true)
        {
            select.Execute(hit.HierarchyPath);
        }

        if (e.ClickCount >= 2)
        {
            ICommand? enterSubSim = EnterSubSimCommand;
            if (enterSubSim?.CanExecute(hit.HierarchyPath) == true)
            {
                enterSubSim.Execute(hit.HierarchyPath);
            }
        }
    }

    private SignalHitTarget? HitTestSignal(Point point) => _signalHitTargets.FirstOrDefault(hit => hit.Bounds.Contains(point));

    private SignalReferenceHitTarget? HitTestSignalReference(Point point) =>
        _signalReferenceHitTargets.FirstOrDefault(hit => hit.Contains(point, CompactLayout ? 5 : 6));

    private ScopeHitTarget? HitTestScope(Point point) => _scopeHitTargets.FirstOrDefault(hit => hit.Bounds.Contains(point));

    private ExpansionHitTarget? HitTestExpansion(Point point) => _expansionHitTargets.FirstOrDefault(hit => hit.Bounds.Contains(point));

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
