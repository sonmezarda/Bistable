using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bistable.App.ViewModels;
using Bistable.Core.Design.Schematic;

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
    private readonly SchematicLayoutService _layoutService;
    private readonly object _cacheGate = new();
    private readonly LinkedList<CacheEntry> _cache = new();
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _cacheIndex = new(StringComparer.Ordinal);

    public ElkSchematicEngine()
        : this(new SchematicLayoutService())
    {
    }

    public ElkSchematicEngine(SchematicLayoutService layoutService)
    {
        _layoutService = layoutService ?? throw new ArgumentNullException(nameof(layoutService));
    }

    public async Task<ElkLayoutResult> ComputeAsync(
        ElkScopeData scope,
        bool compactLayout,
        CancellationToken cancellationToken = default)
    {
        string key = GetCacheKey(scope, compactLayout);
        CacheEntry? cached = TryGetCached(key);
        if (cached is not null)
        {
            if (cached.Result is { } result)
            {
                return result;
            }

            if (cached.Error is { } error)
            {
                throw new SchematicRoutingException(error);
            }
        }

        try
        {
            ElkBuildResult build = await Task.Run(
                () => _builder.Build(scope, compactLayout),
                cancellationToken).ConfigureAwait(false);
            ElkGraph layouted = await _layoutService.LayoutAsync(
                build.Graph,
                cancellationToken).ConfigureAwait(false);
            ElkLayoutResult result = new(layouted, build.PortRefs);
            StoreInCache(key, new CacheEntry(result, null));
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SchematicRoutingException ex)
        {
            StoreInCache(key, new CacheEntry(null, ex.Message));
            throw;
        }
    }

    private void StoreInCache(string key, CacheEntry entry)
    {
        lock (_cacheGate)
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
                string? staleKey = _cacheIndex.FirstOrDefault(kv => ReferenceEquals(kv.Value, oldest)).Key;
                if (staleKey is not null) _cacheIndex.Remove(staleKey);
            }
        }
    }

    private CacheEntry? TryGetCached(string key)
    {
        lock (_cacheGate)
        {
            if (!_cacheIndex.TryGetValue(key, out LinkedListNode<CacheEntry>? hit))
            {
                return null;
            }

            _cache.Remove(hit);
            _cache.AddFirst(hit);
            return hit.Value;
        }
    }

    private sealed record CacheEntry(ElkLayoutResult? Result, string? Error);

    public static string GetCacheKey(ElkScopeData scope, bool compactLayout)
    {
        StringBuilder sb = new();
        sb.Append(compactLayout ? "C|" : "N|");
        AppendExpandedPaths(sb, scope.ExpandedPaths);
        AppendBoundaryPorts(sb, scope.BoundaryPorts);
        AppendChildScopes(sb, scope.ChildScopes);
        AppendLocalSignals(sb, scope.LocalSignals);
        AppendContAssigns(sb, scope.ContAssigns);
        AppendPrimitives(sb, scope.Primitives);
        AppendPrimitiveCatalog(sb, scope.PrimitivesByModule);

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

    private static void AppendPrimitives(
        StringBuilder sb,
        IReadOnlyList<SchematicPrimitive>? primitives)
    {
        if (primitives is null) return;
        foreach (SchematicPrimitive primitive in primitives.OrderBy(static p => p.Id, StringComparer.Ordinal))
        {
            sb.Append("R:")
                .Append(primitive.GetType().Name)
                .Append(':')
                .Append(JsonSerializer.Serialize(primitive, primitive.GetType()))
                .Append('|');
        }
    }

    private static void AppendPrimitiveCatalog(
        StringBuilder sb,
        IReadOnlyDictionary<string, IReadOnlyList<SchematicPrimitive>>? catalog)
    {
        if (catalog is null) return;
        foreach ((string moduleName, IReadOnlyList<SchematicPrimitive> primitives) in
                 catalog.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append("RM:").Append(moduleName).Append('|');
            AppendPrimitives(sb, primitives);
        }
    }
}

public sealed record ElkLayoutResult(ElkGraph Graph, IReadOnlyDictionary<string, ElkPortRef> PortRefs);
