using System.Globalization;
using System.Numerics;
using Bistable.Core.Design;
using Bistable.Core.Projects;

namespace Bistable.App.Services;

public sealed record SubSimulationConfiguration(
    ProjectConfiguration Project,
    string RequestedModuleName,
    string BuildTopModule);

public static class SubSimulationConfigurationResolver
{
    public static SubSimulationConfiguration Resolve(ProjectConfiguration baseConfiguration, ModuleMetadata module)
    {
        ArgumentNullException.ThrowIfNull(baseConfiguration);
        ArgumentNullException.ThrowIfNull(module);

        string buildTop = string.IsNullOrWhiteSpace(module.SourceName) ? module.Name : module.SourceName;
        Dictionary<string, string> parameters = new(StringComparer.Ordinal);
        foreach (DesignParameter parameter in module.Parameters)
        {
            if (string.IsNullOrWhiteSpace(parameter.Name) || string.IsNullOrWhiteSpace(parameter.Value))
                continue;

            parameters[parameter.Name] = NormalizeVerilatorParameterValue(parameter.Value);
        }

        ProjectConfiguration project = baseConfiguration with
        {
            TopModule = buildTop,
            Parameters = parameters
        };

        return new SubSimulationConfiguration(project, module.Name, buildTop);
    }

    internal static string NormalizeVerilatorParameterValue(string value)
    {
        string trimmed = value.Trim();
        return TryParseVerilogIntegerLiteral(trimmed, out BigInteger parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : trimmed;
    }

    private static bool TryParseVerilogIntegerLiteral(string text, out BigInteger value)
    {
        string literal = text.Replace("_", string.Empty, StringComparison.Ordinal);
        if (literal.Length == 0)
        {
            value = BigInteger.Zero;
            return false;
        }

        int apostrophe = literal.IndexOf('\'', StringComparison.Ordinal);
        if (apostrophe < 0)
            return BigInteger.TryParse(literal, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        string sizeText = literal[..apostrophe];
        string body = literal[(apostrophe + 1)..];
        bool isSigned = false;
        if (body.StartsWith("s", StringComparison.OrdinalIgnoreCase))
        {
            isSigned = true;
            body = body[1..];
        }

        if (body.Length < 2 || !TryGetRadix(body[0], out int radix))
        {
            value = BigInteger.Zero;
            return false;
        }

        string digits = body[1..];
        if (digits.Length == 0 || digits.Contains("x", StringComparison.OrdinalIgnoreCase)
                               || digits.Contains("z", StringComparison.OrdinalIgnoreCase)
                               || digits.Contains('?'))
        {
            value = BigInteger.Zero;
            return false;
        }

        if (!TryParseDigits(digits, radix, out value))
            return false;

        if (isSigned
            && int.TryParse(sizeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int width)
            && width > 0
            && value >= (BigInteger.One << (width - 1)))
        {
            value -= BigInteger.One << width;
        }

        return true;
    }

    private static bool TryGetRadix(char baseChar, out int radix)
    {
        radix = char.ToLowerInvariant(baseChar) switch
        {
            'b' => 2,
            'o' => 8,
            'd' => 10,
            'h' => 16,
            _ => 0
        };
        return radix != 0;
    }

    private static bool TryParseDigits(string digits, int radix, out BigInteger value)
    {
        value = BigInteger.Zero;
        foreach (char ch in digits)
        {
            int digit = ch switch
            {
                >= '0' and <= '9' => ch - '0',
                >= 'a' and <= 'f' => ch - 'a' + 10,
                >= 'A' and <= 'F' => ch - 'A' + 10,
                _ => -1
            };

            if (digit < 0 || digit >= radix)
                return false;

            value = value * radix + digit;
        }

        return true;
    }
}
