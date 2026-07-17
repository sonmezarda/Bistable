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
