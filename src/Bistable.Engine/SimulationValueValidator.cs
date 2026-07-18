using System.Globalization;
using System.Numerics;

namespace Bistable.Engine;

/// <summary>
/// Parses and range-checks a user-entered signal value (binary <c>0b…</c>,
/// hex <c>0x…</c>, or decimal) against a port width <em>before</em> any worker
/// IPC. A width-overflow or malformed literal is rejected here so a bad value
/// never reaches the compiled worker.
/// </summary>
public static class SimulationValueValidator
{
    public static SimulationValueValidation Validate(string? rawValue, int width)
    {
        if (width <= 0)
        {
            return SimulationValueValidation.Invalid($"Signal width must be positive (was {width}).");
        }
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return SimulationValueValidation.Invalid("Value cannot be empty.");
        }

        string trimmed = rawValue.Trim();
        bool negative = false;
        if (trimmed.StartsWith('-'))
        {
            negative = true;
            trimmed = trimmed[1..].TrimStart();
        }
        if (trimmed.Length == 0)
        {
            return SimulationValueValidation.Invalid("Value cannot be empty.");
        }

        if (!TryParseMagnitude(trimmed, out BigInteger magnitude, out string? parseError))
        {
            return SimulationValueValidation.Invalid(parseError!);
        }

        // Two's-complement range for the width: [-2^(w-1), 2^w - 1] so both a
        // signed negative and an unsigned max fit the same field.
        BigInteger max = (BigInteger.One << width) - 1;
        BigInteger value = negative ? -magnitude : magnitude;
        BigInteger min = negative ? -(BigInteger.One << (width - 1)) : BigInteger.Zero;

        if (negative)
        {
            if (value < min)
            {
                return SimulationValueValidation.Invalid(
                    $"Value {rawValue.Trim()} does not fit signed width {width} (min {min}).");
            }
            // Encode as the two's-complement bit pattern within the width.
            value = (BigInteger.One << width) + value;
        }
        else if (magnitude > max)
        {
            return SimulationValueValidation.Invalid(
                $"Value {rawValue.Trim()} does not fit width {width} (max {max}).");
        }

        // The worker accepts a decimal string that it parses to uint64; we send
        // the canonical unsigned bit-pattern for the width.
        return SimulationValueValidation.Ok(value.ToString(CultureInfo.InvariantCulture));
    }

    private static bool TryParseMagnitude(string text, out BigInteger magnitude, out string? error)
    {
        magnitude = BigInteger.Zero;
        error = null;
        try
        {
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                string hex = text[2..];
                if (hex.Length == 0 || !hex.All(Uri.IsHexDigit))
                {
                    error = $"Invalid hexadecimal value '{text}'.";
                    return false;
                }
                // Prefix a 0 so a leading hex nibble >= 8 is never read as negative.
                magnitude = BigInteger.Parse("0" + hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return true;
            }
            if (text.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
            {
                string bits = text[2..];
                if (bits.Length == 0 || bits.Any(c => c != '0' && c != '1'))
                {
                    error = $"Invalid binary value '{text}'.";
                    return false;
                }
                foreach (char bit in bits)
                {
                    magnitude = (magnitude << 1) + (bit == '1' ? 1 : 0);
                }
                return true;
            }
            if (!text.All(char.IsDigit))
            {
                error = $"Invalid decimal value '{text}'.";
                return false;
            }
            magnitude = BigInteger.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
            return true;
        }
        catch (FormatException)
        {
            error = $"Invalid numeric value '{text}'.";
            return false;
        }
    }
}

/// <summary>Result of validating a user value against a signal width.</summary>
public sealed record SimulationValueValidation(bool IsValid, string? NormalizedValue, string? Error)
{
    public static SimulationValueValidation Ok(string normalized) => new(true, normalized, null);

    public static SimulationValueValidation Invalid(string error) => new(false, null, error);
}
