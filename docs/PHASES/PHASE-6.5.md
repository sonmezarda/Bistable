# Phase 6.5 — Vivado-class Gate-Level Viewer

**Status:** In progress — Wave 1/2/3 landed; Wave 4 gate-level worker landed; Wave 5 routing-performance work landed.
**Master plan:** `/home/ardac/.claude/plans/fluffy-wishing-kettle.md` Phase 6.
**Phase goal:** Turn the current single-page Canvas-based gate viewer into a Vivado-Synthesized-Design-class navigator: hierarchical module browsing, click-to-expand, pan + zoom, real gate symbols, tabbed scope, performant rendering on RV32I-scale designs.
**Prerequisite:** P6-1..P6-7 done (this branch). Phase 4 live-value infrastructure recommended but not blocking.

---

## 1. Why this phase matters

The current `GateLevelSchematicWindow` proves the pipeline (RTL → Yosys → JSON → ELK → Canvas) works, but the window itself is unusable on real designs:

- **No zoom + no pan**: the RISC-V netlist renders thousands of cells in a fixed scrolled area, you can only see a corner.
- **Avalonia control explosion**: one `Border` per node + one `Polyline` per edge means N×Avalonia controls. RV32I synthesised reaches ~2k cells → 2k Borders → layout loop chokes the UI.
- **Flat netlist**: even though `flatten: false` is set, the renderer only walks the top module's `cells` dictionary. Sub-module hierarchy is in the Yosys JSON (`u_middle.type = "middle"`, `middle.cells = …`) but we never descend into it.
- **No symbols**: the RTL viewer has IEEE-91 gate bodies (`DrawAndGate`, `DrawOrGate`, FF box with `>` clock arrow, mux trapezoid). The gate viewer redraws every node as a featureless rectangle.
- **No simulation**: gate-level worker isn't compiled, so we can't tick the post-synthesis design and compare RTL vs gate-level outputs.

Vivado's Synthesized Design viewer solves all of this. Until the gate viewer behaves like Vivado, the synthesis backend stays a demo.

---

## 2. Vivado reference behaviour (what we model on)

What Vivado does that we need:

| # | Behaviour | Why it matters |
|---|---|---|
| 1 | Hierarchical canvas with sub-module instances as expandable blocks | Without it, a 2k-cell netlist is a wall of gates |
| 2 | Double-click instance → enter that module's schematic (breadcrumb up top) | Drill-down navigation, same as the RTL viewer |
| 3 | Ctrl+wheel zoom, middle-mouse / right-mouse drag pan, F6 / `F` fit | Required to read anything |
| 4 | Real gate symbols (AND / OR / XOR / NOT / BUF / MUX / FF / Latch) | Recognisability — flat boxes don't tell you what gate it is |
| 5 | Tabbed scope: multiple schematic windows can be open at the same time, snap to dock | Compare RTL vs gate-level side by side |
| 6 | Click a wire → highlight every fanout segment | Trace signals across the design |
| 7 | Click a cell → properties panel: cell type, parameters, source file:line | Cross-probe to the RTL line that produced this gate |
| 8 | Find (Ctrl+F): cell name or net name → scroll the canvas there + highlight | Navigate a 100k-cell design |
| 9 | Gate-level simulation correlated with the RTL waveform | "Did synthesis change behaviour?" smoke check |

What Vivado does that we are NOT chasing yet (out of scope for 6.5):

- Liberty / timing back-annotation, SDF, STA reports
- Power / area visualisation overlays
- Layout-aware view (post-place-and-route)
- Formal equivalence checking

---

## 3. Architecture overview

```
GateNetlist (whole design, with hierarchy preserved)
      │
      ▼ enter scope (path: [top, u_middle, u_inner])
GateScopeViewModel
      ├── current module
      ├── breadcrumb stack
      ├── expansion set (which sub-instances are inlined?)
      └── selection (cell or net)
      │
      ▼ build per-scope
GateScopeElkBuilder (replaces single-shot GateNetlistElkBuilder)
      │
      ▼
ElkGraph (compound nodes for expanded sub-modules)
      │
      ▼
ElkRunner (Node subprocess, cached)
      │
      ▼
GateSchematicControl (custom Avalonia Control)
      ├── DrawingContext-based rendering (NOT Avalonia child controls)
      ├── Pan + zoom (mirror SchematicPreviewControl)
      ├── Real gate symbols (reuse SchematicPreviewControl.Symbols.cs painters)
      └── Hit testing for click-to-select / click-to-expand
```

The key shift: stop using one Avalonia Border per node. Custom-paint nodes the same way the RTL schematic does. The RTL renderer already handles 1k+ primitives smoothly because it draws geometry directly to `DrawingContext`; the gate viewer must do the same.

---

## 4. Task board

Status legend: `todo`, `in_progress`, `done`, `blocked`

| ID | Task | Status | Est. | Notes |
|----|------|--------|------|-------|
| P6.5-1 | Extend `GateNetlistElkBuilder` to support hierarchical scopes | done | 2 d | `BuildScope(netlist, scopePath)` returns one module scope; sub-module instances render as `inst_` nodes. |
| P6.5-2 | New `GateScopeViewModel` — breadcrumb path, expansion set, selection | done | 2 d | Scope path and breadcrumb state are currently owned by `GateLevelSchematicWindow`; separate VM extraction deferred until dock/tab integration. |
| P6.5-3 | New `GateSchematicControl` Avalonia control — pan / zoom / hit-test, draws to `DrawingContext` | done | 5 d | Implemented as `GateSchematicCanvas`: DrawingContext render, middle-drag pan, Ctrl+wheel/+/-/F/R navigation. |
| P6.5-4 | Reuse RTL symbol library for gate-level cells | done | 2 d | Gate-level painter uses `GateCellLibrary` dispatch and draws AND/OR/XOR, inversions, BUF/NOT, MUX, FF, latch, generic boxes. |
| P6.5-5 | Double-click / + button on sub-module instance → push breadcrumb + enter scope | done | 1 d | `inst_` node double-click emits `SubModuleActivated`; window pushes breadcrumb scope and reloads layout. |
| P6.5-6 | Wire highlight on click — every fanout of the clicked net | done | 2 d | Canvas hit-tests edge segments and port dots, parses edge `net{id}` labels, highlights all fanout edges for the selected net. |
| P6.5-7 | Cell properties side panel | done | 1 d | Right-side properties panel shows selected cell name/type/source/parameters/attributes; `YosysJsonReader` now reads cell attributes. |
| P6.5-8 | Ctrl+F search across cells + nets | done | 1 d | Right-side search filters current-scope cells and named nets; result click centers canvas and selects/highlights the target. |
| P6.5-9 | Tabbed scope dock — multiple schematic tabs in one window | todo | 2 d | Use the existing Dock.Avalonia infrastructure the RTL viewer already lives in. RTL tab + gate-level tab(s) side by side. |
| P6.5-10 | Gate-level worker build path (Yosys → Verilog → Verilator) | done | 4 d | Yosys emits synthesized SV via `write_verilog`; `GateLevelWorkerBuildService` feeds it through Verilator XML + `SimulationWorkerBuilder` with separate `top__gate` worker artifacts. |
| P6.5-11 | RTL vs gate-level smoke comparison | done | 4 d | `RtlVsGateLevelComparator` drives both workers in lockstep and diffs frames per cycle; `RtlVsGateLevelComparatorTests` pins the diff semantics with synthetic frames; `RtlVsGateLevelIntegrationTests` runs a real RTL ↔ Yosys-synthesized worker pair through 20 cycles and asserts every top-level port agrees. |
| P6.5-12 | Performance regression test (RV32I 2k-cell render < 2 s, pan stays > 30 fps) | todo | 1 d | Headless `Bistable.UiTests` measurement against the RISC-V sample. |

**Total estimate: ~27 days serial, ~16 days with 2-agent parallelism.**

---

## 5. Implementation phasing

Recommended landing order, smallest user-visible improvement first:

```
Wave 1 — make the current view usable (4-5 days):
  P6.5-3  GateSchematicControl with pan/zoom (draws to DrawingContext)
  P6.5-4  Real symbol painters

Wave 2 — hierarchy + navigation (4-5 days):
  P6.5-1  Hierarchical builder
  P6.5-2  GateScopeViewModel + breadcrumb
  P6.5-5  Double-click expand
  P6.5-12 Performance regression test

Wave 3 — selection + search (4-5 days):
  P6.5-6  Net highlight
  P6.5-7  Properties panel
  P6.5-8  Find

Wave 4 — gate-level simulation + dock integration (8-10 days):
  P6.5-10 Gate-level worker
  P6.5-11 RTL vs gate-level compare
  P6.5-9  Tabbed scope dock
```

Each wave produces something the user can drive on the RISC-V sample. Wave 1 alone fixes "I can't see anything".

---

## 6. Risk register

| Risk | Mitigation |
|------|-----------|
| Custom `Control` shares too little with `SchematicPreviewControl` and we end up duplicating rendering | Wave 1 includes a refactor: extract pan/zoom + symbol painters into a shared `SchematicCanvasBase` or static helpers BEFORE writing the gate-level control. |
| Yosys hierarchy preservation breaks when techmap is run inside sub-modules | `techmap` runs after `hierarchy`; sub-module cells stay separate. Verified on `top/middle/inner` chain — see `docs/PHASES/PHASE-6.5-research.md` (TODO). |
| Gate-level worker compile time on big designs | Reuse `SimulationWorkerBuilder` caching; mark gate-level worker as opt-in. |
| RTL vs gate-level mismatch is a real bug we need to fix | That's the point — the compare test is how we find them. |

---

## 7. Recent activity

- **2026-06-04 — Wave 1/2 baseline confirmed from branch state.**
  - `GateSchematicCanvas` is the active custom DrawingContext renderer with pan/zoom and symbol painters.
  - `GateLevelSchematicWindow` owns scope path, breadcrumb, Up navigation, and double-click drill-in through `SubModuleActivated`.
  - `GateNetlistElkBuilder.BuildScope` renders sub-module instances as `inst_` nodes and throws on invalid scope paths.

- **2026-06-04 — Wave 3 selection/search landed.**
  - P6.5-6: added wire/port hit-testing in `GateSchematicCanvas`; selected Yosys net ids highlight all matching `net{id}` edge fanout segments with accent stroke and report the selection in the toolbar/properties panel.
  - P6.5-7: added right-side properties panel for selected cells and nets. `GateCell` now carries generic `Attributes`; `YosysJsonReader` preserves `attributes.src` for source cross-probe readiness.
  - P6.5-8: added Ctrl+F/right-panel search across current-scope cell names/types and user-named nets. Selecting a result centers the canvas and selects/highlights the target.
  - Added synthesis tests for edge net labels and Yosys cell attributes.

- **2026-06-04 — Gate hierarchy usability follow-up.**
  - Sub-module instance double-click drill-in remains available through the breadcrumb navigation path.
  - Sub-module instance sizing now scales with connected bus bit rows, not just declared port count, so wide modules such as register files get enough height for separated pin rows.
  - Added a synthesis regression test covering 32-bit bus sub-module sizing and emitted pin rows.

- **2026-06-04 — Gate-level in-place expansion and boundary density follow-up.**
  - `+` badges now toggle Vivado-style in-place expansion; double-click remains available for breadcrumb drill-in.
  - Gate-level ELK build now accepts expanded instance paths and emits expanded sub-modules as compound nodes with scoped net keys, avoiding cross-module Yosys net-id collisions.
  - Boundary `IN` / `OUT` anchors now size and index ports by rendered bit rows, preventing multi-bit input/output ports from stacking subsequent wires on the same row.
  - `GateSchematicCanvas` now renders and hit-tests gate-level compound nodes recursively, including nested ports and edge coordinate offsets.

- **2026-06-04 — Wave 4 P6.5-10 gate-level worker path landed.**
  - `SynthesisConfiguration` now carries `OutputVerilog`; `YosysScriptBuilder` emits both `write_json` and `write_verilog -noattr`.
  - Added `GateLevelWorkerBuildService`, which elaborates synthesized Verilog through Verilator XML and builds a native simulation worker with the existing Phase 3 worker pipeline.
  - `ProjectConfiguration.WorkerBuildName` separates RTL and gate-level worker artifacts so `top` and `top__gate` builds do not overwrite each other.
  - `MainWindowViewModel` now attempts gate-level worker build after successful synthesis while still opening the gate-level schematic when the worker build fails.
  - Added unit tests for artifact paths/project conversion and an optional real Yosys → Verilog → Verilator worker smoke test.

- **2026-06-04 — Synthesis settings moved from JSON-gated to GUI-first.**
  - The toolbar Synthesize action is now available for every loaded project; projects without a `synthesis` JSON block use GUI-supplied default settings.
  - Added a Project-panel Synthesis settings section with enable, top module, JSON output, synthesized Verilog output, generic-cell, and flatten controls.
  - Added Save support so GUI edits persist back to the project's `.bistable.json`; JSON is now storage, not the only configuration surface.
  - Added ViewModel regression tests for default synthesis settings, in-memory edits, JSON persistence, and disabled-command behavior.

- **2026-06-04 — Wave 4 P6.5-11 RTL vs gate-level smoke comparator landed.**
  - Added `RtlVsGateLevelComparator` (App layer): drives a paired RTL and gate-level `SimulationWorkerClient` through Reset → setup → Eval → N×Tick in lockstep and produces a `CompareReport` with per-cycle `SignalDiff` records. Intersection-by-default signal set so RTL-only `--public-flat-rw` probes don't false-positive; explicit `SignalsToCompare` whitelist forces missing-side surfacing.
  - `CompareReport.FormatSummary(maxLines)` prints the first divergent cycles in a fixed-width table with a `… and N more mismatches` tail, so test assertions surface the actionable signal/cycle without dumping the full timeline.
  - `RtlVsGateLevelComparatorTests` pins diff semantics with synthetic `SimulationFrame` data (no toolchain): identical frames, single divergence, RTL-only signal dropped by default, explicit whitelist surfaces missing side, `FormatSummary` truncation, all-match single-line output.
  - `RtlVsGateLevelIntegrationTests` (Integration trait, skipped without yosys+verilator) builds a tiny `toggle_counter` (clk + async reset + enable + 4-bit count + msb) through the full RTL pipeline AND the Yosys → `GateLevelWorkerBuildService` gate path, runs both workers for 20 cycles, and asserts every top-level port matches every cycle.

- **2026-06-04 — Wave 5 W5-1..W5-6 routing-performance foundation landed.**
  - Added `RoutingQuality` + `SchematicLayoutOptions` presets so ELK routing cost can be controlled as data instead of hard-coded constants.
  - Added `SchematicLayoutService`: per-window `ElkRunner` owner, serialized background layouts, cancellation via runner restart, soft warning, and 10-minute hard timeout.
  - `GateLevelSchematicWindow` now reuses one layout service per window, routes asynchronously, cancels superseded scope/expand requests, shows a delayed cancel overlay, and keeps the previous successful schematic visible on cancel/failure.
  - Added project-scoped `schematic.routingQuality` + `schematic.autoDowngradeLargeGraphs` settings, editable from Preferences and persisted through project JSON.
  - Added large-graph routing-quality resolver: above 1000 routable nodes, gate-level layout auto-switches to `FastPreview` unless the project disables auto-downgrade.
  - Replaced `ElkRunner`'s 45-second default cap with a 10-minute sanity timeout; user-facing progress/cancel now lives in the gate-level layout service.
  - Acceptance recap: gate-level routing is no longer spawn-per-layout or UI-thread blocking; superseded layouts are cancelled; cancel preserves the previous successful canvas; routing quality is GUI-editable and project-persisted; large designs get a fast-preview escape hatch.
