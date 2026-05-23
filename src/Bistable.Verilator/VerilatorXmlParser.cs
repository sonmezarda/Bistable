using System.Globalization;
using System.Xml.Linq;
using Bistable.Core.Design;

namespace Bistable.Verilator;

public sealed class VerilatorXmlParser
{
    private const string VarRefElement = "varref";

    public static ModuleMetadata Parse(string xmlPath)
        => ParseDesign(xmlPath).TopModule;

    public static ElaboratedDesign ParseDesign(string xmlPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xmlPath);

        XDocument document = XDocument.Load(xmlPath, LoadOptions.None);
        List<XElement> moduleElements = document.Descendants("module").ToList();
        XElement module = moduleElements
            .FirstOrDefault(static e => string.Equals((string?)e.Attribute("topModule"), "1", StringComparison.Ordinal))
            ?? moduleElements.FirstOrDefault()
            ?? throw new InvalidDataException("Verilator XML does not contain a module.");

        Dictionary<string, DType> dtypes = document
            .Descendants()
            .Where(static e => e.Name.LocalName.EndsWith("dtype", StringComparison.Ordinal))
            .Select(ParseDType)
            .Where(static dtype => dtype.Id is not null)
            .ToDictionary(static dtype => dtype.Id!, static dtype => dtype);

        Dictionary<string, ModuleMetadata> moduleCatalog = moduleElements
            .Select(element => ParseModuleMetadata(element, dtypes))
            .ToDictionary(static metadata => metadata.Name, static metadata => metadata, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, DesignModuleDefinition> moduleDefinitions = moduleElements
            .Select(element => ParseModuleDefinition(element, dtypes, moduleCatalog))
            .ToDictionary(static definition => definition.Metadata.Name, static definition => definition, StringComparer.OrdinalIgnoreCase);

        string topModuleName = (string?)module.Attribute("name") ?? "unknown";
        ModuleMetadata topModule = moduleCatalog.TryGetValue(topModuleName, out ModuleMetadata? catalogEntry)
            ? catalogEntry
            : ParseModuleMetadata(module, dtypes);
        DesignHierarchyNode hierarchyRoot = ParseHierarchy(document, topModule.Name);
        return new ElaboratedDesign(topModule, hierarchyRoot, moduleCatalog, moduleDefinitions);
    }

    private static ModuleMetadata ParseModuleMetadata(XElement module, IReadOnlyDictionary<string, DType> dtypes)
    {
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

    private static DesignModuleDefinition ParseModuleDefinition(
        XElement module,
        IReadOnlyDictionary<string, DType> dtypes,
        IReadOnlyDictionary<string, ModuleMetadata> moduleCatalog)
    {
        string moduleName = (string?)module.Attribute("name") ?? "unknown";
        ModuleMetadata metadata = moduleCatalog.TryGetValue(moduleName, out ModuleMetadata? existing)
            ? existing
            : ParseModuleMetadata(module, dtypes);

        List<DesignLocalSignal> locals = module
            .Elements("var")
            .Where(static e => e.Attribute("dir") is null && !string.Equals((string?)e.Attribute("param"), "true", StringComparison.Ordinal))
            .Select(e => ParseLocalSignal(e, dtypes))
            .ToList();

        List<DesignInstanceDefinition> instances = module
            .Elements("instance")
            .Select(ParseInstanceDefinition)
            .ToList();

        List<DesignContAssign> contAssigns = module
            .Elements("contassign")
            .Select(ParseContAssign)
            .Where(static assign => assign is not null)
            .Cast<DesignContAssign>()
            .ToList();

        return new DesignModuleDefinition(metadata, locals, instances, contAssigns);
    }

    private static DesignHierarchyNode ParseHierarchy(XDocument document, string fallbackTopModuleName)
    {
        List<CellInfo> cells = document
            .Descendants("cells")
            .Descendants("cell")
            .Select(static element => new CellInfo(
                (string?)element.Attribute("name") ?? string.Empty,
                (string?)element.Attribute("submodname") ?? string.Empty,
                (string?)element.Attribute("hier") ?? string.Empty))
            .Where(static cell => !string.IsNullOrWhiteSpace(cell.HierarchyPath))
            .OrderBy(static cell => GetDepth(cell.HierarchyPath))
            .ThenBy(static cell => cell.HierarchyPath, StringComparer.Ordinal)
            .ToList();

        if (cells.Count == 0)
        {
            XElement? rootCell = document
                .Descendants("cells")
                .Elements("cell")
                .FirstOrDefault();
            if (rootCell is not null)
            {
                return ParseHierarchyCell(rootCell);
            }

            return new DesignHierarchyNode(fallbackTopModuleName, fallbackTopModuleName, fallbackTopModuleName, []);
        }

        Dictionary<string, MutableHierarchyNode> nodes = new(StringComparer.Ordinal);
        MutableHierarchyNode? root = null;
        foreach (CellInfo cell in cells)
        {
            string[] segments = cell.HierarchyPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length == 0)
            {
                continue;
            }

            string instanceName = !string.IsNullOrWhiteSpace(cell.InstanceName) ? cell.InstanceName : segments[^1];
            string moduleName = !string.IsNullOrWhiteSpace(cell.ModuleName) ? cell.ModuleName : segments[^1];
            MutableHierarchyNode current = new(instanceName, moduleName, cell.HierarchyPath);
            nodes[cell.HierarchyPath] = current;

            if (segments.Length == 1)
            {
                root = current;
                continue;
            }

            string parentPath = string.Join('.', segments[..^1]);
            if (nodes.TryGetValue(parentPath, out MutableHierarchyNode? parent))
            {
                parent.Children.Add(current);
            }
        }

        root ??= nodes.Values.OrderBy(static node => GetDepth(node.HierarchyPath)).First();
        return ToImmutable(root);
    }

    private static DesignHierarchyNode ParseHierarchyCell(XElement element)
    {
        string hierarchyPath = (string?)element.Attribute("hier") ?? string.Empty;
        string instanceName = (string?)element.Attribute("name")
            ?? hierarchyPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault()
            ?? "instance";
        string moduleName = (string?)element.Attribute("submodname") ?? instanceName;
        return new DesignHierarchyNode(
            instanceName,
            moduleName,
            hierarchyPath,
            element.Elements("cell").Select(ParseHierarchyCell).ToArray());
    }

    private static int GetDepth(string hierarchyPath) => hierarchyPath.Count(static c => c == '.');

    private static DesignHierarchyNode ToImmutable(MutableHierarchyNode node) =>
        new(
            node.InstanceName,
            node.ModuleName,
            node.HierarchyPath,
            node.Children.Select(ToImmutable).ToArray());

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

    private static DesignLocalSignal ParseLocalSignal(XElement element, IReadOnlyDictionary<string, DType> dtypes)
    {
        string name = RequiredAttribute(element, "name");
        string dtypeId = RequiredAttribute(element, "dtype_id");
        DType dtype = dtypes.TryGetValue(dtypeId, out DType? value) ? value : DType.Scalar(dtypeId);
        return new DesignLocalSignal(name, dtype.Width, dtype.IsSigned);
    }

    private static DesignInstanceDefinition ParseInstanceDefinition(XElement element)
    {
        string name = RequiredAttribute(element, "name");
        string moduleName = RequiredAttribute(element, "defName");
        List<DesignInstancePortConnection> connections = element
            .Elements("port")
            .Select(ParseInstancePortConnection)
            .OrderBy(static connection => connection.PortIndex)
            .ToList();
        return new DesignInstanceDefinition(name, moduleName, connections);
    }

    private static DesignInstancePortConnection ParseInstancePortConnection(XElement element)
    {
        // Direct varref (simple wire connection)
        string? signalName = (string?)element.Element(VarRefElement)?.Attribute("name");

        // Packed-struct field access: <sel><varref name="struct"/><const offset/>...</sel>
        // Verilator represents e.g. control_pins.ops as a bit-slice of the struct variable.
        // Treat the base variable as the connected signal so the schematic shows the wire.
        signalName ??= (string?)element.Element("sel")?.Element(VarRefElement)?.Attribute("name");

        signalName ??= (string?)element.Element("const")?.Attribute("name") ?? "?";

        return new DesignInstancePortConnection(
            RequiredAttribute(element, "name"),
            signalName,
            (string?)element.Attribute("direction") ?? string.Empty,
            ParseInt((string?)element.Attribute("portIndex"), 0));
    }

    private static DesignContAssign? ParseContAssign(XElement element)
    {
        // In Verilator XML, <contassign> children are ordered [RHS expr] [LHS varref].
        // The LHS target is always the last direct <varref> child.
        XElement? target = element.Elements(VarRefElement).LastOrDefault()
            ?? element.Descendants(VarRefElement).LastOrDefault();
        string? targetName = (string?)target?.Attribute("name");
        if (string.IsNullOrWhiteSpace(targetName))
        {
            return null;
        }

        // Bit-range select: first non-varref child is <sel> — parse range and route to a splitter node.
        XElement? rhs = element.Elements().FirstOrDefault(static e => e.Name.LocalName != VarRefElement);
        if (rhs?.Name.LocalName == "sel")
        {
            return ParseSelContAssign(targetName, rhs);
        }

        List<string> sourceNames = element
            .Descendants(VarRefElement)
            .Where(varref => !ReferenceEquals(varref, target))
            .Select(static varref => (string?)varref.Attribute("name"))
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name!)
            .Where(name => !string.Equals(name, targetName, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (sourceNames.Count == 0)
        {
            return null;
        }

        string? operatorSymbol = DetectOperatorSymbol(element);
        return new DesignContAssign(targetName, sourceNames, operatorSymbol);
    }

    private static DesignContAssign? ParseSelContAssign(string targetName, XElement sel)
    {
        // <sel> children: <varref> (bus signal), <const> (lo bit offset), <const> (bit width)
        string? sourceName = (string?)sel.Element(VarRefElement)?.Attribute("name");
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return null;
        }

        List<XElement> consts = sel.Elements("const").ToList();
        if (consts.Count >= 2)
        {
            int lo = ParseVerilogConst(consts[0]);
            int width = ParseVerilogConst(consts[1]);
            int hi = lo + Math.Max(1, width) - 1;
            return new DesignContAssign(targetName, [sourceName!], null, new DesignBitRange(hi, lo));
        }

        // Fallback: no range info available, fall back to transparent wire alias.
        return new DesignContAssign(targetName, [sourceName!]);
    }

    private static int ParseVerilogConst(XElement element)
    {
        // Verilog constant format: "32'hc" (hex), "4'b1100" (binary), "32'd12" (decimal)
        string? name = (string?)element.Attribute("name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return 0;
        }

        int apostrophe = name!.IndexOf('\'', StringComparison.Ordinal);
        if (apostrophe < 0)
        {
            return ParseInt(name, 0);
        }

        string rest = name[(apostrophe + 1)..];
        if (rest.Length < 2)
        {
            return 0;
        }

        return char.ToLowerInvariant(rest[0]) switch
        {
            'h' => int.TryParse(rest[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int h) ? h : 0,
            'd' => ParseInt(rest[1..], 0),
            'b' => ParseBinaryConst(rest[1..]),
            _ => 0
        };
    }

    private static int ParseBinaryConst(string s)
    {
        int result = 0;
        foreach (char c in s.Where(static c => c is '0' or '1'))
        {
            result = (result << 1) | (c - '0');
        }

        return result;
    }

    private static string? DetectOperatorSymbol(XElement contassignElement)
    {
        // The RHS expression is the first non-varref direct child.
        XElement? rhs = contassignElement.Elements()
            .FirstOrDefault(static e => e.Name.LocalName != VarRefElement);

        return rhs?.Name.LocalName switch
        {
            "add" => "+",
            "sub" => "-",
            "mul" => "*",
            "div" => "/",
            "moddiv" => "%",
            "and" => "&",
            "or" => "|",
            "xor" => "^",
            "not" => "~",
            "logand" => "&&",
            "logor" => "||",
            "lognot" => "!",
            "eq" => "=",
            "neq" => "≠",
            "lt" => "<",
            "gt" => ">",
            "lte" => "≤",
            "gte" => "≥",
            "shiftl" => "<<",
            "shiftr" => ">>",
            "shiftrs" => ">>>",
            "concat" => "{}",
            "cond" => "?:",
            // sel (bit-range select) is a single-source wire alias, not a combinational operator
            "sel" => null,
            _ => null
        };
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

    private sealed record CellInfo(string InstanceName, string ModuleName, string HierarchyPath);

    private sealed class MutableHierarchyNode(string instanceName, string moduleName, string hierarchyPath)
    {
        public string InstanceName { get; } = instanceName;

        public string ModuleName { get; } = moduleName;

        public string HierarchyPath { get; } = hierarchyPath;

        public List<MutableHierarchyNode> Children { get; } = [];
    }
}
