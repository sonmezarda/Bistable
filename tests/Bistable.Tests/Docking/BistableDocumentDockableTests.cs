using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Controls;
using Bistable.App.ViewModels;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;

namespace Bistable.Tests.Docking;

public sealed class BistableDocumentDockableTests
{
    [Fact]
    public void OnClose_CloseableDocument_InvokesResourceCleanup()
    {
        int closeCalls = 0;
        BistableDocumentDockable document = new(
            DockPanelKind.Schematic,
            "gate-top",
            "Gate: top",
            static () => new Border(),
            canClose: true,
            closed: () => closeCalls++);

        bool accepted = document.OnClose();

        Assert.True(accepted);
        Assert.Equal(1, closeCalls);
        Assert.True(document.CanClose);
        Assert.True(document.CanDrag);
        Assert.True(document.CanFloat);
    }

    [Fact]
    public void GetOrCreateContent_ReusesSingleControlInstance()
    {
        int factoryCalls = 0;
        BistableDocumentDockable document = new(
            DockPanelKind.Schematic,
            "gate-top",
            "Gate: top",
            () =>
            {
                factoryCalls++;
                return new Border();
            });

        Control first = document.GetOrCreateContent();
        Control second = document.GetOrCreateContent();

        Assert.Same(first, second);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public void AddAndActivate_PlacesDynamicDocumentInWorkspaceTabGroup()
    {
        Factory factory = new();
        BistableDocumentDockable inspector = new(
            DockPanelKind.Project,
            "inspector",
            "Inspector",
            static () => new Border());
        BistableDocumentDockable waveform = new(
            DockPanelKind.Waveform,
            "waveform",
            "Waveform",
            static () => new Border());
        BistableDocumentDockable schematic = new(
            DockPanelKind.Schematic,
            "schematic",
            "Schematic",
            static () => new Border());
        BistableDocumentDockable gate = new(
            DockPanelKind.Schematic,
            "gate-top",
            "Gate: top",
            static () => new Border(),
            canClose: true);
        DocumentDock documents = new()
        {
            VisibleDockables = [inspector, waveform, schematic],
            ActiveDockable = schematic,
            DefaultDockable = schematic,
        };

        DocumentDockCoordinator.AddAndActivate(factory, documents, gate);

        Assert.Equal([inspector, waveform, schematic, gate], documents.VisibleDockables);
        Assert.Same(gate, documents.ActiveDockable);
        Assert.Same(gate, documents.FocusedDockable);
        Assert.Same(gate, documents.DefaultDockable);
    }

    [Fact]
    public void AddAndActivate_ExistingDocumentIsNotDuplicated()
    {
        Factory factory = new();
        BistableDocumentDockable gate = new(
            DockPanelKind.Schematic,
            "gate-top",
            "Gate: top",
            static () => new Border(),
            canClose: true);
        DocumentDock documents = new()
        {
            VisibleDockables = [gate],
        };

        DocumentDockCoordinator.AddAndActivate(factory, documents, gate);

        Assert.Single(documents.VisibleDockables!);
        Assert.Same(gate, documents.ActiveDockable);
        Assert.Same(gate, documents.FocusedDockable);
    }

    [Fact]
    public void MoveDockable_ObservableDocumentsNotifyTabStripAboutReorder()
    {
        Factory factory = new();
        BistableDocumentDockable first = new(
            DockPanelKind.Project,
            "first",
            "First",
            static () => new Border());
        BistableDocumentDockable second = new(
            DockPanelKind.Waveform,
            "second",
            "Second",
            static () => new Border());
        ObservableCollection<Dock.Model.Core.IDockable> visible = [first, second];
        DocumentDock documents = new() { VisibleDockables = visible };
        List<NotifyCollectionChangedAction> notifications = [];
        visible.CollectionChanged += (_, args) => notifications.Add(args.Action);

        factory.MoveDockable(documents, second, first);

        Assert.Equal([second, first], documents.VisibleDockables);
        Assert.Contains(notifications, action =>
            action is NotifyCollectionChangedAction.Move
                or NotifyCollectionChangedAction.Add
                or NotifyCollectionChangedAction.Remove);
    }
}
