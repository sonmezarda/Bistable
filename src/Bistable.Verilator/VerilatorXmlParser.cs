using System.Globalization;
using System.Xml.Linq;
using Bistable.Core.Design;

namespace Bistable.Verilator;

public sealed class VerilatorXmlParser
{
    public ModuleMetadata Parse(string xmlPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xmlPath);

        XDocument document = XDocument.Load(xmlPath, LoadOptions.None);
        XElement module = document
            .Descendants("module")
            .FirstOrDefault(static e => string.Equals((string?)e.Attribute("topModule"), "1", StringComparison.Ordinal))
            ?? document.Descendants("module").FirstOrDefault()
            ?? throw new InvalidDataException("Verilator XML does not contain a module.");

        Dictionary<string, DType> dtypes = document
            .Descendants()
            .Where(static e => e.Name.LocalName.EndsWith("dtype", StringComparison.Ordinal))
            .Select(ParseDType)
            .Where(static dtype => dtype.Id is not null)
            .ToDictionary(static dtype => dtype.Id!, static dtype => dtype);

        List<SignalPort> ports = module
            .Elements("var")
            .Where(static e => e.Attribute("dir") is not null)
            .Select(e => ParsePort(e, dtypes))
            .OrderBy(static port => port.PinIndex)
            .ToList();

        List<DesignParameter> parameters = module
            .Elements("var")
            .Where(static e => string.Equals((string?)e.Attribute("param"), "true", StringComparison.Ordinal))
            .Select(ParseParameter)
            .ToList();

        return new ModuleMetadata((string?)module.Attribute("name") ?? "unknown", ports, parameters);
    }

    private static SignalPort ParsePort(XElement element, IReadOnlyDictionary<string, DType> dtypes)
    {
        string name = RequiredAttribute(element, "name");
        string dtypeId = RequiredAttribute(element, "dtype_id");
        string directionText = RequiredAttribute(element, "dir");
        DType dtype = dtypes.TryGetValue(dtypeId, out DType? value) ? value : DType.Scalar(dtypeId);

        return new SignalPort(
            name,
            ParseDirection(directionText),
            dtype.Width,
            dtype.IsSigned,
            ParseInt((string?)element.Attribute("pinIndex"), 0));
    }

    private static DesignParameter ParseParameter(XElement element)
    {
        string name = RequiredAttribute(element, "name");
        XElement? constant = element.Element("const");
        string value = (string?)constant?.Attribute("name") ?? string.Empty;
        return new DesignParameter(name, value);
    }

    private static DType ParseDType(XElement element)
    {
        string? id = (string?)element.Attribute("id");
        int left = ParseInt((string?)element.Attribute("left"), 0);
        int right = ParseInt((string?)element.Attribute("right"), 0);
        bool hasRange = element.Attribute("left") is not null && element.Attribute("right") is not null;
        int width = hasRange ? Math.Abs(left - right) + 1 : 1;
        bool isSigned = string.Equals((string?)element.Attribute("signed"), "true", StringComparison.Ordinal);
        return new DType(id, Math.Max(1, width), isSigned);
    }

    private static SignalDirection ParseDirection(string value) => value switch
    {
        "input" => SignalDirection.Input,
        "output" => SignalDirection.Output,
        "inout" => SignalDirection.InOut,
        _ => SignalDirection.Internal
    };

    private static string RequiredAttribute(XElement element, string name) =>
        (string?)element.Attribute(name) ?? throw new InvalidDataException($"Missing '{name}' attribute on '{element.Name}'.");

    private static int ParseInt(string? value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;

    private sealed record DType(string? Id, int Width, bool IsSigned)
    {
        public static DType Scalar(string id) => new(id, 1, false);
    }
}
