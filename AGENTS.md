# AGENTS.md — Cold-Start Guide for Bistable

> Single entry point for any agent starting fresh on this repo. Read this first.
> It tells you what the project is, what is *actually true in the code* (docs
> drift), which document to trust for what, and the guardrails you must not break.
>
> **Last reconciled with code:** 2026-07-18. If you find code that contradicts
> this file, trust the code and update this file in the same change.

---

## 1. What Bistable is

An interactive **desktop EDA tool** (.NET 10 engine; retained Avalonia frontend
plus an active Eclipse Theia workbench POC) for inspecting, simulating,
and now synthesizing SystemVerilog designs through **Verilator** and **Yosys**.
The north-star target is a **Vivado-class schematic viewer + live simulation
debugger** that scales to RV32-class CPU cores.

Core loop: load a `.bistable.json` project → elaborate with Verilator
(`--xml-only`) → build a native Verilator worker → evaluate/tick/run/reset →
inspect ports, hierarchy, internal signals, waveform, and schematic live.

## 2. Solution layout

| Project | Responsibility |
|---------|----------------|
| `src/Bistable.Core` | Backend-agnostic Design IR/AST, projects, synthesis config |
| `src/Bistable.App` | Avalonia UI — schematic (RTL + gate), waveform, hierarchy, viewmodels |
| `src/Bistable.Protocol` | JSON line-protocol between GUI and native worker |
| `src/Bistable.Verilator` | Verilator XML reader + native worker generation |
| `src/Bistable.Yosys` | Gate-level synthesis: `YosysTool`, `YosysScriptBuilder`, `YosysJsonReader`, `GateCellLibrary` |
| `src/Bistable.Engine` | UI-independent elaboration + simulation-session services and shared diagnostics |
| `src/Bistable.EngineHost` | Versioned JSON-line process boundary (protocol v2 with `simulation.*`) for external frontends |
| `src/Bistable.Theia` | Phase 9.5 Theia browser/Electron product-shell POC |

Tests: `tests/Bistable.Tests` (unit/algorithmic), `tests/Bistable.Regression`
(one test per fixed bug), `tests/Bistable.Snapshots` (golden ELK graphs),
`tests/Bistable.UiTests` (headless Avalonia + real-Skia render).

## 3. Build & test

```bash
dotnet build Bistable.slnx
dotnet test Bistable.slnx --no-build
dotnet run --project src/Bistable.App/Bistable.App.csproj   # run the app
```

Requirements: .NET SDK 10, Verilator 5.x, Node.js (for elkjs layout), Yosys (for
synthesis). A build with `0 warnings, 0 errors` is the baseline expectation.

**Known-flaky under parallel load:** `ElkRunnerCancellationTests.*`,
`SimulationWorkerClientCancellationTests.*`, and
`GateSchematicPerformanceTests.*` contain timing assertions that occasionally
trip during the full solution run under CPU contention. Confirm by re-running
the failed test in isolation before treating it as a real regression.

## 4. Actual architecture — read this before touching the schematic

**The schematic router pivoted twice and the older plan docs do not reflect it.**

- The RTL schematic backend is **ELK** (Eclipse Layout Kernel via elkjs Node
  subprocess), selected as the default `SchematicRoutingEngine.Elk` in
  `src/Bistable.App/Views/SchematicPreviewControl.cs`.
- `SchematicRoutingEngine` has four backends: `Elk` (default), `Internal`
  (the pure-C# maze router from the rewrite plan — still present, no longer
  default), `GraphvizDot`, `GraphvizNeato`.
- Layout runs **off the UI thread**, is cancellable (`SchematicLayoutService.LayoutAsync`),
  and LRU-cached (`ElkSchematicEngine` 8-entry SHA-1 cache; gate path adds
  `GateLevelLayoutCache`). Viewport culling exists in `GateSchematicCanvas`.
- **Do not** re-introduce the "pure C# only, delete the old router each phase"
  assumption from `SCHEMATIC_REWRITE_PLAN.md` — that decision was superseded.

Two distinct schematic surfaces exist; keep them separate unless a shared
abstraction removes proven duplication:
- **RTL** schematic: `SchematicPreviewControl.*.cs` + `Services/Routing/Elk/`.
- **Gate-level** schematic: `GateSchematicCanvas.cs`, `GateNetlistElkBuilder.cs`,
  `GateHierarchicalLayoutEngine.cs`, `GateHighFanoutRouter.cs`, bundles, LOD labels.

## 5. Document map — what to trust for what

**Current / authoritative:**

| Doc | Use for |
|-----|---------|
| `AGENTS.md` (this file) | Orientation, real state, guardrails |
| `docs/ROADMAP.md` | **PLAN OF RECORD (2026-07-16):** binding phase order 7→14 for the vision. Check here before starting or proposing ANY work. |
| `docs/VISION_GAP_ANALYSIS.md` | **North star (2026-07-16):** owner's 6-goal vision vs. reality; the evidence behind the roadmap. Read before proposing new features. |
| `docs/ARCHITECTURE.md` | Project layout, ELK pipeline (§5), threading model |
| `docs/SCHEMATIC_ROUTING_BACKENDS.md` | Which router backend is active and why |
| `docs/ELK_ROUTING_PERFORMANCE_ANALYSIS.md` | ELK routing presets + perf numbers (2026-06-12) |
| `docs/GATE_LEVEL_SCHEMATIC.md` | User-facing gate viewer behavior |
| `docs/PHASES/PHASE-9.5.md` | **Active work:** owner-approved Theia + .NET Engine Host workbench spike; measured go/no-go before more UI-heavy phases |
| `docs/PHASES/PHASE-9.md` | Live-reload backend complete; Avalonia Source/dock manual UI gate rejected and moved to Phase 9.5 |
| `docs/PHASES/PHASE-6.5.md` + `docs/HANDOFFS/PHASE-6.5-GATE-PIN-LABELS-NEXT.md` | Historical gate-level status; remaining closure moved to Phase 13 |
| `docs/RTL_SCHEMATIC_VISUAL_ISSUES.md` | RTL-schematic expand-defects: Issues 1–5 **done** (2026-07-16); open follow-ups = Issue 4 **Stage 2** (MemoryWritePrimitive) + pending user visual acceptance on `riscv_single_cycle` |
| `docs/DESIGN_AST.md` | Design IR / AST spec (`Bistable.Core.Design.Ast`) |
| `docs/PROTOCOL.md` | GUI ↔ worker JSON line protocol |
| `docs/ENGINE_HOST_PROTOCOL.md` | Frontend ↔ EngineHost RPC (v2, incl. `simulation.*`) used by Theia |
| `docs/SIMULATION_INTERACTION_UX.md` | Binding manual-drive UX: Vivado visual + Logisim/Digital Poke/popover semantics |
| `docs/SCHEMATIC_COVERAGE.md` | "What the renderer understood" coverage model (Phase 2.9) |
| `docs/TESTING.md` | Test project layout and conventions |

**Historical / partially superseded — read for rationale, not current state:**

| Doc | Caveat |
|-----|--------|
| `docs/SCHEMATIC_REWRITE_PLAN.md` | Phase 0-7 plan for the pure-C# maze router. **See its §0** — RTL moved to ELK; Faz 6 largely delivered on the ELK path; Faz 7 (SVG export) is the only genuinely-unstarted item. |
| `docs/PROFESSIONAL_TOOL_CAPABILITY_ANALYSIS.md` | 2026-06-02 snapshot; synthesis, RTL-vs-gate compare, async ELK, and RV32I have since landed (see the note at its top). |
| `docs/DESIGN_FANOUT_SPEC.md` | Draft spec for deferred RTL packed-struct rendering (`P2-11`); not the gate-level `GateHighFanoutRouter`. |
| `docs/RTL_COVERAGE_TODO.md` | Explicit-unsupported endpoints, not a reopened phase. |
| `docs/PHASES/PHASE-0..6.5.md` | Older phase history. Remaining Phase 6.5 closure is tracked by Phase 13. |

## 6. Current status snapshot (2026-07-18)

- Branch `theia/workbench-poc`. The owner committed the Poke/Drive slice as
  `9da4bb7`; the current P9.5-10 hierarchy-navigation slice is uncommitted. Do
  not commit or push without explicit user approval.
- Build clean (0/0). Current validation: 969/973 passed in the parallel full
  run; known timing failures in `ElkRunnerCancellation*`/
  `SimulationWorkerClientCancellation*` (three) and `GateSchematicPerformance*`
  (one) passed in isolated family runs (cancellation 5/5, gate performance
  5/5). Golden snapshots are unchanged; all six Theia check scripts pass and
  the browser bundle builds with 0 errors.
- **Most recent work (this session):** Phases 7 and 8 completed. Phase 7 added
  `CombinationalProjector`, comb target/read coverage, the `u_alu.zero` edge
  regression, and owner-accepted visual closure. Phase 8 added protocol v3
  hello/capabilities, `ReadSignals` batch IPC with per-path outcomes and 4K
  chunking, one-event `LiveProbeService` frame refresh, and stale-worker
  rejection. The 128-probe measurement improved from 533.9 ms / 128 IPC turns
  to 7.7 ms / one turn.
- **Open work is governed by `docs/ROADMAP.md`**. Phases 7 and 8 are complete;
  Phase 9 backend gates are implemented, but its Avalonia workbench failed
  owner manual acceptance. Active work is owner-approved **Phase 9.5**: a
  measured Theia + .NET Engine Host spike before Phase 10. Old follow-ups were
  absorbed: Issue 4 Stage 2 → P12-9, Phase 6.5 closure + Expand Cone → Phase 13,
  SVG export → Phase 10. Do not start visual-polish work before Phases 7–9 close
  (owner's instruction).
- **Phase 9.5 first slice:** Theia 1.73.1 browser workbench, closeable Bistable
  widget, Explorer/Monaco/Problems packages, `Bistable.Engine`, and protocol-v1
  EngineHost are implemented. Electron validation still needs the host's
  `libxkbfile-dev`/`libsecret-1-dev`; measured migration closure remains open.
- **Phase 9.5 migration slice:** the owner accepted Theia as the strategic UI
  direction. Root project auto-load, 400 ms latest-save-wins HDL reload,
  Problems error/recovery lifecycle, and a top-module schematic graph DTO + SVG
  renderer are implemented. The schematic is now a separate main document;
  ELK runs in the Theia backend and the frontend uses typed RTL symbols/pins
  instead of generic cards. Hierarchical module documents, waveform transport,
  Electron packaging, and measured migration closure remain open.
- **Phase 9.5 live-loop first gate (this session):** the drive→watch loop runs
  on the Theia schematic. `Bistable.Engine.SimulationSessionService` owns the
  native worker (build/`Hello`/probes/live loop) with session generations for
  atomic reload swap; `SimulationValueValidator` rejects bad values before any
  IPC. EngineHost is protocol **v2** with the `simulation.*` methods
  (`docs/ENGINE_HOST_PROTOCOL.md`). The Theia frontend selects a signal, shows
  path/dir/width/value, drives bin/hex/dec + Apply (SetInput→Eval→one batched
  `ReadSignals`), and has Eval/Tick/Reset — value overlays reuse existing
  geometry (no per-frame re-layout). No C# sim math is duplicated in TypeScript.
  Automated tests are green; **owner visual/interaction acceptance is pending.**
- **Phase 9.5 schematic visual contract:** the owner selected AMD Vivado as the
  primary schematic UX reference. Exact net/probe identity is now separate from
  semantic display pins (`S/I0/Y`, `A/B/Y`, `D/CLK/Q`, instance HDL ports).
  Node-side deterministic text metrics give ELK bounded content-aware sizes;
  protected left/right label columns, middle elision, clip paths, two-line
  instance headers, tooltips, and overview LOD prevent generated
  `__schematic_*` names from covering symbols. The next binding slice is
  hierarchical instance expand/collapse + module documents/breadcrumbs.
- **Phase 9.5 manual-simulation UX contract:** visible constant boxes now select
  their exact driven net and remain read-only. Logisim Evolution and Digital
  are the primary interaction references. The owner pulled this slice before
  hierarchy on 2026-07-18: separate Poke/Drive mode now provides one-click
  scalar toggle and an anchored non-modal multi-bit editor with
  BIN/HEX/UDEC/SDEC, per-bit toggles, Apply/OK/Escape, and width-safe BigInt
  conversion. Select mode never mutates simulation state. Owner manual
  acceptance of the Poke slice is still pending.
- **Phase 9.5 hierarchy navigation (P9.5-10, this session, 2026-07-18):**
  Vivado-style instance descent is implemented on the Theia schematic.
  Double-clicking an instance opens its **hierarchical instance path**
  (`top.u_core.u_alu` — never the module type) as a separate dockable
  document; re-opening the same path activates the existing tab. Every
  document shows a `top › u_core › u_alu` breadcrumb whose parent segments
  navigate. EngineHost stays protocol v2 with an additive `schematic.module`
  capability and `loadModuleSchematic` method (`docs/ENGINE_HOST_PROTOCOL.md`),
  served from a cached elaboration; unresolved paths return `invalid_path`.
  Child probe paths are prefixed with the document path, all open documents
  refresh from one batched `ReadSignals` union, and the `topLevelDrivePort`
  choke point keeps child boundary ports read-only even when their names
  collide with top-level inputs (locked by `check-schematic-hierarchy.mjs` and
  `EngineInstancePathResolverTests`). The second slice (2026-07-19) added
  Vivado-style **selective inline expand/collapse**: `EngineSchematicComposer`
  composes chosen instances as nested Container nodes with instance-namespaced
  exact nets and pass-through boundary ports; `loadModuleSchematic` gained an
  additive `expand[]` param (`schematic.expand` capability), ELK lays out the
  nested containers in the backend, and the widget's ⊞/⊟ header toggle
  expands/collapses with per-expansion-state memoization and generation-guard
  cancellation. Remaining: owner visual acceptance (Poke + hierarchy).

## 7. Guardrails (do not break these)

- Preserve **per-bit net ids and selection semantics**. Bus grouping is
  presentation-only; never collapse per-bit edges into a cosmetic single wire.
- `GatePort.Bits` ordering and Yosys net ids are authoritative — **do not infer
  buses from names**.
- Preserve `GateBit` constant behavior.
- Keep layout and text measurement **off the UI thread**. Do not add per-frame
  LINQ over the full RV32 graph.
- Do not regress cancellation, worker process ownership, or the two-pass renderer.
- Keep RTL `SchematicPreviewControl` separate from gate-level rendering unless a
  shared abstraction removes proven duplication.
- **Do not commit or push without explicit user approval.** The working tree may
  hold substantial uncommitted work.

## 8. Working conventions

- Tests: every user-reported bug gets a regression test in `tests/Bistable.Regression`
  in the same change as the fix (see `docs/TESTING.md`).
- When you change how something works, update the doc that documents it in the
  same change — and update this file if the change touches architecture, backend
  selection, or guardrails.
- Samples for manual checks: `samples/riscv_single_cycle` (gate/RV32),
  `samples/arnicomp` (RTL completeness), `samples/tiny_cpu` / `samples/bus_fabric`
  (routing density).
