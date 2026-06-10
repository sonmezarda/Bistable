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
- `GateBusBundleTests` cover wide buses, edge tagging, per-bit preservation,
  fan-out splits (no bundle), and constants thinning bundle membership.

**Step B — remaining work:**

- Let the renderer draw one trunk at overview/compact LOD and fan out near
  endpoints. Read the bundle list from `GateNetlistElkBuildResult.Bundles`
  and look member edges up via `LayoutOptions["bistable.bundleId"]`.
- Preserve member edges for detailed LOD and hit testing.
- Clicking a trunk should select the bundle; expanding selection should expose
  individual bits.
- Route bundles in the builder/ELK layer only after measuring whether ELK
  hyperedges or synthetic join/split nodes produce stable orthogonal routes.
- Add tests for reversed ranges, sparse bits, concatenations, and partial
  buses on top of the existing constant-bit / scalar guard coverage.

Do not infer buses only from names. Yosys `GatePort.Bits` ordering and net ids
are authoritative.

### 2. Collision-aware label placement

The current placement is deterministic and protected from wires by a background,
but it is not a general occupancy solver.

- Build a lightweight screen-space occupancy index per frame.
- Candidate positions: inside-above, inside-below, outside-above, outside-below.
- Prefer stable positions to prevent labels jumping while panning.
- Give selected/hovered labels priority.
- Avoid sub-module title, expand badge, ports, and neighboring labels.
- Cap work at visible nodes only; do not scan the whole graph each frame.

Benchmark against the existing 2,000-cell UI performance test.

### 3. Interaction overrides

- Hovering a pin should reveal its label regardless of automatic LOD.
- Selected net/cell labels should remain visible.
- Tooltip should distinguish:
  - module pin name
  - connected net name
  - bit/range
  - direction and width
- Add `Show connected pins` / `Show all pins` equivalent behavior rather than
  overloading `Always`.

### 4. Visual regression coverage

- Add deterministic headless screenshots at zoom values just below/above both
  thresholds.
- Cover grouped and ungrouped buses, WEST/EAST ports, boundaries, expanded
  compounds, and long names.
- Prefer small golden crops over full-window snapshots to reduce unrelated
  churn.

### 5. Documentation and phase closure

- Document the settings in user-facing schematic documentation.
- Record performance measurements for labels enabled/disabled on the RISC-V
  sample.
- Update `docs/PHASES/PHASE-6.5.md` only after manual acceptance and full suite.

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
