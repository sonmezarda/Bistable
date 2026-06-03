# Architecture (current state — 2026-05-29)

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
- `SimulationCommand` (request: `Type`, `Signal?`, `Value?`, `Cycles`, `Path?`, `MemoryAddress?`, `MemoryCount?`).
- `SimulationCommandType` enum: 14 values across v1 stepping (SetInput, Eval, Tick, RunCycles, Reset, GetSnapshot, Pause) and v2 probes (ReadSignal, WriteSignal, ForceSignal, ReleaseSignal, ReadMemory, WriteMemory, ListProbes).
- `WorkerResponse` (abstract record, `[JsonPolymorphic("kind")]`) with six subtypes:
  - `SimulationFrame` (`"kind":"frame"`) — v1 stepping response: `Time`, `Signals`, optional `Trace`.
  - `SignalReadResponse` (`"kind":"signalRead"`) — probe-table read result.
  - `MemoryReadResponse` (`"kind":"memoryRead"`) — reserved for P3-6 memory probes.
  - `ProbeListResponse` (`"kind":"probeList"`) — enumeration of the worker's probe table.
  - `AckResponse` (`"kind":"ack"`) — write/force/release success.
  - `ErrorResponse` (`"kind":"error"`) — structured failure message.
- DTOs: `SignalSample`, `SignalReadResult`, `MemoryReadResult`, `ProbeDescriptor`.

Full wire-format spec in `docs/PROTOCOL.md`.

### `Bistable.Verilator`
- `VerilatorTool` — invokes `verilator` CLI.
- `VerilatorXmlParser` — XML → `ElaboratedDesign` (current).
- `SimulationWorkerBuilder` — generates C++ worker source, compiles via Verilator. Optionally takes a `DesignAst` to emit the Phase 3 probe table.
- `SimulationWorkerClient` — JSON IPC over stdin/stdout to the compiled worker. Includes typed wrappers (`ReadSignalAsync`, `ForceSignalAsync`, `ListProbesAsync`, …) over the raw command/response channel.
- `ProbeTableEnumerator` — walks a `DesignAst` and yields one `ProbeEntry` per hierarchical signal (filters `__V*` tmps, `Width > 64`, unpacked arrays). Path mangler turns `"top.acc.q"` into Verilator's `"top__DOT__acc__DOT__q"` field name.

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
| `MainWindowViewModel.ApplyFrame` | UI thread | mutates ObservableCollections |

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

## 6. Schematic coverage diagnostics

Phase 2.9 adds a machine-readable completeness layer around the schematic path. The goal is to make missing wires auditable before large RV32I or out-of-order designs are debugged visually.

```
DesignAst / ModuleAst
        │
        ▼
SchematicDecoder.Decode
        │
        ▼
SchematicCoverageAnalyzer.Analyze  ──>  SchematicCoverageReport
        │                                      │
        │                                      └── UI: View > Schematic Coverage...
        ▼
ElkGraphBuilder.Build
        │
        ▼
ElkGraphCoverageAnalyzer  ──>  graph/port/edge routing diagnostics
```

Core ownership:

- `Bistable.Core.Design.Schematic.SchematicCoverageReport` owns the report records and `SchematicCoverageAnalyzer`.
- `Bistable.Core.Design.Schematic.SchematicCoverageReportJson` owns stable JSON artifact serialization.
- `Bistable.App.Services.Routing.Elk.ElkGraphCoverageAnalyzer` owns graph-level port/edge validation.
- `Bistable.App.Views.DiagnosticsWindow` is a thin viewer over the current in-memory `SchematicCoverageReport`.

Status semantics:

- `Routed`: decoded/rendered endpoint exists.
- `IntentionalOmission`: endpoint was deliberately hidden with a reason.
- `Unsupported`: endpoint is known but not renderable yet, with a diagnostic reason.
- `SilentMiss`: endpoint should have appeared but did not. This is a bug, not an acceptable limitation.

The diagnostics window rebuilds the report on demand from the loaded AST. JSON artifacts are produced through `SchematicCoverageReportJson`; automatic UI export remains a separate workflow decision.

## 7. The simulation pipeline

```
ProjectConfiguration (.bistable.json)
        │
        ▼
DesignLoadService.LoadAsync  ──>  ElaboratedDesign (XML-elaborated) + DesignAst
        │                                                 │
        ▼                                                 │
SimulationWorkerBuilder.BuildAsync(designAst:)            │
        │  + ProbeTableEnumerator.Enumerate(ast, top)     │
        │       └──> IReadOnlyList<ProbeEntry>            │
        │                                                 │
        ▼                                                 │
generated C++ source (probe_table populated via init_probe_table)
        │  ──[--public-flat-rw]──>  every hier signal is a public field
        ▼
verilator --cc --exe --build  ──>  compiled native worker (per-project cache)
        │
        ▼
SimulationWorkerClient  ──[JSON over stdio]──>  worker subprocess
        │                                            │
        │  v1: SetInput/Eval/Tick/RunCycles          ▼
        │      → SimulationFrame              VCD trace file
        │                                            │
        │  v2: ReadSignal/WriteSignal/Force/         │
        │      Release/ListProbes                    │
        │      → typed WorkerResponse                │
        ▼                                            │
ApplyFrame  ──>  Output SignalViewModels + waveform append
```

The worker holds a `std::unordered_map<std::string, ProbeEntry> probe_table`
built at startup from the AST signal list. Each entry binds a read/write
lambda over `model->rootp->{mangled_field}`. A small `std::map<std::string,
uint64_t> forced_signals` is re-applied at the top of every eval and after
every eval inside `drive_clock` — the latter is what makes `forceSignal`
survive the FF latch on the rising clock edge.

Full wire-format spec: `docs/PROTOCOL.md`. Phase 3 task board:
`docs/PHASES/PHASE-3.md`. Phase 4 (live values on schematic) consumes this
API; Phase 5 (sub-sim maturation) reuses the probe table to enumerate a
sub-module's internal signals.

## 8. Sub-simulation

Currently a state-swap pattern in `MainWindowViewModel`:

1. Save: `_savedTopInputs`, `_savedTopOutputs`, `_savedTopAllSignals`, `_savedTopTraceSignals`, `_savedTopModule`, `_savedTopTraceFilePath`, `_savedTopDesign`, `_savedTopHierarchyRoot`, `_savedTopSelectedHierarchyNode`, `_savedTopExpandedPaths`.
2. Re-elaborate sub-module via `DesignLoadService.ElaborateAsync`.
3. Swap in sub-module state. Run sub-worker.
4. On exit, restore all saved fields.

Phase 5 will refactor this into a `SimulationContext` record so adding/removing state requires updating one place, not eleven.

## 9. Phase plan summary

See `/home/ardac/.claude/plans/fluffy-wishing-kettle.md` for the master plan. Per-phase status in `docs/PHASES/PHASE-<N>.md`.

**2026-06-02 capability pivot:** Phase 2.7 UX/persistence work is paused as the main next step. The product target is now explicitly defined as professional RTL/simulation/synthesis capability: no silent missing wires, RV32I execution, and gate-level synthesis. See `docs/PROFESSIONAL_TOOL_CAPABILITY_ANALYSIS.md`.

| Phase | Focus |
|-------|-------|
| 0 | Test & CI infrastructure (current) |
| 1 | Design IR + AST parser |
| 2 | Schematic builder from AST (FF/mux/buffer/memory) |
| 2.9 | RTL completeness and coverage audit — no silent missing wires |
| 3 | Worker Protocol v2 (live internal probe) |
| 4 | Live schematic values |
| 5 | RV32I execution target — program load, reset/run, pass/fail, CPU state probes |
| 6 | Gate-level synthesis backend — Yosys, netlist import, gate schematic, RTL vs gate compare |
| 7 | Generate / FSM / force-release UI |
| 8 | Multi-clock / breakpoints / assertions |
