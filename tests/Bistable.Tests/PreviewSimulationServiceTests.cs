using Bistable.App.Services;
using Bistable.App.ViewModels;
using Bistable.Core.Design;

namespace Bistable.Tests;

public sealed class PreviewSimulationServiceTests
{
    [Fact]
    public void EvaluatesBundledAluShape()
    {
        List<SignalViewModel> inputs =
        [
            new(new SignalPort("clk", SignalDirection.Input, 1, false, 1)),
            new(new SignalPort("rst_n", SignalDirection.Input, 1, false, 2)),
            new(new SignalPort("a", SignalDirection.Input, 8, false, 3)) { Value = "0x12" },
            new(new SignalPort("b", SignalDirection.Input, 8, false, 4)) { Value = "0x22" },
            new(new SignalPort("op", SignalDirection.Input, 3, false, 5)) { Value = "0" }
        ];
        List<SignalViewModel> outputs =
        [
            new(new SignalPort("y", SignalDirection.Output, 9, false, 6)),
            new(new SignalPort("zero", SignalDirection.Output, 1, false, 7))
        ];

        PreviewSimulationResult result = new PreviewSimulationService().Evaluate("alu", inputs, outputs);

        Assert.True(result.IsSuccess);
        Assert.Equal("0x034", outputs[0].Value);
        Assert.Equal("0", outputs[1].Value);
    }
}
