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
}
