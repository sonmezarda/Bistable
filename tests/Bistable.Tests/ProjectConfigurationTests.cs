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

        IReadOnlyList<string> errors = new ProjectConfigurationValidator().Validate(configuration, "/tmp");

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
}
