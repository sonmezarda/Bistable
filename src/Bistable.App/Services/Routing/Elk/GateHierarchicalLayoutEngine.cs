namespace Bistable.App.Services.Routing.Elk;

/// <summary>
/// Routes large expanded gate scopes from the leaves upward. ELK sees each
/// completed child compound as a fixed-size macro while routing its parent,
/// avoiding one monolithic solve across every hierarchy level.
/// </summary>
public sealed class GateHierarchicalLayoutEngine
{
    private const string InstanceNodePrefix = "inst_";

    private readonly Func<ElkGraph, CancellationToken, Task<ElkGraph>> _layoutAsync;
    private readonly GateLevelLayoutCache? _layoutCache;

    public GateHierarchicalLayoutEngine(
        SchematicLayoutService layoutService,
        GateLevelLayoutCache? layoutCache = null)
        : this(layoutService.LayoutAsync, layoutCache)
    {
    }

    internal GateHierarchicalLayoutEngine(
        Func<ElkGraph, CancellationToken, Task<ElkGraph>> layoutAsync,
        GateLevelLayoutCache? layoutCache = null)
    {
        _layoutAsync = layoutAsync ?? throw new ArgumentNullException(nameof(layoutAsync));
        _layoutCache = layoutCache;
    }

    public Task<ElkGraph> LayoutAsync(
        ElkGraph input,
        bool useHierarchicalLayout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        return useHierarchicalLayout && FindExpandedCompounds(input.Children).Count > 0
            ? LayoutHierarchicallyAsync(input, cancellationToken)
            : _layoutAsync(input, cancellationToken);
    }

    private async Task<ElkGraph> LayoutHierarchicallyAsync(
        ElkGraph input,
        CancellationToken cancellationToken)
    {
        HierarchyIndex index = HierarchyIndex.Build(input.Children);
        IReadOnlyList<ElkNode> topCompounds = FindExpandedCompounds(input.Children);
        Dictionary<string, CompoundLayout> compoundLayouts = new(StringComparer.Ordinal);
        foreach (ElkNode compound in topCompounds)
        {
            compoundLayouts[compound.Id] = await LayoutCompoundAsync(
                compound,
                input.Edges,
                index,
                cancellationToken).ConfigureAwait(false);
        }

        HashSet<string> internalEdgeIds = compoundLayouts.Values
            .SelectMany(static result => result.InternalEdges)
            .Select(static edge => edge.Id)
            .ToHashSet(StringComparer.Ordinal);
        ElkGraph parentInput = new()
        {
            Id = input.Id,
            LayoutOptions = CloneOptions(input.LayoutOptions),
            Children =
            [
                .. input.Children.Select(node =>
                    compoundLayouts.TryGetValue(node.Id, out CompoundLayout? result)
                        ? CloneCollapsedCompound(result.Node)
                        : CloneNode(node)),
            ],
            Edges =
            [
                .. input.Edges
                    .Where(edge => !internalEdgeIds.Contains(edge.Id))
                    .Select(CloneEdgeWithoutLayout),
            ],
        };

        ElkGraph parentLayout =
            await _layoutAsync(parentInput, cancellationToken).ConfigureAwait(false);
        foreach ((string nodeId, CompoundLayout result) in compoundLayouts)
        {
            ElkNode? parentNode = FindNode(parentLayout.Children, nodeId);
            if (parentNode is not null)
            {
                RestoreCompoundContents(parentNode, result.Node);
            }
        }
        parentLayout.Edges.AddRange(
            compoundLayouts.Values.SelectMany(static result => result.InternalEdges));
        return parentLayout;
    }

    private async Task<CompoundLayout> LayoutCompoundAsync(
        ElkNode compound,
        IReadOnlyList<ElkEdge> allEdges,
        HierarchyIndex index,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ElkNode> directCompounds = FindExpandedCompounds(compound.Children);
        Dictionary<string, CompoundLayout> childLayouts = new(StringComparer.Ordinal);
        foreach (ElkNode child in directCompounds)
        {
            childLayouts[child.Id] = await LayoutCompoundAsync(
                child,
                allEdges,
                index,
                cancellationToken).ConfigureAwait(false);
        }

        HashSet<string> nestedInternalEdgeIds = childLayouts.Values
            .SelectMany(static result => result.InternalEdges)
            .Select(static edge => edge.Id)
            .ToHashSet(StringComparer.Ordinal);
        List<ElkEdge> stageEdges =
        [
            .. allEdges
                .Where(edge =>
                    index.IsInternalTo(compound.Id, edge)
                    && !nestedInternalEdgeIds.Contains(edge.Id))
                .Select(CloneEdgeWithoutLayout),
        ];
        ElkNode stageCompound = CloneNode(
            compound,
            node => childLayouts.TryGetValue(node.Id, out CompoundLayout? result)
                ? CloneCollapsedCompound(result.Node)
                : null);
        ElkGraph stageInput = new()
        {
            Id = "stage_" + compound.Id,
            LayoutOptions = CloneOptions(compound.LayoutOptions),
            Children = [stageCompound],
            Edges = stageEdges,
        };

        string? cacheKey = _layoutCache is null
            ? null
            : GateLevelLayoutCache.ComputeCompoundFingerprint(stageInput);
        if (cacheKey is not null
            && _layoutCache!.TryGetCompound(cacheKey, out GateCompoundLayoutCacheEntry? cached)
            && cached is not null)
        {
            return new CompoundLayout(cached.Node, cached.InternalEdges);
        }

        ElkGraph stageLayout =
            await _layoutAsync(stageInput, cancellationToken).ConfigureAwait(false);
        ElkNode laidCompound = stageLayout.Children.Single();
        laidCompound.X = 0;
        laidCompound.Y = 0;
        foreach ((string nodeId, CompoundLayout result) in childLayouts)
        {
            ElkNode? childNode = FindNode(laidCompound.Children, nodeId);
            if (childNode is not null)
            {
                RestoreCompoundContents(childNode, result.Node);
            }
        }

        List<ElkEdge> internalEdges =
        [
            .. stageLayout.Edges,
            .. childLayouts.Values.SelectMany(static result => result.InternalEdges),
        ];
        CompoundLayout completed = new(laidCompound, internalEdges);
        if (cacheKey is not null)
        {
            _layoutCache!.StoreCompound(
                cacheKey,
                new GateCompoundLayoutCacheEntry(completed.Node, completed.InternalEdges));
        }
        return completed;
    }

    private static IReadOnlyList<ElkNode> FindExpandedCompounds(
        IReadOnlyList<ElkNode>? nodes) =>
        nodes is null
            ? []
            : [.. nodes.Where(static node =>
                node.Id.StartsWith(InstanceNodePrefix, StringComparison.Ordinal)
                && node.Children is { Count: > 0 })];

    private static void RestoreCompoundContents(ElkNode target, ElkNode laidCompound)
    {
        target.Children = laidCompound.Children;
        target.Width = laidCompound.Width;
        target.Height = laidCompound.Height;
        if (laidCompound.Ports is not null)
        {
            IReadOnlyDictionary<string, ElkPort> ports =
                laidCompound.Ports.ToDictionary(static port => port.Id, StringComparer.Ordinal);
            if (target.Ports is not null)
            {
                foreach (ElkPort targetPort in target.Ports)
                {
                    if (!ports.TryGetValue(targetPort.Id, out ElkPort? source)) continue;
                    targetPort.X = source.X;
                    targetPort.Y = source.Y;
                }
            }
        }
    }

    private static ElkNode CloneCollapsedCompound(ElkNode source)
    {
        ElkNode clone = CloneNode(source);
        clone.Children = null;
        clone.LayoutOptions ??= [];
        clone.LayoutOptions["elk.portConstraints"] = "FIXED_POS";
        return clone;
    }

    private static ElkNode CloneNode(
        ElkNode source,
        Func<ElkNode, ElkNode?>? replacement = null)
    {
        if (replacement?.Invoke(source) is { } replaced)
        {
            return replaced;
        }

        return new ElkNode
        {
            Id = source.Id,
            Width = source.Width,
            Height = source.Height,
            X = source.X,
            Y = source.Y,
            LayoutOptions = CloneOptions(source.LayoutOptions),
            Labels = source.Labels is null ? null : [.. source.Labels.Select(CloneLabel)],
            Ports = source.Ports is null ? null : [.. source.Ports.Select(ClonePort)],
            Children = source.Children is null
                ? null
                : [.. source.Children.Select(child => CloneNode(child, replacement))],
            Edges = source.Edges is null
                ? null
                : [.. source.Edges.Select(CloneEdgeWithoutLayout)],
        };
    }

    private static ElkPort ClonePort(ElkPort source) => new()
    {
        Id = source.Id,
        Width = source.Width,
        Height = source.Height,
        X = source.X,
        Y = source.Y,
        LayoutOptions = CloneOptions(source.LayoutOptions),
        Labels = source.Labels is null ? null : [.. source.Labels.Select(CloneLabel)],
    };

    private static ElkLabel CloneLabel(ElkLabel source) => new()
    {
        Text = source.Text,
        Width = source.Width,
        Height = source.Height,
        X = source.X,
        Y = source.Y,
    };

    private static ElkEdge CloneEdgeWithoutLayout(ElkEdge source) => new()
    {
        Id = source.Id,
        Sources = [.. source.Sources],
        Targets = [.. source.Targets],
        Labels = source.Labels is null ? null : [.. source.Labels.Select(CloneLabel)],
        LayoutOptions = CloneOptions(source.LayoutOptions),
    };

    private static Dictionary<string, string>? CloneOptions(
        IReadOnlyDictionary<string, string>? source) =>
        source is null
            ? null
            : source.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);

    private static ElkNode? FindNode(IReadOnlyList<ElkNode>? nodes, string id)
    {
        if (nodes is null) return null;
        foreach (ElkNode node in nodes)
        {
            if (string.Equals(node.Id, id, StringComparison.Ordinal)) return node;
            if (FindNode(node.Children, id) is { } nested) return nested;
        }
        return null;
    }

    private sealed record CompoundLayout(
        ElkNode Node,
        IReadOnlyList<ElkEdge> InternalEdges);

    private sealed class HierarchyIndex
    {
        private readonly IReadOnlyDictionary<string, string[]> _nodePaths;
        private readonly IReadOnlyDictionary<string, string> _portOwners;

        private HierarchyIndex(
            IReadOnlyDictionary<string, string[]> nodePaths,
            IReadOnlyDictionary<string, string> portOwners)
        {
            _nodePaths = nodePaths;
            _portOwners = portOwners;
        }

        public static HierarchyIndex Build(IReadOnlyList<ElkNode> nodes)
        {
            Dictionary<string, string[]> nodePaths = new(StringComparer.Ordinal);
            Dictionary<string, string> portOwners = new(StringComparer.Ordinal);
            Visit(nodes, []);
            return new HierarchyIndex(nodePaths, portOwners);

            void Visit(IReadOnlyList<ElkNode> current, string[] parentPath)
            {
                foreach (ElkNode node in current)
                {
                    string[] path = [.. parentPath, node.Id];
                    nodePaths[node.Id] = path;
                    if (node.Ports is not null)
                    {
                        foreach (ElkPort port in node.Ports)
                        {
                            portOwners[port.Id] = node.Id;
                        }
                    }
                    if (node.Children is { Count: > 0 })
                    {
                        Visit(node.Children, path);
                    }
                }
            }
        }

        public bool IsInternalTo(string compoundId, ElkEdge edge)
        {
            bool hasEndpoint = false;
            foreach (string endpoint in edge.Sources.Concat(edge.Targets))
            {
                if (!_portOwners.TryGetValue(endpoint, out string? owner)
                    || !_nodePaths.TryGetValue(owner, out string[]? path)
                    || !path.Contains(compoundId, StringComparer.Ordinal))
                {
                    return false;
                }
                hasEndpoint = true;
            }
            return hasEndpoint;
        }
    }
}
