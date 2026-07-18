using Bistable.Core.Design.Schematic;
using Bistable.Engine;

namespace Bistable.Regression;

/// <summary>
/// Regression for the Theia RTL schematic where long generated net identities
/// were painted on both sides of symbols and covered each other.
/// </summary>
public sealed class TheiaSchematicPinLabelRegressionTests
{
    [Fact]
    public void Mux_UsesSemanticDisplayPinsWithoutLosingExactGeneratedSignals()
    {
        MuxPrimitive mux = new(
            "mux:branch",
            "branch_taken",
            ["__schematic_expr_select_42"],
            [
                new MuxInput("1", new MuxSignalSource("__schematic_expr_true_42")),
                new MuxInput("0", new MuxSignalSource("alu_zero"))
            ],
            1);
        SchematicPrimitiveList primitives = new("top", [], [], [], [mux]);

        EngineSchematicNode node = Assert.Single(
            new EngineSchematicProjectionService().Project(primitives).Nodes,
            static candidate => candidate.Kind == "Mux");

        Assert.Equal(
            ["__schematic_expr_select_42", "__schematic_expr_true_42", "alu_zero"],
            node.Inputs);
        Assert.Equal(["S", "I0", "I1"], node.InputLabels);
        Assert.Equal(["Y"], node.OutputLabels);
        Assert.All(node.InputLabels!, static label => Assert.DoesNotContain("__schematic", label));
    }
}
