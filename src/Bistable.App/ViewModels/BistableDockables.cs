using Avalonia.Controls;
using Dock.Model.Mvvm.Controls;

namespace Bistable.App.ViewModels;

public abstract class BistableDockableBase
{
    private readonly Func<Control> _contentFactory;
    private Control? _cachedContent;

    protected BistableDockableBase(DockPanelKind panelKind, Func<Control> contentFactory)
    {
        PanelKind = panelKind;
        _contentFactory = contentFactory;
    }

    public DockPanelKind PanelKind { get; }

    public Control GetOrCreateContent() => _cachedContent ??= _contentFactory();
}

public sealed class BistableToolDockable : Tool
{
    private readonly BistableDockableBase _content;

    public BistableToolDockable(DockPanelKind panelKind, string id, string title, Func<Control> contentFactory)
    {
        _content = new DockContent(panelKind, contentFactory);
        Id = id;
        Title = title;
        CanClose = false;
        CanPin = false;
    }

    public DockPanelKind PanelKind => _content.PanelKind;

    public Control GetOrCreateContent() => _content.GetOrCreateContent();
}

public sealed class BistableDocumentDockable : Document
{
    private readonly BistableDockableBase _content;

    public BistableDocumentDockable(DockPanelKind panelKind, string id, string title, Func<Control> contentFactory)
    {
        _content = new DockContent(panelKind, contentFactory);
        Id = id;
        Title = title;
        CanClose = false;
        CanPin = false;
    }

    public DockPanelKind PanelKind => _content.PanelKind;

    public Control GetOrCreateContent() => _content.GetOrCreateContent();
}

file sealed class DockContent : BistableDockableBase
{
    public DockContent(DockPanelKind panelKind, Func<Control> contentFactory)
        : base(panelKind, contentFactory)
    {
    }
}
