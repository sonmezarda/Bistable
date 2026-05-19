namespace Bistable.App.ViewModels;

public sealed class DockPanelViewModel(DockPanelKind kind, string title) : ViewModelBase
{
    private DockZone _zone;

    public DockPanelKind Kind { get; } = kind;

    public string Title { get; } = title;

    public DockZone Zone
    {
        get => _zone;
        set => SetProperty(ref _zone, value);
    }
}
