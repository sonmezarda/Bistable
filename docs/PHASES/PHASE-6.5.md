# Phase 6.5 — Vivado-class Gate-Level Viewer

**Status:** Proposed — follows the GUI-side P6-5/6/7 wave that landed a working but minimal gate-level window.
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
| P6.5-1 | Extend `GateNetlistElkBuilder` to support hierarchical scopes | todo | 2 d | New `BuildScope(netlist, scopePath)` returns the ELK graph for one module. Sub-module instances become either flat blocks (collapsed) or compound nodes (expanded). |
| P6.5-2 | New `GateScopeViewModel` — breadcrumb path, expansion set, selection | todo | 2 d | Mirrors `HierarchyScopeInstanceViewModel` but over `GateNetlist`. Owns the navigation state for the gate-level view. |
| P6.5-3 | New `GateSchematicControl` Avalonia control — pan / zoom / hit-test, draws to `DrawingContext` | todo | 5 d | Lift the relevant pieces from `SchematicPreviewControl`. Don't fork — pull shared bits into a small base class or share renderer helpers (e.g. `DrawElkGateNode`). |
| P6.5-4 | Reuse RTL symbol library for gate-level cells | todo | 2 d | `GateCellLibrary` already maps cell types → `GateCellShape` + `GateKind`. Wire `GateCellShape.Gate` → `DrawElkGateNode`, `FlipFlop` → `DrawElkFlipFlopNode`, etc. |
| P6.5-5 | Double-click / + button on sub-module instance → push breadcrumb + enter scope | todo | 1 d | Same UX as the RTL breadcrumb + expansion strip. Single Window with the schematic + breadcrumb bar. |
| P6.5-6 | Wire highlight on click — every fanout of the clicked net | todo | 2 d | Net id is unique within a module; collect all edges with that net id and paint thicker / accent. Reuses the pinned-signal infrastructure (P2.7-5). |
| P6.5-7 | Cell properties side panel | todo | 1 d | Reusable: small right-side ListBox showing `cell.type`, `cell.parameters`, source attribute (`src`) for cross-probing. |
| P6.5-8 | Ctrl+F search across cells + nets | todo | 1 d | Live-filter ListBox of `cell.name` + `net.name` matches; click → scroll + select. |
| P6.5-9 | Tabbed scope dock — multiple schematic tabs in one window | todo | 2 d | Use the existing Dock.Avalonia infrastructure the RTL viewer already lives in. RTL tab + gate-level tab(s) side by side. |
| P6.5-10 | Gate-level worker build path (Yosys → Verilog → Verilator) | todo | 4 d | Yosys `write_verilog` emits the synthesised SV; feed that back through `SimulationWorkerBuilder`. Reuses Phase 3 / 5 worker infrastructure. |
| P6.5-11 | RTL vs gate-level smoke comparison | todo | 4 d | Run the RISC-V CPU on RTL worker + gate-level worker with the same program; assert same `pc` / `halted` / `debug_xN` per cycle. New `Bistable.Tests` integration test. |
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

(empty — proposed)
