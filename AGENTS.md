# AGENTS.md — Cold-Start Guide for Bistable

> Single entry point for any agent starting fresh on this repo. Read this first.
> It tells you what the project is, what is *actually true in the code* (docs
> drift), which document to trust for what, and the guardrails you must not break.
>
> **Last reconciled with code:** 2026-07-16. If you find code that contradicts
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

**Known-flaky under parallel load:** `ElkRunnerCancellationTests.Restart_KillsInFlightProcess_...`
and `GateSchematicPerformanceTests.Rv32ClassGraph_...WithinBudget` are timing
assertions that occasionally trip during the full solution run under CPU
contention. Both pass in isolation. Confirm by re-running the single test before
treating either as a real regression.

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
| `docs/ARCHITECTURE.md` | Project layout, ELK pipeline (§5), threading model |
| `docs/SCHEMATIC_ROUTING_BACKENDS.md` | Which router backend is active and why |
| `docs/ELK_ROUTING_PERFORMANCE_ANALYSIS.md` | ELK routing presets + perf numbers (2026-06-12) |
| `docs/GATE_LEVEL_SCHEMATIC.md` | User-facing gate viewer behavior |
| `docs/PHASES/PHASE-6.5.md` + `docs/HANDOFFS/PHASE-6.5-GATE-PIN-LABELS-NEXT.md` | **Active work**: gate-level viewer status, open closure gates, next tasks |
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
| `docs/PHASES/PHASE-0..6.md` | Older phase history. Phase status is stated per file; PHASE-6.5 is the live one. |

## 6. Current status snapshot (2026-07-16)

- Branch `schematic/label-placement`, **19 commits ahead of `main`** (unmerged).
- Build clean; tests green except the two flaky timing tests above.
- **Active work:** Phase 6.5 gate-level viewer is feature-complete but has open
  **closure gates** in `docs/HANDOFFS/PHASE-6.5-GATE-PIN-LABELS-NEXT.md` §5:
  regenerate RV32 synthesis JSON through the GUI/Yosys flow, record labels
  hidden/grouped/detailed frame timings, manual RV32 acceptance, add Vivado-style
  `Expand Cone` / macro views, then close the phase.
- **Genuinely unstarted:** SVG export (rewrite plan Faz 7).

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
