using System.Diagnostics;
using Bistable.App.Services;
using Bistable.Core.Projects;
using Bistable.Protocol;
using Bistable.Verilator;
using Bistable.Yosys;

namespace Bistable.Tests.Synthesis;

[Trait("Category", "Integration")]
public sealed class GateLevelWorkerIntegrationTests
{
    [Fact]
    public async Task YosysVerilogOutput_BuildsAndEvaluatesGateLevelWorker()
    {
        if (!ToolAvailable("yosys", "-V") || !ToolAvailable("verilator", "--version"))
        {
            return;
        }

        string workDir = Path.Combine(Path.GetTempPath(), $"bistable-gate-worker-rt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(workDir, "tiny_and.sv"), """
                module tiny_and(input logic a, input logic b, output logic y);
                    assign y = a & b;
                endmodule
                """);

            ProjectConfiguration project = new()
            {
                TopModule = "tiny_and",
                Sources = ["tiny_and.sv"],
                EnableInternalProbes = false,
                Trace = new TraceConfiguration(Enabled: true, Format: "vcd", Depth: 1),
                Synthesis = new SynthesisConfiguration(
                    Enabled: true,
                    OutputJson: "tiny_and.json",
                    OutputVerilog: "tiny_and_synth.sv"),
            };

            string script = YosysScriptBuilder.Build(project, project.Synthesis!, workDir);
            string scriptPath = Path.Combine(workDir, "synth.ys");
            await File.WriteAllTextAsync(scriptPath, script);
            await new YosysTool().RunScriptAsync(scriptPath, workDir, CancellationToken.None);

            GateLevelWorkerBuildResult build = await new GateLevelWorkerBuildService().BuildAsync(
                project,
                project.Synthesis!,
                workDir,
                CancellationToken.None);

            Assert.Equal("tiny_and__gate", build.Project.WorkerBuildName);
            Assert.True(File.Exists(build.Worker.ExecutablePath));

            await using SimulationWorkerClient client = new(build.Worker.ExecutablePath);
            await client.StepAsync(new SimulationCommand(SimulationCommandType.SetInput, "a", "1"), CancellationToken.None);
            await client.StepAsync(new SimulationCommand(SimulationCommandType.SetInput, "b", "1"), CancellationToken.None);
            SimulationFrame frame = await client.StepAsync(
                new SimulationCommand(SimulationCommandType.Eval),
                CancellationToken.None);

            SignalSample y = Assert.Single(frame.Signals, static sample => sample.Signal == "y");
            Assert.Equal("1", y.Value);

            await client.StepAsync(
                new SimulationCommand(SimulationCommandType.Reset),
                CancellationToken.None);
            await client.StepAsync(
                new SimulationCommand(SimulationCommandType.SetInput, "a", "0"),
                CancellationToken.None);
            await client.StepAsync(
                new SimulationCommand(SimulationCommandType.SetInput, "b", "1"),
                CancellationToken.None);
            SimulationFrame secondFrame = await client.StepAsync(
                new SimulationCommand(SimulationCommandType.Eval),
                CancellationToken.None);

            SignalSample secondY = Assert.Single(secondFrame.Signals, static sample => sample.Signal == "y");
            Assert.Equal("0", secondY.Value);
        }
        finally
        {
            try { if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task FlattenedArbitrarilyNamedMemory_IsExposedThroughMemoryProtocol()
    {
        if (!ToolAvailable("yosys", "-V") || !ToolAvailable("verilator", "--version"))
        {
            return;
        }

        string workDir = Path.Combine(Path.GetTempPath(), $"bistable-gate-memory-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(workDir, "memory_top.sv"), """
                module storage_block(
                    input  wire       clk,
                    input  wire       write_enable,
                    input  wire [1:0] address,
                    input  wire [7:0] write_data,
                    output wire [7:0] read_data
                );
                    reg [7:0] storage_bank [0:3];
                    always @(posedge clk) begin
                        if (write_enable) storage_bank[address] <= write_data;
                    end
                    assign read_data = storage_bank[address];
                endmodule

                module memory_top(
                    input  wire       clk,
                    input  wire       write_enable,
                    input  wire [1:0] address,
                    input  wire [7:0] write_data,
                    output wire [7:0] read_data
                );
                    storage_block u_storage(
                        .clk(clk),
                        .write_enable(write_enable),
                        .address(address),
                        .write_data(write_data),
                        .read_data(read_data)
                    );
                endmodule
                """);

            ProjectConfiguration project = new()
            {
                TopModule = "memory_top",
                Sources = ["memory_top.sv"],
                Trace = new TraceConfiguration(Enabled: false),
                Runtime = new CpuRuntimeConfiguration(
                    ProgramImages:
                    [
                        new ProgramImageBinding(
                            Path: "program.hex",
                            Format: "hex",
                            ProbePath: "memory_top.u_storage.storage_bank"),
                    ]),
                Synthesis = new SynthesisConfiguration(
                    Enabled: true,
                    OutputJson: "memory_top.json",
                    OutputVerilog: "memory_top_synth.sv",
                    Flatten: true),
            };

            string script = YosysScriptBuilder.Build(project, project.Synthesis!, workDir);
            string scriptPath = Path.Combine(workDir, "synth.ys");
            await File.WriteAllTextAsync(scriptPath, script);
            await new YosysTool().RunScriptAsync(scriptPath, workDir, CancellationToken.None);

            GateLevelWorkerBuildResult build = await new GateLevelWorkerBuildService().BuildAsync(
                project,
                project.Synthesis!,
                workDir,
                CancellationToken.None);

            GateMemoryProbeMapping mapping = Assert.Single(
                build.RuntimeProbeManifest.Memories,
                static memory => memory.LogicalPath == "memory_top.u_storage.storage_bank");
            Assert.Equal(GateMemoryMappingKind.LoweredElements, mapping.Kind);
            Assert.True(File.Exists(build.RuntimeProbeManifestPath));

            await using SimulationWorkerClient client = new(build.Worker.ExecutablePath);
            ProbeDescriptor descriptor = Assert.Single(
                await client.ListProbesAsync(CancellationToken.None),
                static probe => probe.Path == "memory_top.u_storage.storage_bank");
            Assert.True(descriptor.IsMemory);
            Assert.Equal(4, descriptor.MemoryDepth);
            Assert.Equal(8, descriptor.Width);

            await client.WriteMemoryAsync(
                descriptor.Path,
                address: 2,
                value: "0xa5",
                CancellationToken.None);
            MemoryReadResult read = await client.ReadMemoryAsync(
                descriptor.Path,
                address: 0,
                count: 4,
                CancellationToken.None);

            Assert.Equal("0xa5", read.Cells[2]);
        }
        finally
        {
            try { if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task RiscvSingleCycle_GateWorkerExecutesProgramLoadedThroughLogicalMemory()
    {
        if (!ToolAvailable("yosys", "-V") || !ToolAvailable("verilator", "--version"))
        {
            return;
        }

        string root = FindRepositoryRoot();
        string sampleDirectory = Path.Combine(root, "samples", "riscv_single_cycle");
        ProjectConfiguration project = await ProjectConfiguration.LoadAsync(
            Path.Combine(sampleDirectory, "riscv_single_cycle.bistable.json"),
            CancellationToken.None);
        SynthesisConfiguration synthesis = project.Synthesis
            ?? throw new InvalidOperationException("RISC-V sample synthesis configuration is missing.");

        string script = YosysScriptBuilder.Build(project, synthesis, sampleDirectory);
        string scriptPath = Path.Combine(
            sampleDirectory,
            ".bistable",
            "synthesis",
            "gate-worker-integration.ys");
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        await File.WriteAllTextAsync(scriptPath, script);
        await new YosysTool().RunScriptAsync(scriptPath, sampleDirectory, CancellationToken.None);

        GateLevelWorkerBuildResult build = await new GateLevelWorkerBuildService().BuildAsync(
            project,
            synthesis,
            sampleDirectory,
            CancellationToken.None);
        GateMemoryProbeMapping instructionMemory = Assert.Single(
            build.RuntimeProbeManifest.Memories,
            static memory => memory.LogicalPath == "riscv_single_cycle_top.u_imem.mem");
        Assert.Equal(GateMemoryMappingKind.LoweredElements, instructionMemory.Kind);

        await using SimulationWorkerClient client = new(build.Worker.ExecutablePath);
        string[] program =
        [
            "0x00500093", // addi x1, x0, 5
            "0x00300113", // addi x2, x0, 3
            "0x002081b3", // add  x3, x1, x2
            "0x00100073", // ebreak
        ];

        await client.StepAsync(
            new SimulationCommand(SimulationCommandType.Reset),
            CancellationToken.None);
        await client.StepAsync(
            new SimulationCommand(SimulationCommandType.SetInput, "enable", "0"),
            CancellationToken.None);
        for (int address = 0; address < program.Length; address++)
        {
            await client.WriteMemoryAsync(
                instructionMemory.LogicalPath,
                (ulong)address,
                program[address],
                CancellationToken.None);
        }

        await client.StepAsync(
            new SimulationCommand(SimulationCommandType.SetInput, "enable", "1"),
            CancellationToken.None);
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

    private static string Sample(SimulationFrame frame, string signal) =>
        Assert.Single(frame.Signals, sample => sample.Signal == signal).Value;

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Bistable.slnx")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static bool ToolAvailable(string executable, string arguments)
    {
        try
        {
            using Process p = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            })!;
            p.WaitForExit(2000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
