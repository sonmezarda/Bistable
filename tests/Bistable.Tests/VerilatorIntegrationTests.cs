using Bistable.Core.Projects;
using Bistable.App.Services;
using Bistable.Protocol;
using Bistable.Verilator;

namespace Bistable.Tests;

public sealed class VerilatorIntegrationTests
{
    [Fact]
    public async Task GeneratesXmlForParameterizedAluWhenVerilatorIsAvailable()
    {
        string root = FindRepositoryRoot();
        string sampleDirectory = Path.Combine(root, "samples", "alu");
        string outputXml = Path.Combine(Path.GetTempPath(), $"bistable-alu-{Guid.NewGuid():N}.xml");

        ProjectConfiguration configuration = await ProjectConfiguration.LoadAsync(
            Path.Combine(sampleDirectory, "alu.bistable.json"),
            CancellationToken.None);

        try
        {
            VerilatorTool tool = new();
            await tool.GenerateXmlAsync(configuration, sampleDirectory, outputXml, CancellationToken.None);

            Assert.True(File.Exists(outputXml));
            Assert.Contains("module", await File.ReadAllTextAsync(outputXml, CancellationToken.None));
        }
        finally
        {
            File.Delete(outputXml);
        }
    }

    [Fact]
    public async Task BuildsNativeWorkerForParameterizedAlu()
    {
        string root = FindRepositoryRoot();
        string sampleDirectory = Path.Combine(root, "samples", "alu");

        ProjectConfiguration configuration = await ProjectConfiguration.LoadAsync(
            Path.Combine(sampleDirectory, "alu.bistable.json"),
            CancellationToken.None);

        string outputXml = Path.Combine(Path.GetTempPath(), $"bistable-worker-{Guid.NewGuid():N}.xml");
        VerilatorTool tool = new();
        await tool.GenerateXmlAsync(configuration, sampleDirectory, outputXml, CancellationToken.None);

        try
        {
            Bistable.Core.Design.ModuleMetadata metadata = new VerilatorXmlParser().Parse(outputXml);
            SimulationWorkerBuildResult result = await new SimulationWorkerBuilder().BuildAsync(
                configuration,
                metadata,
                sampleDirectory,
                CancellationToken.None);

            Assert.True(File.Exists(result.ExecutablePath));
        }
        finally
        {
            File.Delete(outputXml);
        }
    }

    [Fact]
    public async Task NativeWorkerEvaluatesParameterizedAlu()
    {
        string root = FindRepositoryRoot();
        string sampleDirectory = Path.Combine(root, "samples", "alu");

        ProjectConfiguration configuration = await ProjectConfiguration.LoadAsync(
            Path.Combine(sampleDirectory, "alu.bistable.json"),
            CancellationToken.None);

        string outputXml = Path.Combine(Path.GetTempPath(), $"bistable-worker-eval-{Guid.NewGuid():N}.xml");
        VerilatorTool tool = new();
        await tool.GenerateXmlAsync(configuration, sampleDirectory, outputXml, CancellationToken.None);

        try
        {
            Bistable.Core.Design.ModuleMetadata metadata = new VerilatorXmlParser().Parse(outputXml);
            SimulationWorkerBuildResult result = await new SimulationWorkerBuilder().BuildAsync(
                configuration,
                metadata,
                sampleDirectory,
                CancellationToken.None);

            await using SimulationWorkerClient client = new(result.ExecutablePath);
            await client.SendAsync(new SimulationCommand(SimulationCommandType.SetInput, "a", "0x12"), CancellationToken.None);
            await client.SendAsync(new SimulationCommand(SimulationCommandType.SetInput, "b", "0x22"), CancellationToken.None);
            await client.SendAsync(new SimulationCommand(SimulationCommandType.SetInput, "op", "0"), CancellationToken.None);

            SimulationSnapshot snapshot = await client.SendAsync(
                new SimulationCommand(SimulationCommandType.Eval),
                CancellationToken.None);

            SignalSample y = Assert.Single(snapshot.Signals, static sample => sample.Signal == "y");
            SignalSample zero = Assert.Single(snapshot.Signals, static sample => sample.Signal == "zero");
            Assert.Equal("52", y.Value);
            Assert.Equal("0", zero.Value);
        }
        finally
        {
            File.Delete(outputXml);
        }
    }

    [Fact]
    public async Task NativeWorkerTicksSequentialCounter()
    {
        string root = FindRepositoryRoot();
        string sampleDirectory = Path.Combine(root, "samples", "counter");

        ProjectConfiguration configuration = await ProjectConfiguration.LoadAsync(
            Path.Combine(sampleDirectory, "counter.bistable.json"),
            CancellationToken.None);

        string outputXml = Path.Combine(Path.GetTempPath(), $"bistable-counter-{Guid.NewGuid():N}.xml");
        VerilatorTool tool = new();
        await tool.GenerateXmlAsync(configuration, sampleDirectory, outputXml, CancellationToken.None);

        try
        {
            Bistable.Core.Design.ModuleMetadata metadata = new VerilatorXmlParser().Parse(outputXml);
            SimulationWorkerBuildResult result = await new SimulationWorkerBuilder().BuildAsync(
                configuration,
                metadata,
                sampleDirectory,
                CancellationToken.None);

            await using SimulationWorkerClient client = new(result.ExecutablePath);
            await client.SendAsync(new SimulationCommand(SimulationCommandType.Reset), CancellationToken.None);
            await client.SendAsync(new SimulationCommand(SimulationCommandType.SetInput, "enable", "1"), CancellationToken.None);

            SimulationSnapshot first = await client.SendAsync(
                new SimulationCommand(SimulationCommandType.Tick, Signal: "clk"),
                CancellationToken.None);
            SimulationSnapshot second = await client.SendAsync(
                new SimulationCommand(SimulationCommandType.Tick, Signal: "clk"),
                CancellationToken.None);

            Assert.Equal("1", Assert.Single(first.Signals, static sample => sample.Signal == "count").Value);
            Assert.Equal("2", Assert.Single(second.Signals, static sample => sample.Signal == "count").Value);
            Assert.NotNull(first.Trace);
            Assert.Contains(first.Trace!, static sample => sample is { Signal: "clk", Value: "1", Time: 0 });
            Assert.Contains(first.Trace!, static sample => sample is { Signal: "clk", Value: "0", Time: 1 });
            Assert.Contains(first.Trace!, static sample => sample is { Signal: "count", Value: "1", Time: 0 or 1 });
        }
        finally
        {
            File.Delete(outputXml);
        }
    }

    [Fact]
    public async Task GeneratesHierarchyForHierarchicalSample()
    {
        string root = FindRepositoryRoot();
        string sampleDirectory = Path.Combine(root, "samples", "hierarchy");

        ProjectConfiguration configuration = await ProjectConfiguration.LoadAsync(
            Path.Combine(sampleDirectory, "hierarchy.bistable.json"),
            CancellationToken.None);

        string outputXml = Path.Combine(Path.GetTempPath(), $"bistable-hierarchy-{Guid.NewGuid():N}.xml");
        VerilatorTool tool = new();
        await tool.GenerateXmlAsync(configuration, sampleDirectory, outputXml, CancellationToken.None);

        try
        {
            Bistable.Core.Design.ElaboratedDesign design = new VerilatorXmlParser().ParseDesign(outputXml);

            Assert.Equal("system_top", design.TopModule.Name);
            Bistable.Core.Design.DesignHierarchyNode core = Assert.Single(design.HierarchyRoot.Children);
            Assert.Equal("u_core", core.InstanceName);
            Assert.Equal("core_cluster", core.ModuleName);
            Assert.Contains(core.Children, static child => child.InstanceName == "u_logic");
            Assert.Contains(core.Children, static child => child.InstanceName == "u_status");
        }
        finally
        {
            File.Delete(outputXml);
        }
    }

    [Fact]
    public async Task NativeWorkerProducesTraceCatalogForHierarchicalSample()
    {
        string root = FindRepositoryRoot();
        string sampleDirectory = Path.Combine(root, "samples", "hierarchy");

        ProjectConfiguration configuration = await ProjectConfiguration.LoadAsync(
            Path.Combine(sampleDirectory, "hierarchy.bistable.json"),
            CancellationToken.None);

        string outputXml = Path.Combine(Path.GetTempPath(), $"bistable-hierarchy-trace-{Guid.NewGuid():N}.xml");
        VerilatorTool tool = new();
        await tool.GenerateXmlAsync(configuration, sampleDirectory, outputXml, CancellationToken.None);

        try
        {
            Bistable.Core.Design.ModuleMetadata metadata = new VerilatorXmlParser().Parse(outputXml);
            SimulationWorkerBuildResult result = await new SimulationWorkerBuilder().BuildAsync(
                configuration,
                metadata,
                sampleDirectory,
                CancellationToken.None);

            await using SimulationWorkerClient client = new(result.ExecutablePath);
            await client.SendAsync(new SimulationCommand(SimulationCommandType.SetInput, "a", "0x03"), CancellationToken.None);
            await client.SendAsync(new SimulationCommand(SimulationCommandType.SetInput, "b", "0x04"), CancellationToken.None);
            await client.SendAsync(new SimulationCommand(SimulationCommandType.Eval), CancellationToken.None);

            Assert.NotNull(result.TraceFilePath);
            Assert.True(File.Exists(result.TraceFilePath));

            VcdTraceDocument trace = new VcdTraceReader().Load(result.TraceFilePath!, configuration.TopModule);
            Assert.Contains(trace.Signals, static signal => signal.Name == "system_top.u_core.u_logic.sum");
            Assert.True(trace.TryGetEvents("result", out IReadOnlyList<VcdTraceEvent>? resultEvents));
            Assert.NotEmpty(resultEvents);
        }
        finally
        {
            File.Delete(outputXml);
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
