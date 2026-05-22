# Schematic Routing Backends

Bistable keeps routing implementations behind `ISchematicRouter` so experimental
backends can be tried without deleting the current C# router.

## Internal

`SchematicMazeRouter` is the in-process C# router. It has no runtime tool
dependency and remains available for comparison and offline development.

## Graphviz Dot

`GraphvizDot` is the current functional schematic backend. Unlike
`GraphvizNeato`, it does not try to route wires through an already-fixed
Avalonia layout. It gives Graphviz the schematic graph and lets Graphviz place
the boundary ports, local nets, module nodes, and routes together. Bistable then
renders the resulting graph with its own dark theme and hit-test metadata.

This backend prioritizes correctness and readability over matching the earlier
hand-drawn card layout. It is the right direction for production external
schematic rendering because node placement and edge routing are solved as one
problem.

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
