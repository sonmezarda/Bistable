# Gate-Level Schematic

The gate-level schematic displays the structural netlist produced by Yosys.
It preserves bit-level net identity for selection, simulation cross-probe, and
RTL-versus-gate comparison while applying presentation-only LOD to large buses.

## Navigation

- Click a module `+` badge to expand or collapse it in place.
- Double-click a module body to enter that scope.
- Use the breadcrumb or `Up` button to return to a parent scope.
- Middle-drag pans. `Ctrl+wheel`, `+`, and `-` zoom.
- `F` fits the current graph and `R` resets the viewport.
- Click a wire, bus, gate, or module to inspect it in Properties.
- `Ctrl+F` searches cells and named nets in the current scope.

## Pin Labels

The `Pins` toolbar menu applies changes immediately. The same settings are
available in Preferences and are persisted under `schematic` in the project
JSON.

`Pin label mode`:

- `Automatic`: labels appear according to the compact and detailed zoom
  thresholds.
- `Always`: labels remain enabled at every zoom. Collision handling may still
  hide non-selected labels when no safe placement exists.
- `Hidden`: normal labels are hidden.

`Pin visibility`:

- `Connected pins only`: suppresses unconnected pins from normal label
  presentation.
- `All pins`: includes connected and unconnected pins.

`Group bus labels` shows a range such as `data[31:0]` instead of one label per
bit. Grouping is presentation-only; the underlying nets remain bit-accurate.

Hovering a pin temporarily reveals its individual label regardless of normal
LOD. The tooltip reports pin name, connected net, bit/range, direction, and
width. Selecting a net keeps its endpoint labels visible; selecting a cell
keeps that cell's pin labels visible.

## Bus Wires

`Bus visualization` controls wire presentation:

- `Automatic`: consolidated trunks below the configured zoom threshold,
  individual wires above it.
- `Bundled`: prefer consolidated trunks.
- `Individual`: always draw bit-level wires.

Bundling does not rewrite connectivity. The original Yosys net ids and per-bit
edges remain authoritative. If trunk geometry cannot be produced, rendering
falls back to the original bit-level routes.

## Routing Quality

- `FastPreview`: intended for dense CPU-class scopes.
- `Balanced`: default quality/cost trade-off.
- `Production`: highest routing effort for manageable scopes.

Automatic downgrade uses node, port, and edge density. Graphs beyond the
monolithic safety limits are rejected with an actionable hierarchy-preserving
re-synthesis diagnostic rather than being sent to a runaway ELK layout.

## Visual Regression

Gate pin LOD is protected by deterministic Skia PNG crops in
`tests/Bistable.UiTests/golden/`. They cover both sides of compact and detailed
thresholds, grouped and individual bus labels, WEST/EAST pins, boundaries,
expanded compounds, and long names.

Regenerate only after reviewing the visual change:

```bash
dotnet build tests/Bistable.UiTests/Bistable.UiTests.csproj \
  --no-restore -p:GenerateVisualGoldens=true
tests/Bistable.UiTests/bin/Debug/net10.0/Bistable.UiTests
```

Then run the normal comparison:

```bash
dotnet build Bistable.slnx
dotnet test tests/Bistable.UiTests/Bistable.UiTests.csproj --no-build
```

## Current Performance Baseline

- Hierarchical RV32 artifact: 179 nodes, 2,013 ports, 1,138 edges, 21 bus
  bundles, and 21/21 trunk geometries.
- Graph build: about 32 ms.
- FastPreview ELK route: about 2.4 seconds on the development machine.
- Real-Skia headless render: the 2,000-cell pan test remains at or above 30 FPS.
- Dense visible-label fixture: 80 modules and 640 labels remains below the
  1.5-second initial-render budget.

The checked-in `samples/riscv_single_cycle` synthesis JSON must be regenerated
before recording a labels-enabled versus labels-hidden RV32 frame delta; the
current artifact is stale/flattened and is not a valid hierarchy-preserving
viewer benchmark.
