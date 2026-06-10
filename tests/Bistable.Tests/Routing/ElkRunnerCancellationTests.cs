using System.Diagnostics;
using Bistable.App.Services;
using Bistable.App.Services.Routing.Elk;

namespace Bistable.Tests.Routing;

public sealed class ElkRunnerCancellationTests
{
    [Fact]
    public async Task LayoutService_CancelWithRealRunner_ReturnsWithinDeadline_AndRecovers()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/bin/bash"))
        {
            return;
        }

        string directory = Path.Combine(
            Path.GetTempPath(),
            $"bistable-elk-service-cancel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string scriptPath = Path.Combine(directory, "blocking-router.sh");
        await File.WriteAllTextAsync(scriptPath, """
            marker="$0.marker"
            while IFS= read -r line; do
                if [ ! -f "$marker" ]; then
                    touch "$marker"
                    sleep 30
                else
                    printf '%s\n' '{"ok":true,"graph":{"id":"recovered"}}'
                fi
            done
            """);

        try
        {
            using CancellationTokenSource cts = new();
            await using SchematicLayoutService service = new(
                new ElkRunner("/bin/bash", scriptPath, TimeSpan.FromSeconds(30)));
            Task<ElkGraph> layout = service.LayoutAsync(
                new ElkGraph { Id = "blocked" },
                cts.Token);
            await Task.Delay(150);

            Stopwatch stopwatch = Stopwatch.StartNew();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await layout);
            stopwatch.Stop();

            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromMilliseconds(500),
                $"Cancellation took {stopwatch.Elapsed.TotalMilliseconds:0} ms.");

            ElkGraph recovered = await service.LayoutAsync(new ElkGraph { Id = "next" });
            Assert.Equal("recovered", recovered.Id);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task Restart_KillsInFlightProcess_WithoutWaitingForLayoutLock()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/bin/bash"))
        {
            return;
        }

        string directory = Path.Combine(
            Path.GetTempPath(),
            $"bistable-elk-cancel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string scriptPath = Path.Combine(directory, "blocking-router.sh");
        await File.WriteAllTextAsync(scriptPath, """
            while IFS= read -r line; do
                sleep 30
            done
            """);

        try
        {
            using ElkRunner runner = new(
                "/bin/bash",
                scriptPath,
                TimeSpan.FromSeconds(30));
            Task<ElkGraph> layout = Task.Run(() => runner.Layout(new ElkGraph { Id = "blocked" }));
            await Task.Delay(150);

            Stopwatch stopwatch = Stopwatch.StartNew();
            runner.Restart();
            stopwatch.Stop();

            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromMilliseconds(500),
                $"Restart took {stopwatch.Elapsed.TotalMilliseconds:0} ms.");
            await Assert.ThrowsAsync<SchematicRoutingException>(async () => await layout);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task Dispose_KillsInFlightProcess_WithoutWaitingForLayoutLock()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/bin/bash"))
        {
            return;
        }

        string directory = Path.Combine(
            Path.GetTempPath(),
            $"bistable-elk-dispose-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string scriptPath = Path.Combine(directory, "blocking-router.sh");
        await File.WriteAllTextAsync(scriptPath, """
            while IFS= read -r line; do
                sleep 30
            done
            """);

        try
        {
            ElkRunner runner = new(
                "/bin/bash",
                scriptPath,
                TimeSpan.FromSeconds(30));
            Task<ElkGraph> layout = Task.Run(() => runner.Layout(new ElkGraph { Id = "blocked" }));
            await Task.Delay(150);

            Stopwatch stopwatch = Stopwatch.StartNew();
            runner.Dispose();
            stopwatch.Stop();

            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromMilliseconds(500),
                $"Dispose took {stopwatch.Elapsed.TotalMilliseconds:0} ms.");
            await Assert.ThrowsAsync<SchematicRoutingException>(async () => await layout);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { /* best effort */ }
        }
    }
}
