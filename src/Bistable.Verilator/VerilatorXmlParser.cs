using System.Globalization;
using System.Xml.Linq;
using Bistable.Core.Design;

namespace Bistable.Verilator;

public sealed class VerilatorXmlParser
{
    public ModuleMetadata Parse(string xmlPath)
        => ParseDesign(xmlPath).TopModule;

    public ElaboratedDesign ParseDesign(string xmlPath)
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
        string signalName = (string?)element.Element("varref")?.Attribute("name")
            ?? (string?)element.Element("const")?.Attribute("name")
            ?? "?";
        return new DesignInstancePortConnection(
            RequiredAttribute(element, "name"),
            signalName,
            (string?)element.Attribute("direction") ?? string.Empty,
            ParseInt((string?)element.Attribute("portIndex"), 0));
    }

    private static DesignContAssign? ParseContAssign(XElement element)
    {
        XElement? target = element.Elements("varref").FirstOrDefault()
            ?? element.Descendants("varref").FirstOrDefault();
        string? targetName = (string?)target?.Attribute("name");
        if (string.IsNullOrWhiteSpace(targetName))
        {
            return null;
        }

        List<string> sourceNames = element
            .Descendants("varref")
            .Where(varref => !ReferenceEquals(varref, target))
            .Select(static varref => (string?)varref.Attribute("name"))
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name!)
            .Where(name => !string.Equals(name, targetName, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return sourceNames.Count == 0 ? null : new DesignContAssign(targetName, sourceNames);
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
