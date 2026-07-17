using Bistable.App.Services;
using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Projects;
using Bistable.Protocol;
using Bistable.Verilator;

namespace Bistable.Tests.Protocol;

public sealed class SimulationWorkerHotSwapServiceTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task PrepareAsync_KeepsCurrentWorkerResponsive_AndPreservesInputs()
    {
        string root = FindRepositoryRoot();
        string sampleDirectory = Path.Combine(root, "samples", "counter");
        ProjectConfiguration project = await ProjectConfiguration.LoadAsync(
            Path.Combine(sampleDirectory, "counter.bistable.json"));
        string xmlPath = Path.Combine(Path.GetTempPath(), $"bistable-hot-swap-{Guid.NewGuid():N}.xml");
        try
        {
            VerilatorTool verilator = new();
            await verilator.GenerateXmlAsync(project, sampleDirectory, xmlPath);
            ModuleMetadata metadata = VerilatorXmlParser.Parse(xmlPath);
            DesignAst ast = new VerilatorXmlAstReader().Read(xmlPath);
            SimulationWorkerBuilder builder = new();
            SimulationWorkerBuildResult initialBuild = await builder.BuildAsync(
                project, metadata, sampleDirectory, designAst: ast);
            await using SimulationWorkerClient current = await SimulationWorkerClient.StartAsync(
                initialBuild.ExecutablePath,
                CancellationToken.None);
            await current.StepAsync(new SimulationCommand(SimulationCommandType.SetInput, "enable", "1"), CancellationToken.None);

            SimulationWorkerHotSwapService service = new(builder);
            Task<PreparedSimulationWorker> preparing = service.PrepareAsync(
                project,
                metadata,
                ast,
                sampleDirectory,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["enable"] = "1" },
                slot: 1,
                CancellationToken.None);

            SignalReadResult whileBuilding = await current.ReadSignalAsync("counter.enable", CancellationToken.None);
            Assert.Equal("0x1", whileBuilding.Value);

            await using PreparedSimulationWorker prepared = await preparing;
            SignalReadResult replacementInput = await prepared.Client.ReadSignalAsync("counter.enable", CancellationToken.None);
            Assert.Equal("0x1", replacementInput.Value);
            Assert.False(ReferenceEquals(current, prepared.Client));
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Bistable.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Repository root could not be found.");
    }
}
