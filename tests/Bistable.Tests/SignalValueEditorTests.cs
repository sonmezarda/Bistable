using System.Numerics;
using Bistable.App.Services;
using Bistable.App.ViewModels;

namespace Bistable.Tests;

public sealed class SignalValueEditorTests
{
    [Fact]
    public void CodecParsesAndFormatsBusValues()
    {
        Assert.True(SignalValueCodec.TryParse("0x1f", out BigInteger hexValue));
        Assert.Equal(new BigInteger(31), hexValue);

        Assert.True(SignalValueCodec.TryParse("1010", SignalValueFormat.Binary, out BigInteger binaryValue));
        Assert.Equal(new BigInteger(10), binaryValue);

        Assert.Equal("0x0A", SignalValueCodec.FormatCanonical(binaryValue, 8));
        Assert.Equal("0b00001010", SignalValueCodec.FormatForDisplay(binaryValue, 8, SignalValueFormat.Binary));
    }

    [Fact]
    public void EditorUpdatesBitsWhenTextIsApplied()
    {
        SignalValueEditorViewModel viewModel = new("data", 8, "0x00")
        {
            SelectedFormat = SignalValueFormat.Hex,
            ValueText = "0xA5"
        };

        Assert.True(viewModel.TryApplyText());
        Assert.Equal("0xA5", viewModel.CanonicalValue);
        Assert.True(viewModel.Bits[0].IsSet);
        Assert.False(viewModel.Bits[1].IsSet);
        Assert.True(viewModel.Bits[2].IsSet);
        Assert.False(viewModel.Bits[3].IsSet);
        Assert.False(viewModel.Bits[4].IsSet);
        Assert.True(viewModel.Bits[5].IsSet);
        Assert.False(viewModel.Bits[6].IsSet);
        Assert.True(viewModel.Bits[7].IsSet);
    }

    [Fact]
    public void EditorUpdatesCanonicalValueWhenBitsChange()
    {
        SignalValueEditorViewModel viewModel = new("data", 4, "0x0");

        viewModel.Bits[0].IsSet = true;
        viewModel.Bits[2].IsSet = true;

        Assert.Equal("0x5", viewModel.CanonicalValue);
        Assert.Contains("5", viewModel.ValueText, StringComparison.Ordinal);
    }
}
