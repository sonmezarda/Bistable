using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Projects;
using Bistable.Protocol;
using Bistable.Verilator;

namespace Bistable.App.Services;

/// <summary>
/// Builds and initializes a replacement worker in a separate artifact slot.
/// The caller keeps the current worker alive until this method succeeds, then
/// performs one atomic ownership swap on the UI thread.
/// </summary>
public sealed class SimulationWorkerHotSwapService(SimulationWorkerBuilder builder)
{
    public async Task<PreparedSimulationWorker> PrepareAsync(
        ProjectConfiguration project,
        ModuleMetadata metadata,
        DesignAst? ast,
        string projectDirectory,
        IReadOnlyDictionary<string, string> inputValues,
        int slot,
        CancellationToken cancellationToken)
    {
        ProjectConfiguration stagedProject = project with
        {
            WorkerBuildName = $"{project.TopModule}__hotreload_{Math.Abs(slot % 2)}"
        };
        SimulationWorkerBuildResult build = await builder.BuildAsync(
            stagedProject,
            metadata,
            projectDirectory,
            cancellationToken,
            progress: null,
            designAst: ast);
        SimulationWorkerClient client = await SimulationWorkerClient.StartAsync(
            build.ExecutablePath,
            cancellationToken);
        try
        {
            foreach (SignalPort input in metadata.Inputs)
            {
                string value = inputValues.TryGetValue(input.Name, out string? preserved) ? preserved : "0";
                _ = await client.StepAsync(
                    new SimulationCommand(SimulationCommandType.SetInput, input.Name, value),
                    cancellationToken);
            }
            SimulationFrame frame = await client.StepAsync(
                new SimulationCommand(SimulationCommandType.Eval),
                cancellationToken);
            return new PreparedSimulationWorker(client, frame, build.TraceFilePath);
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }
}

public sealed record PreparedSimulationWorker(
    SimulationWorkerClient Client,
    SimulationFrame InitialFrame,
    string? TraceFilePath) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Client.DisposeAsync();
}
