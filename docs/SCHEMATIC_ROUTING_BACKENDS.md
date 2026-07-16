# Schematic Routing Backends

Bistable keeps routing implementations behind `ISchematicRouter` so experimental
backends can be tried without deleting the current C# router. The active backend
is selected by the `SchematicRoutingEngine` enum in
[SchematicConnectionRouter.cs](../src/Bistable.App/Services/SchematicConnectionRouter.cs):
`Elk` (default), `Internal`, `GraphvizNeato`, `GraphvizDot`.

## ELK (active default)

`SchematicRoutingEngine.Elk` is the current production backend
([SchematicPreviewControl.cs](../src/Bistable.App/Views/SchematicPreviewControl.cs)
registers `Elk` as the default `RoutingEngine`). It solves placement and routing
together with elkjs (Eclipse Layout Kernel) run as a Node subprocess, following
the "Production External Backend Direction" below.

- Pipeline: `ElkGraphBuilder.Build` → `ElkRunner.Layout` (elkjs subprocess) →
  `SchematicPreviewControl.Elk` rendering. Gate-level uses
  `GateNetlistElkBuilder` + `GateHierarchicalLayoutEngine`.
- Layout runs off the UI thread and is cancellable through
  `SchematicLayoutService.LayoutAsync`, with an 8-entry SHA-1 LRU
  (`ElkSchematicEngine`) plus `GateLevelLayoutCache` fingerprinting on the gate
  hierarchy path.
- Net identity is carried on `bistable.netId` metadata (not visible ELK labels),
  preserving per-bit selection and simulation cross-probe.
- Performance and routing-preset details: see
  [ELK_ROUTING_PERFORMANCE_ANALYSIS.md](ELK_ROUTING_PERFORMANCE_ANALYSIS.md).

The three backends below remain selectable for comparison/offline work but are
not the production default.

## Internal

`SchematicMazeRouter` is the in-process C# router (`SchematicRoutingEngine.Internal`),
the pure-C# maze router built in the `SCHEMATIC_REWRITE_PLAN.md` Phase 0-5 work.
It has no runtime tool dependency and remains available for comparison and
offline development, but it is no longer the RTL default.

## Graphviz Dot

`GraphvizDot` is the current functional schematic backend. Unlike
`GraphvizNeato`, it does not try to route wires through an already-fixed
Avalonia layout. It gives Graphviz the schematic graph and lets Graphviz place
the boundary ports, module nodes, clusters, and routes together. Bistable then
renders the resulting graph with its own dark theme and hit-test metadata.

This backend prioritizes correctness and readability over matching the earlier
hand-drawn card layout. It is the right direction for production external
schematic rendering because node placement and edge routing are solved as one
problem.

Implementation notes:

- Local/internal net helper chips are not emitted to DOT. If they are present as
  invisible nodes, Graphviz still routes toward them and the renderer can show
  dangling wires. Bistable instead normalizes local-net connections into direct
  visible driver-to-consumer edges and keeps the local signal only as selection
  metadata.
- Route geometry is cached after Graphviz plain output is converted into
  Bistable drawing primitives. Hover and selection redraws should not rerun
  Graphviz or the post-route obstacle pass.
- Spacing is density-aware. Dense scopes increase `nodesep`, `ranksep`, cluster
  margin, and post-layout module spread so readability is preferred over compact
  placement.
- Graphviz remains an optional external executable. Its output is treated as a
  layout source, not as a public visual style dependency.

## Graphviz Neato

`GraphvizNeatoSchematicRouter` invokes the external `neato` executable with
fixed endpoint positions and orthogonal splines. This backend is selected from
the schematic toolbar as `GraphvizNeato`.

This backend is deprecated-experimental. Graphviz is good at graph layout, but this adapter
asks it to route between already placed Bistable ports. In fixed-coordinate mode
Graphviz does not provide a strong "avoid these existing Avalonia rectangles"
contract, so the adapter validates the output and rejects invalid routes instead
of rendering a misleading schematic.

This backend intentionally does not silently fall back to `Internal`. If Graphviz
is not installed, returns an error, or produces routes through obstacles, the
schematic canvas shows the diagnostic so route quality problems are visible
during evaluation.

Linux install command:

```bash
sudo apt install graphviz
```

Licensing note: Graphviz is an optional external executable. Bistable does not
embed or redistribute Graphviz binaries.

## Production External Backend Direction

A production external backend must own the layout-and-route problem together:

- build a schematic graph with modules, boundary ports, local nets, and buses;
- let the backend place module boxes and ports;
- consume the backend edge routes in the same coordinate system;
- map rendered nodes and edges back to Bistable hit testing and live probe data.

Using an external tool only as a post-layout wire router is not reliable enough
for Bistable's schematic view.
