using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Bistable.Core.Projects;
using Bistable.Core.Synthesis;

namespace Bistable.App.Services.Routing.Elk;

/// <summary>
/// Synthesis-session LRU for completed gate-level layouts. The cache is shared
/// by all gate documents opened from one netlist, while its structural
/// fingerprint prevents results from crossing synthesis artifacts.
/// </summary>
public sealed class GateLevelLayoutCache
{
    private const string GeometryVersion = "gate-layout-v3";
    private const int DefaultCapacity = 6;
    private const int DefaultComplexityBudget = 120_000;

    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly int _complexityBudget;
    private readonly string _netlistFingerprint;
    private readonly LinkedList<CacheItem> _lru = [];
    private readonly Dictionary<GateLevelLayoutCacheKey, LinkedListNode<CacheItem>> _index = [];
    private readonly LinkedList<CompoundCacheItem> _compoundLru = [];
    private readonly Dictionary<string, LinkedListNode<CompoundCacheItem>> _compoundIndex =
        new(StringComparer.Ordinal);
    private int _totalComplexity;
    private int _compoundComplexity;

    public GateLevelLayoutCache(
        GateNetlist netlist,
        int capacity = DefaultCapacity,
        int complexityBudget = DefaultComplexityBudget)
    {
        ArgumentNullException.ThrowIfNull(netlist);
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
        }
        if (complexityBudget < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(complexityBudget),
                complexityBudget,
                "Complexity budget must be positive.");
        }

        _capacity = capacity;
        _complexityBudget = complexityBudget;
        _netlistFingerprint = ComputeNetlistFingerprint(netlist);
    }

    public GateLevelLayoutCacheKey CreateKey(
        IReadOnlyList<string> scopePath,
        IReadOnlySet<string> expandedInstancePaths,
        RoutingQuality requestedQuality,
        bool autoDowngradeLargeGraphs)
    {
        ArgumentNullException.ThrowIfNull(scopePath);
        ArgumentNullException.ThrowIfNull(expandedInstancePaths);

        return new GateLevelLayoutCacheKey(
            _netlistFingerprint,
            EncodeSequence(scopePath),
            EncodeSequence(expandedInstancePaths.Order(StringComparer.Ordinal)),
            requestedQuality,
            autoDowngradeLargeGraphs,
            GeometryVersion);
    }

    public bool TryGet(
        GateLevelLayoutCacheKey key,
        out GateLevelLayoutCacheEntry? entry)
    {
        lock (_gate)
        {
            if (!_index.TryGetValue(key, out LinkedListNode<CacheItem>? node))
            {
                entry = null;
                return false;
            }

            _lru.Remove(node);
            _lru.AddFirst(node);
            entry = node.Value.Entry;
            return true;
        }
    }

    public void Store(
        GateLevelLayoutCacheKey key,
        GateLevelLayoutCacheEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        int complexity = MeasureComplexity(entry.Metrics);

        lock (_gate)
        {
            if (_index.Remove(key, out LinkedListNode<CacheItem>? existing))
            {
                _lru.Remove(existing);
                _totalComplexity -= existing.Value.Complexity;
            }

            LinkedListNode<CacheItem> node =
                _lru.AddFirst(new CacheItem(key, entry, complexity));
            _index[key] = node;
            _totalComplexity += complexity;

            while (_lru.Count > 1
                   && (_lru.Count > _capacity || _totalComplexity > _complexityBudget))
            {
                RemoveOldest();
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _lru.Count;
            }
        }
    }

    internal bool TryGetCompound(
        string key,
        out GateCompoundLayoutCacheEntry? entry)
    {
        lock (_gate)
        {
            if (!_compoundIndex.TryGetValue(key, out LinkedListNode<CompoundCacheItem>? node))
            {
                entry = null;
                return false;
            }

            _compoundLru.Remove(node);
            _compoundLru.AddFirst(node);
            entry = node.Value.Entry;
            return true;
        }
    }

    internal void StoreCompound(
        string key,
        GateCompoundLayoutCacheEntry entry)
    {
        int complexity = MeasureCompoundComplexity(entry);
        lock (_gate)
        {
            if (_compoundIndex.Remove(key, out LinkedListNode<CompoundCacheItem>? existing))
            {
                _compoundLru.Remove(existing);
                _compoundComplexity -= existing.Value.Complexity;
            }

            LinkedListNode<CompoundCacheItem> node =
                _compoundLru.AddFirst(new CompoundCacheItem(key, entry, complexity));
            _compoundIndex[key] = node;
            _compoundComplexity += complexity;
            while (_compoundLru.Count > 1
                   && (_compoundLru.Count > _capacity
                       || _compoundComplexity > _complexityBudget))
            {
                LinkedListNode<CompoundCacheItem>? oldest = _compoundLru.Last;
                if (oldest is null) break;
                _compoundLru.RemoveLast();
                _compoundIndex.Remove(oldest.Value.Key);
                _compoundComplexity -= oldest.Value.Complexity;
            }
        }
    }

    internal static string ComputeCompoundFingerprint(ElkGraph graph)
    {
        string json = ElkRunner.SerializeForDebug(graph);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private void RemoveOldest()
    {
        LinkedListNode<CacheItem>? oldest = _lru.Last;
        if (oldest is null) return;

        _lru.RemoveLast();
        _index.Remove(oldest.Value.Key);
        _totalComplexity -= oldest.Value.Complexity;
    }

    private static int MeasureComplexity(SchematicGraphMetrics metrics) =>
        checked(metrics.NodeCount + metrics.PortCount + metrics.EdgeCount);

    private static int MeasureCompoundComplexity(GateCompoundLayoutCacheEntry entry)
    {
        SchematicGraphMetrics metrics = SchematicGraphMetrics.Measure(new ElkGraph
        {
            Children = [entry.Node],
            Edges = [.. entry.InternalEdges],
        });
        return MeasureComplexity(metrics);
    }

    private static string EncodeSequence(IEnumerable<string> values)
    {
        StringBuilder builder = new();
        foreach (string value in values)
        {
            builder.Append(value.Length.ToString(CultureInfo.InvariantCulture))
                .Append(':')
                .Append(value)
                .Append('|');
        }
        return builder.ToString();
    }

    internal static string ComputeNetlistFingerprint(GateNetlist netlist)
    {
        StringBuilder builder = new();
        Append(builder, netlist.TopModule);
        foreach ((string moduleName, GateModule module) in
                 netlist.Modules.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            Append(builder, moduleName);
            Append(builder, module.Name);
            // Port/cell order is geometry-significant because the builder
            // assigns fixed port indices and deterministic node ids from it.
            foreach (GatePort port in module.Ports)
            {
                Append(builder, port.Name);
                Append(builder, port.Direction.ToString());
                AppendBits(builder, port.Bits);
            }
            foreach (GateCell cell in module.Cells)
            {
                Append(builder, cell.Name);
                Append(builder, cell.Type);
                foreach ((string name, GateConnection connection) in
                         cell.Connections.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                {
                    Append(builder, name);
                    Append(builder, connection.PortName);
                    AppendBits(builder, connection.Bits);
                }
                AppendMap(builder, cell.PortDirections);
                AppendMap(builder, cell.Parameters);
                AppendMap(builder, cell.Attributes);
            }
            foreach (GateNet net in module.Nets)
            {
                Append(builder, net.Name);
                AppendBits(builder, net.Bits);
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void AppendMap<T>(StringBuilder builder, IReadOnlyDictionary<string, T> values)
    {
        foreach ((string key, T value) in values.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            Append(builder, key);
            Append(builder, value?.ToString() ?? string.Empty);
        }
    }

    private static void AppendBits(StringBuilder builder, IReadOnlyList<GateBit> bits)
    {
        builder.Append(bits.Count.ToString(CultureInfo.InvariantCulture)).Append('[');
        foreach (GateBit bit in bits)
        {
            builder.Append((int)bit.Kind)
                .Append(':')
                .Append(bit.NetId.ToString(CultureInfo.InvariantCulture))
                .Append(',');
        }
        builder.Append(']');
    }

    private static void Append(StringBuilder builder, string value)
    {
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value)
            .Append('|');
    }

    private sealed record CacheItem(
        GateLevelLayoutCacheKey Key,
        GateLevelLayoutCacheEntry Entry,
        int Complexity);

    private sealed record CompoundCacheItem(
        string Key,
        GateCompoundLayoutCacheEntry Entry,
        int Complexity);
}

public sealed record GateLevelLayoutCacheKey(
    string NetlistFingerprint,
    string ScopePath,
    string ExpandedInstancePaths,
    RoutingQuality RequestedQuality,
    bool AutoDowngradeLargeGraphs,
    string GeometryVersion);

public sealed record GateLevelLayoutCacheEntry(
    ElkGraph Graph,
    SchematicLayoutDecision Decision,
    SchematicGraphMetrics Metrics,
    IReadOnlyList<GateBusBundle> Bundles);

internal sealed record GateCompoundLayoutCacheEntry(
    ElkNode Node,
    IReadOnlyList<ElkEdge> InternalEdges);
