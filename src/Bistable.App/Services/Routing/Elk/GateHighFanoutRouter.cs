namespace Bistable.App.Services.Routing.Elk;

/// <summary>
/// Rewrites extreme scalar fanout into an invisible balanced splitter tree.
/// ELK.js 0.9.x does not support the multi-target hyperedges needed for a
/// logical net, so bounded-degree synthetic nodes keep layered routing costs
/// predictable while every segment retains the original net identity.
/// </summary>
internal sealed class GateHighFanoutRouter
{
    public const int Threshold = 64;
    public const int BranchingFactor = 16;

    private readonly Dictionary<string, ElkPortRef> _portRefs;
    private readonly FanoutHierarchyIndex _hierarchy;
    private int _hubSequence;

    public GateHighFanoutRouter(
        ElkGraph graph,
        Dictionary<string, ElkPortRef> portRefs)
    {
        _portRefs = portRefs;
        _hierarchy = FanoutHierarchyIndex.Build(graph);
    }

    public void Emit(
        List<ElkEdge> edges,
        ElkPortRef source,
        IReadOnlyList<ElkPortRef> targets,
        string netId,
        string netKey,
        ref int edgeSequence)
    {
        List<ElkNode> ownerChildren =
            _hierarchy.FindDeepestCommonOwnerChildren(source, targets);
        int nextEdgeSequence = edgeSequence;
        Connect(source.PortId, targets);
        edgeSequence = nextEdgeSequence;
        return;

        void Connect(string physicalSourcePortId, IReadOnlyList<ElkPortRef> branchTargets)
        {
            if (branchTargets.Count <= BranchingFactor)
            {
                foreach (ElkPortRef target in branchTargets)
                {
                    edges.Add(CreateEdge(
                        physicalSourcePortId,
                        target.PortId,
                        netId,
                        ref nextEdgeSequence));
                }
                return;
            }

            int groupCount = Math.Min(
                BranchingFactor,
                (branchTargets.Count + BranchingFactor - 1) / BranchingFactor);
            int offset = 0;
            for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
            {
                int remaining = branchTargets.Count - offset;
                int groupsLeft = groupCount - groupIndex;
                int groupSize = (remaining + groupsLeft - 1) / groupsLeft;
                IReadOnlyList<ElkPortRef> group =
                    branchTargets.Skip(offset).Take(groupSize).ToArray();
                offset += groupSize;

                ElkNode hub = CreateHub(netKey, _hubSequence++);
                ownerChildren.Add(hub);
                ElkPort input = hub.Ports![0];
                ElkPort output = hub.Ports[1];
                _portRefs[input.Id] = new ElkPortRef(
                    hub.Id, input.Id, ElkPortRole.SplitterInput, Width: 1);
                _portRefs[output.Id] = new ElkPortRef(
                    hub.Id, output.Id, ElkPortRole.SplitterOutput, Width: 1);
                edges.Add(CreateEdge(
                    physicalSourcePortId,
                    input.Id,
                    netId,
                    ref nextEdgeSequence));
                Connect(output.Id, group);
            }
        }
    }

    private static ElkNode CreateHub(string netKey, int sequence)
    {
        string nodeId = $"{GateSyntheticNodeIds.FanoutHubPrefix}{netKey}_{sequence}";
        return new ElkNode
        {
            Id = nodeId,
            Width = 2,
            Height = 2,
            LayoutOptions = new Dictionary<string, string>
            {
                ["elk.portConstraints"] = "FIXED_ORDER",
                ["elk.padding"] = "[top=0,left=0,right=0,bottom=0]",
            },
            Ports =
            [
                new ElkPort
                {
                    Id = nodeId + ".in",
                    LayoutOptions = PortLayout("WEST"),
                },
                new ElkPort
                {
                    Id = nodeId + ".out",
                    LayoutOptions = PortLayout("EAST"),
                },
            ],
        };
    }

    private static ElkEdge CreateEdge(
        string sourcePortId,
        string targetPortId,
        string netId,
        ref int edgeSequence) =>
        new()
        {
            Id = $"e{edgeSequence++}",
            Sources = [sourcePortId],
            Targets = [targetPortId],
            LayoutOptions = new Dictionary<string, string>
            {
                [GateEdgeMetadataKeys.NetIdLayoutOption] = netId,
                [GateEdgeMetadataKeys.SyntheticFanoutLayoutOption] = "true",
            },
        };

    private static Dictionary<string, string> PortLayout(string side) =>
        new()
        {
            ["elk.port.side"] = side,
            ["elk.port.index"] = "0",
        };

    private sealed class FanoutHierarchyIndex
    {
        private readonly List<ElkNode> _rootChildren;
        private readonly IReadOnlyDictionary<string, ElkNode> _nodes;
        private readonly IReadOnlyDictionary<string, string[]> _nodePaths;
        private readonly IReadOnlyDictionary<string, string> _portOwners;

        private FanoutHierarchyIndex(
            List<ElkNode> rootChildren,
            IReadOnlyDictionary<string, ElkNode> nodes,
            IReadOnlyDictionary<string, string[]> nodePaths,
            IReadOnlyDictionary<string, string> portOwners)
        {
            _rootChildren = rootChildren;
            _nodes = nodes;
            _nodePaths = nodePaths;
            _portOwners = portOwners;
        }

        public static FanoutHierarchyIndex Build(ElkGraph graph)
        {
            Dictionary<string, ElkNode> nodes = new(StringComparer.Ordinal);
            Dictionary<string, string[]> nodePaths = new(StringComparer.Ordinal);
            Dictionary<string, string> portOwners = new(StringComparer.Ordinal);
            Visit(graph.Children, []);
            return new FanoutHierarchyIndex(graph.Children, nodes, nodePaths, portOwners);

            void Visit(IReadOnlyList<ElkNode> current, string[] parentPath)
            {
                foreach (ElkNode node in current)
                {
                    string[] path = [.. parentPath, node.Id];
                    nodes[node.Id] = node;
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

        public List<ElkNode> FindDeepestCommonOwnerChildren(
            ElkPortRef source,
            IReadOnlyList<ElkPortRef> targets)
        {
            string[] commonPath = GetPath(source.PortId);
            foreach (ElkPortRef target in targets)
            {
                commonPath = CommonPrefix(commonPath, GetPath(target.PortId));
                if (commonPath.Length == 0)
                {
                    return _rootChildren;
                }
            }

            for (int i = commonPath.Length - 1; i >= 0; i--)
            {
                if (_nodes.TryGetValue(commonPath[i], out ElkNode? node)
                    && node.Children is not null)
                {
                    return node.Children;
                }
            }
            return _rootChildren;
        }

        private string[] GetPath(string portId) =>
            _portOwners.TryGetValue(portId, out string? owner)
            && _nodePaths.TryGetValue(owner, out string[]? path)
                ? path
                : [];

        private static string[] CommonPrefix(string[] left, string[] right)
        {
            int length = Math.Min(left.Length, right.Length);
            int common = 0;
            while (common < length
                && string.Equals(left[common], right[common], StringComparison.Ordinal))
            {
                common++;
            }
            return left[..common];
        }
    }
}
