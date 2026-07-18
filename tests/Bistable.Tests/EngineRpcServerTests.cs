using System.Text.Json;
using Bistable.Engine;
using Bistable.EngineHost;

namespace Bistable.Tests;

public sealed class EngineRpcServerTests
{
    [Fact]
    public async Task RunAsync_HelloThenShutdown_UsesVersionedJsonLines()
    {
        StringReader input = new("""
            {"id":"one","method":"hello","params":{}}
            {"id":"two","method":"shutdown","params":{}}
            """);
        StringWriter output = new();

        await new EngineRpcServer(new DesignElaborationService())
            .RunAsync(input, output, CancellationToken.None);

        string[] lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        using JsonDocument hello = JsonDocument.Parse(lines[0]);
        Assert.Equal("one", hello.RootElement.GetProperty("id").GetString());
        Assert.Equal(EngineRpcProtocol.Version, hello.RootElement.GetProperty("result").GetProperty("protocolVersion").GetInt32());
        Assert.Contains("project.load", hello.RootElement.GetProperty("result").GetProperty("capabilities")
            .EnumerateArray().Select(static value => value.GetString()));
        using JsonDocument shutdown = JsonDocument.Parse(lines[1]);
        Assert.True(shutdown.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean());
    }

    [Fact]
    public async Task RunAsync_UnknownMethod_ReturnsStructuredError()
    {
        StringReader input = new("""
            {"id":"bad","method":"missing","params":{}}
            """);
        StringWriter output = new();

        await new EngineRpcServer(new DesignElaborationService())
            .RunAsync(input, output, CancellationToken.None);

        using JsonDocument response = JsonDocument.Parse(output.ToString());
        Assert.Equal("method_not_found", response.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RunAsync_LoadRiscvProject_ReturnsElaboratedSummary()
    {
        string root = FindRepositoryRoot();
        string projectPath = Path.Combine(root, "samples", "riscv_single_cycle", "riscv_single_cycle.bistable.json");
        StringReader input = new(JsonSerializer.Serialize(new
        {
            id = "load",
            method = "loadProject",
            @params = new { projectPath }
        }) + Environment.NewLine);
        StringWriter output = new();

        await new EngineRpcServer(new DesignElaborationService())
            .RunAsync(input, output, CancellationToken.None);

        using JsonDocument response = JsonDocument.Parse(output.ToString());
        JsonElement result = response.RootElement.GetProperty("result");
        Assert.Equal("riscv_single_cycle_top", result.GetProperty("topModule").GetString());
        Assert.True(result.GetProperty("moduleCount").GetInt32() > 1);
        Assert.NotEmpty(result.GetProperty("ports").EnumerateArray());
        JsonElement schematic = result.GetProperty("schematic");
        Assert.Equal("riscv_single_cycle_top", schematic.GetProperty("moduleName").GetString());
        Assert.Contains(
            schematic.GetProperty("nodes").EnumerateArray(),
            static node => node.GetProperty("label").GetString() == "u_alu");
        Assert.NotEmpty(schematic.GetProperty("edges").EnumerateArray());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RunAsync_InvalidHdl_ReturnsClickableStructuredDiagnostics()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"bistable-engine-diagnostics-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string sourcePath = Path.Combine(directory, "top.sv");
            string projectPath = Path.Combine(directory, "top.bistable.json");
            await File.WriteAllTextAsync(sourcePath, """
                module top(output logic value);
                  always_comb value = ;
                endmodule
                """);
            await File.WriteAllTextAsync(projectPath, """
                {
                  "topModule": "top",
                  "sources": ["top.sv"],
                  "trace": { "enabled": false, "format": "vcd", "depth": 1 }
                }
                """);
            StringReader input = new(JsonSerializer.Serialize(new
            {
                id = "invalid-hdl",
                method = "loadProject",
                @params = new { projectPath }
            }) + Environment.NewLine);
            StringWriter output = new();

            await new EngineRpcServer(new DesignElaborationService())
                .RunAsync(input, output, CancellationToken.None);

            using JsonDocument response = JsonDocument.Parse(output.ToString());
            JsonElement error = response.RootElement.GetProperty("error");
            Assert.Equal("elaboration_failed", error.GetProperty("code").GetString());
            JsonElement diagnostic = Assert.Single(error.GetProperty("data").GetProperty("diagnostics").EnumerateArray());
            Assert.Equal("Error", diagnostic.GetProperty("severity").GetString());
            Assert.Equal(Path.GetFullPath(sourcePath), diagnostic.GetProperty("filePath").GetString());
            Assert.True(diagnostic.GetProperty("line").GetInt32() > 0);
            Assert.True(diagnostic.GetProperty("column").GetInt32() > 0);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_Hello_AdvertisesProtocolV2AndSimulationCapabilities()
    {
        StringReader input = new("""{"id":"one","method":"hello","params":{}}""");
        StringWriter output = new();

        await new EngineRpcServer(new DesignElaborationService())
            .RunAsync(input, output, CancellationToken.None);

        using JsonDocument hello = JsonDocument.Parse(output.ToString());
        JsonElement result = hello.RootElement.GetProperty("result");
        // A v1-only frontend guards on this and must reject the v2 host.
        Assert.Equal(2, result.GetProperty("protocolVersion").GetInt32());
        string?[] capabilities = result.GetProperty("capabilities").EnumerateArray()
            .Select(static value => value.GetString()).ToArray();
        Assert.Contains("simulation.start", capabilities);
        Assert.Contains("simulation.readSignals", capabilities);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RunAsync_SimulationLifecycle_DrivesInputAndBatchReadsOverRpc()
    {
        string root = FindRepositoryRoot();
        string projectPath = Path.Combine(root, "samples", "counter", "counter.bistable.json");
        string commands = string.Join(Environment.NewLine,
            Line("start", "simulation.start", new { projectPath }),
            Line("release", "simulation.setInput", new { signal = "rst_n", value = "1" }),
            Line("drive", "simulation.setInput", new { signal = "enable", value = "1" }),
            Line("tick", "simulation.tick", new { clock = "clk" }),
            Line("read", "simulation.readSignals", new { paths = new[] { "counter.count", "counter.enable" } }),
            Line("stop", "simulation.stop", new { }),
            Line("bye", "shutdown", new { }));
        StringWriter output = new();

        await new EngineRpcServer(new DesignElaborationService())
            .RunAsync(new StringReader(commands), output, CancellationToken.None);

        string[] lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        using JsonDocument start = JsonDocument.Parse(lines[0]);
        Assert.Equal("counter", start.RootElement.GetProperty("result").GetProperty("topModule").GetString());

        using JsonDocument tick = JsonDocument.Parse(lines[3]);
        JsonElement count = tick.RootElement.GetProperty("result").GetProperty("signals")
            .EnumerateArray().Single(s => s.GetProperty("signal").GetString() == "count");
        Assert.Equal("1", count.GetProperty("value").GetString());

        using JsonDocument read = JsonDocument.Parse(lines[4]);
        Assert.Equal(2, read.RootElement.GetProperty("result").GetProperty("results").GetArrayLength());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RunAsync_SetInputBadValue_ReturnsInvalidValueError()
    {
        string root = FindRepositoryRoot();
        string projectPath = Path.Combine(root, "samples", "counter", "counter.bistable.json");
        string commands = string.Join(Environment.NewLine,
            Line("start", "simulation.start", new { projectPath }),
            Line("drive", "simulation.setInput", new { signal = "enable", value = "2" }),
            Line("bye", "shutdown", new { }));
        StringWriter output = new();

        await new EngineRpcServer(new DesignElaborationService())
            .RunAsync(new StringReader(commands), output, CancellationToken.None);

        string[] lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        using JsonDocument drive = JsonDocument.Parse(lines[1]);
        Assert.Equal("invalid_value", drive.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private static string Line(string id, string method, object parameters) =>
        JsonSerializer.Serialize(new { id, method, @params = parameters });

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
