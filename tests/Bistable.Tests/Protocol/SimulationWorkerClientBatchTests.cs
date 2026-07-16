using System.Diagnostics;
using Bistable.App.Services;
using Bistable.Protocol;
using Xunit.Abstractions;

namespace Bistable.Tests.Protocol;

public sealed class SimulationWorkerClientBatchTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ReadSignalsAsync_AboveWorkerLimit_ChunksRequests()
    {
        if (OperatingSystem.IsWindows()) return;
        string workerPath = await CreateWorkerAsync(
            "{\"kind\":\"signalsRead\",\"result\":{\"results\":[]}}");
        try
        {
            await using SimulationWorkerClient client = new(workerPath);
            string[] paths = Enumerable.Range(0, WorkerProtocol.MaxSignalsPerBatch + 1)
                .Select(static i => $"top.p{i}")
                .ToArray();

            long before = client.CompletedRoundTrips;
            SignalsReadResult result = await client.ReadSignalsAsync(paths, CancellationToken.None);

            Assert.Empty(result.Results);
            Assert.Equal(2, client.CompletedRoundTrips - before);
        }
        finally
        {
            File.Delete(workerPath);
        }
    }

    [Fact]
    public async Task StartAsync_ProtocolMismatch_RejectsStaleWorker()
    {
        if (OperatingSystem.IsWindows()) return;
        string workerPath = await CreateWorkerAsync(
            "{\"kind\":\"hello\",\"protocolVersion\":2,\"capabilities\":[]}");
        try
        {
            InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(
                () => SimulationWorkerClient.StartAsync(workerPath, CancellationToken.None));

            Assert.Contains("rebuild the worker", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(workerPath);
        }
    }

    [Fact]
    public async Task ReadSignalsAsync_CancelAfterWrite_DrainsBatchBeforeNextCommand()
    {
        if (OperatingSystem.IsWindows()) return;
        string workerPath = Path.Combine(Path.GetTempPath(), $"bistable-batch-drain-{Guid.NewGuid():N}.sh");
        await File.WriteAllTextAsync(workerPath, """
            #!/bin/sh
            IFS= read -r first
            sleep 0.2
            printf '%s\n' '{"kind":"signalsRead","result":{"results":[]}}'
            IFS= read -r second
            printf '%s\n' '{"kind":"frame","time":2,"signals":[]}'
            """);
        MakeExecutable(workerPath);

        try
        {
            await using SimulationWorkerClient client = new(workerPath);
            using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(50));

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                client.ReadSignalsAsync(["top.a"], cancellation.Token));

            WorkerResponse next = await client.SendAsync(
                new SimulationCommand(SimulationCommandType.Eval),
                CancellationToken.None);
            Assert.Equal((ulong)2, Assert.IsType<SimulationFrame>(next).Time);
        }
        finally
        {
            File.Delete(workerPath);
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task VisibleFrame_128Probes_BatchIsOneRoundTripAndFasterThanSingles()
    {
        if (OperatingSystem.IsWindows()) return;

        string[] paths = Enumerable.Range(0, 128).Select(static i => $"top.p{i:000}").ToArray();
        ProbeListResponse descriptors = new(paths
            .Select(static path => new ProbeDescriptor(path, 8, false, false, false, null))
            .ToArray());
        SignalsReadResponse batchResponse = new(new SignalsReadResult(paths
            .Select(static path => new SignalReadOutcome(path, "0x2a", 8, false, null))
            .ToArray()));
        string workerPath = await CreateDispatchWorkerAsync(
            ProtocolJson.Serialize<WorkerResponse>(descriptors),
            ProtocolJson.Serialize<WorkerResponse>(batchResponse));

        try
        {
            await using SimulationWorkerClient client = new(workerPath);

            Stopwatch singles = Stopwatch.StartNew();
            long singlesBefore = client.CompletedRoundTrips;
            foreach (string path in paths)
            {
                _ = await client.ReadSignalAsync(path, CancellationToken.None);
            }
            singles.Stop();
            Assert.Equal(128, client.CompletedRoundTrips - singlesBefore);

            LiveProbeService service = new();
            service.AttachWorker(client);
            await service.RefreshDescriptorsAsync(CancellationToken.None);
            int batchEvents = 0;
            service.ValuesUpdated += (_, _) => batchEvents++;

            Stopwatch batched = Stopwatch.StartNew();
            long batchBefore = client.CompletedRoundTrips;
            await service.RefreshScalarsAsync(paths, CancellationToken.None);
            batched.Stop();

            Assert.Equal(1, client.CompletedRoundTrips - batchBefore);
            Assert.Equal(1, batchEvents);
            Assert.All(paths, path => Assert.Equal("0x2a", service.GetCached(path)));
            Assert.True(
                batched.Elapsed < singles.Elapsed,
                $"128 singles took {singles.Elapsed.TotalMilliseconds:F1} ms; one batch took {batched.Elapsed.TotalMilliseconds:F1} ms.");
            output.WriteLine(
                "128 visible probes: singles={0:F1} ms/{1} round-trips; batch={2:F1} ms/{3} round-trip.",
                singles.Elapsed.TotalMilliseconds,
                128,
                batched.Elapsed.TotalMilliseconds,
                1);
        }
        finally
        {
            File.Delete(workerPath);
        }
    }

    private static async Task<string> CreateWorkerAsync(string response)
    {
        string path = Path.Combine(Path.GetTempPath(), $"bistable-batch-worker-{Guid.NewGuid():N}.sh");
        await File.WriteAllTextAsync(path, $$$"""
            #!/bin/sh
            while IFS= read -r command; do
              printf '%s\n' '{{{response}}}'
            done
            """);
        MakeExecutable(path);
        return path;
    }

    private static async Task<string> CreateDispatchWorkerAsync(string probeList, string batchResponse)
    {
        string path = Path.Combine(Path.GetTempPath(), $"bistable-batch-perf-{Guid.NewGuid():N}.sh");
        await File.WriteAllTextAsync(path, $$$"""
            #!/bin/sh
            while IFS= read -r command; do
              case "$command" in
                *'"type":"listProbes"'*)
                  printf '%s\n' '{{{probeList}}}'
                  ;;
                *'"type":"readSignals"'*)
                  sleep 0.003
                  printf '%s\n' '{{{batchResponse}}}'
                  ;;
                *)
                  sleep 0.003
                  printf '%s\n' '{"kind":"signalRead","result":{"path":"top.p000","value":"0x2a","width":8,"isSigned":false}}'
                  ;;
              esac
            done
            """);
        MakeExecutable(path);
        return path;
    }

    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
