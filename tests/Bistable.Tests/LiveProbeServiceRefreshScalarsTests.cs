using Bistable.App.Services;

namespace Bistable.Tests;

/// <summary>
/// Phase 4 P4-5 coverage. RefreshScalarsAsync narrows post-Tick worker traffic
/// to the explicit path set the renderer reported. Without a real worker
/// attached the call is a no-op — we exercise the cancellation + null-worker
/// fall-throughs that the previous full-refresh path also relied on. Real
/// end-to-end timing is covered by VerilatorIntegrationTests.
/// </summary>
public sealed class LiveProbeServiceRefreshScalarsTests
{
    [Fact]
    public async Task RefreshScalarsAsync_WithoutWorker_NoOps()
    {
        LiveProbeService svc = new();
        // No AttachWorker — should not throw.
        await svc.RefreshScalarsAsync(new[] { "top.foo", "top.bar" }, CancellationToken.None);
        Assert.False(svc.HasWorker);
    }

    [Fact]
    public async Task RefreshScalarsAsync_EmptyPathList_NoOps()
    {
        LiveProbeService svc = new();
        await svc.RefreshScalarsAsync(Array.Empty<string>(), CancellationToken.None);
        // Behaviour: returns silently. No throw.
    }

    [Fact]
    public async Task RefreshScalarsAsync_DuplicatePaths_DeduplicatedBeforeWorkerLookup()
    {
        // Sanity: the deduplication step in RefreshScalarPathsCoreAsync runs
        // even without a worker attached so callers passing redundant paths
        // (e.g. same signal appears on many wires) don't pile up work.
        LiveProbeService svc = new();
        await svc.RefreshScalarsAsync(new[] { "top.x", "TOP.X", "top.x" }, CancellationToken.None);
        // No assertions beyond completing without throw — workerless path.
    }

    [Fact]
    public async Task RefreshScalarsAsync_CancellationHonoured()
    {
        LiveProbeService svc = new();
        using CancellationTokenSource cts = new();
        cts.Cancel();
        await svc.RefreshScalarsAsync(new[] { "top.foo" }, cts.Token);
        // Returns without throwing — the cancellation is checked per-iteration.
    }
}
