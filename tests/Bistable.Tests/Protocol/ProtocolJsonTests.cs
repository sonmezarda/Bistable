using Bistable.Protocol;

namespace Bistable.Tests.Protocol;

/// <summary>
/// Round-trip JSON serialization for the worker IPC contract.
/// Commands are sent GUI → worker; <see cref="WorkerResponse"/> subtypes come
/// back. Tests cover every command type and every response subtype.
/// </summary>
public sealed class ProtocolJsonTests
{
    // ── Command type discriminator ───────────────────────────────────────

    [Theory]
    [InlineData(SimulationCommandType.Hello,        "hello")]
    [InlineData(SimulationCommandType.SetInput,     "setInput")]
    [InlineData(SimulationCommandType.Eval,         "eval")]
    [InlineData(SimulationCommandType.Tick,         "tick")]
    [InlineData(SimulationCommandType.RunCycles,    "runCycles")]
    [InlineData(SimulationCommandType.Reset,        "reset")]
    [InlineData(SimulationCommandType.GetSnapshot,  "getSnapshot")]
    [InlineData(SimulationCommandType.Pause,        "pause")]
    [InlineData(SimulationCommandType.ReadSignal,   "readSignal")]
    [InlineData(SimulationCommandType.ReadSignals,  "readSignals")]
    [InlineData(SimulationCommandType.WriteSignal,  "writeSignal")]
    [InlineData(SimulationCommandType.ForceSignal,  "forceSignal")]
    [InlineData(SimulationCommandType.ReleaseSignal,"releaseSignal")]
    [InlineData(SimulationCommandType.ReadMemory,   "readMemory")]
    [InlineData(SimulationCommandType.WriteMemory,  "writeMemory")]
    [InlineData(SimulationCommandType.ListProbes,   "listProbes")]
    public void Command_SerializesTypeAsCamelCaseString(SimulationCommandType type, string expectedJsonLiteral)
    {
        SimulationCommand command = new(type);

        string json = ProtocolJson.Serialize(command);
        SimulationCommand? restored = ProtocolJson.Deserialize<SimulationCommand>(json);

        Assert.Contains($"\"type\":\"{expectedJsonLiteral}\"", json, StringComparison.Ordinal);
        Assert.NotNull(restored);
        Assert.Equal(type, restored.Type);
    }

    // ── Command payloads ─────────────────────────────────────────────────

    [Fact]
    public void SetInputCommand_RoundTripsSignalAndValue()
    {
        SimulationCommand command = new(
            SimulationCommandType.SetInput,
            Signal: "instruction",
            Value: "0x81");

        SimulationCommand? restored = ProtocolJson.Deserialize<SimulationCommand>(ProtocolJson.Serialize(command));

        Assert.NotNull(restored);
        Assert.Equal("instruction", restored.Signal);
        Assert.Equal("0x81", restored.Value);
    }

    [Fact]
    public void RunCyclesCommand_RoundTripsCycles()
    {
        SimulationCommand command = new(SimulationCommandType.RunCycles, Signal: "clk", Cycles: 16);

        SimulationCommand? restored = ProtocolJson.Deserialize<SimulationCommand>(ProtocolJson.Serialize(command));

        Assert.NotNull(restored);
        Assert.Equal(16, restored.Cycles);
        Assert.Equal("clk", restored.Signal);
    }

    [Fact]
    public void ReadSignalCommand_RoundTripsHierarchyPath()
    {
        SimulationCommand command = new(SimulationCommandType.ReadSignal, Path: "arnicomp_top.acc.q");

        SimulationCommand? restored = ProtocolJson.Deserialize<SimulationCommand>(ProtocolJson.Serialize(command));

        Assert.NotNull(restored);
        Assert.Equal("arnicomp_top.acc.q", restored.Path);
    }

    [Fact]
    public void ReadSignalsCommand_RoundTripsHierarchyPaths()
    {
        SimulationCommand command = new(
            SimulationCommandType.ReadSignals,
            Paths: ["top.a", "top.u_core.result"]);

        SimulationCommand? restored = ProtocolJson.Deserialize<SimulationCommand>(ProtocolJson.Serialize(command));

        Assert.NotNull(restored);
        Assert.Equal(new[] { "top.a", "top.u_core.result" }, restored.Paths);
    }

    [Fact]
    public void ForceSignalCommand_RoundTripsPathAndValue()
    {
        SimulationCommand command = new(SimulationCommandType.ForceSignal, Path: "top.reg_a.q", Value: "0x42");

        SimulationCommand? restored = ProtocolJson.Deserialize<SimulationCommand>(ProtocolJson.Serialize(command));

        Assert.NotNull(restored);
        Assert.Equal("top.reg_a.q", restored.Path);
        Assert.Equal("0x42", restored.Value);
    }

    [Fact]
    public void ReadMemoryCommand_RoundTripsAddressAndCount()
    {
        SimulationCommand command = new(
            SimulationCommandType.ReadMemory,
            Path: "top.mem",
            MemoryAddress: 4,
            MemoryCount: 8);

        SimulationCommand? restored = ProtocolJson.Deserialize<SimulationCommand>(ProtocolJson.Serialize(command));

        Assert.NotNull(restored);
        Assert.Equal("top.mem", restored.Path);
        Assert.Equal((ulong?)4, restored.MemoryAddress);
        Assert.Equal(8, restored.MemoryCount);
    }

    // ── WorkerResponse subtypes: discriminator round-trip ────────────────

    [Fact]
    public void SimulationFrame_RoundTripsAsKindFrame()
    {
        SimulationFrame frame = new(
            Time: 7,
            Signals: [new SignalSample("out", "0x42", 7)]);

        string json = ProtocolJson.Serialize<WorkerResponse>(frame);
        WorkerResponse? restored = ProtocolJson.Deserialize<WorkerResponse>(json);

        Assert.Contains("\"kind\":\"frame\"", json, StringComparison.Ordinal);
        SimulationFrame asFrame = Assert.IsType<SimulationFrame>(restored);
        Assert.Equal((ulong)7, asFrame.Time);
        Assert.Single(asFrame.Signals);
    }

    [Fact]
    public void SimulationFrame_DeserializesTrace()
    {
        const string json = """
            {
              "kind": "frame",
              "time": 2,
              "signals": [ { "signal": "count", "value": "2", "time": 2 } ],
              "trace": [
                { "signal": "clk", "value": "1", "time": 1 },
                { "signal": "clk", "value": "0", "time": 2 }
              ]
            }
            """;

        WorkerResponse? restored = ProtocolJson.Deserialize<WorkerResponse>(json);

        SimulationFrame frame = Assert.IsType<SimulationFrame>(restored);
        Assert.Equal((ulong)2, frame.Time);
        Assert.NotNull(frame.Trace);
        Assert.Collection(frame.Trace!,
            sample => { Assert.Equal("clk", sample.Signal); Assert.Equal("1", sample.Value); Assert.Equal((ulong)1, sample.Time); },
            sample => { Assert.Equal("clk", sample.Signal); Assert.Equal("0", sample.Value); Assert.Equal((ulong)2, sample.Time); });
    }

    [Fact]
    public void SignalReadResponse_RoundTripsAsKindSignalRead()
    {
        SignalReadResponse response = new(new SignalReadResult("top.q", "0xA2", 8, false));

        string json = ProtocolJson.Serialize<WorkerResponse>(response);
        WorkerResponse? restored = ProtocolJson.Deserialize<WorkerResponse>(json);

        Assert.Contains("\"kind\":\"signalRead\"", json, StringComparison.Ordinal);
        SignalReadResponse asRead = Assert.IsType<SignalReadResponse>(restored);
        Assert.Equal("top.q", asRead.Result.Path);
        Assert.Equal("0xA2", asRead.Result.Value);
        Assert.Equal(8, asRead.Result.Width);
    }

    [Fact]
    public void SignalsReadResponse_RoundTripsMixedPerPathOutcomes()
    {
        SignalsReadResponse response = new(new SignalsReadResult([
            new SignalReadOutcome("top.a", "0x1", 1, false, null),
            new SignalReadOutcome("top.missing", null, 0, false, "unknown probe path: top.missing"),
        ]));

        string json = ProtocolJson.Serialize<WorkerResponse>(response);
        WorkerResponse? restored = ProtocolJson.Deserialize<WorkerResponse>(json);

        Assert.Contains("\"kind\":\"signalsRead\"", json, StringComparison.Ordinal);
        SignalsReadResponse batch = Assert.IsType<SignalsReadResponse>(restored);
        Assert.Collection(batch.Result.Results,
            first => { Assert.True(first.IsSuccess); Assert.Equal("0x1", first.Value); },
            second => { Assert.False(second.IsSuccess); Assert.Contains("unknown probe", second.Error); });
    }

    [Fact]
    public void WorkerHelloResponse_RoundTripsProtocolVersionAndCapabilities()
    {
        WorkerHelloResponse response = new(
            WorkerProtocol.CurrentVersion,
            [WorkerProtocol.ReadSignalsCapability]);

        string json = ProtocolJson.Serialize<WorkerResponse>(response);
        WorkerResponse? restored = ProtocolJson.Deserialize<WorkerResponse>(json);

        Assert.Contains("\"kind\":\"hello\"", json, StringComparison.Ordinal);
        WorkerHelloResponse hello = Assert.IsType<WorkerHelloResponse>(restored);
        Assert.Equal(3, hello.ProtocolVersion);
        Assert.Contains("readSignals", hello.Capabilities);
    }

    [Fact]
    public void MemoryReadResponse_RoundTripsCells()
    {
        MemoryReadResponse response = new(
            new MemoryReadResult("memory_demo.mem", 0, 8, ["0x00", "0xA2", "0x55", "0xFF"]));

        string json = ProtocolJson.Serialize<WorkerResponse>(response);
        WorkerResponse? restored = ProtocolJson.Deserialize<WorkerResponse>(json);

        Assert.Contains("\"kind\":\"memoryRead\"", json, StringComparison.Ordinal);
        MemoryReadResponse asMem = Assert.IsType<MemoryReadResponse>(restored);
        Assert.Equal(new[] { "0x00", "0xA2", "0x55", "0xFF" }, asMem.Result.Cells);
    }

    [Fact]
    public void ProbeListResponse_RoundTripsProbeArray()
    {
        ProbeListResponse response = new([
            new ProbeDescriptor("top.a",   1, false, false, false, null),
            new ProbeDescriptor("top.mem", 8, false, false, true,  16),
        ]);

        string json = ProtocolJson.Serialize<WorkerResponse>(response);
        WorkerResponse? restored = ProtocolJson.Deserialize<WorkerResponse>(json);

        Assert.Contains("\"kind\":\"probeList\"", json, StringComparison.Ordinal);
        ProbeListResponse asList = Assert.IsType<ProbeListResponse>(restored);
        Assert.Equal(2, asList.Probes.Count);
        Assert.True(asList.Probes[1].IsMemory);
        Assert.Equal(16, asList.Probes[1].MemoryDepth);
    }

    [Fact]
    public void AckResponse_RoundTripsAsKindAck()
    {
        AckResponse response = new();

        string json = ProtocolJson.Serialize<WorkerResponse>(response);
        WorkerResponse? restored = ProtocolJson.Deserialize<WorkerResponse>(json);

        Assert.Contains("\"kind\":\"ack\"", json, StringComparison.Ordinal);
        Assert.IsType<AckResponse>(restored);
    }

    [Fact]
    public void ErrorResponse_RoundTripsMessage()
    {
        ErrorResponse response = new("unknown probe path: top.nonexistent.q");

        string json = ProtocolJson.Serialize<WorkerResponse>(response);
        WorkerResponse? restored = ProtocolJson.Deserialize<WorkerResponse>(json);

        Assert.Contains("\"kind\":\"error\"", json, StringComparison.Ordinal);
        ErrorResponse asErr = Assert.IsType<ErrorResponse>(restored);
        Assert.Equal("unknown probe path: top.nonexistent.q", asErr.Message);
    }

    // ── Payload DTOs (round-trip in isolation) ───────────────────────────

    [Fact]
    public void SignalReadResult_HandlesWideHexValues()
    {
        // 128-bit bus — value too wide for ulong. Must come through as hex string.
        SignalReadResult result = new("top.wide_bus", "0xCAFEBABEDEADBEEF12345678ABCDEF00", 128, false);

        SignalReadResult? restored = ProtocolJson.Deserialize<SignalReadResult>(ProtocolJson.Serialize(result));

        Assert.NotNull(restored);
        Assert.Equal("0xCAFEBABEDEADBEEF12345678ABCDEF00", restored.Value);
        Assert.Equal(128, restored.Width);
    }

    [Fact]
    public void MemoryReadResult_EmptyCellList_RoundTrips()
    {
        MemoryReadResult result = new("top.mem", 16, 8, []);
        MemoryReadResult? restored = ProtocolJson.Deserialize<MemoryReadResult>(ProtocolJson.Serialize(result));

        Assert.NotNull(restored);
        Assert.Empty(restored.Cells);
    }

    [Fact]
    public void ProbeDescriptor_ScalarVsMemory_DiscriminatedByIsMemoryFlag()
    {
        ProbeDescriptor scalar = new("top.q", 8, false, true, false, MemoryDepth: null);
        ProbeDescriptor memory = new("top.mem", 8, false, false, true, MemoryDepth: 16);

        ProbeDescriptor? rScalar = ProtocolJson.Deserialize<ProbeDescriptor>(ProtocolJson.Serialize(scalar));
        ProbeDescriptor? rMemory = ProtocolJson.Deserialize<ProbeDescriptor>(ProtocolJson.Serialize(memory));

        Assert.NotNull(rScalar);
        Assert.False(rScalar.IsMemory);
        Assert.True(rScalar.IsRegistered);
        Assert.Null(rScalar.MemoryDepth);

        Assert.NotNull(rMemory);
        Assert.True(rMemory.IsMemory);
        Assert.Equal(16, rMemory.MemoryDepth);
    }
}
