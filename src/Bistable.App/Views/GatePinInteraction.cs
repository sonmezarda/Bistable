using Avalonia;
using Bistable.App.Services.Routing.Elk;
using Bistable.Core.Synthesis;

namespace Bistable.App.Views;

internal sealed record GatePinInfo(
    string PortId,
    string NodeId,
    string PinName,
    string BaseName,
    int? BitIndex,
    GatePortDirection Direction,
    int Width,
    int? Msb,
    int? Lsb,
    int? NetId,
    string? NetName,
    Point Centre)
{
    public bool IsConnected => NetId.HasValue;

    public string FormatTooltip()
    {
        string range = Width > 1 && Msb.HasValue && Lsb.HasValue
            ? $"[{Msb}:{Lsb}]"
            : BitIndex.HasValue
                ? $"[{BitIndex}]"
                : "(scalar)";
        string net = NetId.HasValue
            ? string.IsNullOrWhiteSpace(NetName)
                ? $"net{NetId}"
                : $"{NetName} (net{NetId})"
            : "(unconnected)";
        return string.Join(
            Environment.NewLine,
            $"Pin: {PinName}",
            $"Net: {net}",
            $"Bit/range: {range}",
            $"Direction: {Direction}",
            $"Width: {Width}");
    }
}

internal sealed class GatePinInteractionIndex
{
    private readonly IReadOnlyDictionary<string, GatePinInfo> _byPortId;
    private readonly IReadOnlyDictionary<int, IReadOnlySet<string>> _portIdsByNetId;

    private GatePinInteractionIndex(
        IReadOnlyDictionary<string, GatePinInfo> byPortId,
        IReadOnlyDictionary<int, IReadOnlySet<string>> portIdsByNetId)
    {
        _byPortId = byPortId;
        _portIdsByNetId = portIdsByNetId;
    }

    public static GatePinInteractionIndex Build(ElkGraph? graph, GateModule? module)
    {
        if (graph?.Children is not { Count: > 0 })
        {
            return new GatePinInteractionIndex(
                new Dictionary<string, GatePinInfo>(StringComparer.Ordinal),
                new Dictionary<int, IReadOnlySet<string>>());
        }

        Dictionary<string, int> netIdByPortId = BuildNetIndex(graph.Edges);
        Dictionary<int, string> netNameById = BuildNetNameIndex(module);
        Dictionary<string, GateCell> cellByName = module?.Cells.ToDictionary(
                static cell => cell.Name,
                StringComparer.Ordinal)
            ?? new Dictionary<string, GateCell>(StringComparer.Ordinal);
        Dictionary<string, GatePinInfo> byPortId = new(StringComparer.Ordinal);
        Dictionary<int, HashSet<string>> mutablePortIdsByNetId = [];

        foreach ((ElkNode node, double absoluteX, double absoluteY) in EnumerateNodes(graph.Children))
        {
            if (node.Ports is not { Count: > 0 })
            {
                continue;
            }

            GateCell? cell = ResolveCell(node, cellByName);
            GatePinDraft[] drafts = node.Ports
                .Select(port => CreateDraft(node, port, cell))
                .Where(static draft => draft is not null)
                .Cast<GatePinDraft>()
                .ToArray();
            IReadOnlyDictionary<string, GatePinRange> ranges = drafts
                .GroupBy(static draft => draft.BaseName, StringComparer.Ordinal)
                .ToDictionary(
                    static group => group.Key,
                    static group => GatePinRange.From(group),
                    StringComparer.Ordinal);

            foreach (GatePinDraft draft in drafts)
            {
                int? netId = netIdByPortId.TryGetValue(draft.Port.Id, out int connectedNetId)
                    ? connectedNetId
                    : null;
                GatePinRange range = ranges[draft.BaseName];
                GatePinInfo info = new(
                    draft.Port.Id,
                    node.Id,
                    draft.PinName,
                    draft.BaseName,
                    draft.BitIndex,
                    draft.Direction,
                    range.Width,
                    range.Msb,
                    range.Lsb,
                    netId,
                    netId.HasValue ? netNameById.GetValueOrDefault(netId.Value) : null,
                    new Point(absoluteX + draft.Port.X, absoluteY + draft.Port.Y));
                byPortId[draft.Port.Id] = info;

                if (netId.HasValue)
                {
                    if (!mutablePortIdsByNetId.TryGetValue(netId.Value, out HashSet<string>? ids))
                    {
                        ids = new HashSet<string>(StringComparer.Ordinal);
                        mutablePortIdsByNetId[netId.Value] = ids;
                    }
                    ids.Add(draft.Port.Id);
                }
            }
        }

        return new GatePinInteractionIndex(
            byPortId,
            mutablePortIdsByNetId.ToDictionary(
                static pair => pair.Key,
                static pair => (IReadOnlySet<string>)pair.Value));
    }

    public GatePinInfo? Get(string portId) =>
        _byPortId.GetValueOrDefault(portId);

    public IReadOnlySet<string> GetPortIdsForNet(int? netId) =>
        netId.HasValue && _portIdsByNetId.TryGetValue(netId.Value, out IReadOnlySet<string>? ids)
            ? ids
            : EmptyPortIds;

    private static readonly IReadOnlySet<string> EmptyPortIds =
        new HashSet<string>(StringComparer.Ordinal);

    private static GatePinDraft? CreateDraft(
        ElkNode node,
        ElkPort port,
        GateCell? cell)
    {
        string? pinName = port.Labels?.FirstOrDefault()?.Text;
        if (string.IsNullOrWhiteSpace(pinName))
        {
            return null;
        }

        (string baseName, int? bitIndex) = GatePinName.Parse(pinName);
        GatePortDirection direction = ResolveDirection(node, port, cell, baseName);
        return new GatePinDraft(port, pinName, baseName, bitIndex, direction);
    }

    private static GatePortDirection ResolveDirection(
        ElkNode node,
        ElkPort port,
        GateCell? cell,
        string baseName)
    {
        if (node.Id == "boundary_in")
        {
            return GatePortDirection.Input;
        }
        if (node.Id == "boundary_out")
        {
            return GatePortDirection.Output;
        }
        if (cell?.PortDirections.TryGetValue(baseName, out GatePortDirection direction) == true)
        {
            return direction;
        }

        string? side = port.LayoutOptions?.GetValueOrDefault("elk.port.side");
        return string.Equals(side, "WEST", StringComparison.OrdinalIgnoreCase)
            ? GatePortDirection.Input
            : GatePortDirection.Output;
    }

    private static GateCell? ResolveCell(
        ElkNode node,
        IReadOnlyDictionary<string, GateCell> cellByName)
    {
        if (node.Labels is not { Count: > 0 })
        {
            return null;
        }

        string? cellName = node.Id.StartsWith("inst_", StringComparison.Ordinal)
            ? node.Labels[0].Text
            : node.Labels.Count > 2
                ? node.Labels[2].Text
                : null;
        return cellName is not null
            ? cellByName.GetValueOrDefault(cellName)
            : null;
    }

    private static Dictionary<string, int> BuildNetIndex(IReadOnlyList<ElkEdge>? edges)
    {
        Dictionary<string, int> result = new(StringComparer.Ordinal);
        if (edges is null)
        {
            return result;
        }

        foreach (ElkEdge edge in edges)
        {
            int? netId = GateSchematicCanvas.TryGetEdgeNetId(edge);
            if (!netId.HasValue)
            {
                continue;
            }
            foreach (string portId in edge.Sources ?? [])
            {
                result.TryAdd(portId, netId.Value);
            }
            foreach (string portId in edge.Targets ?? [])
            {
                result.TryAdd(portId, netId.Value);
            }
        }
        return result;
    }

    private static Dictionary<int, string> BuildNetNameIndex(GateModule? module)
    {
        Dictionary<int, string> result = [];
        if (module is null)
        {
            return result;
        }
        foreach (GateNet net in module.Nets)
        {
            foreach (GateBit bit in net.Bits)
            {
                if (bit.Kind == BitKind.Net)
                {
                    result.TryAdd(bit.NetId, net.Name);
                }
            }
        }
        return result;
    }

    private static IEnumerable<(ElkNode Node, double AbsoluteX, double AbsoluteY)> EnumerateNodes(
        IReadOnlyList<ElkNode> nodes,
        double baseX = 0,
        double baseY = 0)
    {
        foreach (ElkNode node in nodes)
        {
            double absoluteX = baseX + node.X;
            double absoluteY = baseY + node.Y;
            yield return (node, absoluteX, absoluteY);
            if (node.Children is { Count: > 0 })
            {
                foreach (var child in EnumerateNodes(node.Children, absoluteX, absoluteY))
                {
                    yield return child;
                }
            }
        }
    }

    private sealed record GatePinDraft(
        ElkPort Port,
        string PinName,
        string BaseName,
        int? BitIndex,
        GatePortDirection Direction);

    private sealed record GatePinRange(int Width, int? Msb, int? Lsb)
    {
        public static GatePinRange From(IEnumerable<GatePinDraft> drafts)
        {
            GatePinDraft[] values = drafts.ToArray();
            int[] indices = values
                .Where(static draft => draft.BitIndex.HasValue)
                .Select(static draft => draft.BitIndex!.Value)
                .ToArray();
            return new GatePinRange(
                values.Length,
                indices.Length > 0 ? indices.Max() : null,
                indices.Length > 0 ? indices.Min() : null);
        }
    }
}

internal static class GatePinName
{
    public static (string BaseName, int? BitIndex) Parse(string displayName)
    {
        int open = displayName.LastIndexOf('[');
        if (open <= 0 || !displayName.EndsWith(']'))
        {
            return (displayName, null);
        }

        return int.TryParse(displayName.AsSpan(open + 1, displayName.Length - open - 2), out int index)
            ? (displayName[..open], index)
            : (displayName, null);
    }
}
