using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Media;
using Bistable.App.Services;
using Bistable.App.ViewModels;
using Bistable.Core.Design;

namespace Bistable.App.Views;

public sealed partial class SchematicPreviewControl
{
    private void DrawConnectionRoutes(
        DrawingContext context,
        CurrentPortLayout currentPortLayout,
        IReadOnlyList<ChildNodeLayout> childLayouts,
        IReadOnlyDictionary<string, LocalSignalAnchor> localSignalAnchors)
    {
        List<SchematicConnectionRouteRequest> requests = [];
        Dictionary<string, List<PendingLocalConnection>> localGroups = new(StringComparer.OrdinalIgnoreCase);
        IReadOnlyDictionary<string, string> signalValues = BuildSignalValueLookup();
        foreach (ChildNodeLayout child in childLayouts)
        {
            foreach (HierarchyScopeInstancePortConnectionViewModel connection in child.Instance.PortConnections)
            {
                if (!child.PortAnchors.TryGetValue(connection.PortName, out Point childAnchor))
                {
                    continue;
                }

                Point? boundaryPoint = null;
                if (currentPortLayout.PortAnchors.TryGetValue(connection.SignalName, out PortAnchor? currentPort) && currentPort is not null)
                {
                    boundaryPoint = currentPort.Point;
                }

                LocalSignalAnchor? localAnchor = null;
                if (localSignalAnchors.TryGetValue(connection.SignalName, out LocalSignalAnchor? candidateLocalAnchor))
                {
                    localAnchor = candidateLocalAnchor;
                }

                string? selectionSignalName = currentPortLayout.PortAnchors.TryGetValue(connection.SignalName, out PortAnchor? portAnchor) && portAnchor is not null
                    ? connection.SignalName
                    : localSignalAnchors.TryGetValue(connection.SignalName, out LocalSignalAnchor? localSelection)
                        ? localSelection.ResolvedSignalName
                        : null;

                if (connection.IsInput)
                {
                    if (boundaryPoint is null)
                    {
                        AddPendingLocalConnection(localGroups, child, connection, childAnchor, localAnchor);
                        continue;
                    }

                    requests.Add(new SchematicConnectionRouteRequest(
                        $"{child.Instance.HierarchyPath}:{connection.PortName}:{connection.SignalName}:i",
                        connection.SignalName,
                        selectionSignalName,
                        connection.Width,
                        boundaryPoint.Value,
                        childAnchor,
                        SchematicConnectionRouteKind.BoundaryToChildInput,
                        ResolveSignalValue(signalValues, connection.SignalName, localAnchor)));
                    continue;
                }

                if (boundaryPoint is null)
                {
                    AddPendingLocalConnection(localGroups, child, connection, childAnchor, localAnchor);
                    continue;
                }

                requests.Add(new SchematicConnectionRouteRequest(
                    $"{child.Instance.HierarchyPath}:{connection.PortName}:{connection.SignalName}:o",
                    connection.SignalName,
                    selectionSignalName,
                    connection.Width,
                    childAnchor,
                    boundaryPoint.Value,
                    SchematicConnectionRouteKind.ChildOutputToBoundary,
                    ResolveSignalValue(signalValues, connection.SignalName, localAnchor)));
            }
        }

        AddLocalNetRouteRequests(requests, localGroups, signalValues);

        if (requests.Count == 0)
        {
            return;
        }

        Dictionary<string, SchematicConnectionRouteRequest> requestIndex = requests.ToDictionary(static request => request.Id, StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<SchematicConnectionRoute> routes;
        try
        {
            routes = ConnectionRouter.Compute(
                new SchematicConnectionRoutingInput(
                    currentPortLayout.Layout,
                    CompactLayout,
                    requests,
                    BuildRoutingObstacles(currentPortLayout.Layout)),
                RoutingEngine);
        }
        catch (SchematicRoutingException exception)
        {
            DrawRoutingError(context, currentPortLayout.Layout.PanelRect, exception.Message);
            return;
        }

        HashSet<string> drawnSegments = new(StringComparer.OrdinalIgnoreCase);
        bool anyHovered = !string.IsNullOrEmpty(_hoveredSignalName);
        // P2.7-5: pins count as highlight just like hover.
        bool anyPinned = _pinnedSignalNames.Count > 0;

        foreach (SchematicConnectionRoute route in routes)
        {
            SchematicConnectionRouteRequest request = requestIndex[route.Id];
            bool selected = !string.IsNullOrWhiteSpace(request.SelectionSignalName)
                && string.Equals(SelectedSignalName, request.SelectionSignalName, StringComparison.OrdinalIgnoreCase);
            bool isHoveredNet = anyHovered && string.Equals(
                request.SelectionSignalName, _hoveredSignalName, StringComparison.OrdinalIgnoreCase);
            bool isPinnedNet = !string.IsNullOrWhiteSpace(request.SelectionSignalName)
                && _pinnedSignalNames.Contains(request.SelectionSignalName!);
            bool isHighlighted = isHoveredNet || isPinnedNet;
            bool shouldDim = (anyHovered || anyPinned) && !isHighlighted && !selected;
            IBrush routeBrush = ResolveRouteBrush(request);

            if (shouldDim)
            {
                using IDisposable _ = context.PushOpacity(0.22);
                DrawScopedConnectionRoute(context, route, routeBrush, selected, request.LabelWidth, drawnSegments);
            }
            else
            {
                DrawScopedConnectionRoute(context, route, routeBrush, selected, request.LabelWidth, drawnSegments);
            }

            if (!string.IsNullOrWhiteSpace(request.SelectionSignalName))
            {
                _signalReferenceHitTargets.Add(new SignalReferenceHitTarget(
                    request.SelectionSignalName!,
                    null,
                    route.Points));
            }
        }
    }

    private static void AddPendingLocalConnection(
        Dictionary<string, List<PendingLocalConnection>> localGroups,
        ChildNodeLayout child,
        HierarchyScopeInstancePortConnectionViewModel connection,
        Point childAnchor,
        LocalSignalAnchor? localAnchor)
    {
        if (!localGroups.TryGetValue(connection.SignalName, out List<PendingLocalConnection>? group))
        {
            group = [];
            localGroups[connection.SignalName] = group;
        }

        group.Add(new PendingLocalConnection(child, connection, childAnchor, localAnchor));
    }

    private void AddLocalNetRouteRequests(
        List<SchematicConnectionRouteRequest> requests,
        Dictionary<string, List<PendingLocalConnection>> localGroups,
        IReadOnlyDictionary<string, string> signalValues)
    {
        foreach ((string signalName, List<PendingLocalConnection> group) in localGroups)
        {
            PendingLocalConnection[] producers = group.Where(static item => item.Connection.IsOutput).ToArray();
            PendingLocalConnection[] consumers = group.Where(static item => item.Connection.IsInput).ToArray();
            string? selectionSignalName = group
                .Select(static item => item.LocalAnchor?.ResolvedSignalName)
                .FirstOrDefault(static name => !string.IsNullOrWhiteSpace(name));
            string? value = ResolveSignalValue(signalValues, signalName, group[0].LocalAnchor);

            if (producers.Length > 0 && consumers.Length > 0)
            {
                foreach (PendingLocalConnection producer in producers)
                {
                    foreach (PendingLocalConnection consumer in consumers)
                    {
                        requests.Add(new SchematicConnectionRouteRequest(
                            $"{producer.Child.Instance.HierarchyPath}:{producer.Connection.PortName}:{consumer.Child.Instance.HierarchyPath}:{consumer.Connection.PortName}:{signalName}:local",
                            signalName,
                            selectionSignalName,
                            Math.Max(producer.Connection.Width, consumer.Connection.Width),
                            producer.ChildAnchor,
                            consumer.ChildAnchor,
                            SchematicConnectionRouteKind.ChildOutputToChildInput,
                            value));
                    }
                }

                continue;
            }

            foreach (PendingLocalConnection consumer in consumers)
            {
                if (consumer.LocalAnchor is null) continue;
                requests.Add(new SchematicConnectionRouteRequest(
                    $"{consumer.Child.Instance.HierarchyPath}:{consumer.Connection.PortName}:{signalName}:li",
                    signalName,
                    selectionSignalName,
                    consumer.Connection.Width,
                    consumer.LocalAnchor.Point,
                    consumer.ChildAnchor,
                    SchematicConnectionRouteKind.LocalToChildInput,
                    value));
            }

            foreach (PendingLocalConnection producer in producers)
            {
                if (producer.LocalAnchor is null) continue;
                requests.Add(new SchematicConnectionRouteRequest(
                    $"{producer.Child.Instance.HierarchyPath}:{producer.Connection.PortName}:{signalName}:lo",
                    signalName,
                    selectionSignalName,
                    producer.Connection.Width,
                    producer.ChildAnchor,
                    producer.LocalAnchor.Point,
                    SchematicConnectionRouteKind.ChildOutputToLocal,
                    value));
            }
        }
    }

    private IReadOnlyDictionary<string, string> BuildSignalValueLookup(
        IReadOnlyList<DesignContAssign>? contAssigns = null)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        AddSignalValues(values, Signals);
        AddSignalValues(values, ScopeSignals);
        if (contAssigns is not null)
        {
            EnrichWithSliceValues(values, contAssigns);
        }

        return values;
    }

    // For each sel-based contassign whose source signal has a known value, compute and store
    // the slice value so that edges and probes downstream show correct data.
    private static void EnrichWithSliceValues(
        Dictionary<string, string> values,
        IReadOnlyList<DesignContAssign> contAssigns)
    {
        foreach (DesignContAssign assign in contAssigns
                     .Where(static a => a.SourceRange.HasValue && a.SourceNames.Count == 1))
        {
            if (values.TryGetValue(assign.TargetName, out string? existing)
                && !string.IsNullOrWhiteSpace(existing) && existing != "-")
            {
                continue; // already has a live value from the simulator
            }

            string sourceName = assign.SourceNames[0];
            if (!values.TryGetValue(sourceName, out string? sourceValue)
                || string.IsNullOrWhiteSpace(sourceValue) || sourceValue == "-")
            {
                continue;
            }

            if (!TryParseNumericValue(sourceValue, out BigInteger numeric))
            {
                continue;
            }

            DesignBitRange range = assign.SourceRange!.Value;
            BigInteger mask = (BigInteger.One << range.Width) - 1;
            BigInteger sliceValue = (numeric >> range.Lo) & mask;
            values[assign.TargetName] = range.Width == 1
                ? sliceValue.ToString(CultureInfo.InvariantCulture)
                : $"0x{sliceValue:X}";
        }
    }

    internal static bool TryParseNumericValue(string value, out BigInteger result)
    {
        result = BigInteger.Zero;
        string s = value.Trim();
        if (s is "-" or "" or "x" or "X" or "z" or "Z")
        {
            return false;
        }

        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return BigInteger.TryParse(s[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out result);
        }

        if (s.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
        {
            result = BigInteger.Zero;
            foreach (char c in s[2..])
            {
                if (c is not '0' and not '1') return false;
                result = (result << 1) | (c == '1' ? BigInteger.One : BigInteger.Zero);
            }
            return true;
        }

        return BigInteger.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    private static void AddSignalValues(Dictionary<string, string> values, IEnumerable<SignalViewModel>? signals)
    {
        if (signals is null)
        {
            return;
        }

        foreach (SignalViewModel signal in signals)
        {
            values[signal.Name] = signal.Value;
            if (!string.IsNullOrWhiteSpace(signal.ShortName))
            {
                values.TryAdd(signal.ShortName, signal.Value);
            }
        }
    }

    private static string? ResolveSignalValue(
        IReadOnlyDictionary<string, string> signalValues,
        string signalName,
        LocalSignalAnchor? localAnchor)
    {
        if (localAnchor is not null && !string.IsNullOrWhiteSpace(localAnchor.CurrentValue) && localAnchor.CurrentValue != "-")
        {
            return localAnchor.CurrentValue;
        }

        return signalValues.TryGetValue(signalName, out string? value) ? value : null;
    }

    private IBrush ResolveRouteBrush(SchematicConnectionRouteRequest request)
    {
        if (TryResolveRouteActivity(request.SignalValue, out bool isActive) && !isActive)
        {
            if (request.UsesLocalNet)
            {
                return Palette.InactiveLocalRoute;
            }

            return request.RoutesToChildInput ? Palette.InactiveInputRoute : Palette.InactiveOutputRoute;
        }

        if (request.SignalValue is not null && !TryResolveRouteActivity(request.SignalValue, out _))
        {
            return Palette.UnknownRoute;
        }

        if (request.UsesLocalNet)
        {
            return Palette.LocalNet;
        }

        return request.RoutesToChildInput ? Palette.PinStroke : Palette.OutputValue;
    }

    private static bool TryResolveRouteActivity(string? value, out bool isActive)
    {
        isActive = false;
        if (string.IsNullOrWhiteSpace(value) || value == "-")
        {
            return false;
        }

        string normalized = value.Trim();
        if (normalized.Equals("x", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("z", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (System.Numerics.BigInteger.TryParse(
                    normalized[2..],
                    System.Globalization.NumberStyles.AllowHexSpecifier,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out System.Numerics.BigInteger hex))
            {
                isActive = hex != System.Numerics.BigInteger.Zero;
                return true;
            }

            return false;
        }

        if (normalized.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[2..];
        }

        if (normalized.All(static c => c is '0' or '1'))
        {
            isActive = normalized.Any(static c => c == '1');
            return true;
        }

        if (System.Numerics.BigInteger.TryParse(
                normalized,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out System.Numerics.BigInteger decimalValue))
        {
            isActive = decimalValue != System.Numerics.BigInteger.Zero;
            return true;
        }

        return false;
    }

    private static IReadOnlyList<Rect> BuildRoutingObstacles(SchematicScopePanelLayout layout)
    {
        List<Rect> obstacles = [];
        obstacles.Add(layout.CurrentNodeRect);
        if (layout.ParentNodeRect is Rect parentRect)
        {
            obstacles.Add(parentRect);
        }

        obstacles.AddRange(layout.ChildNodeRects);
        if (layout.LocalSectionRect is Rect localSection)
        {
            obstacles.Add(localSection);
        }

        obstacles.Add(layout.ProbeSectionRect);
        return obstacles;
    }

    private void DrawRoutingError(DrawingContext context, Rect panelRect, string message)
    {
        Rect box = new(
            panelRect.X + 18,
            panelRect.Y + 18,
            Math.Min(680, Math.Max(320, panelRect.Width - 36)),
            64);
        context.FillRectangle(Palette.ValueFill, box, 6);
        context.DrawRectangle(new Pen(Palette.Selected, 1.2), box, 6);
        DrawText(context, $"Schematic router: {RoutingEngine}", box.X + 12, box.Y + 10, Palette.Selected, 11);
        DrawText(context, Ellipsize(message, 96, box.Width - 24), box.X + 12, box.Y + 32, Palette.Text, 10);
    }

    private void DrawScopedConnectionRoute(
        DrawingContext context,
        SchematicConnectionRoute route,
        IBrush brush,
        bool selected,
        int width,
        HashSet<string>? drawnSegments = null)
    {
        IReadOnlyList<Point> points = route.Points;
        if (selected)
        {
            Pen highlight = new(Palette.Selected, CompactLayout ? 2.8 : 3.2);
            for (int index = 0; index < points.Count - 1; index++)
            {
                context.DrawLine(highlight, points[index], points[index + 1]);
            }
        }

        bool lodDetail = _viewportZoom >= 0.5;
        bool lodFull = _viewportZoom >= 0.3;

        double thickness = !lodDetail ? 1.0 : (width > 1 ? (CompactLayout ? 1.8 : 2.1) : 1.1);
        Pen pen = new(brush, selected ? Math.Max(thickness, 2.1) : thickness);
        bool isBus = width > 1;
        for (int index = 0; index < points.Count - 1; index++)
        {
            if (!selected && drawnSegments is not null && !drawnSegments.Add(BuildRouteSegmentKey(route.BundleKey, points[index], points[index + 1], width)))
            {
                continue;
            }

            if (isBus && lodFull)
            {
                DrawBusRibbonSegment(context, points[index], points[index + 1], brush, selected);
            }
            else
            {
                context.DrawLine(pen, points[index], points[index + 1]);
            }
        }

        if (isBus && points.Count > 0 && lodFull)
        {
            DrawBusTapMarker(context, points[^1], brush);
        }

        if (route.Bridges is { Count: > 0 } && lodDetail)
        {
            DrawRouteBridges(context, route.Bridges, brush, selected);
        }

        if (route.Junctions is { Count: > 0 })
        {
            double junctionRadius = lodDetail ? (CompactLayout ? 2.2 : 2.8) : 1.5;
            DrawRouteJunctions(context, route.Junctions, selected ? Palette.Selected : brush, junctionRadius);
        }
    }

    private void DrawBusRibbonSegment(
        DrawingContext context,
        Point a,
        Point b,
        IBrush brush,
        bool selected)
    {
        double railOffset = CompactLayout ? 1.4 : 1.8;
        double thickness = selected ? 1.6 : 1.2;
        Pen pen = new(brush, thickness, lineCap: Avalonia.Media.PenLineCap.Square);
        bool isHorizontal = Math.Abs(a.Y - b.Y) < 0.5;
        if (isHorizontal)
        {
            context.DrawLine(pen, new Point(a.X, a.Y - railOffset), new Point(b.X, b.Y - railOffset));
            context.DrawLine(pen, new Point(a.X, a.Y + railOffset), new Point(b.X, b.Y + railOffset));
        }
        else
        {
            context.DrawLine(pen, new Point(a.X - railOffset, a.Y), new Point(b.X - railOffset, b.Y));
            context.DrawLine(pen, new Point(a.X + railOffset, a.Y), new Point(b.X + railOffset, b.Y));
        }
    }

    private void DrawBusTapMarker(DrawingContext context, Point point, IBrush brush)
    {
        double size = CompactLayout ? 5 : 6;
        Pen tapPen = new(brush, 1.4);
        context.DrawLine(tapPen,
            new Avalonia.Point(point.X - size * 0.35, point.Y + size * 0.5),
            new Avalonia.Point(point.X + size * 0.35, point.Y - size * 0.5));
    }

    private static void DrawRouteJunctions(DrawingContext context, IReadOnlyList<Point> junctions, IBrush brush, double radius)
    {
        foreach (Point junction in junctions)
        {
            context.DrawEllipse(brush, null, junction, radius, radius);
        }
    }

    private void DrawRouteBridges(DrawingContext context, IReadOnlyList<SchematicRouteBridge> bridges, IBrush brush, bool selected)
    {
        double gap = CompactLayout ? 4.2 : 5.2;
        double rise = CompactLayout ? 3.2 : 4.2;
        double thickness = selected ? 2.2 : 1.4;
        Pen background = new(Palette.FocusPanelFill, CompactLayout ? 3.8 : 4.6);
        Pen foreground = new(brush, thickness);
        foreach (SchematicRouteBridge bridge in bridges)
        {
            if (bridge.Orientation == SchematicRouteBridgeOrientation.Horizontal)
            {
                Point left = new(bridge.Center.X - gap, bridge.Center.Y);
                Point right = new(bridge.Center.X + gap, bridge.Center.Y);
                context.DrawLine(background, left, right);
                StreamGeometry geometry = new();
                using (StreamGeometryContext geometryContext = geometry.Open())
                {
                    geometryContext.BeginFigure(left, isFilled: false);
                    geometryContext.QuadraticBezierTo(new Point(bridge.Center.X, bridge.Center.Y - rise), right);
                    geometryContext.EndFigure(isClosed: false);
                }

                context.DrawGeometry(null, foreground, geometry);
                continue;
            }

            Point top = new(bridge.Center.X, bridge.Center.Y - gap);
            Point bottom = new(bridge.Center.X, bridge.Center.Y + gap);
            context.DrawLine(background, top, bottom);
            context.DrawLine(foreground, top, bottom);
        }
    }

    private static string BuildRouteSegmentKey(string bundleKey, Point start, Point end, int width)
    {
        double x1 = Math.Round(start.X, 1);
        double y1 = Math.Round(start.Y, 1);
        double x2 = Math.Round(end.X, 1);
        double y2 = Math.Round(end.Y, 1);
        if (x2 < x1 || Math.Abs(x1 - x2) < 0.01 && y2 < y1)
        {
            (x1, x2) = (x2, x1);
            (y1, y2) = (y2, y1);
        }

        return $"{bundleKey}:{(width > 1 ? "b" : "s")}:{x1:F1},{y1:F1}:{x2:F1},{y2:F1}";
    }

    private void DrawConnectionRouteLabel(
        DrawingContext context,
        SchematicConnectionRouteRequest request,
        SchematicConnectionRoute route,
        bool selected,
        int routeCount)
    {
        string text = request.LabelWidth <= 1
            ? request.SignalName
            : routeCount > 1
                ? $"{request.SignalName} [{request.LabelWidth}b] x{routeCount}"
                : $"{request.SignalName} [{request.LabelWidth}b]";
        string label = Ellipsize(text, 9, route.LabelBounds.Width - 8);
        IBrush stroke = selected ? Palette.Selected : (request.RoutesToChildInput ? Palette.PinStroke : Palette.OutputValue);
        context.FillRectangle(Palette.ValueFill, route.LabelBounds, 4);
        context.DrawRectangle(new Pen(stroke, selected ? 1.2 : 1), route.LabelBounds, 4);
        DrawText(context, label, route.LabelBounds.X + 4, route.LabelBounds.Y + 3, stroke, 9);
    }
}
