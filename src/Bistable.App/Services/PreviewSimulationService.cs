using System.Globalization;
using System.Numerics;
using Bistable.App.ViewModels;

namespace Bistable.App.Services;

public sealed class PreviewSimulationService
{
    public PreviewSimulationResult Evaluate(string topModule, IReadOnlyList<SignalViewModel> inputs, IReadOnlyList<SignalViewModel> outputs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topModule);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(outputs);

        if (!string.Equals(topModule, "alu", StringComparison.OrdinalIgnoreCase))
        {
            return PreviewSimulationResult.Unsupported("Preview eval currently supports the bundled ALU sample. Native Verilator worker is next.");
        }

        Dictionary<string, SignalViewModel> inputMap = inputs.ToDictionary(static s => s.Name, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, SignalViewModel> outputMap = outputs.ToDictionary(static s => s.Name, StringComparer.OrdinalIgnoreCase);

        if (!inputMap.TryGetValue("a", out SignalViewModel? aSignal)
            || !inputMap.TryGetValue("b", out SignalViewModel? bSignal)
            || !inputMap.TryGetValue("op", out SignalViewModel? opSignal)
            || !outputMap.TryGetValue("y", out SignalViewModel? ySignal)
            || !outputMap.TryGetValue("zero", out SignalViewModel? zeroSignal))
        {
            return PreviewSimulationResult.Unsupported("Loaded module is named alu but does not match the sample ALU port shape.");
        }

        if (!SignalValueCodec.TryParse(aSignal.Value, out BigInteger a)
            || !SignalValueCodec.TryParse(bSignal.Value, out BigInteger b)
            || !SignalValueCodec.TryParse(opSignal.Value, out BigInteger op))
        {
            return PreviewSimulationResult.Failed("Input values must be decimal, 0x-prefixed hex, or 0b-prefixed binary.");
        }

        BigInteger result = ((int)op & 0b111) switch
        {
            0 => a + b,
            1 => a - b,
            2 => a & b,
            3 => a | b,
            _ => BigInteger.Zero
        };

        BigInteger mask = (BigInteger.One << ySignal.Width) - BigInteger.One;
        result &= mask;

        ySignal.Value = FormatHex(result, ySignal.Width);
        zeroSignal.Value = result.IsZero ? "1" : "0";

        return PreviewSimulationResult.Success("Preview ALU eval completed.");
    }
    private static string FormatHex(BigInteger value, int width)
    {
        int digits = Math.Max(1, (width + 3) / 4);
        string hex = value.ToString("X", CultureInfo.InvariantCulture);
        if (hex.Length < digits)
        {
            hex = hex.PadLeft(digits, '0');
        }

        return "0x" + hex;
    }
}
