using System.Text.Json;
using Bistable.Core.Projects;

namespace Bistable.Tests;

public sealed class ProjectConfigurationTests
{
    [Fact]
    public void DeserializesProjectConfiguration()
    {
        const string json = """
        {
          "topModule": "alu",
          "sources": ["alu.sv"],
          "parameters": { "W": "8" },
          "clocks": [{ "name": "clk", "defaultPeriodNs": 10 }]
        }
        """;

        ProjectConfiguration? configuration = JsonSerializer.Deserialize<ProjectConfiguration>(
            json,
            ProjectConfiguration.JsonOptions);

        Assert.NotNull(configuration);
        Assert.Equal("alu", configuration.TopModule);
        Assert.Equal("alu.sv", configuration.Sources.Single());
        Assert.Equal("8", configuration.Parameters["W"]);
        Assert.Equal("clk", configuration.Clocks.Single().Name);
    }

    [Fact]
    public void ValidatorReportsMissingSource()
    {
        ProjectConfiguration configuration = new()
        {
            TopModule = "alu",
            Sources = ["missing.sv"]
        };

        IReadOnlyList<string> errors = new ProjectConfigurationValidator().Validate(configuration, Path.GetTempPath());

        Assert.Contains(errors, static error => error.Contains("Source file does not exist", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatorRejectsUnsupportedTraceFormat()
    {
        ProjectConfiguration configuration = new()
        {
            TopModule = "alu",
            Sources = ["/tmp/alu.sv"],
            Trace = new TraceConfiguration(true, "fst", 2)
        };

        IReadOnlyList<string> errors = new ProjectConfigurationValidator().Validate(configuration);

        Assert.Contains(errors, static error => error.Contains("Trace format", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BundledSampleConfigurationsValidate()
    {
        string root = FindRepositoryRoot();
        string samplesRoot = Path.Combine(root, "samples");
        string[] sampleConfigs = Directory.GetFiles(samplesRoot, "*.bistable.json", SearchOption.AllDirectories);

        Assert.NotEmpty(sampleConfigs);
        foreach (string sampleConfig in sampleConfigs)
        {
            ProjectConfiguration configuration = await ProjectConfiguration.LoadAsync(sampleConfig, CancellationToken.None);
            IReadOnlyList<string> errors = new ProjectConfigurationValidator().Validate(configuration, Path.GetDirectoryName(sampleConfig));

            Assert.True(errors.Count == 0, $"{sampleConfig}: {string.Join(", ", errors)}");
        }
    }

    private static string FindRepositoryRoot()
    {
        string directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "Bistable.slnx")))
            {
                return directory;
            }

            DirectoryInfo? parent = Directory.GetParent(directory);
            if (parent is null)
            {
                break;
            }

            directory = parent.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate Bistable.slnx.");
    }
}
