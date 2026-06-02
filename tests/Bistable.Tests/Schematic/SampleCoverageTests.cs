using Bistable.Core.Design.Ast;
using Bistable.Core.Design.Schematic;
using Bistable.Verilator;

namespace Bistable.Tests.Schematic;

public sealed class SampleCoverageTests
{
    [Fact]
    public void Arnicomp_MetadataXml_HasNoSilentSchematicCoverageMisses()
    {
        string xmlPath = ResolveRepoFile("samples/arnicomp/.bistable/metadata/arnicomp_top.xml");
        VerilatorXmlAstReader reader = new();
        DesignAst design = reader.Read(xmlPath);

        SchematicCoverageReport report = SchematicCoverageAnalyzer.Analyze(design);

        Assert.Empty(reader.LastDiagnostics);
        Assert.True(
            report.SilentMissCount == 0,
            "Silent misses:\n" + FormatEndpoints(report, EndpointCoverageStatus.SilentMiss));
    }

    [Fact]
    public void Arnicomp_MetadataXml_ReportsCurrentUnsupportedConstructsExplicitly()
    {
        string xmlPath = ResolveRepoFile("samples/arnicomp/.bistable/metadata/arnicomp_top.xml");
        DesignAst design = new VerilatorXmlAstReader().Read(xmlPath);

        SchematicCoverageReport report = SchematicCoverageAnalyzer.Analyze(design);

        Assert.All(report.Modules, module => Assert.True(module.ExpectedEndpointCount > 0));
        Assert.Equal(9, report.UnsupportedCount);
        Assert.Contains(report.UnsupportedConstructs, static d => d.ConstructId == "primitive:ff_flush_next_instr_0:d");
        Assert.Contains(report.UnsupportedConstructs, static d => d.ConstructId == "primitive:mux_mem_addr_1:in.0");
        Assert.Contains(report.UnsupportedConstructs, static d => d.ConstructId == "primitive:mux_mem_addr_1:in.1");
        Assert.Contains(report.UnsupportedConstructs, static d => d.ConstructId == "primitive:mux_mem_wdata_2:in.0");
        Assert.Contains(report.UnsupportedConstructs, static d => d.ConstructId == "primitive:op_mem_ren_3:in.1");
        Assert.Contains(report.UnsupportedConstructs, static d => d.ConstructId == "primitive:mux_bus_4:in.2");
        Assert.Contains(report.UnsupportedConstructs, static d => d.ConstructId == "primitive:op_is_push_instr_7:left");
        Assert.Contains(report.UnsupportedConstructs, static d => d.ConstructId == "primitive:op_is_push_instr_7:right");
        Assert.Contains(report.UnsupportedConstructs, static d => d.ConstructId == "primitive:op_zero_flag_1:left");
    }

    private static string FormatEndpoints(SchematicCoverageReport report, EndpointCoverageStatus status)
    {
        EndpointCoverage[] endpoints = [.. report.Modules
            .SelectMany(static module => module.Endpoints)
            .Where(endpoint => endpoint.Status == status)
            .Take(50)];

        return endpoints.Length == 0
            ? "<none>"
            : string.Join(
                Environment.NewLine,
                endpoints.Select(static endpoint =>
                    $"{endpoint.ModuleName} {endpoint.EndpointId} {endpoint.SignalName}: {endpoint.Reason}"));
    }

    private static string FormatUnsupported(SchematicCoverageReport report) =>
        string.Join(
            Environment.NewLine,
            report.UnsupportedConstructs.Select(static diagnostic =>
                $"{diagnostic.ModuleName} {diagnostic.ConstructId} {diagnostic.ConstructKind}: {diagnostic.Reason}"));

    private static string ResolveRepoFile(string relativePath)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate repo file '{relativePath}'.");
    }
}
