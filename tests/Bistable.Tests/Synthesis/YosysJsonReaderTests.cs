using Bistable.Core.Synthesis;
using Bistable.Yosys;

namespace Bistable.Tests.Synthesis;

/// <summary>
/// Phase 6 P6-4: assertions against real Yosys 0.33 <c>write_json</c> output.
/// Fixtures under <c>Synthesis/fixtures/</c> were produced from the SV
/// snippets the comments reference; if Yosys's format ever drifts these
/// tests catch it.
/// </summary>
public sealed class YosysJsonReaderTests
{
    private static string LoadFixture(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Synthesis", "fixtures", name));

    [Fact]
    public void Read_And2Sample_ParsesPortsAndSingleAndCell()
    {
        // and2.sv:
        //   module and2(input a, input b, output y);
        //       assign y = a & b;
        //   endmodule
        GateNetlist netlist = YosysJsonReader.Read(LoadFixture("and2.json"));

        Assert.Equal("and2", netlist.TopModule);
        GateModule module = netlist.Modules["and2"];
        Assert.Equal(3, module.Ports.Count);
        Assert.Equal(GatePortDirection.Input,  module.Ports.Single(p => p.Name == "a").Direction);
        Assert.Equal(GatePortDirection.Input,  module.Ports.Single(p => p.Name == "b").Direction);
        Assert.Equal(GatePortDirection.Output, module.Ports.Single(p => p.Name == "y").Direction);

        GateCell andCell = Assert.Single(module.Cells);
        Assert.Equal("$_AND_", andCell.Type);
        Assert.Equal(3, andCell.PortDirections.Count);
        Assert.Equal(GatePortDirection.Output, andCell.PortDirections["Y"]);
        Assert.Equal(GatePortDirection.Input,  andCell.PortDirections["A"]);

        GateConnection y = andCell.Connections["Y"];
        GateBit yBit = Assert.Single(y.Bits);
        Assert.Equal(BitKind.Net, yBit.Kind);
        Assert.True(yBit.NetId >= 2, "Yosys net ids are >= 2 (0/1 reserved for constants).");
    }

    [Fact]
    public void Read_DffBus_ParsesWideBusAsOrderedBits()
    {
        // dff_bus.sv:
        //   module dff_bus(input clk, input [3:0] d, output reg [3:0] q);
        //       always @(posedge clk) q <= d;
        GateNetlist netlist = YosysJsonReader.Read(LoadFixture("dff_bus.json"));

        GateModule module = netlist.Modules["dff_bus"];
        GatePort d = module.Ports.Single(p => p.Name == "d");
        GatePort q = module.Ports.Single(p => p.Name == "q");
        Assert.Equal(4, d.Bits.Count);
        Assert.Equal(4, q.Bits.Count);
        Assert.All(d.Bits, b => Assert.Equal(BitKind.Net, b.Kind));

        // techmap lowers the bus reg into N individual $_DFF_P_ cells.
        Assert.Equal(4, module.Cells.Count);
        Assert.All(module.Cells, c => Assert.Equal("$_DFF_P_", c.Type));
    }

    [Fact]
    public void Read_WithConst_DistinguishesConstantBitFromNet()
    {
        // with_const.sv:
        //   module with_const(input a, output [1:0] y);
        //       assign y = {1'b0, a};   // y[1] = 0, y[0] = a
        GateNetlist netlist = YosysJsonReader.Read(LoadFixture("with_const.json"));

        GateModule module = netlist.Modules["with_const"];
        GatePort y = module.Ports.Single(p => p.Name == "y");
        Assert.Equal(2, y.Bits.Count);

        // First bit is a real net (a), second is the literal 0.
        Assert.Equal(BitKind.Net,          y.Bits[0].Kind);
        Assert.Equal(BitKind.ConstantZero, y.Bits[1].Kind);
    }

    [Fact]
    public void Read_Mux2_ProducesMuxOrDecomposedCells()
    {
        // mux2.sv:
        //   module mux2(input sel, input a, input b, output y);
        //       assign y = sel ? b : a;
        GateNetlist netlist = YosysJsonReader.Read(LoadFixture("mux2.json"));

        GateModule module = netlist.Modules["mux2"];
        Assert.NotEmpty(module.Cells);
        // After techmap a ternary lowers either to a $_MUX_ cell or to a
        // small combinational tree; assert at least one recognisable gate is
        // present and every cell type starts with $_ (generic library).
        Assert.All(module.Cells, c => Assert.StartsWith("$_", c.Type, StringComparison.Ordinal));
    }

    [Fact]
    public void Read_NetnamesArePreservedForUserSignals()
    {
        GateNetlist netlist = YosysJsonReader.Read(LoadFixture("and2.json"));
        GateModule module = netlist.Modules["and2"];

        // User wrote a/b/y, all three must appear in the net list.
        Assert.Contains(module.Nets, n => n.Name == "a");
        Assert.Contains(module.Nets, n => n.Name == "b");
        Assert.Contains(module.Nets, n => n.Name == "y");
    }

    [Fact]
    public void Read_ThrowsOnMissingModulesField()
    {
        Assert.Throws<InvalidDataException>(() =>
            YosysJsonReader.Read("""{ "creator": "fake" }"""));
    }

    [Fact]
    public void Read_TopModule_FallsBackWhenNoneFlagged()
    {
        // Single module without the "top" attribute — reader should still
        // pick it as the implicit top.
        string json = """
            {
              "creator": "fake",
              "modules": {
                "only_one": {
                  "ports": {},
                  "cells": {},
                  "netnames": {}
                }
              }
            }
            """;
        GateNetlist netlist = YosysJsonReader.Read(json);
        Assert.Equal("only_one", netlist.TopModule);
    }
}
