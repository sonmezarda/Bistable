using Bistable.Core.Design.Ast;
using Bistable.Core.Design.Schematic;
using Bistable.Core.Projects;
using Bistable.Verilator;

namespace Bistable.Tests.Schematic;

public sealed class SampleCoverageTests
{
    public static IEnumerable<object[]> SampleProjects =>
    [
        ["arnicomp", "arnicomp.bistable.json"],
        ["tiny_cpu", "tiny_cpu.bistable.json"],
        ["bus_fabric", "bus_fabric.bistable.json"],
        ["memory_demo", "memory_demo.bistable.json"],
        ["riscv_single_cycle", "riscv_single_cycle.bistable.json"],
    ];

    [Fact]
    public void Arnicomp_MetadataXml_HasNoSilentSchematicCoverageMisses()
    {
        VerilatorXmlAstReader reader = new();
        DesignAst design = ReadArnicompMetadataXml(reader);

        SchematicCoverageReport report = SchematicCoverageAnalyzer.Analyze(design);

        Assert.Empty(reader.LastDiagnostics);
        Assert.True(
            report.SilentMissCount == 0,
            "Silent misses:\n" + FormatEndpoints(report, EndpointCoverageStatus.SilentMiss));
    }

    [Theory]
    [MemberData(nameof(SampleProjects))]
    [Trait("Category", "Integration")]
    public async Task SampleProject_GeneratedXml_HasNoSilentSchematicCoverageMisses(
        string sampleName,
        string projectFileName)
    {
        DesignAst design = await ReadGeneratedSampleXmlAsync(sampleName, projectFileName);

        SchematicCoverageReport report = SchematicCoverageAnalyzer.Analyze(design);

        Assert.All(report.Modules, module => Assert.True(module.ExpectedEndpointCount > 0));
        Assert.True(
            report.SilentMissCount == 0,
            $"{sampleName} silent misses:\n" + FormatEndpoints(report, EndpointCoverageStatus.SilentMiss));
    }

    [Theory]
    [MemberData(nameof(SampleProjects))]
    [Trait("Category", "Integration")]
    public async Task SampleProject_CoverageReport_WritesReadableJsonArtifact(
        string sampleName,
        string projectFileName)
    {
        DesignAst design = await ReadGeneratedSampleXmlAsync(sampleName, projectFileName);
        SchematicCoverageReport report = SchematicCoverageAnalyzer.Analyze(design);
        string reportPath = Path.Combine(
            Path.GetTempPath(),
            "bistable-coverage-tests",
            sampleName,
            $"{report.TopModule}.schematic-coverage.json");

        try
        {
            await SchematicCoverageReportJson.WriteAsync(reportPath, report, CancellationToken.None);

            string json = await File.ReadAllTextAsync(reportPath, CancellationToken.None);
            Assert.Contains("\"topModule\"", json, StringComparison.Ordinal);
            Assert.Contains("\"modules\"", json, StringComparison.Ordinal);
            Assert.Contains("\"status\"", json, StringComparison.Ordinal);

            SchematicCoverageReport roundTripped = SchematicCoverageReportJson.Deserialize(json);
            Assert.Equal(report.TopModule, roundTripped.TopModule);
            Assert.Equal(report.Modules.Count, roundTripped.Modules.Count);
            Assert.Equal(report.SilentMissCount, roundTripped.SilentMissCount);
        }
        finally
        {
            File.Delete(reportPath);
        }
    }

    private static DesignAst ReadArnicompMetadataXml(VerilatorXmlAstReader reader)
    {
        string sourcePath = ResolveRepoFile("samples/arnicomp/.bistable/metadata/arnicomp_top.xml");
        for (int attempt = 0; attempt < 10; attempt++)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), $"bistable-arnicomp-{Guid.NewGuid():N}.xml");
            try
            {
                File.Copy(sourcePath, tempPath);
                return reader.Read(tempPath);
            }
            catch (System.Xml.XmlException) when (attempt < 9)
            {
                Thread.Sleep(50);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        throw new InvalidOperationException("Unreachable arnicomp metadata read retry state.");
    }

    private static async Task<DesignAst> ReadGeneratedSampleXmlAsync(string sampleName, string projectFileName)
    {
        string sampleDirectory = ResolveRepoDirectory(Path.Combine("samples", sampleName));
        string projectPath = Path.Combine(sampleDirectory, projectFileName);
        ProjectConfiguration configuration = await ProjectConfiguration.LoadAsync(projectPath, CancellationToken.None);
        string outputXmlPath = Path.Combine(
            Path.GetTempPath(),
            $"bistable-{sampleName}-{configuration.TopModule}-{Guid.NewGuid():N}.xml");

        try
        {
            VerilatorTool tool = new();
            await tool.GenerateXmlAsync(configuration, sampleDirectory, outputXmlPath, CancellationToken.None);

            VerilatorXmlAstReader reader = new();
            return reader.Read(outputXmlPath);
        }
        finally
        {
            File.Delete(outputXmlPath);
        }
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

    private static string ResolveRepoDirectory(string relativePath)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, relativePath);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate repo directory '{relativePath}'.");
    }
}
