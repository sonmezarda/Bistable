using Bistable.App.ViewModels;

namespace Bistable.App.Services;

public sealed record LayoutState(
    double WaveformZoom,
    int WaveformOffset,
    double LeftDockWidth,
    double RightDockWidth,
    double BottomDockHeight,
    DockZone ProjectDockZone,
    DockZone WaveformDockZone,
    DockZone SchematicDockZone)
{
    public static LayoutState Default { get; } = new(
        WaveformZoom: 1,
        WaveformOffset: 0,
        LeftDockWidth: 260,
        RightDockWidth: 320,
        BottomDockHeight: 280,
        ProjectDockZone: DockZone.Left,
        WaveformDockZone: DockZone.Bottom,
        SchematicDockZone: DockZone.Right);
}
