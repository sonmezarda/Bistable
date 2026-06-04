using Bistable.Core.Design.Schematic;
using Bistable.Yosys;

namespace Bistable.Tests.Synthesis;

/// <summary>
/// Phase 6 P6-5: every Yosys cell type we claim to support must resolve to a
/// descriptor with the right symbol family + pin role assignments. If a cell
/// type silently drops to <see cref="GateCellShape.Unknown"/>, the gate-level
/// schematic falls back to a generic block — these tests pin the supported
/// surface so a regression there is loud.
/// </summary>
public sealed class GateCellLibraryTests
{
    [Theory]
    [InlineData("$_AND_",  GateKind.And)]
    [InlineData("$_OR_",   GateKind.Or)]
    [InlineData("$_XOR_",  GateKind.Xor)]
    [InlineData("$_NAND_", GateKind.Nand)]
    [InlineData("$_NOR_",  GateKind.Nor)]
    [InlineData("$_XNOR_", GateKind.Xnor)]
    public void Gates_HaveTwoInputsAndOutputY(string cellType, GateKind expectedKind)
    {
        GateCellDescriptor d = GateCellLibrary.Lookup(cellType);
        Assert.Equal(GateCellShape.Gate, d.Shape);
        Assert.Equal(expectedKind, d.GateKind);
        Assert.Equal(new[] { "A", "B" }, d.Inputs);
        Assert.Equal("Y", d.Output);
        Assert.Null(d.ClockPin);
        Assert.Null(d.EnablePin);
    }

    [Fact]
    public void Inverter_IsRecognised()
    {
        GateCellDescriptor d = GateCellLibrary.Lookup("$_NOT_");
        Assert.Equal(GateCellShape.Inverter, d.Shape);
        Assert.Equal(new[] { "A" }, d.Inputs);
        Assert.Equal("Y", d.Output);
    }

    [Fact]
    public void Buffer_IsRecognised()
    {
        GateCellDescriptor d = GateCellLibrary.Lookup("$_BUF_");
        Assert.Equal(GateCellShape.Buffer, d.Shape);
        Assert.Equal(new[] { "A" }, d.Inputs);
        Assert.Equal("Y", d.Output);
    }

    [Fact]
    public void Mux_HasSelectAsEnablePin()
    {
        // Renderer uses EnablePin to know where to draw the south-side selector.
        GateCellDescriptor d = GateCellLibrary.Lookup("$_MUX_");
        Assert.Equal(GateCellShape.Mux, d.Shape);
        Assert.Equal(new[] { "A", "B" }, d.Inputs);
        Assert.Equal("Y", d.Output);
        Assert.Equal("S", d.EnablePin);
    }

    [Theory]
    [InlineData("$_DFF_P_")]
    [InlineData("$_DFF_N_")]
    public void Dff_HasDInputClockPinQOutput(string cellType)
    {
        GateCellDescriptor d = GateCellLibrary.Lookup(cellType);
        Assert.Equal(GateCellShape.FlipFlop, d.Shape);
        Assert.Equal(new[] { "D" }, d.Inputs);
        Assert.Equal("C", d.ClockPin);
        Assert.Equal("Q", d.Output);
    }

    [Theory]
    [InlineData("$_DLATCH_P_")]
    [InlineData("$_DLATCH_N_")]
    public void Latch_HasEnablePinNotClock(string cellType)
    {
        GateCellDescriptor d = GateCellLibrary.Lookup(cellType);
        Assert.Equal(GateCellShape.Latch, d.Shape);
        Assert.Equal(new[] { "D" }, d.Inputs);
        Assert.Null(d.ClockPin);
        Assert.Equal("E", d.EnablePin);
        Assert.Equal("Q", d.Output);
    }

    [Fact]
    public void UnknownCellType_FallsBackToUnknownDescriptor()
    {
        GateCellDescriptor d = GateCellLibrary.Lookup("$_MADE_UP_CELL_");
        Assert.True(d.IsUnknown);
        Assert.Equal(GateCellShape.Unknown, d.Shape);
        Assert.False(GateCellLibrary.IsKnown("$_MADE_UP_CELL_"));
    }
}
