using Bistable.Engine;

namespace Bistable.Tests.Engine;

/// <summary>
/// Category 2: width/format validation is a pure, worker-free gate. A value that
/// fails here never reaches the compiled worker.
/// </summary>
public sealed class SimulationValueValidatorTests
{
    [Theory]
    [InlineData("1", 1, "1")]
    [InlineData("0b1010", 4, "10")]
    [InlineData("0xff", 8, "255")]
    [InlineData("255", 8, "255")]
    [InlineData("0", 1, "0")]
    public void AcceptsValuesThatFitWidth(string raw, int width, string normalized)
    {
        SimulationValueValidation result = SimulationValueValidator.Validate(raw, width);
        Assert.True(result.IsValid, result.Error);
        Assert.Equal(normalized, result.NormalizedValue);
    }

    [Theory]
    [InlineData("2", 1)]          // 2 > max for 1 bit
    [InlineData("256", 8)]        // 256 > max for 8 bits
    [InlineData("0x100", 8)]      // 0x100 > 0xFF
    [InlineData("0b100", 2)]      // 3 bits into a 2-bit field
    public void RejectsWidthOverflow(string raw, int width)
    {
        SimulationValueValidation result = SimulationValueValidator.Validate(raw, width);
        Assert.False(result.IsValid);
        Assert.Contains("does not fit", result.Error);
    }

    [Theory]
    [InlineData("0xZZ", 8)]
    [InlineData("0b12", 8)]
    [InlineData("hello", 8)]
    [InlineData("", 8)]
    [InlineData("   ", 8)]
    public void RejectsMalformedValues(string raw, int width)
    {
        SimulationValueValidation result = SimulationValueValidator.Validate(raw, width);
        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void EncodesNegativeAsTwosComplementWithinWidth()
    {
        SimulationValueValidation result = SimulationValueValidator.Validate("-1", 8);
        Assert.True(result.IsValid, result.Error);
        Assert.Equal("255", result.NormalizedValue);
    }

    [Fact]
    public void RejectsNegativeBeyondSignedRange()
    {
        SimulationValueValidation result = SimulationValueValidator.Validate("-129", 8);
        Assert.False(result.IsValid);
    }
}
