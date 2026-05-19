using Bistable.Protocol;

namespace Bistable.Tests;

public sealed class ProtocolJsonTests
{
    [Fact]
    public void SerializesCommandWithStringEnum()
    {
        SimulationCommand command = new(SimulationCommandType.RunCycles, Cycles: 16);

        string json = ProtocolJson.Serialize(command);
        SimulationCommand? restored = ProtocolJson.Deserialize<SimulationCommand>(json);

        Assert.Contains("\"type\":\"runCycles\"", json, StringComparison.Ordinal);
        Assert.NotNull(restored);
        Assert.Equal(SimulationCommandType.RunCycles, restored.Type);
        Assert.Equal(16, restored.Cycles);
    }

    [Fact]
    public void DeserializesSnapshotTrace()
    {
        const string json = """
            {
              "time": 2,
              "signals": [
                { "signal": "count", "value": "2", "time": 2 }
              ],
              "trace": [
                { "signal": "clk", "value": "1", "time": 1 },
                { "signal": "clk", "value": "0", "time": 2 }
              ]
            }
            """;

        SimulationSnapshot? snapshot = ProtocolJson.Deserialize<SimulationSnapshot>(json);

        Assert.NotNull(snapshot);
        Assert.Equal((ulong)2, snapshot.Time);
        Assert.NotNull(snapshot.Trace);
        Assert.Collection(
            snapshot.Trace!,
            sample =>
            {
                Assert.Equal("clk", sample.Signal);
                Assert.Equal("1", sample.Value);
                Assert.Equal((ulong)1, sample.Time);
            },
            sample =>
            {
                Assert.Equal("clk", sample.Signal);
                Assert.Equal("0", sample.Value);
                Assert.Equal((ulong)2, sample.Time);
            });
    }
}
