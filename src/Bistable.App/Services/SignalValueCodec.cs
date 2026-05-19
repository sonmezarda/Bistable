using System.Globalization;
using System.Numerics;

namespace Bistable.App.Services;

public static class SignalValueCodec
{
    public static bool TryParse(string text, out BigInteger value) =>
        TryParse(text, SignalValueFormat.Decimal, out value);

    public static bool TryParse(string text, SignalValueFormat defaultFormat, out BigInteger value)
    {
        string normalized = Normalize(text);
        if (normalized.Length == 0)
        {
            value = BigInteger.Zero;
            return false;
        }

        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return BigInteger.TryParse(normalized[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
        }

        if (normalized.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseBinary(normalized[2..], out value);
        }

        return defaultFormat switch
        {
            SignalValueFormat.Hex => BigInteger.TryParse(normalized, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value),
            SignalValueFormat.Binary => TryParseBinary(normalized, out value),
            _ => BigInteger.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
        };
    }

    public static BigInteger MaskToWidth(BigInteger value, int width)
    {
        if (width <= 0)
        {
            return BigInteger.Zero;
        }

        BigInteger mask = (BigInteger.One << width) - BigInteger.One;
        return value & mask;
    }

    public static string FormatCanonical(BigInteger value, int width)
    {
        BigInteger masked = MaskToWidth(value, width);
        if (width <= 1)
        {
            return masked.IsZero ? "0" : "1";
        }

        int digits = Math.Max(1, (width + 3) / 4);
        string hex = masked.IsZero
            ? "0"
            : Convert.ToHexString(masked.ToByteArray(isUnsigned: true, isBigEndian: true)).TrimStart('0');
        if (hex.Length == 0)
        {
            hex = "0";
        }
        return "0x" + hex.PadLeft(digits, '0');
    }

    public static string FormatForDisplay(BigInteger value, int width, SignalValueFormat format)
    {
        BigInteger masked = MaskToWidth(value, width);
        return format switch
        {
            SignalValueFormat.Hex => FormatCanonical(masked, Math.Max(2, width)),
            SignalValueFormat.Binary => "0b" + ToBinaryString(masked, Math.Max(1, width)),
            _ => masked.ToString(CultureInfo.InvariantCulture)
        };
    }

    public static IReadOnlyList<bool> ToBits(BigInteger value, int width)
    {
        BigInteger masked = MaskToWidth(value, width);
        bool[] bits = new bool[Math.Max(1, width)];
        for (int bit = 0; bit < bits.Length; bit++)
        {
            bits[bit] = ((masked >> bit) & BigInteger.One) == BigInteger.One;
        }

        return bits;
    }

    public static BigInteger FromBits(IEnumerable<bool> bits)
    {
        BigInteger value = BigInteger.Zero;
        int index = 0;
        foreach (bool bit in bits)
        {
            if (bit)
            {
                value |= BigInteger.One << index;
            }

            index++;
        }

        return value;
    }

    private static string Normalize(string text) =>
        text.Trim().Replace("_", string.Empty, StringComparison.Ordinal);

    private static bool TryParseBinary(string bits, out BigInteger value)
    {
        value = BigInteger.Zero;
        if (bits.Length == 0)
        {
            return false;
        }

        foreach (char bit in bits)
        {
            if (bit is not ('0' or '1'))
            {
                return false;
            }

            value <<= 1;
            if (bit == '1')
            {
                value += BigInteger.One;
            }
        }

        return true;
    }

    private static string ToBinaryString(BigInteger value, int width)
    {
        char[] buffer = new char[width];
        for (int bit = 0; bit < width; bit++)
        {
            int sourceBit = width - 1 - bit;
            buffer[bit] = ((value >> sourceBit) & BigInteger.One) == BigInteger.One ? '1' : '0';
        }

        return new string(buffer);
    }
}
