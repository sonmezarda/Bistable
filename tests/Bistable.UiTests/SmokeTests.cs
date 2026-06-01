using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Bistable.UiTests;

// Sanity tests for the headless app infrastructure. If these fail, all higher-level
// UI tests will too — fix headless setup first.
[Trait("Category", "UI")]
public sealed class SmokeTests
{
    [AvaloniaFact]
    public void DispatcherIsRunning_OnTestThread()
    {
        // [AvaloniaFact] guarantees we're on the UI thread with a live Dispatcher.
        Assert.True(Dispatcher.UIThread.CheckAccess(),
            "Tests marked [AvaloniaFact] must execute on the Avalonia UI thread.");
    }

    [AvaloniaFact]
    public void ApplicationInstance_IsConfigured()
    {
        Assert.NotNull(Avalonia.Application.Current);
    }
}
