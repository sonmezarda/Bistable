# Phase 6.5 Gate Pin Labels - Cold-Start Handoff

## Mission

Continue the gate-level schematic work toward a Vivado-class viewer. The current
change adds configurable pin-name rendering and zoom LOD without changing
bit-level connectivity. The next agent must preserve correctness, selection,
simulation cross-probe, and large-design render performance.

Repository: `/home/ardac/projects/verilatorGUI`

Branch: `schematic/label-placement`

Do not commit unless the user explicitly asks. The worktree contains substantial
uncommitted Phase 6.5 work from multiple tasks. Do not revert unrelated changes.

Read first:

1. `AGENTS.md`
2. `docs/PHASES/PHASE-6.5.md`
3. `/home/ardac/.claude/plans/phase-6_5-wave5-routing-performance.md`
4. `/home/ardac/.claude/projects/-home-ardac-projects-verilatorGUI/memory/MEMORY.md`
5. The files listed under "Implemented surface".

## Implemented Surface

### Project configuration

`src/Bistable.Core/Projects/SchematicConfiguration.cs`

- Added `GatePinLabelMode`: `Automatic`, `Always`, `Hidden`.
- Added project-scoped settings:
  - `GroupGateBusPinLabels`
  - `GatePinLabelCompactZoom`
  - `GatePinLabelDetailedZoom`
- Defaults are conservative for RV32-scale designs:
  - automatic mode
  - grouping enabled
  - compact labels at `0.55`
  - detailed labels at `0.90`

These fields serialize under the existing `schematic` object in
`.bistable.json`. JSON remains storage; all settings are editable from the GUI.

### LOD and grouping model

`src/Bistable.App/Views/GatePinLabelLayout.cs`

- Resolves labels without mutating the ELK graph.
- Below compact threshold: no labels.
- Compact range: bus bits collapse to one range label such as `data[31:0]`.
- Detailed range: grouping follows the project toggle.
- `Always` bypasses zoom thresholds.
- `Hidden` suppresses labels at every zoom.
- Threshold normalization clamps to `0.05..8.0` and enforces
  `DetailedZoom >= CompactZoom`.

Important: grouping is currently **label grouping only**. The graph retains one
port/edge/net id per bit. Do not replace those edges with a cosmetic single wire;
that would break per-bit net selection and simulation cross-probe.

### Rendering and sizing

`src/Bistable.App/Views/GateSchematicCanvas.cs`

- Draws pin labels in the overlay pass above wires.
- Places WEST-side labels inside the node to the right of the pin and EAST-side
  labels inside the node to the left.
- Uses a semi-opaque background behind text to prevent wire/text collisions.
- LOD is evaluated from current world zoom.
- Existing two-pass body/edge/overlay ordering remains intact.

`src/Bistable.App/Services/Routing/Elk/GateNetlistElkBuilder.cs`

- Sub-module width accounts for connected input and output pin label lengths.
- Top-level `IN/OUT` boundaries account for long pin labels.
- Height still scales by rendered bit rows, preserving wire separation.

### GUI

`src/Bistable.App/Views/GateLevelSchematicView.cs`

- Added a `Pins` flyout to the gate toolbar.
- Mode, grouping, compact threshold, and detailed threshold apply immediately.
- Open gate documents observe `MainWindowViewModel` changes.

`src/Bistable.App/Views/PreferencesWindow.cs`

- Added the same settings to project Preferences.
- The form is scrollable.

`src/Bistable.App/ViewModels/MainWindowViewModel.cs`

- Exposes normalized two-way properties.
- Updates the in-memory project and existing Save flow.

`src/Bistable.App/Views/MainWindow.cs` and
`src/Bistable.App/Views/GateLevelSchematicWindow.cs`

- Pass the full `SchematicConfiguration` into gate schematic views.

## Tests Added or Extended

- `tests/Bistable.Tests/Synthesis/GatePinLabelLayoutTests.cs`
  - automatic hide
  - compact grouping
  - detailed ungrouped labels
  - always mode
  - hidden mode
  - threshold normalization
- `tests/Bistable.Tests/Synthesis/GateNetlistHierarchyTests.cs`
  - sub-module width includes pin labels
  - boundary width includes pin labels
- `tests/Bistable.Tests/Synthesis/SynthesisSettingsViewModelTests.cs`
  - in-memory settings
  - JSON persistence
- `tests/Bistable.UiTests/GateSchematicPerformanceTests.cs`
  - all LOD modes execute through the real Avalonia render path
  - graph connectivity remains unchanged

Final verification:

```text
Build: 0 warnings, 0 errors
Focused Bistable.Tests: 35 passed
GateSchematicPerformanceTests: 3 passed
Full solution: 797/797 passed
  - Bistable.Tests: 774
  - Bistable.Snapshots: 14
  - Bistable.Regression: 4
  - Bistable.UiTests: 5
```

Run before making further changes:

```bash
dotnet build Bistable.slnx
dotnet test Bistable.slnx --no-build
```

## Required Manual Acceptance

Use `samples/riscv_single_cycle`:

1. Build and synthesize with hierarchy retained.
2. Open the gate-level top.
3. Verify ALU/register-file module pins show names at normal zoom.
4. Zoom out below compact threshold: names disappear in `Automatic`.
5. Set mode to `Always`: names remain visible.
6. Disable grouping and zoom beyond detailed threshold: bit labels appear
   individually.
7. Enable grouping: a bus becomes one range label.
8. Expand ALU in place: primitive symbols remain correct and labels stay above
   wires.
9. Reopen the project and verify saved settings return.
10. Confirm wire click/highlight and per-bit selection still work.

Also inspect a long-port-name design and top-level `IN/OUT` boundaries for
clipping.

## Next Production Work

Proceed in this order.

### 1. True bus visualization model

Implement real bus presentation as metadata, not destructive graph rewriting.

**Step A — metadata model (landed 2026-06-10):**

- `GateBusBundle` + `GateBusBundleMember` records live in
  `src/Bistable.App/Services/Routing/Elk/GateBusBundle.cs`.
- `GateNetlistElkBuildResult.Bundles` exposes inferred bundles; per-bit edges
  and port refs are unchanged.
- Inference is structural (grouped by `(sourceNode, sourceBase) →
  (targetNode, targetBase)`); single-bit groups never produce bundles.
- Member edges are tagged with `LayoutOptions["bistable.bundleId"]`.

**Step B — render + selection + bus-shape coverage (landed 2026-06-10):**

- `GateSchematicCanvas.SetGraph` accepts the bundle list and indexes it by id.
- The initial compact presentation thickened bundle members while preserving
  bit-accurate edges. Step C below supersedes that presentation with a real
  consolidated centerline and endpoint collectors.
- `HitTestNet` now returns `(netId, bundleId?)` derived from the clicked
  member edge's `LayoutOptions[bistable.bundleId]`; clicking any single bit of
  a bus selects the whole bundle.
- New `BundleSelected` event surfaces a `GateBusBundleSelection` payload (full
  bundle record). `GateLevelSchematicView` renders Name / Range / Width /
  From / To in the right-side properties panel and updates the selection
  status bar.
- Tests cover wide bus, edge tagging, per-bit preservation, single-bit guard,
  fan-out split to different targets (no bundle), constant-bit thinning,
  reversed bit order, sparse buses with a missing bit, two-input concatenation
  producing two distinct bundles, and partial fan-out to two child instances.

**Step C — true trunk geometry + bit drill-down (landed 2026-06-11):**

- `GateBusBundleGeometryBuilder` derives one consolidated centerline from the
  representative member's routed ELK path, preserving ELK obstacle avoidance.
- Synthetic orthogonal collectors connect all source/target bit pins to the
  trunk near each endpoint. Geometry generation is post-layout and
  non-destructive; the original per-bit edges remain the connectivity model.
- Bundled LOD suppresses member-edge painting and draws one screen-stable trunk
  plus thinner fan legs. If geometry cannot be produced, rendering
  automatically falls back to the original per-bit edges.
- Hit-testing covers both trunk and fan geometry. Bundle highlight remains
  coherent in bundled and individual modes.
- Added project-scoped `Automatic` / `Bundled` / `Individual` wire modes and an
  adjustable trunk zoom threshold. Both gate toolbar and Preferences edit the
  values, which persist under `schematic`.
- The Bus properties panel now contains a virtualized constituent-bit list.
  Selecting a bit drops into the exact member net and centers/highlights it.
- Real RV32 artifact measurement: 179 top-level nodes, 1,138 edges, 21 bundles,
  and 21/21 trunk geometries produced. Build took about 32 ms and FastPreview
  ELK routing about 2.4 s on the development machine.

Step C is complete. "Show connected pins" / "Show all pins" remains part of
interaction overrides below.

Step C verification:

```text
Build: 0 warnings, 0 errors
Full project totals: 817/817 passed
  - Bistable.Tests: 793
  - Bistable.Snapshots: 14
  - Bistable.Regression: 4
  - Bistable.UiTests: 6
```

An earlier solution-wide parallel invocation briefly exceeded two pre-existing
500 ms cancellation timing assertions under concurrent project load. Both
tests passed in isolation (244 ms / 380 ms), and the final sequential project
runs passed 817/817. No cancellation code changed in Step C.

Do not infer buses only from names. Yosys `GatePort.Bits` ordering and net ids
are authoritative.

### 2. Collision-aware label placement

Completed on 2026-06-11.

- Added a deterministic screen-space occupancy engine with stable candidate
  order: inside-above, inside-below, outside-above, outside-below.
- Sub-module titles, expand badges, primitive bodies, boundary headers, pin
  dots, and already placed labels participate in collision checks.
- Module labels may occupy their own body, while request-level ownership lets a
  label ignore only its own pin dot rather than every pin on the same node.
- Selected-cell labels are placed first and use a least-collision fallback.
  Non-priority labels are hidden when no safe candidate exists.
- A graph-scoped world-space node index is rebuilt only when `SetGraph` runs.
  Each frame queries only the visible region plus a bounded margin, including
  nested absolute coordinates and a guarded path for very large compounds.
- Placement remains screen-space stable across zoom levels, while rendering and
  hit-test connectivity remain unchanged.
- Added shape tests for collision ordering, ownership, viewport culling,
  determinism, nested offsets, large nodes, and stable deduplication.
- The existing 2,000-cell render/pan budgets remain green. A dedicated dense
  visible-label case (80 modules / 640 labels) also remains below its 1.5 s
  initial-render budget.

Verification:

```text
Build: 0 warnings, 0 errors
Full project totals: 830/830 passed
  - Bistable.Tests: 805
  - Bistable.Snapshots: 14
  - Bistable.Regression: 4
  - Bistable.UiTests: 7
```

Hovered-pin priority is intentionally part of interaction overrides below,
because it requires pointer-state and tooltip semantics rather than placement
policy alone.

### 3. Interaction overrides

Completed on 2026-06-11.

- Added a graph-scoped `GatePinInteractionIndex`, built once in `SetGraph`.
  Pin-to-net lookup, selected-net endpoint lookup, direction, width/range, and
  named-net metadata no longer require scanning every edge on pointer movement.
- Hover hit-testing queries only nodes near the pointer through the existing
  spatial index. A 350 ms delayed tooltip reports pin name, connected net,
  bit/range, direction, and width.
- A hovered pin gets an individual high-priority label even below Automatic
  LOD. Selected-net endpoint labels and all selected-cell labels remain visible;
  interaction overrides also work when the normal label mode is `Hidden`.
- Added project-scoped `ConnectedOnly` / `All` visibility modes. The gate
  toolbar and Preferences edit the setting, and `.bistable.json` persists it.
  This scope is orthogonal to `Automatic` / `Always` / `Hidden` LOD policy.
- Cell port directions come from Yosys `port_directions`; top-level boundary
  direction uses module semantics rather than the visual WEST/EAST side.
  Structural edge net ids and `GateNet.Bits` provide connectivity and names.
- Wide-bus forced-label deduplication uses hash indexes, avoiding quadratic
  work when selecting 512/1024-bit nets.

Verification:

```text
Build: 0 warnings, 0 errors
Full project totals: 834/834 passed
  - Bistable.Tests: 809
  - Bistable.Snapshots: 14
  - Bistable.Regression: 4
  - Bistable.UiTests: 7
```

### 4. Visual regression coverage

Completed on 2026-06-11.

- UI tests now use the real Skia rasterizer rather than the null headless
  drawing backend.
- Added four deterministic 640x340 PNG crops at zoom `0.54`, `0.56`, `0.89`,
  and `0.91`, immediately below/above compact `0.55` and detailed `0.90`.
- One controlled fixture covers grouped and individual bus labels, WEST/EAST
  ports, top-level boundaries, an expanded compound with a primitive child,
  and long input/output names.
- PNG bytes are compared exactly. A mismatch writes `.actual.png`; baseline
  regeneration is an explicit conditional executable path and must be reviewed.
- Two consecutive comparison runs produced byte-identical output.

Verification:

```text
Build: 0 warnings, 0 errors
Full project totals: 838/838 passed
  - Bistable.Tests: 809
  - Bistable.Snapshots: 14
  - Bistable.Regression: 4
  - Bistable.UiTests: 11
```

### 5. Documentation and phase closure

In progress.

- User-facing settings, navigation, interaction, routing, visual-regression,
  and performance guidance is now in `docs/GATE_LEVEL_SCHEMATIC.md`.
- Recorded the validated hierarchical RV32 baseline: 179 nodes, 2,013 ports,
  1,138 edges, 21 bundles, graph build about 32 ms, FastPreview route about
  2.4 s.
- Real-Skia 2,000-cell pan and 80-module/640-label budgets remain green.
- Exact labels-hidden versus labels-enabled RV32 frame timing is intentionally
  not reported from the checked-in sample artifact: that JSON is currently
  stale/flattened and does not represent the hierarchy-preserving viewer path.

Remaining closure gates:

1. Regenerate the RISC-V synthesis JSON through the current GUI/Yosys flow.
2. Record labels hidden / grouped / detailed frame timings on that artifact.
3. Manually accept RV32 pin readability, hover tooltip, bus trunk, and expanded
   module behavior.
4. Add a bounded logic-cone or multi-resolution macro view for intrinsically
   large leaf modules. The current RISC-V register file is hierarchy-preserved
   but still contains 5,393 primitive cells because its full-array asynchronous
   reset forces Yosys `mem2reg`; re-synthesis cannot make that leaf routable.
5. Close Phase 6.5 only after those measurements and manual acceptance.

## Vivado Reference Behavior

Vivado exposes named hierarchical pins and allows pins to be hidden/shown to
control schematic density. The implementation intentionally follows that model:
labels are available, but their visibility is user- and zoom-controlled.

- https://docs.amd.com/r/en-US/ug893-vivado-ide/Using-the-Schematic-Window
- https://docs.amd.com/r/en-US/ug893-vivado-ide/Expanding-Logic-from-Selected-Cells-and-Pins
- https://docs.amd.com/r/en-US/ug893-vivado-ide/Schematic-Window-Display-Settings
- https://docs.amd.com/r/en-US/ug893-vivado-ide/Schematic-Window-Toolbar-Commands

## Guardrails

- Keep RTL `SchematicPreviewControl` separate from gate-level rendering unless a
  shared abstraction removes proven duplication.
- Preserve `GateBit` constant behavior.
- Preserve per-bit net ids and selection semantics.
- Do not put layout or text measurement back on the UI thread.
- Do not add per-frame LINQ over the complete RV32 graph.
- Do not regress cancellation, process ownership, or the two-pass renderer.
- Do not commit without explicit user approval.
