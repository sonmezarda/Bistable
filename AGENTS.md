# AGENTS.md — Cold-Start Guide for Bistable

> Single entry point for any agent starting fresh on this repo. Read this first.
> It tells you what the project is, what is *actually true in the code* (docs
> drift), which document to trust for what, and the guardrails you must not break.
>
> **Last reconciled with code:** 2026-07-17. If you find code that contradicts
> this file, trust the code and update this file in the same change.

---

## 1. What Bistable is

An interactive **desktop EDA tool** (.NET 10 + Avalonia) for inspecting, simulating,
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
| `docs/PHASES/PHASE-9.md` | **Active work:** live-reload pipeline and IDE-like Source/Problems workspace; automatic gates complete, owner manual acceptance pending |
| `docs/PHASES/PHASE-6.5.md` + `docs/HANDOFFS/PHASE-6.5-GATE-PIN-LABELS-NEXT.md` | Historical gate-level status; remaining closure moved to Phase 13 |
| `docs/RTL_SCHEMATIC_VISUAL_ISSUES.md` | RTL-schematic expand-defects: Issues 1–5 **done** (2026-07-16); open follow-ups = Issue 4 **Stage 2** (MemoryWritePrimitive) + pending user visual acceptance on `riscv_single_cycle` |
| `docs/DESIGN_AST.md` | Design IR / AST spec (`Bistable.Core.Design.Ast`) |
| `docs/PROTOCOL.md` | GUI ↔ worker JSON line protocol |
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

## 6. Current status snapshot (2026-07-17)

- Branch `schematic/label-placement`, ahead of `main`, with **uncommitted work**
  in the tree (RTL-schematic fixes + docs + this file). Do not commit without
  explicit user approval.
- Build clean (0/0). Phase 9 validation: 919/924 tests passed in the parallel
  full run; the three known `ElkRunnerCancellation*`, one
  `SimulationWorkerClientCancellation*`, and one `GateSchematicPerformance*`
  timing failures all passed in isolated family runs. Golden snapshots are
  unchanged.
- **Most recent work (this session):** Phases 7 and 8 completed. Phase 7 added
  `CombinationalProjector`, comb target/read coverage, the `u_alu.zero` edge
  regression, and owner-accepted visual closure. Phase 8 added protocol v3
  hello/capabilities, `ReadSignals` batch IPC with per-path outcomes and 4K
  chunking, one-event `LiveProbeService` frame refresh, and stale-worker
  rejection. The 128-probe measurement improved from 533.9 ms / 128 IPC turns
  to 7.7 ms / one turn.
- **Open work is governed by `docs/ROADMAP.md`** (phases 7–14, binding order):
  Phases 7 and 8 are complete; active work is **Phase 9** (watch → incremental
  elaborate live loop + its source/diagnostics workspace). Its automatic gates
  are implemented; owner manual acceptance remains before Phase 10. Old follow-ups were
  absorbed: Issue 4 Stage 2 → P12-9, Phase 6.5 closure + Expand Cone → Phase 13,
  SVG export → Phase 10. Do not start visual-polish work before Phases 7–9 close
  (owner's instruction).

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
