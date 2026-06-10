using Avalonia.Controls;
using Bistable.App.ViewModels;

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
}
