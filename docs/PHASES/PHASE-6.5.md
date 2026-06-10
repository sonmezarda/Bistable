# Phase 6.5 — Vivado-class Gate-Level Viewer

**Status:** Complete — hierarchical viewer, simulation, docked scope tabs, cancellable routing, and RV32-class rendering performance landed.
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
| P6.5-9 | Tabbed scope dock — multiple schematic tabs in one window | done | 2 d | Gate scopes are closeable/floatable Dock.Avalonia documents; double-click opens or reuses a scope tab and each tab owns/disposes its layout process. |
| P6.5-10 | Gate-level worker + interactive simulation path (Yosys → Verilog → Verilator) | done | 4 d | Yosys emits synthesized SV; the GUI now selects RTL/Gate targets and routes top-level input drive, Eval, Tick, Run, Reset, output values, waveform refresh, and live probes to the active worker. |
| P6.5-11 | RTL vs gate-level smoke comparison | done | 4 d | `RtlVsGateLevelComparator` drives both workers in lockstep; GUI `Compare` runs the selected cycle budget against top-level outputs and reports the first mismatches. Real RTL ↔ Yosys worker integration remains covered. |
| P6.5-12 | Performance regression test (RV32I 2k-cell render < 2 s, pan stays > 30 fps) | done | 1 d | Deterministic 2,000-cell/1,999-edge headless benchmark enforces initial render <2 s and panning ≥30 FPS; overview LOD and graph caches satisfy the budget. |

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
  - Added large-graph routing-quality resolver: dense node, port, or edge counts auto-switch gate-level layout to `FastPreview` unless the project disables auto-downgrade.
  - Replaced `ElkRunner`'s 45-second default cap with a 10-minute sanity timeout; user-facing progress/cancel now lives in the gate-level layout service.
  - Acceptance recap: gate-level routing is no longer spawn-per-layout or UI-thread blocking; superseded layouts are cancelled; cancel preserves the previous successful canvas; routing quality is GUI-editable and project-persisted; large designs get a fast-preview escape hatch.

- **2026-06-07 — Gate-level simulation connected to the GUI.**
  - Added an explicit `RTL` / `GateLevel` simulation target shared by the main toolbar and gate-level window.
  - Existing input drive, Eval, Tick, Run, Reset, output-value, waveform, trace, and live-probe flows now resolve through the active worker instead of always using the RTL worker.
  - Added GUI `Compare`: resets both workers, applies the current top-level inputs, runs the selected clock/cycle budget in lockstep, and reports top-level output mismatches.
  - Gate worker lifecycle now tracks its own trace artifact, safely falls back to RTL when rebuilt/disposed, and is unavailable during isolated RTL sub-simulation.

- **2026-06-07 — Synthesized memory runtime mapping landed.**
  - Added `LoweredMemoryProbeMapper`: logical memory identity comes from source `DesignAst` unpacked-array declarations, never from `mem` naming conventions or indexed-name regex inference.
  - When Yosys lowers a memory into scalar registers, the mapper resolves every physical gate-AST element and publishes one synthetic memory probe at the original RTL hierarchy path.
  - `SimulationWorkerBuilder` now emits indexed C++ accessors for lowered-memory elements; existing `ReadMemory` / `WriteMemory` protocol and `CpuRunEngine` remain target-independent.
  - `GateLevelWorkerBuildService` validates configured program-memory bindings and writes `gate-runtime-map.json` beside the native worker. Partial or optimized-away mappings fail with an explicit diagnostic instead of silently loading the wrong state.
  - Acceptance: an arbitrary `storage_bank` memory passes a flattened Yosys → Verilator read/write integration test; the real `riscv_single_cycle` gate worker loads instructions, executes them, and reaches `x1=5`, `x2=3`, `x3=8`, `halted=1`.
  - Verification: **766/766 tests green** (746 Tests + 14 Snapshots + 4 Regression + 2 UI); solution build has 0 warnings and 0 errors.

- **2026-06-07 — Routing cancellation deadlock fixed.**
  - `ElkRunner.Layout` no longer holds the process-state lock while waiting for Node/ELK output.
  - Cancel and dispose atomically detach the active process and kill its complete process tree, so a blocked layout cannot prevent the Cancel button or application shutdown.
  - Real blocking-subprocess tests verify `Restart`, `Dispose`, and service-level cancellation complete within 500 ms; the service then starts a fresh router and successfully handles the next layout.

- **2026-06-07 — P6.5-9/P6.5-12 closed.**
  - Extracted reusable `GateLevelSchematicView`; the compatibility window is now a thin host.
  - Gate scopes open as closeable, draggable, floatable Dock.Avalonia documents. Reopening the same scope activates the existing tab; tab/project/application close disposes its ELK process.
  - Added overview LOD for dense zoom levels plus cached cell lookup and edge-coordinate context. Full symbols and ports return automatically when zoomed in.
  - Added a 2,000-cell/1,999-edge headless performance regression enforcing <2 s initial render and ≥30 FPS pan rendering.
  - Final verification: **772/772 tests green** (751 Tests + 14 Snapshots + 4 Regression + 3 UI); solution build has 0 warnings and 0 errors.

- **2026-06-10 — Large synthesized-design routing stall fixed.**
  - Yosys now writes the schematic JSON before optional flattening; the viewer retains module hierarchy while gate-level simulation Verilog can remain flattened.
  - Routing quality selection now measures nodes, ports, and edges instead of node count alone. The hierarchical RISC-V top (179 nodes, 2,013 ports, 1,138 edges) selects `FastPreview`.
  - `FastPreview` remains orthogonal and uses minimum layered thoroughness; it routes the reference RISC-V top in about 2.6 seconds without misleading diagonal wires.
  - Large-graph fit zoom now reaches 0.5% when required instead of clipping at 5%; overview nodes and wires retain screen-space visibility at deep zoom-out levels.
  - Expanded-module primitive rendering now resolves exact cell metadata from ELK labels instead of substring-matching hierarchy-prefixed node IDs. ALU children render as AND/OR/XOR/MUX symbols rather than inheriting the parent `riscv_alu` module type.
  - Monolithic artifacts above the safety limits fail before entering ELK with an actionable re-synthesis diagnostic. The former flattened RISC-V graph measured 13,233 nodes, 47,381 ports, and 33,906 edges.

- **2026-06-10 — Bus bundle metadata landed (non-destructive).**
  - `GateNetlistElkBuildResult` now carries a `Bundles` list of `GateBusBundle` records describing which per-bit edges form a logical bus, alongside the existing per-bit ELK ports and edges.
  - Bundle inference is structural, not name-based: edges from the same `(sourceNode, sourceBaseName)` group to the same `(targetNode, targetBaseName)` group on more than one bit form a bundle. `GatePort.Bits` ordering remains authoritative; constants and split fan-out do not collapse into spurious bundles.
  - Each member edge is tagged with `LayoutOptions["bistable.bundleId"]` so the future trunk overlay/hit-test can recover bundle membership in O(1) without re-parsing labels.
  - Per-bit net ids, edge ids, selection, and simulation cross-probe are untouched: the graph still emits one ELK port and one ELK edge per bit. Single-bit scalars never produce a bundle.
  - Added `GateBusBundleTests` covering pass-through buses, edge tagging, per-bit connectivity preservation, fan-out to different targets (no bundle), constants thinning bundle membership, and the single-bit scalar guard.
  - Verification: **803/803 tests green** (780 Tests + 14 Snapshots + 4 Regression + 5 UI); solution build has 0 warnings and 0 errors.
  - Trunk rendering, fan-out drawing, and bundle-aware hit-testing remain follow-ups; see `docs/HANDOFFS/PHASE-6.5-GATE-PIN-LABELS-NEXT.md` next-work items 2–4.

- **2026-06-10 — Configurable gate pin labels and zoom LOD landed.**
  - Gate-level ports now render their declared pin names above wires with an opaque-enough text backing, including hierarchical module pins and top-level boundaries.
  - Added project-scoped `Automatic` / `Always` / `Hidden` modes, configurable compact/detailed zoom thresholds, and optional bus label grouping. The gate toolbar applies changes live; Preferences persists them to `.bistable.json`.
  - Compact LOD displays one logical range such as `data[31:0]`; detailed LOD can display individual bit labels. Connectivity, net ids, selection, and simulation cross-probe remain bit-accurate.
  - Sub-module and boundary widths now account for connected pin label lengths as well as pin-row density.
  - Added resolver, sizing, persistence, and headless Avalonia render coverage. True bus trunk routing remains a separate metadata-preserving follow-up documented in `docs/HANDOFFS/PHASE-6.5-GATE-PIN-LABELS-NEXT.md`.
  - Verification: **797/797 tests green** (774 Tests + 14 Snapshots + 4 Regression + 5 UI); solution build has 0 warnings and 0 errors.
  - Added script-order, graph-density, recursive metric, safety-limit, and real-Yosys hierarchy regressions.
  - Verification: **782/782 tests green** (760 Tests + 14 Snapshots + 4 Regression + 4 UI); solution build has 0 warnings and 0 errors.

- **2026-06-10 — RTL/gate simulation and RTL schematic production hardening.**
  - Simulation actions now share one GUI operation coordinator. Build, Eval, Tick, Run, Reset, Compare, Synthesize, CPU run, isolated simulation, project load, and probe writes cannot mutate worker/session state concurrently; the toolbar exposes a real Cancel action and busy state.
  - Long GUI runs are dispatched in bounded 1,024-cycle worker chunks, providing cancellation checkpoints without corrupting worker state.
  - Worker IPC now treats each written command plus response as an atomic transaction. Cancellation drains the pending response before releasing the stream, preventing the next command from consuming stale protocol output.
  - Worker disposal is idempotent, kills an in-flight process tree promptly, and waits for the IPC gate before releasing resources.
  - Input push no longer publishes one UI/probe frame per input. Only the final Eval/Tick/Run frame refreshes the UI.
  - VCD parsing is streaming and runs off the UI thread; stale parse results are rejected after target/project switches.
  - `LiveProbeService` uses worker generations so late RTL/gate/sub-sim reads cannot populate a replacement session's cache.
  - RTL/Gate comparison drives independent workers concurrently and stops at the first divergent cycle by default.
  - RTL ELK layout no longer runs inside Avalonia `Render()`. Build/routing is asynchronous, cancellable, LRU-cached, and stale results are discarded. Cache identity now includes concrete primitive types and nested primitive catalogs.
  - Added cancellation/framing, shutdown, command cancellation, and layout-cache regressions.
  - Verification: **788/788 tests green** (766 Tests + 14 Snapshots + 4 Regression + 4 UI); solution build has 0 warnings and 0 errors.

### Remaining production-scale simulation work

1. Add a batch probe protocol (`ReadSignals`) so one visible schematic frame costs one IPC round-trip rather than one command per signal.
2. Replace full VCD reparse-after-step with an incremental trace index/tailer and bounded waveform retention.
3. Add a dedicated worker control channel so Pause/Cancel can preempt inside a single native run, rather than at the next 1,024-cycle chunk boundary.
4. Extract worker/target/trace lifecycle from `MainWindowViewModel` into a tested simulation-session controller.
