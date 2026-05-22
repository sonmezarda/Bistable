using Avalonia;
using Bistable.App.Services.Routing;

namespace Bistable.App.Services;

public sealed class SchematicConnectionRouter
{
    private readonly ISchematicRouter _internalRouter;
    private readonly ISchematicRouter _graphvizRouter;

    public SchematicConnectionRouter()
        : this(new SchematicMazeRouter(), new GraphvizNeatoSchematicRouter())
    {
    }

    public SchematicConnectionRouter(ISchematicRouter internalRouter, ISchematicRouter graphvizRouter)
    {
        _internalRouter = internalRouter ?? throw new ArgumentNullException(nameof(internalRouter));
        _graphvizRouter = graphvizRouter ?? throw new ArgumentNullException(nameof(graphvizRouter));
    }

    public IReadOnlyList<SchematicConnectionRoute> Compute(
        SchematicConnectionRoutingInput input,
        SchematicRoutingEngine engine = SchematicRoutingEngine.Internal) =>
        ResolveRouter(engine).Compute(input);

    private ISchematicRouter ResolveRouter(SchematicRoutingEngine engine) =>
        engine switch
        {
            SchematicRoutingEngine.Internal => _internalRouter,
            SchematicRoutingEngine.GraphvizNeato => _graphvizRouter,
            _ => _internalRouter
        };
}

public interface ISchematicRouter
{
    IReadOnlyList<SchematicConnectionRoute> Compute(SchematicConnectionRoutingInput input);
}

public enum SchematicRoutingEngine
{
    Elk,
    Internal,
    GraphvizNeato,
    GraphvizDot
}

public sealed class SchematicRoutingException : Exception
{
    public SchematicRoutingException(string message)
        : base(message)
    {
    }

    public SchematicRoutingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public interface ISchematicLayoutEngine
{
    SchematicScopePanelLayout Compute(SchematicScopeLayoutInput input);
}

public sealed record SchematicNet(string Key, IReadOnlyList<SchematicConnectionRouteRequest> Requests)
{
    public int Fanout => Requests.Count;

    public string PrimaryRequestId => Requests[0].Id;

    public SchematicConnectionRouteKind PrimaryKind => ResolvePrimaryKind(Requests);

    private static SchematicConnectionRouteKind ResolvePrimaryKind(IReadOnlyList<SchematicConnectionRouteRequest> requests)
    {
        if (requests.Any(static request => request.Kind == SchematicConnectionRouteKind.BoundaryToChildInput))
        {
            return SchematicConnectionRouteKind.BoundaryToChildInput;
        }

        if (requests.Any(static request => request.Kind == SchematicConnectionRouteKind.ChildOutputToBoundary))
        {
            return SchematicConnectionRouteKind.ChildOutputToBoundary;
        }

        return requests[0].Kind;
    }

    public double AverageSource => Requests.Average(static request => request.Source.Y);

    public double AverageTarget => Requests.Average(static request => request.Target.Y);
}

public sealed record SchematicConnectionRoutingInput(
    SchematicScopePanelLayout Layout,
    bool CompactLayout,
    IReadOnlyList<SchematicConnectionRouteRequest> Requests,
    IReadOnlyList<Rect>? Obstacles = null,
    IReadOnlyList<Rect>? PortChannels = null);

public sealed record SchematicConnectionRouteRequest(
    string Id,
    string SignalName,
    string? SelectionSignalName,
    int LabelWidth,
    Point Source,
    Point Target,
    SchematicConnectionRouteKind Kind,
    string? SignalValue = null)
{
    public string BundleKey => string.IsNullOrWhiteSpace(SelectionSignalName) ? SignalName : SelectionSignalName;

    public bool RoutesToChildInput =>
        Kind is SchematicConnectionRouteKind.BoundaryToChildInput
            or SchematicConnectionRouteKind.LocalToChildInput
            or SchematicConnectionRouteKind.ChildOutputToChildInput;

    public bool RoutesFromChildOutput =>
        Kind is SchematicConnectionRouteKind.ChildOutputToBoundary
            or SchematicConnectionRouteKind.ChildOutputToLocal
            or SchematicConnectionRouteKind.ChildOutputToChildInput;

    public bool RoutesToLocalNet => Kind is SchematicConnectionRouteKind.ChildOutputToLocal;

    public bool UsesLocalNet =>
        Kind is SchematicConnectionRouteKind.LocalToChildInput
            or SchematicConnectionRouteKind.ChildOutputToLocal
            or SchematicConnectionRouteKind.ChildOutputToChildInput;
}

public enum SchematicConnectionRouteKind
{
    BoundaryToChildInput,
    LocalToChildInput,
    ChildOutputToBoundary,
    ChildOutputToLocal,
    ChildOutputToChildInput
}

public sealed record SchematicConnectionRoute(
    string Id,
    string BundleKey,
    int BundleSize,
    bool IsBundlePrimary,
    IReadOnlyList<Point> Points,
    Rect LabelBounds,
    Point LabelAnchor,
    IReadOnlyList<Point>? Junctions = null,
    IReadOnlyList<SchematicRouteBridge>? Bridges = null);

public sealed record SchematicRouteBridge(Point Center, SchematicRouteBridgeOrientation Orientation);

public enum SchematicRouteBridgeOrientation
{
    Horizontal,
    Vertical
}
