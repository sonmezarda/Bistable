namespace Bistable.App.Services.Routing.Elk;

/// <summary>
/// Structural coverage report for an <see cref="ElkBuildResult"/>.
/// This catches graph/port-reference mismatches before they reach elkjs or the renderer.
/// </summary>
internal sealed record ElkGraphCoverageReport(
    int NodeCount,
    int PortCount,
    int PortRefCount,
    int EdgeCount,
    int ProducerSignalCount,
    int ConsumerSignalCount,
    int DanglingProducerSignalCount,
    int DanglingConsumerSignalCount,
    int ReferencedPortRefCount,
    int UnreferencedPortRefCount,
    IReadOnlyList<ElkGraphCoverageDiagnostic> Diagnostics)
{
    public int ErrorCount => Diagnostics.Count(static d => d.Severity == ElkGraphCoverageSeverity.Error);
    public bool HasErrors => ErrorCount > 0;
}

internal sealed record ElkGraphCoverageDiagnostic(
    ElkGraphCoverageSeverity Severity,
    ElkGraphCoverageDiagnosticKind Kind,
    string SubjectId,
    string Reason);

internal enum ElkGraphCoverageSeverity
{
    Warning,
    Error
}

internal enum ElkGraphCoverageDiagnosticKind
{
    DuplicateShapeId,
    MissingPortRefNode,
    MissingPortRefPort,
    PortRefNodeMismatch,
    EdgeWithoutSource,
    EdgeWithoutTarget,
    EdgeEndpointIsNode,
    UnresolvedEdgeEndpoint,
    UntrackedEdgeEndpoint,
    DanglingConsumerSignal
}

internal static class ElkGraphCoverageAnalyzer
{
    public static ElkGraphCoverageReport Analyze(ElkBuildResult result) =>
        Analyze(result.Graph, result.PortRefs, result.RoutingTelemetry);

    public static ElkGraphCoverageReport Analyze(
        ElkGraph graph,
        IReadOnlyDictionary<string, ElkPortRef> portRefs) =>
        Analyze(graph, portRefs, ElkRoutingTelemetry.Empty);

    public static ElkGraphCoverageReport Analyze(
        ElkGraph graph,
        IReadOnlyDictionary<string, ElkPortRef> portRefs,
        ElkRoutingTelemetry routingTelemetry)
    {
        GraphShapeIndex index = GraphShapeIndex.Create(graph);
        List<ElkGraphCoverageDiagnostic> diagnostics = [];

        AddDuplicateDiagnostics(index, diagnostics);
        ValidatePortRefs(portRefs, index, diagnostics);
        HashSet<string> edgeEndpointPortIds = ValidateEdges(graph.Edges, portRefs, index, diagnostics);
        AddRoutingTelemetryDiagnostics(routingTelemetry, diagnostics);

        int referencedPortRefCount = portRefs.Values
            .Select(static r => r.PortId)
            .Distinct(StringComparer.Ordinal)
            .Count(edgeEndpointPortIds.Contains);

        int uniquePortRefCount = portRefs.Values
            .Select(static r => r.PortId)
            .Distinct(StringComparer.Ordinal)
            .Count();

        return new ElkGraphCoverageReport(
            NodeCount: index.NodeIds.Count,
            PortCount: index.PortOwnerByPortId.Count,
            PortRefCount: portRefs.Count,
            EdgeCount: graph.Edges.Count,
            ProducerSignalCount: routingTelemetry.ProducerSignalCount,
            ConsumerSignalCount: routingTelemetry.ConsumerSignalCount,
            DanglingProducerSignalCount: routingTelemetry.DanglingProducerSignalCount,
            DanglingConsumerSignalCount: CountActionableDanglingConsumers(routingTelemetry),
            ReferencedPortRefCount: referencedPortRefCount,
            UnreferencedPortRefCount: Math.Max(0, uniquePortRefCount - referencedPortRefCount),
            Diagnostics: diagnostics);
    }

    private static void AddDuplicateDiagnostics(
        GraphShapeIndex index,
        List<ElkGraphCoverageDiagnostic> diagnostics)
    {
        foreach (string id in index.DuplicateShapeIds)
        {
            diagnostics.Add(new ElkGraphCoverageDiagnostic(
                ElkGraphCoverageSeverity.Error,
                ElkGraphCoverageDiagnosticKind.DuplicateShapeId,
                id,
                "ELK shape IDs must be globally unique so edge endpoints resolve deterministically."));
        }
    }

    private static void ValidatePortRefs(
        IReadOnlyDictionary<string, ElkPortRef> portRefs,
        GraphShapeIndex index,
        List<ElkGraphCoverageDiagnostic> diagnostics)
    {
        foreach ((string key, ElkPortRef portRef) in portRefs)
        {
            if (!index.NodeIds.Contains(portRef.NodeId))
            {
                diagnostics.Add(new ElkGraphCoverageDiagnostic(
                    ElkGraphCoverageSeverity.Error,
                    ElkGraphCoverageDiagnosticKind.MissingPortRefNode,
                    key,
                    $"Port reference points at missing node '{portRef.NodeId}'."));
            }

            if (!index.PortOwnerByPortId.TryGetValue(portRef.PortId, out string? ownerNodeId))
            {
                diagnostics.Add(new ElkGraphCoverageDiagnostic(
                    ElkGraphCoverageSeverity.Error,
                    ElkGraphCoverageDiagnosticKind.MissingPortRefPort,
                    key,
                    $"Port reference points at missing port '{portRef.PortId}'."));
                continue;
            }

            if (!string.Equals(ownerNodeId, portRef.NodeId, StringComparison.Ordinal))
            {
                diagnostics.Add(new ElkGraphCoverageDiagnostic(
                    ElkGraphCoverageSeverity.Error,
                    ElkGraphCoverageDiagnosticKind.PortRefNodeMismatch,
                    key,
                    $"Port '{portRef.PortId}' belongs to node '{ownerNodeId}', not '{portRef.NodeId}'."));
            }
        }
    }

    private static HashSet<string> ValidateEdges(
        IReadOnlyList<ElkEdge> edges,
        IReadOnlyDictionary<string, ElkPortRef> portRefs,
        GraphShapeIndex index,
        List<ElkGraphCoverageDiagnostic> diagnostics)
    {
        HashSet<string> trackedPortIds = portRefs.Values
            .Select(static r => r.PortId)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> referencedPortIds = new(StringComparer.Ordinal);

        foreach (ElkEdge edge in edges)
        {
            if (edge.Sources.Count == 0)
            {
                diagnostics.Add(new ElkGraphCoverageDiagnostic(
                    ElkGraphCoverageSeverity.Error,
                    ElkGraphCoverageDiagnosticKind.EdgeWithoutSource,
                    edge.Id,
                    "Edge has no source endpoint."));
            }

            if (edge.Targets.Count == 0)
            {
                diagnostics.Add(new ElkGraphCoverageDiagnostic(
                    ElkGraphCoverageSeverity.Error,
                    ElkGraphCoverageDiagnosticKind.EdgeWithoutTarget,
                    edge.Id,
                    "Edge has no target endpoint."));
            }

            foreach (string endpointId in edge.Sources.Concat(edge.Targets))
            {
                ValidateEdgeEndpoint(edge.Id, endpointId, trackedPortIds, referencedPortIds, index, diagnostics);
            }
        }

        return referencedPortIds;
    }

    private static void AddRoutingTelemetryDiagnostics(
        ElkRoutingTelemetry routingTelemetry,
        List<ElkGraphCoverageDiagnostic> diagnostics)
    {
        foreach ((string signal, IReadOnlyList<ElkPortRef> consumers) in routingTelemetry.ConsumersBySignal)
        {
            if (routingTelemetry.ProducersBySignal.ContainsKey(signal) || IsIntentionalUnroutedSignal(signal))
            {
                continue;
            }

            diagnostics.Add(new ElkGraphCoverageDiagnostic(
                ElkGraphCoverageSeverity.Warning,
                ElkGraphCoverageDiagnosticKind.DanglingConsumerSignal,
                signal,
                $"Signal has {consumers.Count} consumer endpoint(s) but no registered producer."));
        }
    }

    private static int CountActionableDanglingConsumers(ElkRoutingTelemetry routingTelemetry) =>
        routingTelemetry.ConsumersBySignal.Keys
            .Count(signal => !routingTelemetry.ProducersBySignal.ContainsKey(signal)
                             && !IsIntentionalUnroutedSignal(signal));

    private static bool IsIntentionalUnroutedSignal(string signal)
    {
        string trimmed = signal.Trim();
        return trimmed.Length == 0
               || trimmed == "?"
               || trimmed == "0"
               || trimmed == "1"
               || trimmed.StartsWith("'")
               || trimmed.Contains("'b", StringComparison.OrdinalIgnoreCase)
               || trimmed.Contains("'h", StringComparison.OrdinalIgnoreCase)
               || trimmed.Contains("'d", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateEdgeEndpoint(
        string edgeId,
        string endpointId,
        HashSet<string> trackedPortIds,
        HashSet<string> referencedPortIds,
        GraphShapeIndex index,
        List<ElkGraphCoverageDiagnostic> diagnostics)
    {
        if (index.PortOwnerByPortId.ContainsKey(endpointId))
        {
            referencedPortIds.Add(endpointId);
            if (!trackedPortIds.Contains(endpointId))
            {
                diagnostics.Add(new ElkGraphCoverageDiagnostic(
                    ElkGraphCoverageSeverity.Warning,
                    ElkGraphCoverageDiagnosticKind.UntrackedEdgeEndpoint,
                    edgeId,
                    $"Edge endpoint '{endpointId}' is a graph port but has no matching PortRef."));
            }

            return;
        }

        if (index.NodeIds.Contains(endpointId))
        {
            diagnostics.Add(new ElkGraphCoverageDiagnostic(
                ElkGraphCoverageSeverity.Error,
                ElkGraphCoverageDiagnosticKind.EdgeEndpointIsNode,
                edgeId,
                $"Edge endpoint '{endpointId}' resolves to a node. Builder edges must target ports."));
            return;
        }

        diagnostics.Add(new ElkGraphCoverageDiagnostic(
            ElkGraphCoverageSeverity.Error,
            ElkGraphCoverageDiagnosticKind.UnresolvedEdgeEndpoint,
            edgeId,
            $"Edge endpoint '{endpointId}' does not match any graph node or port."));
    }

    private sealed class GraphShapeIndex
    {
        private GraphShapeIndex(
            HashSet<string> nodeIds,
            Dictionary<string, string> portOwnerByPortId,
            IReadOnlyList<string> duplicateShapeIds)
        {
            NodeIds = nodeIds;
            PortOwnerByPortId = portOwnerByPortId;
            DuplicateShapeIds = duplicateShapeIds;
        }

        public HashSet<string> NodeIds { get; }
        public Dictionary<string, string> PortOwnerByPortId { get; }
        public IReadOnlyList<string> DuplicateShapeIds { get; }

        public static GraphShapeIndex Create(ElkGraph graph)
        {
            HashSet<string> seenShapeIds = new(StringComparer.Ordinal);
            HashSet<string> duplicateShapeIds = new(StringComparer.Ordinal);
            HashSet<string> nodeIds = new(StringComparer.Ordinal);
            Dictionary<string, string> portOwnerByPortId = new(StringComparer.Ordinal);

            foreach (ElkNode child in graph.Children)
            {
                Visit(child, seenShapeIds, duplicateShapeIds, nodeIds, portOwnerByPortId);
            }

            return new GraphShapeIndex(nodeIds, portOwnerByPortId, [.. duplicateShapeIds.Order(StringComparer.Ordinal)]);
        }

        private static void Visit(
            ElkNode node,
            HashSet<string> seenShapeIds,
            HashSet<string> duplicateShapeIds,
            HashSet<string> nodeIds,
            Dictionary<string, string> portOwnerByPortId)
        {
            AddShapeId(node.Id, seenShapeIds, duplicateShapeIds);
            nodeIds.Add(node.Id);

            if (node.Ports is not null)
            {
                foreach (ElkPort port in node.Ports)
                {
                    AddShapeId(port.Id, seenShapeIds, duplicateShapeIds);
                    portOwnerByPortId[port.Id] = node.Id;
                }
            }

            if (node.Children is null)
            {
                return;
            }

            foreach (ElkNode child in node.Children)
            {
                Visit(child, seenShapeIds, duplicateShapeIds, nodeIds, portOwnerByPortId);
            }
        }

        private static void AddShapeId(
            string id,
            HashSet<string> seenShapeIds,
            HashSet<string> duplicateShapeIds)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            if (!seenShapeIds.Add(id))
            {
                duplicateShapeIds.Add(id);
            }
        }
    }
}
