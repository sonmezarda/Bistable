using Bistable.App.Services;
using Bistable.Core.Design;
using Bistable.Core.Projects;

namespace Bistable.Tests;

public sealed class SubSimulationConfigurationResolverTests
{
    [Fact]
    public void Resolve_ParameterizedElaboratedModule_UsesOriginalTopAndElaboratedParameters()
    {
        ProjectConfiguration baseConfiguration = new()
        {
            TopModule = "arnicomp_top",
            Sources = ["reg_cell.sv"],
            Parameters = new Dictionary<string, string> { ["TOP_ONLY"] = "99" }
        };
        ModuleMetadata module = new(
            "reg_cell__W4",
            [new SignalPort("d", SignalDirection.Input, 4, false, 1)],
            [
                new DesignParameter("W", "32'sh4"),
                new DesignParameter("RESET_VALUE", "4'h0")
            ],
            OriginalName: "reg_cell");

        SubSimulationConfiguration result =
            SubSimulationConfigurationResolver.Resolve(baseConfiguration, module);

        Assert.Equal("reg_cell__W4", result.RequestedModuleName);
        Assert.Equal("reg_cell", result.BuildTopModule);
        Assert.Equal("reg_cell", result.Project.TopModule);
        Assert.Equal("4", result.Project.Parameters["W"]);
        Assert.Equal("0", result.Project.Parameters["RESET_VALUE"]);
        Assert.DoesNotContain("TOP_ONLY", result.Project.Parameters.Keys);
    }

    [Theory]
    [InlineData("32'sh4", "4")]
    [InlineData("8'hff", "255")]
    [InlineData("8'shff", "-1")]
    [InlineData("4'b1010", "10")]
    [InlineData("16", "16")]
    [InlineData("4'hx", "4'hx")]
    public void NormalizeVerilatorParameterValue_ParsesIntegerLiterals(string input, string expected)
    {
        Assert.Equal(expected, SubSimulationConfigurationResolver.NormalizeVerilatorParameterValue(input));
    }
}
