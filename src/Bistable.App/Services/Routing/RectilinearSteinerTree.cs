using Avalonia;

namespace Bistable.App.Services.Routing;

/// <summary>
/// Builds a minimum spanning tree (Prim's, Manhattan distance) over a set of terminal points.
/// The resulting tree defines the trunk-first routing order for fanout nets, producing a
/// Steiner-tree-like result where all fanout branches share a common trunk segment.
/// </summary>
internal static class RectilinearSteinerTree
{
    /// <summary>
    /// Returns MST edges as (fromIndex, toIndex) pairs. Index 0 is always the source.
    /// Edges are rooted at index 0, so traversal from 0 gives trunk-first order.
    /// </summary>
    public static (int[] Parent, IReadOnlyList<(int From, int To)> Edges) Build(IReadOnlyList<Point> points)
    {
        int n = points.Count;
        int[] parent = new int[n];
        double[] minDist = new double[n];
        bool[] inMst = new bool[n];
        Array.Fill(parent, -1);
        Array.Fill(minDist, double.MaxValue);
        minDist[0] = 0;

        for (int step = 0; step < n; step++)
        {
            int u = FindMinDistNode(inMst, minDist, n);
            inMst[u] = true;

            for (int v = 0; v < n; v++)
            {
                if (inMst[v])
                {
                    continue;
                }

                double dist = Manhattan(points[u], points[v]);
                if (dist < minDist[v])
                {
                    minDist[v] = dist;
                    parent[v] = u;
                }
            }
        }

        List<(int From, int To)> edges = new(n - 1);
        for (int i = 1; i < n; i++)
        {
            edges.Add((parent[i], i));
        }

        return (parent, edges);
    }

    /// <summary>
    /// Returns the sequence of point indices (root to leaf) for the path from index 0 to targetIndex.
    /// </summary>
    public static IReadOnlyList<int> PathToTarget(int targetIndex, int[] parent)
    {
        List<int> reversed = [];
        int current = targetIndex;
        while (current != -1)
        {
            reversed.Add(current);
            if (current == 0)
            {
                break;
            }

            current = parent[current];
        }

        reversed.Reverse();
        return reversed;
    }

    /// <summary>
    /// Returns MST edges in BFS order from root (index 0), so trunk edges come before branch edges.
    /// </summary>
    public static IReadOnlyList<(int From, int To)> BfsOrder(IReadOnlyList<(int From, int To)> edges, int nodeCount)
    {
        List<int>[] adj = new List<int>[nodeCount];
        for (int i = 0; i < nodeCount; i++)
        {
            adj[i] = [];
        }

        foreach ((int from, int to) in edges)
        {
            adj[from].Add(to);
        }

        List<(int From, int To)> ordered = new(edges.Count);
        Queue<int> queue = new();
        queue.Enqueue(0);
        while (queue.Count > 0)
        {
            int u = queue.Dequeue();
            foreach (int v in adj[u])
            {
                ordered.Add((u, v));
                queue.Enqueue(v);
            }
        }

        return ordered;
    }

    private static int FindMinDistNode(bool[] inMst, double[] minDist, int n)
    {
        int best = -1;
        for (int i = 0; i < n; i++)
        {
            if (!inMst[i] && (best == -1 || minDist[i] < minDist[best]))
            {
                best = i;
            }
        }

        return best;
    }

    private static double Manhattan(Point a, Point b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
}
