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
            Bistable.Core.Design.ModuleMetadata metadata = VerilatorXmlParser.Parse(outputXml);
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
    public async Task ParallelWorkerBuildsShareOutputDirectorySafely()
    {
        string root = FindRepositoryRoot();
        string sampleDirectory = Path.Combine(root, "samples", "alu");

        ProjectConfiguration configuration = await ProjectConfiguration.LoadAsync(
            Path.Combine(sampleDirectory, "alu.bistable.json"),
            CancellationToken.None);

        string outputXml = Path.Combine(Path.GetTempPath(), $"bistable-worker-parallel-{Guid.NewGuid():N}.xml");
        VerilatorTool tool = new();
        await tool.GenerateXmlAsync(configuration, sampleDirectory, outputXml, CancellationToken.None);

        try
        {
            Bistable.Core.Design.ModuleMetadata metadata = VerilatorXmlParser.Parse(outputXml);
            SimulationWorkerBuilder builder = new();

            Task<SimulationWorkerBuildResult> first = builder.BuildAsync(configuration, metadata, sampleDirectory, CancellationToken.None);
            Task<SimulationWorkerBuildResult> second = builder.BuildAsync(configuration, metadata, sampleDirectory, CancellationToken.None);

            SimulationWorkerBuildResult[] results = await Task.WhenAll(first, second);

            Assert.All(results, result => Assert.True(File.Exists(result.ExecutablePath)));
            await using SimulationWorkerClient client = new(results[0].ExecutablePath);
            SimulationFrame frame = await client.StepAsync(new SimulationCommand(SimulationCommandType.Eval), CancellationToken.None);
            Assert.NotNull(frame);
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
            Bistable.Core.Design.ModuleMetadata metadata = VerilatorXmlParser.Parse(outputXml);
            SimulationWorkerBuildResult result = await new SimulationWorkerBuilder().BuildAsync(
                configuration,
                metadata,
                sampleDirectory,
                CancellationToken.None);

            await using SimulationWorkerClient client = new(result.ExecutablePath);
            await client.StepAsync(new SimulationCommand(SimulationCommandType.SetInput, "a", "0x12"), CancellationToken.None);
            await client.StepAsync(new SimulationCommand(SimulationCommandType.SetInput, "b", "0x22"), CancellationToken.None);
            await client.StepAsync(new SimulationCommand(SimulationCommandType.SetInput, "op", "0"), CancellationToken.None);

            SimulationFrame frame = await client.StepAsync(
                new SimulationCommand(SimulationCommandType.Eval),
                CancellationToken.None);

            SignalSample y = Assert.Single(frame.Signals, static sample => sample.Signal == "y");
            SignalSample zero = Assert.Single(frame.Signals, static sample => sample.Signal == "zero");
            Assert.Equal("52", y.Value);
            Assert.Equal("0", zero.Value);
        }
        finally
        {
            File.Delete(outputXml);
        }
    }

    [Fact]
    public async Task NativeWorkerDrivesAndReadsWideTopLevelPorts()
    {
        string projectDirectory = Path.Combine(Path.GetTempPath(), $"bistable-wide-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectDirectory);
        string sourcePath = Path.Combine(projectDirectory, "wide_top.sv");
        await File.WriteAllTextAsync(sourcePath, """
            module wide_top(
                input  logic [95:0] a,
                output logic [95:0] y
            );
                assign y = a;
            endmodule
            """);

        ProjectConfiguration configuration = new()
        {
            TopModule = "wide_top",
            Sources = ["wide_top.sv"],
            EnableInternalProbes = false
        };

        string outputXml = Path.Combine(projectDirectory, "wide_top.xml");
        try
        {
            VerilatorTool tool = new();
            await tool.GenerateXmlAsync(configuration, projectDirectory, outputXml, CancellationToken.None);

            Bistable.Core.Design.ModuleMetadata metadata = VerilatorXmlParser.Parse(outputXml);
            Assert.Equal(96, Assert.Single(metadata.Inputs).Width);
            Assert.Equal(96, Assert.Single(metadata.Outputs).Width);

            SimulationWorkerBuildResult result = await new SimulationWorkerBuilder().BuildAsync(
                configuration,
                metadata,
                projectDirectory,
                CancellationToken.None);

            await using SimulationWorkerClient client = new(result.ExecutablePath);
            const string value = "0x123456789ABCDEF001234567";
            await client.StepAsync(new SimulationCommand(SimulationCommandType.SetInput, "a", value), CancellationToken.None);

            SimulationFrame frame = await client.StepAsync(
                new SimulationCommand(SimulationCommandType.Eval),
                CancellationToken.None);

            SignalSample y = Assert.Single(frame.Signals, static sample => sample.Signal == "y");
            Assert.Equal("0x123456789abcdef001234567", y.Value);
        }
        finally
        {
            Directory.Delete(projectDirectory, recursive: true);
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
            Bistable.Core.Design.ModuleMetadata metadata = VerilatorXmlParser.Parse(outputXml);
            SimulationWorkerBuildResult result = await new SimulationWorkerBuilder().BuildAsync(
                configuration,
                metadata,
                sampleDirectory,
                CancellationToken.None);

            await using SimulationWorkerClient client = new(result.ExecutablePath);
            await client.StepAsync(new SimulationCommand(SimulationCommandType.Reset), CancellationToken.None);
            await client.StepAsync(new SimulationCommand(SimulationCommandType.SetInput, "enable", "1"), CancellationToken.None);

            SimulationFrame first = await client.StepAsync(
                new SimulationCommand(SimulationCommandType.Tick, Signal: "clk"),
                CancellationToken.None);
            SimulationFrame second = await client.StepAsync(
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
            Bistable.Core.Design.ElaboratedDesign design = VerilatorXmlParser.ParseDesign(outputXml);

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
    public async Task GeneratesHierarchyForTinyCpuSample()
    {
        string root = FindRepositoryRoot();
        string sampleDirectory = Path.Combine(root, "samples", "tiny_cpu");

        ProjectConfiguration configuration = await ProjectConfiguration.LoadAsync(
            Path.Combine(sampleDirectory, "tiny_cpu.bistable.json"),
            CancellationToken.None);

        string outputXml = Path.Combine(Path.GetTempPath(), $"bistable-tiny-cpu-{Guid.NewGuid():N}.xml");
        VerilatorTool tool = new();
        await tool.GenerateXmlAsync(configuration, sampleDirectory, outputXml, CancellationToken.None);

        try
        {
            Bistable.Core.Design.ElaboratedDesign design = VerilatorXmlParser.ParseDesign(outputXml);

            Assert.Equal("tiny_cpu_top", design.TopModule.Name);
            Assert.Contains(design.HierarchyRoot.Children, static child => child.InstanceName == "u_control");
            Assert.Contains(design.HierarchyRoot.Children, static child => child.InstanceName == "u_registers");
            Assert.Contains(design.HierarchyRoot.Children, static child => child.InstanceName == "u_alu");
            Assert.Contains(design.HierarchyRoot.Children, static child => child.InstanceName == "u_status");
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
            Bistable.Core.Design.ModuleMetadata metadata = VerilatorXmlParser.Parse(outputXml);
            SimulationWorkerBuildResult result = await new SimulationWorkerBuilder().BuildAsync(
                configuration,
                metadata,
                sampleDirectory,
                CancellationToken.None);

            await using SimulationWorkerClient client = new(result.ExecutablePath);
            await client.StepAsync(new SimulationCommand(SimulationCommandType.SetInput, "a", "0x03"), CancellationToken.None);
            await client.StepAsync(new SimulationCommand(SimulationCommandType.SetInput, "b", "0x04"), CancellationToken.None);
            await client.StepAsync(new SimulationCommand(SimulationCommandType.Eval), CancellationToken.None);

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
