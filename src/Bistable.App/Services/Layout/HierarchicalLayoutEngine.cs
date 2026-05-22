using Bistable.App.ViewModels;

namespace Bistable.App.Services.Layout;

/// <summary>
/// Sugiyama-style hierarchical layout for child module ordering:
/// (1) build directed connectivity graph, (2) break cycles, (3) rank assignment via
/// longest-path BFS, (4) barycenter crossing minimization.
/// Returns children reordered for left-to-right data-flow display.
/// </summary>
internal static class HierarchicalLayoutEngine
{
    private const int MaxBarycenterPasses = 5;

    public static IReadOnlyList<HierarchyScopeInstanceViewModel> OrderForLayout(
        IReadOnlyList<HierarchyScopeInstanceViewModel> children,
        IReadOnlyList<HierarchyScopePortViewModel> scopePorts)
    {
        if (children.Count <= 1)
        {
            return children;
        }

        LayoutNode[] nodes = BuildNodes(children, scopePorts);
        AssignRanks(nodes);
        ApplyBarycenterMinimization(nodes);

        return [.. nodes
            .OrderBy(static node => node.Rank)
            .ThenBy(static node => node.Position)
            .Select(static node => node.Child)];
    }

    private static LayoutNode[] BuildNodes(
        IReadOnlyList<HierarchyScopeInstanceViewModel> children,
        IReadOnlyList<HierarchyScopePortViewModel> scopePorts)
    {
        // Map each signal name to which children produce / consume it
        Dictionary<string, List<int>> signalProducers = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<int>> signalConsumers = new(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < children.Count; i++)
        {
            foreach (HierarchyScopeInstancePortConnectionViewModel connection in children[i].PortConnections)
            {
                Dictionary<string, List<int>> target = connection.IsOutput ? signalProducers : signalConsumers;
                if (!target.TryGetValue(connection.SignalName, out List<int>? list))
                {
                    list = [];
                    target[connection.SignalName] = list;
                }

                list.Add(i);
            }
        }

        HashSet<string> boundaryInputSignals = new(StringComparer.OrdinalIgnoreCase);
        foreach (HierarchyScopePortViewModel port in scopePorts)
        {
            if (port.IsInput)
            {
                boundaryInputSignals.Add(port.Name);
            }
        }

        // Build directed adjacency: child i produces signal S → child j consumes S → edge i→j
        List<int>[] successors = new List<int>[children.Count];
        List<int>[] predecessors = new List<int>[children.Count];
        for (int i = 0; i < children.Count; i++)
        {
            successors[i] = [];
            predecessors[i] = [];
        }

        HashSet<(int From, int To)> addedEdges = [];
        foreach ((string signal, List<int> producers) in signalProducers)
        {
            if (!signalConsumers.TryGetValue(signal, out List<int>? consumers))
            {
                continue;
            }

            foreach (int from in producers)
            {
                foreach (int to in consumers)
                {
                    if (from != to && addedEdges.Add((from, to)))
                    {
                        successors[from].Add(to);
                        predecessors[to].Add(from);
                    }
                }
            }
        }

        // Cycle breaking: DFS back-edge removal so rank assignment terminates
        int[] color = new int[children.Count];
        List<(int From, int To)> backEdges = [];
        for (int i = 0; i < children.Count; i++)
        {
            if (color[i] == 0)
            {
                BreakCyclesDfs(i, color, successors, backEdges);
            }
        }

        foreach ((int from, int to) in backEdges)
        {
            successors[from].Remove(to);
            predecessors[to].Remove(from);
        }

        LayoutNode[] nodes = new LayoutNode[children.Count];
        for (int i = 0; i < children.Count; i++)
        {
            bool receivesBoundaryInput = children[i].PortConnections
                .Any(c => c.IsInput && boundaryInputSignals.Contains(c.SignalName));
            nodes[i] = new LayoutNode(children[i], i, successors[i], predecessors[i], receivesBoundaryInput);
        }

        return nodes;
    }

    private static void BreakCyclesDfs(int start, int[] color, List<int>[] successors, List<(int, int)> backEdges)
    {
        Stack<(int Node, int SuccessorIndex)> stack = new();
        stack.Push((start, 0));
        color[start] = 1;

        while (stack.Count > 0)
        {
            (int node, int si) = stack.Pop();
            if (si < successors[node].Count)
            {
                stack.Push((node, si + 1));
                int next = successors[node][si];
                if (color[next] == 1)
                {
                    backEdges.Add((node, next));
                }
                else if (color[next] == 0)
                {
                    color[next] = 1;
                    stack.Push((next, 0));
                }
            }
            else
            {
                color[node] = 2;
            }
        }
    }

    private static void AssignRanks(LayoutNode[] nodes)
    {
        int n = nodes.Length;
        int[] inDegree = new int[n];
        for (int i = 0; i < n; i++)
        {
            inDegree[i] = nodes[i].Predecessors.Count;
        }

        int[] rank = new int[n];
        Queue<int> queue = new();
        for (int i = 0; i < n; i++)
        {
            if (inDegree[i] == 0)
            {
                queue.Enqueue(i);
            }
        }

        // Kahn's BFS — longest-path rank assignment
        while (queue.Count > 0)
        {
            int u = queue.Dequeue();
            foreach (int v in nodes[u].Successors)
            {
                rank[v] = Math.Max(rank[v], rank[u] + 1);
                inDegree[v]--;
                if (inDegree[v] == 0)
                {
                    queue.Enqueue(v);
                }
            }
        }

        int maxRank = n > 0 ? rank.Max() : 0;
        int[] rankCounter = new int[maxRank + 1];
        for (int i = 0; i < n; i++)
        {
            nodes[i].Rank = rank[i];
            nodes[i].Position = rankCounter[rank[i]]++;
        }
    }

    private static void ApplyBarycenterMinimization(LayoutNode[] nodes)
    {
        if (nodes.Length <= 2)
        {
            return;
        }

        int maxRank = nodes.Max(static node => node.Rank);
        if (maxRank == 0)
        {
            return;
        }

        List<LayoutNode>[] layers = new List<LayoutNode>[maxRank + 1];
        for (int r = 0; r <= maxRank; r++)
        {
            layers[r] = [.. nodes.Where(n => n.Rank == r).OrderBy(static n => n.Position)];
        }

        // positionOf[originalIndex] = current position in its layer
        int[] positionOf = new int[nodes.Length];
        for (int i = 0; i < nodes.Length; i++)
        {
            positionOf[nodes[i].OriginalIndex] = nodes[i].Position;
        }

        for (int pass = 0; pass < MaxBarycenterPasses; pass++)
        {
            // Forward sweep: order by barycenter of predecessor positions
            for (int r = 1; r <= maxRank; r++)
            {
                SweepLayer(layers[r], positionOf, predecessors: true);
            }

            // Backward sweep: order by barycenter of successor positions
            for (int r = maxRank - 1; r >= 0; r--)
            {
                SweepLayer(layers[r], positionOf, predecessors: false);
            }
        }
    }

    private static void SweepLayer(List<LayoutNode> layer, int[] positionOf, bool predecessors)
    {
        foreach (LayoutNode node in layer)
        {
            IReadOnlyList<int> neighbors = predecessors ? node.Predecessors : node.Successors;
            node.BarycenterScore = ComputeBarycenter(neighbors, positionOf);
        }

        layer.Sort(static (a, b) =>
        {
            int cmp = a.BarycenterScore.CompareTo(b.BarycenterScore);
            return cmp != 0 ? cmp : a.Position.CompareTo(b.Position);
        });

        for (int p = 0; p < layer.Count; p++)
        {
            layer[p].Position = p;
            positionOf[layer[p].OriginalIndex] = p;
        }
    }

    private static double ComputeBarycenter(IReadOnlyList<int> neighborOriginalIndices, int[] positionOf)
    {
        if (neighborOriginalIndices.Count == 0)
        {
            return double.MaxValue;
        }

        double total = 0;
        foreach (int idx in neighborOriginalIndices)
        {
            total += positionOf[idx];
        }

        return total / neighborOriginalIndices.Count;
    }

    private sealed class LayoutNode(
        HierarchyScopeInstanceViewModel child,
        int originalIndex,
        List<int> successors,
        List<int> predecessors,
        bool receivesBoundaryInput)
    {
        public HierarchyScopeInstanceViewModel Child { get; } = child;
        public int OriginalIndex { get; } = originalIndex;
        public IReadOnlyList<int> Successors { get; } = successors;
        public IReadOnlyList<int> Predecessors { get; } = predecessors;

        // Used to prioritize boundary-input receivers at low ranks in tie-breaking (future use)
        public bool ReceivesBoundaryInput { get; } = receivesBoundaryInput;
        public int Rank { get; set; }
        public int Position { get; set; }
        public double BarycenterScore { get; set; }
    }
}
