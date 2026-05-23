using System.Security.Cryptography;
using System.Text;
using Bistable.App.ViewModels;

namespace Bistable.App.Services.Routing.Elk;

/// <summary>
/// Orchestrates the ELK pipeline:
///   scope view-models → ElkGraphBuilder → ElkRunner (Node subprocess) → cached layout.
/// </summary>
public sealed class ElkSchematicEngine
{
    // Multiple cache slots so that navigating back-and-forth between a few scopes does
    // not force a fresh ELK layout each time. The capacity is small because each entry
    // can hold a large laid-out graph; 8 covers typical drill-down sessions.
    private const int CacheCapacity = 8;

    private readonly ElkGraphBuilder _builder = new();
    private readonly ElkRunner _runner;
    private readonly LinkedList<CacheEntry> _cache = new();
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _cacheIndex = new(StringComparer.Ordinal);

    public ElkSchematicEngine()
        : this(new ElkRunner())
    {
    }

    public ElkSchematicEngine(ElkRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public ElkLayoutResult Compute(ElkScopeData scope, bool compactLayout)
    {
        string key = ComputeCacheKey(scope, compactLayout);
        if (_cacheIndex.TryGetValue(key, out LinkedListNode<CacheEntry>? hit))
        {
            // LRU bump: most-recently-used at the front so eviction targets stale entries.
            _cache.Remove(hit);
            _cache.AddFirst(hit);
            if (hit.Value.Result is { } cached)
            {
                return cached;
            }

            if (hit.Value.Error is { } error)
            {
                throw new SchematicRoutingException(error);
            }
        }

        try
        {
            ElkBuildResult build = _builder.Build(scope, compactLayout);
            ElkGraph layouted = _runner.Layout(build.Graph);
            ElkLayoutResult result = new(layouted, build.PortRefs);
            StoreInCache(key, new CacheEntry(result, null));
            return result;
        }
        catch (SchematicRoutingException ex)
        {
            StoreInCache(key, new CacheEntry(null, ex.Message));
            throw;
        }
    }

    private void StoreInCache(string key, CacheEntry entry)
    {
        if (_cacheIndex.TryGetValue(key, out LinkedListNode<CacheEntry>? existing))
        {
            _cache.Remove(existing);
            _cacheIndex.Remove(key);
        }

        LinkedListNode<CacheEntry> node = _cache.AddFirst(entry);
        _cacheIndex[key] = node;

        while (_cache.Count > CacheCapacity)
        {
            LinkedListNode<CacheEntry>? oldest = _cache.Last;
            if (oldest is null) break;
            _cache.RemoveLast();
            // CacheEntry doesn't track the key, so we just clear-and-rebuild the index
            // periodically. For CacheCapacity=8 the linear scan is trivial.
            string? staleKey = _cacheIndex.FirstOrDefault(kv => ReferenceEquals(kv.Value, oldest)).Key;
            if (staleKey is not null) _cacheIndex.Remove(staleKey);
        }
    }

    private sealed record CacheEntry(ElkLayoutResult? Result, string? Error);

    private static string ComputeCacheKey(ElkScopeData scope, bool compactLayout)
    {
        StringBuilder sb = new();
        sb.Append(compactLayout ? "C|" : "N|");
        AppendExpandedPaths(sb, scope.ExpandedPaths);
        AppendBoundaryPorts(sb, scope.BoundaryPorts);
        AppendChildScopes(sb, scope.ChildScopes);
        AppendLocalSignals(sb, scope.LocalSignals);
        AppendContAssigns(sb, scope.ContAssigns);

        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash);
    }

    private static void AppendExpandedPaths(StringBuilder sb, IReadOnlySet<string>? expanded)
    {
        if (expanded is null || expanded.Count == 0) return;
        foreach (string path in expanded.OrderBy(static p => p, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append("X:").Append(path).Append('|');
        }
    }

    private static void AppendBoundaryPorts(StringBuilder sb, IReadOnlyList<HierarchyScopePortViewModel> ports)
    {
        foreach (HierarchyScopePortViewModel port in ports.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append("B:").Append(port.Name).Append(':').Append(port.Direction).Append(':').Append(port.Width).Append('|');
        }
    }

    private static void AppendChildScopes(StringBuilder sb, IReadOnlyList<HierarchyScopeInstanceViewModel> children)
    {
        foreach (HierarchyScopeInstanceViewModel child in children.OrderBy(c => c.HierarchyPath, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append("M:").Append(child.HierarchyPath).Append(':').Append(child.ModuleName).Append('|');
            foreach (HierarchyScopeInstancePortConnectionViewModel pin in child.PortConnections
                         .OrderBy(p => p.PortName, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append("P:").Append(pin.PortName).Append(':')
                    .Append(pin.IsInput ? "i" : "o").Append(':')
                    .Append(pin.SignalName).Append(':').Append(pin.Width).Append('|');
            }
        }
    }

    private static void AppendLocalSignals(StringBuilder sb, IReadOnlyList<HierarchyScopeLocalSignalViewModel> locals)
    {
        foreach (HierarchyScopeLocalSignalViewModel local in locals.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append("L:").Append(local.Name).Append(':').Append(local.Width).Append('|');
        }
    }

    private static void AppendContAssigns(StringBuilder sb, IReadOnlyList<Bistable.Core.Design.DesignContAssign> assigns)
    {
        foreach (Bistable.Core.Design.DesignContAssign assign in assigns.OrderBy(a => a.TargetName, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append("A:").Append(assign.TargetName).Append(':').Append(assign.OperatorSymbol ?? "");
            if (assign.SourceRange.HasValue)
            {
                sb.Append(':').Append(assign.SourceRange.Value.Hi).Append('-').Append(assign.SourceRange.Value.Lo);
            }

            sb.Append(':');
            foreach (string source in assign.SourceNames.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append(source).Append(',');
            }

            sb.Append('|');
        }
    }
}

public sealed record ElkLayoutResult(ElkGraph Graph, IReadOnlyDictionary<string, ElkPortRef> PortRefs);
