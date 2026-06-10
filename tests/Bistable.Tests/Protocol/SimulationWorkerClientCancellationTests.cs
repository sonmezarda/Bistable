using System.Diagnostics;
using Bistable.App.Services;
using Bistable.Protocol;

namespace Bistable.Tests.Protocol;

public sealed class SimulationWorkerClientCancellationTests
{
    [Fact]
    public async Task SendAsync_CancelAfterWrite_DrainsResponseBeforeNextCommand()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string workerPath = Path.Combine(
            Path.GetTempPath(),
            $"bistable-delayed-worker-{Guid.NewGuid():N}.sh");
        await File.WriteAllTextAsync(workerPath, """
            #!/bin/sh
            IFS= read -r first
            sleep 0.2
            printf '%s\n' '{"kind":"frame","time":1,"signals":[]}'
            IFS= read -r second
            printf '%s\n' '{"kind":"frame","time":2,"signals":[]}'
            while IFS= read -r ignored; do
              printf '%s\n' '{"kind":"frame","time":3,"signals":[]}'
            done
            """);
        File.SetUnixFileMode(
            workerPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        try
        {
            await using SimulationWorkerClient client = new(workerPath);
            using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(50));

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                client.SendAsync(
                    new SimulationCommand(SimulationCommandType.Eval),
                    cancellation.Token));

            WorkerResponse response = await client.SendAsync(
                new SimulationCommand(SimulationCommandType.Eval),
                CancellationToken.None);

            SimulationFrame frame = Assert.IsType<SimulationFrame>(response);
            Assert.Equal((ulong)2, frame.Time);
        }
        finally
        {
            File.Delete(workerPath);
        }
    }

    [Fact]
    public async Task DisposeAsync_KillsInFlightWorker_AndIsIdempotent()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string workerPath = Path.Combine(
            Path.GetTempPath(),
            $"bistable-blocked-worker-{Guid.NewGuid():N}.sh");
        await File.WriteAllTextAsync(workerPath, """
            #!/bin/sh
            IFS= read -r command
            sleep 5
            printf '%s\n' '{"kind":"frame","time":1,"signals":[]}'
            """);
        File.SetUnixFileMode(
            workerPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        SimulationWorkerClient client = new(workerPath);
        try
        {
            Task<WorkerResponse> inFlight = client.SendAsync(
                new SimulationCommand(SimulationCommandType.Eval),
                CancellationToken.None);
            await Task.Delay(50);

            Stopwatch stopwatch = Stopwatch.StartNew();
            await client.DisposeAsync();
            stopwatch.Stop();

            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromMilliseconds(500),
                $"Dispose took {stopwatch.Elapsed.TotalMilliseconds:F0} ms.");
            await Assert.ThrowsAnyAsync<Exception>(() => inFlight);
            await client.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(() =>
                client.SendAsync(
                    new SimulationCommand(SimulationCommandType.Eval),
                    CancellationToken.None));
        }
        finally
        {
            await client.DisposeAsync();
            File.Delete(workerPath);
        }
    }
}
