using Bistable.App.Services;

namespace Bistable.Tests;

public sealed class VcdTraceReaderTests
{
    [Fact]
    public void LoadsTopLevelAndInternalSignalsFromVcd()
    {
        string vcdPath = Path.Combine(Path.GetTempPath(), $"bistable-vcd-{Guid.NewGuid():N}.vcd");
        File.WriteAllText(vcdPath, """
$date
  today
$end
$version
  bistable test
$end
$timescale 1ns $end
$scope module TOP $end
$scope module system_top $end
$var wire 1 ! clk $end
$var wire 8 " result [7:0] $end
$scope module u_core $end
$var wire 1 # parity_i $end
$upscope $end
$upscope $end
$upscope $end
$enddefinitions $end
#0
0!
b00000000 "
0#
#5
1!
b00000101 "
1#
""");

        try
        {
            VcdTraceDocument document = new VcdTraceReader().Load(vcdPath, "system_top");

            Assert.Contains(document.Signals, static signal => signal.Name == "clk" && signal.IsTopLevel);
            Assert.Contains(document.Signals, static signal => signal.Name == "result" && signal.IsTopLevel);
            Assert.Contains(document.Signals, static signal => signal.Name == "system_top.u_core.parity_i" && !signal.IsTopLevel);
            Assert.True(document.TryGetEvents("result", out IReadOnlyList<VcdTraceEvent>? resultEvents));
            Assert.Equal("0x00", resultEvents[0].Value);
            Assert.Equal("0x05", resultEvents[1].Value);
            Assert.True(document.TryGetEvents("system_top.u_core.parity_i", out IReadOnlyList<VcdTraceEvent>? parityEvents));
            Assert.Equal("1", parityEvents[^1].Value);
        }
        finally
        {
            File.Delete(vcdPath);
        }
    }
}
