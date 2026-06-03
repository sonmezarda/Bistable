using Bistable.Core.Design.Ast;
using Bistable.Core.Design.Schematic;
using Bistable.Verilator;

namespace Bistable.Tests.Schematic;

public sealed class SampleCoverageTests
{
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

    [Fact]
    public void Arnicomp_MetadataXml_HasNoUnsupportedSchematicConstructs()
    {
        DesignAst design = ReadArnicompMetadataXml(new VerilatorXmlAstReader());

        SchematicCoverageReport report = SchematicCoverageAnalyzer.Analyze(design);

        Assert.All(report.Modules, module => Assert.True(module.ExpectedEndpointCount > 0));
        Assert.Equal(0, report.UnsupportedCount);
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
}
