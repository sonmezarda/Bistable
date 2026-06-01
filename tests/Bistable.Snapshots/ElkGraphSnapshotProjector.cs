using Bistable.App.Services.Routing.Elk;

namespace Bistable.Snapshots;

// Projects an ElkGraph into a deterministic snapshot record:
//   - drops x/y/width/height fields filled by the layout engine (we snapshot the
//     *pre-layout* structure, which depends only on the design model and the builder
//     logic — independent of elkjs version)
//   - sorts dictionary entries (layoutOptions)
//   - children/edges are kept in builder order (which is itself deterministic)
//
// The output type uses anonymous objects so System.Text.Json serializes properties
// in declaration order.
internal static class ElkGraphSnapshotProjector
{
    public static object Project(ElkGraph graph) => new
    {
        id = graph.Id,
        layoutOptions = SortDict(graph.LayoutOptions),
        children = graph.Children.Select(ProjectNode).ToArray(),
        edges = graph.Edges.Select(ProjectEdge).ToArray(),
    };

    private static object ProjectNode(ElkNode node) => new
    {
        id = node.Id,
        labels = node.Labels?.Select(l => l.Text).ToArray() ?? [],
        layoutOptions = SortDict(node.LayoutOptions),
        ports = node.Ports?.Select(ProjectPort).ToArray() ?? [],
        children = node.Children?.Select(ProjectNode).ToArray() ?? [],
    };

    private static object ProjectPort(ElkPort port) => new
    {
        id = port.Id,
        labels = port.Labels?.Select(l => l.Text).ToArray() ?? [],
        layoutOptions = SortDict(port.LayoutOptions),
    };

    private static object ProjectEdge(ElkEdge edge) => new
    {
        id = edge.Id,
        sources = edge.Sources.ToArray(),
        targets = edge.Targets.ToArray(),
        labels = edge.Labels?.Select(l => l.Text).ToArray() ?? [],
    };

    private static SortedDictionary<string, string> SortDict(Dictionary<string, string>? source) =>
        source is null
            ? []
            : new SortedDictionary<string, string>(source, StringComparer.Ordinal);
}
