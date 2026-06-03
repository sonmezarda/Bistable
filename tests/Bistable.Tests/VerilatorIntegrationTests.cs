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
    public async Task NativeWorkerExecutesRiscvSingleCycleSampleProgram()
    {
        string root = FindRepositoryRoot();
        string sampleDirectory = Path.Combine(root, "samples", "riscv_single_cycle");

        ProjectConfiguration configuration = await ProjectConfiguration.LoadAsync(
            Path.Combine(sampleDirectory, "riscv_single_cycle.bistable.json"),
            CancellationToken.None);

        string outputXml = Path.Combine(Path.GetTempPath(), $"bistable-riscv-single-cycle-{Guid.NewGuid():N}.xml");
        VerilatorTool tool = new();
        await tool.GenerateXmlAsync(configuration, sampleDirectory, outputXml, CancellationToken.None);

        try
        {
            Bistable.Core.Design.ModuleMetadata metadata = VerilatorXmlParser.Parse(outputXml);
            Bistable.Core.Design.Ast.DesignAst ast = new VerilatorXmlAstReader().Read(outputXml);
            Bistable.Core.Design.ElaboratedDesign design = LegacyDesignFlattener.Flatten(ast, configuration.TopModule);
            Assert.Contains(design.HierarchyRoot.Children, static child => child.InstanceName == "u_imem");
            Assert.Contains(design.HierarchyRoot.Children, static child => child.InstanceName == "u_decoder");
            Assert.Contains(design.HierarchyRoot.Children, static child => child.InstanceName == "u_registers");
            Assert.Contains(design.HierarchyRoot.Children, static child => child.InstanceName == "u_alu");
            Assert.Contains(design.HierarchyRoot.Children, static child => child.InstanceName == "u_dmem");

            SimulationWorkerBuildResult result = await new SimulationWorkerBuilder().BuildAsync(
                configuration,
                metadata,
                sampleDirectory,
                CancellationToken.None,
                progress: null,
                designAst: ast);

            await using SimulationWorkerClient client = new(result.ExecutablePath);
            string[] program =
            [
                "0x00500093", // addi x1, x0, 5
                "0x00700113", // addi x2, x0, 7
                "0x002081b3", // add  x3, x1, x2
                "0x00302023", // sw   x3, 0(x0)
                "0x00002203", // lw   x4, 0(x0)
                "0x00320463", // beq  x4, x3, +8
                "0x00100293", // addi x5, x0, 1   (skipped)
                "0x02a00293", // addi x5, x0, 42
                "0x0080036f", // jal  x6, +8
                "0x06300293", // addi x5, x0, 99  (skipped)
                "0x00100073"  // ebreak
            ];

            await client.StepAsync(new SimulationCommand(SimulationCommandType.Reset), CancellationToken.None);
            await client.StepAsync(new SimulationCommand(SimulationCommandType.SetInput, "enable", "0"), CancellationToken.None);
            for (int address = 0; address < program.Length; address++)
            {
                await client.StepAsync(new SimulationCommand(SimulationCommandType.SetInput, "prog_addr", address.ToString()), CancellationToken.None);
                await client.StepAsync(new SimulationCommand(SimulationCommandType.SetInput, "prog_wdata", program[address]), CancellationToken.None);
                await client.StepAsync(new SimulationCommand(SimulationCommandType.SetInput, "prog_we", "1"), CancellationToken.None);
                await client.StepAsync(new SimulationCommand(SimulationCommandType.Tick, Signal: "clk"), CancellationToken.None);
            }

            await client.StepAsync(new SimulationCommand(SimulationCommandType.SetInput, "prog_we", "0"), CancellationToken.None);
            MemoryReadResult loadedProgram = await client.ReadMemoryAsync(
                "riscv_single_cycle_top.u_imem.mem", address: 0, count: program.Length, CancellationToken.None);
            Assert.Equal(["0x500093", "0x700113", "0x2081b3", "0x302023", "0x2203", "0x320463", "0x100293", "0x2a00293", "0x80036f", "0x6300293", "0x100073"], loadedProgram.Cells);

            await client.StepAsync(new SimulationCommand(SimulationCommandType.SetInput, "enable", "1"), CancellationToken.None);

            SimulationFrame frame = null!;
            for (int cycle = 0; cycle < 9; cycle++)
            {
                frame = await client.StepAsync(
                    new SimulationCommand(SimulationCommandType.Tick, Signal: "clk"),
                    CancellationToken.None);
            }

            Assert.Equal("40", Sample(frame, "pc"));
            Assert.Equal("5", Sample(frame, "debug_x1"));
            Assert.Equal("7", Sample(frame, "debug_x2"));
            Assert.Equal("12", Sample(frame, "debug_x3"));
            Assert.Equal("12", Sample(frame, "debug_x4"));
            Assert.Equal("42", Sample(frame, "debug_x5"));
            Assert.Equal("36", Sample(frame, "debug_x6"));
            Assert.Equal("12", Sample(frame, "debug_dmem0"));
            Assert.Equal("1", Sample(frame, "halted"));
            Assert.Equal("1048691", Sample(frame, "instruction")); // 0x00100073 ebreak

            MemoryReadResult regs = await client.ReadMemoryAsync(
                "riscv_single_cycle_top.u_registers.regs", address: 1, count: 6, CancellationToken.None);
            Assert.Equal(32, regs.CellWidth);
            Assert.Equal(["0x5", "0x7", "0xc", "0xc", "0x2a", "0x24"], regs.Cells);

            MemoryReadResult dmem = await client.ReadMemoryAsync(
                "riscv_single_cycle_top.u_dmem.mem", address: 0, count: 1, CancellationToken.None);
            Assert.Equal(["0xc"], dmem.Cells);
        }
        finally
        {
            File.Delete(outputXml);
        }
    }

    [Fact]
    public async Task NativeWorkerExecutesRiscvProgramLoadedViaWriteMemory()
    {
        // Reproduces the Memory-Viewer-Load-File user path: the host writes
        // the program directly into instruction memory through the probe table
        // (WriteMemoryAsync), then sets enable=1 and ticks. If the writes land
        // on a Verilator split-var copy distinct from the fetch path's read,
        // every fetched instruction comes back 0 and register values stay 0.
        string root = FindRepositoryRoot();
        string sampleDirectory = Path.Combine(root, "samples", "riscv_single_cycle");

        ProjectConfiguration configuration = await ProjectConfiguration.LoadAsync(
            Path.Combine(sampleDirectory, "riscv_single_cycle.bistable.json"),
            CancellationToken.None);

        string outputXml = Path.Combine(Path.GetTempPath(), $"bistable-riscv-write-{Guid.NewGuid():N}.xml");
        VerilatorTool tool = new();
        await tool.GenerateXmlAsync(configuration, sampleDirectory, outputXml, CancellationToken.None);

        try
        {
            Bistable.Core.Design.ModuleMetadata metadata = VerilatorXmlParser.Parse(outputXml);
            Bistable.Core.Design.Ast.DesignAst ast = new VerilatorXmlAstReader().Read(outputXml);

            SimulationWorkerBuildResult result = await new SimulationWorkerBuilder().BuildAsync(
                configuration, metadata, sampleDirectory, CancellationToken.None,
                progress: null, designAst: ast);

            await using SimulationWorkerClient client = new(result.ExecutablePath);
            string[] program =
            {
                "0x00500093", // addi x1, x0, 5
                "0x00300113", // addi x2, x0, 3
                "0x002081b3", // add  x3, x1, x2   ; x3 = 8
                "0x00100073", // ebreak
            };

            await client.StepAsync(new SimulationCommand(SimulationCommandType.Reset), CancellationToken.None);
            await client.StepAsync(new SimulationCommand(SimulationCommandType.SetInput, "enable", "0"), CancellationToken.None);

            for (int address = 0; address < program.Length; address++)
            {
                await client.WriteMemoryAsync(
                    "riscv_single_cycle_top.u_imem.mem",
                    (ulong)address,
                    program[address],
                    CancellationToken.None);
            }

            MemoryReadResult loaded = await client.ReadMemoryAsync(
                "riscv_single_cycle_top.u_imem.mem", address: 0, count: program.Length, CancellationToken.None);
            Assert.Equal(["0x500093", "0x300113", "0x2081b3", "0x100073"], loaded.Cells);

            await client.StepAsync(new SimulationCommand(SimulationCommandType.SetInput, "enable", "1"), CancellationToken.None);

            SimulationFrame frame = null!;
            for (int cycle = 0; cycle < 5; cycle++)
            {
                frame = await client.StepAsync(
                    new SimulationCommand(SimulationCommandType.Tick, Signal: "clk"),
                    CancellationToken.None);
            }

            Assert.Equal("5", Sample(frame, "debug_x1"));
            Assert.Equal("3", Sample(frame, "debug_x2"));
            Assert.Equal("8", Sample(frame, "debug_x3"));
            Assert.Equal("1", Sample(frame, "halted"));
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

    private static string Sample(SimulationFrame frame, string signal) =>
        Assert.Single(frame.Signals, sample => sample.Signal == signal).Value;

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
