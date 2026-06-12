using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;

namespace Bistable.App.ViewModels;

/// <summary>
/// Keeps dynamic documents in the main workspace tab group and updates the
/// complete Dock.Avalonia active/focus chain. Directly assigning
/// DocumentDock.ActiveDockable does not update owner/root focus state.
/// </summary>
public static class DocumentDockCoordinator
{
    public static void AddAndActivate(
        Factory factory,
        DocumentDock documentDock,
        BistableDocumentDockable document)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(documentDock);
        ArgumentNullException.ThrowIfNull(document);

        if (documentDock.VisibleDockables?.Contains(document) != true)
        {
            factory.AddDockable(documentDock, document);
        }

        Activate(factory, documentDock, document);
    }

    public static void Activate(
        Factory factory,
        DocumentDock documentDock,
        BistableDocumentDockable document)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(documentDock);
        ArgumentNullException.ThrowIfNull(document);

        documentDock.ActiveDockable = document;
        documentDock.FocusedDockable = document;
        documentDock.DefaultDockable = document;
        factory.SetActiveDockable(document);
        factory.SetFocusedDockable(documentDock, document);
    }
}
