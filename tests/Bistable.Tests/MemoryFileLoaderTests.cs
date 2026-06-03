using Bistable.App.Services;

namespace Bistable.Tests;

/// <summary>
/// Phase 2.7 mem-load coverage. The parser feeds
/// <see cref="MemoryViewerWindowViewModel.LoadFromFileAsync"/> and therefore is
/// directly responsible for the correctness of memory-image uploads (RISC-V
/// program loading, test fixtures, etc.). Each test pins one syntactic feature.
/// </summary>
public sealed class MemoryFileLoaderTests
{
    [Fact]
    public void Parse_SequentialHexValues_PlacedAtIncrementingAddresses()
    {
        var image = MemoryFileLoader.Parse("01 02 ff", cellWidth: 8, depth: 256);
        Assert.Equal(3, image.CellCount);
        Assert.Equal(0ul, image.Cells[0].Address);
        Assert.Equal("0x01", image.Cells[0].HexValue);
        Assert.Equal(2ul, image.Cells[2].Address);
        Assert.Equal("0xff", image.Cells[2].HexValue);
        Assert.Equal(0, image.Errors);
    }

    [Fact]
    public void Parse_AtMarker_JumpsCursorToHexAddress()
    {
        var image = MemoryFileLoader.Parse("01 @10 99 9a", cellWidth: 8, depth: 256);
        Assert.Equal(3, image.CellCount);
        Assert.Equal(0ul,  image.Cells[0].Address);
        Assert.Equal(0x10ul, image.Cells[1].Address);
        Assert.Equal(0x11ul, image.Cells[2].Address);
    }

    [Fact]
    public void Parse_StripsLineCommentsBlockCommentsAndHash()
    {
        string source = """
            // header line
            01 02 # this is a hash comment
            /* multi
               line block */ 03
            04 // trailing
            """;
        var image = MemoryFileLoader.Parse(source, cellWidth: 8, depth: 256);
        Assert.Equal(4, image.CellCount);
        Assert.Equal(new[] { "0x01", "0x02", "0x03", "0x04" }, image.Cells.Select(c => c.HexValue));
    }

    [Fact]
    public void Parse_UnderscoresInTokensAreIgnored()
    {
        // Like Verilog $readmemh / Rust numeric literals — underscores are visual.
        var image = MemoryFileLoader.Parse("dead_beef 1234_5678", cellWidth: 32, depth: 16);
        Assert.Equal(2, image.CellCount);
        Assert.Equal("0xdeadbeef", image.Cells[0].HexValue);
        Assert.Equal("0x12345678", image.Cells[1].HexValue);
    }

    [Fact]
    public void Parse_OutOfRangeValue_CountedAsError()
    {
        // cellWidth=8 → max 0xff. 0x100 is one bit too wide.
        var image = MemoryFileLoader.Parse("ff 100 02", cellWidth: 8, depth: 256);
        Assert.Equal(2, image.CellCount); // ff and 02
        Assert.Equal(1, image.Errors);
    }

    [Fact]
    public void Parse_InvalidAtAddress_CountedAsError()
    {
        var image = MemoryFileLoader.Parse("@xyz 01", cellWidth: 8, depth: 16);
        Assert.Equal(1, image.CellCount);
        Assert.Equal(1, image.Errors);
        // cursor wasn't moved, so 01 lands at 0.
        Assert.Equal(0ul, image.Cells[0].Address);
    }

    [Fact]
    public void Parse_AddressBeyondDepth_CountedAsErrorAndSkipped()
    {
        var image = MemoryFileLoader.Parse("@100 01 02", cellWidth: 8, depth: 16);
        Assert.Empty(image.Cells);
        Assert.Equal(2, image.Errors);
    }

    [Fact]
    public void Parse_BinaryFormat_PacksBitsToHex()
    {
        var image = MemoryFileLoader.Parse(
            "10101010 01010101",
            cellWidth: 8, depth: 8,
            format: MemoryFileLoader.NumeralBase.Bin);
        Assert.Equal(2, image.CellCount);
        Assert.Equal("0xaa", image.Cells[0].HexValue);
        Assert.Equal("0x55", image.Cells[1].HexValue);
    }

    [Fact]
    public void Parse_HexDigitsPaddedToCellWidth()
    {
        // 32-bit cells should produce 8 hex digits per value regardless of
        // whether the source was short.
        var image = MemoryFileLoader.Parse("1 ff", cellWidth: 32, depth: 4);
        Assert.Equal("0x00000001", image.Cells[0].HexValue);
        Assert.Equal("0x000000ff", image.Cells[1].HexValue);
    }

    [Fact]
    public void Parse_EmptyInput_NoCellsNoErrors()
    {
        var image = MemoryFileLoader.Parse("   \n  \n", cellWidth: 8, depth: 16);
        Assert.Empty(image.Cells);
        Assert.Equal(0, image.Errors);
    }

    [Fact]
    public void Parse_CellWidthZero_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MemoryFileLoader.Parse("01", cellWidth: 0, depth: 1));
    }

    [Fact]
    public void LoadFromFile_RiscvSampleProgram_ParsesAllInstructions()
    {
        // Sanity-check the bundled RISC-V demo program — protects against any
        // future formatting drift that would break the "Load File…" demo on
        // a fresh checkout.
        string path = LocateSampleProgram();
        Assert.True(File.Exists(path), $"Sample program missing at {path}");
        var image = MemoryFileLoader.LoadFromFile(path, cellWidth: 32, depth: 32);
        Assert.Equal(6, image.CellCount);
        Assert.Equal(0, image.Errors);
        Assert.Equal("0x00500093", image.Cells[0].HexValue); // addi x1, x0, 5
        Assert.Equal("0x00100073", image.Cells[5].HexValue); // ebreak
    }

    private static string LocateSampleProgram()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "samples", "riscv_single_cycle", "programs", "add_then_halt.hex");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return string.Empty;
    }
}
