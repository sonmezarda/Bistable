# Architecture (current state — 2026-05-23)

This document is a layer map of the codebase as it stands today. Update it when layers move or new ones appear.

## 1. Solution layout

```
src/
├── Bistable.Core/          ── design model, project config (no UI, no I/O)
├── Bistable.Protocol/      ── worker IPC types (request/response shapes)
├── Bistable.Verilator/     ── Verilator XML parser + worker build/client
└── Bistable.App/           ── Avalonia UI + view models + services

tests/
├── Bistable.Tests/         ── unit + integration tests
├── Bistable.Regression/    ── bug-locking tests (Phase 0+)
├── Bistable.Snapshots/     ── golden ELK graph snapshots (Phase 0+)
└── Bistable.UiTests/       ── headless Avalonia tests (Phase 0+)

native/
└── worker-template/        ── C++ worker scaffold (currently a stub; real worker is code-generated)

tools/
└── elk-router/             ── Node.js subprocess running elkjs for layout (used by Bistable.App)

samples/                    ── SystemVerilog sample projects (alu, counter, hierarchy, tiny_cpu, bus_fabric, arnicomp)

docs/                       ── this file, TESTING.md, PHASES/PHASE-*.md
```

## 2. Dependency direction (strict; do not violate)

```
Bistable.App  ──depends on──>  Bistable.Verilator  ──depends on──>  Bistable.Core
       │                                                                 ▲
       └────────────────depends on (Bistable.Protocol)──────────────────┘
```

- `Bistable.Core` has zero external deps from this repo.
- `Bistable.Protocol` references nothing from this repo.
- `Bistable.Verilator` references `Bistable.Core` + `Bistable.Protocol`.
- `Bistable.App` references all the above.

**Why this matters:** when Phase 1 introduces a backend-agnostic AST, it lives in `Bistable.Core`. The Verilator-XML reader stays in `Bistable.Verilator`. A future Yosys reader would be a new project `Bistable.Yosys` referencing only `Bistable.Core` and `Bistable.Protocol`.

## 3. Key types per layer

### `Bistable.Core`
- `Bistable.Core.Projects.ProjectConfiguration` — the .bistable.json contract.
- `Bistable.Core.Design.ElaboratedDesign` — root of the parsed design (catalog + hierarchy).
- `Bistable.Core.Design.ModuleMetadata`, `SignalPort`, `DesignParameter`, `DesignHierarchyNode`.
- `Bistable.Core.Design.DesignModuleDefinition`, `DesignInstanceDefinition`, `DesignContAssign`, `DesignLocalSignal`, `DesignBitRange`.
- **`Bistable.Core.Design.Ast.*`** (Phase 1) — backend-agnostic Design IR. Key types:
  - `DesignAst` / `ModuleAst` — root and per-module containers.
  - `SignalDecl` — local signal with `IsRegistered` (derived post-parse).
  - `ContAssignAst` — continuous assignment with full expression tree.
  - `SequentialBlockAst` / `CombinationalBlockAst` — always-block representations.
  - `StatementAst` hierarchy: `BeginAst`, `IfAst`, `CaseAst`, `AssignAst`.
  - `ExpressionAst` hierarchy: `SignalRef`, `ConstExpr`, `BitSelectExpr`, `ConcatExpr`, `CondExpr`, `BinaryExpr`, `UnaryExpr`, and more.
  - Full spec: `docs/DESIGN_AST.md`.

### `Bistable.Protocol`
- `SimulationCommand` (request: `Type`, `Signal?`, `Value?`, `Cycles`).
- `SimulationCommandType` enum (SetInput, Eval, Tick, RunCycles, Reset, GetSnapshot, Pause).
- `SimulationSnapshot` (response: `Time`, `Signals`, optional `Trace`).
- `SignalSample`.

Phase 3 will extend this with `ReadSignal`, `WriteSignal`, `ForceSignal`, `ReleaseSignal`, `ReadMemory`.

### `Bistable.Verilator`
- `VerilatorTool` — invokes `verilator` CLI.
- `VerilatorXmlParser` — XML → `ElaboratedDesign` (current).
- `SimulationWorkerBuilder` — generates C++ worker source, compiles via Verilator.
- `SimulationWorkerClient` — JSON IPC over stdin/stdout to the compiled worker.

**Phase 1 additions:**
  - `VerilatorXmlAstReader` — recursive-descent XML → `DesignAst`. Runs alongside the legacy parser.
  - `LegacyDesignFlattener` — `DesignAst` → `ElaboratedDesign`. Compatibility seam; `DesignLoadService` now calls reader + flattener instead of `VerilatorXmlParser` directly.

### `Bistable.App`
- `ViewModels/MainWindowViewModel.cs` — the main VM (large; refactor candidate).
- `ViewModels/SignalViewModel.cs`, `HierarchyScopeInstanceViewModel.cs`, etc.
- `Services/DesignLoadService.cs` — loads + elaborates designs.
- `Services/VcdTraceDocument.cs` — currently memory-loaded; Phase 6 will stream.
- `Services/Routing/Elk/ElkGraphBuilder.cs` — design → ELK graph.
- `Services/Routing/Elk/ElkSchematicEngine.cs` — LRU-cached layout pipeline.
- `Services/Routing/Elk/ElkRunner.cs` — Node subprocess bridge to elkjs.
- `Views/SchematicPreviewControl.*.cs` — multi-file partial class (rendering, hit-test, ELK draw, viewport).
- `Views/MainWindow.cs` — top-level UI composition.

## 4. Threading model (current)

| Layer | Thread | Notes |
|-------|--------|-------|
| Worker C++ subprocess | own process | spawned by `SimulationWorkerClient` |
| `SimulationWorkerClient.SendAsync` | caller (usually UI thread) | **awaits stdout read on UI thread — Phase 6 fix** |
| `ElkRunner.Layout` | caller | **synchronous; Phase 6 will wrap in Task.Run** |
| `ElkSchematicEngine.Compute` | caller | synchronous; LRU-cached |
| `DesignLoadService.LoadAsync` | background (Task) | Verilator XML generation off UI thread |
| `MainWindowViewModel.ApplySnapshot` | UI thread | mutates ObservableCollections |

**Phase 6 goal:** zero UI thread blocks > 5 ms. Until then, the freeze on large schematics is expected.

## 5. The ELK pipeline (most-touched subsystem)

```
HierarchyScope*ViewModel
        │
        ▼
ElkScopeData  (DTO: ports, child instances, locals, contassigns, expandedPaths)
        │
        ▼
ElkGraphBuilder.Build  ──>  ElkBuildResult (graph + portRefs)
        │
        ▼
ElkRunner.Layout       ──>  laid-out ElkGraph (positions filled)
        │
        ▼
SchematicPreviewControl.Elk.DrawElkScopePanel  ──>  Avalonia drawing
```

Caching: `ElkSchematicEngine` keeps an 8-entry LRU keyed on a SHA-1 of the scope structure + expansion set. Cache misses trigger the full Compute. Cache hits return in microseconds.

## 6. The simulation pipeline

```
ProjectConfiguration (.bistable.json)
        │
        ▼
DesignLoadService.LoadAsync  ──>  ElaboratedDesign (XML-elaborated)
        │
        ▼
SimulationWorkerBuilder.BuildAsync  ──>  compiled native worker (per-project cache)
        │
        ▼
SimulationWorkerClient  ──[JSON over stdio]──>  worker subprocess
        │                                            │
        │                                            ▼
        │                                       VCD trace file
        ▼
SimulationSnapshot  ──>  ApplySnapshot  ──>  Output SignalViewModels + waveform append
```

Worker only exposes top-level ports today. Internal hierarchy signals are accessible only via the VCD file *after* the simulation pauses. Phase 3 fixes this with hot probes.

## 7. Sub-simulation

Currently a state-swap pattern in `MainWindowViewModel`:

1. Save: `_savedTopInputs`, `_savedTopOutputs`, `_savedTopAllSignals`, `_savedTopTraceSignals`, `_savedTopModule`, `_savedTopTraceFilePath`, `_savedTopDesign`, `_savedTopHierarchyRoot`, `_savedTopSelectedHierarchyNode`, `_savedTopExpandedPaths`.
2. Re-elaborate sub-module via `DesignLoadService.ElaborateAsync`.
3. Swap in sub-module state. Run sub-worker.
4. On exit, restore all saved fields.

Phase 5 will refactor this into a `SimulationContext` record so adding/removing state requires updating one place, not eleven.

## 8. Phase plan summary

See `/home/ardac/.claude/plans/fluffy-wishing-kettle.md` for the master plan. Per-phase status in `docs/PHASES/PHASE-<N>.md`.

| Phase | Focus |
|-------|-------|
| 0 | Test & CI infrastructure (current) |
| 1 | Design IR + AST parser |
| 2 | Schematic builder from AST (FF/mux/buffer/memory) |
| 3 | Worker Protocol v2 (live internal probe) |
| 4 | Live schematic values |
| 5 | Sub-sim maturation |
| 6 | Streaming VCD + async layout |
| 7 | Generate / FSM / force-release UI |
| 8 | Multi-clock / breakpoints / assertions |
