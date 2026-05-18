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

        if (!TryParseValue(aSignal.Value, out BigInteger a)
            || !TryParseValue(bSignal.Value, out BigInteger b)
            || !TryParseValue(opSignal.Value, out BigInteger op))
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

    private static bool TryParseValue(string text, out BigInteger value)
    {
        text = text.Trim().Replace("_", string.Empty, StringComparison.Ordinal);
        if (text.Length == 0)
        {
            value = BigInteger.Zero;
            return false;
        }

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return BigInteger.TryParse(text[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
        }

        if (text.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
        {
            value = BigInteger.Zero;
            foreach (char bit in text[2..])
            {
                if (bit is not ('0' or '1'))
                {
                    return false;
                }

                value = (value << 1) + (bit == '1' ? BigInteger.One : BigInteger.Zero);
            }

            return true;
        }

        return BigInteger.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
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
