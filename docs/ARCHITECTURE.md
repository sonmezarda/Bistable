# Architecture (current state — 2026-07-17)

This document is a layer map of the codebase as it stands today. Update it when layers move or new ones appear.

## 1. Solution layout

```
src/
├── Bistable.Core/          ── design model, project config (no UI, no I/O)
├── Bistable.Protocol/      ── worker IPC types (request/response shapes)
├── Bistable.Verilator/     ── Verilator XML parser + worker build/client
├── Bistable.Yosys/         ── Yosys synthesis + gate netlist reader
├── Bistable.Engine/        ── UI-independent application services + diagnostics
├── Bistable.EngineHost/    ── versioned JSON-line host for external frontends
├── Bistable.App/           ── retained Avalonia UI + view models
└── Bistable.Theia/         ── Phase 9.5 Theia workbench product-shell POC

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
Bistable.App ────────> Bistable.Engine ────────> Bistable.Verilator ──> Bistable.Core
                              │                          │
Bistable.EngineHost ──────────┘                          └─────────────> Bistable.Protocol

Bistable.Theia ──versioned JSON-line stdio──> Bistable.EngineHost
```

- `Bistable.Core` has zero external deps from this repo.
- `Bistable.Protocol` references nothing from this repo.
- `Bistable.Verilator` references `Bistable.Core` + `Bistable.Protocol`.
- `Bistable.Engine` owns reusable application services; it has no UI dependency.
- `Bistable.EngineHost` is the process/transport boundary for non-.NET shells.
- `Bistable.App` remains a compatibility frontend and delegates elaboration to
  `Bistable.Engine`.
- `Bistable.Theia` never references .NET assemblies directly; its backend owns
  one engine-host child process and verifies protocol v1 during handshake.

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
- `SimulationCommand` (request: `Type`, `Signal?`, `Value?`, `Cycles`, `Path?`, `Paths?`, `MemoryAddress?`, `MemoryCount?`).
- `SimulationCommandType` enum: 16 values across stepping, protocol handshake, and probe operations; v3 adds `Hello` and `ReadSignals`.
- `WorkerResponse` (abstract record, `[JsonPolymorphic("kind")]`) with eight subtypes:
  - `WorkerHelloResponse` (`"kind":"hello"`) — exact protocol version + capabilities.
  - `SimulationFrame` (`"kind":"frame"`) — v1 stepping response: `Time`, `Signals`, optional `Trace`.
  - `SignalReadResponse` (`"kind":"signalRead"`) — probe-table read result.
  - `SignalsReadResponse` (`"kind":"signalsRead"`) — per-path batch outcomes.
  - `MemoryReadResponse` (`"kind":"memoryRead"`) — reserved for P3-6 memory probes.
  - `ProbeListResponse` (`"kind":"probeList"`) — enumeration of the worker's probe table.
  - `AckResponse` (`"kind":"ack"`) — write/force/release success.
  - `ErrorResponse` (`"kind":"error"`) — structured failure message.
- DTOs: `SignalSample`, `SignalReadResult`, `SignalsReadResult`, `SignalReadOutcome`, `MemoryReadResult`, `ProbeDescriptor`.

Full wire-format spec in `docs/PROTOCOL.md`.

### `Bistable.Verilator`
- `VerilatorTool` — invokes `verilator` CLI.
- `VerilatorXmlParser` — XML → `ElaboratedDesign` (current).
- `SimulationWorkerBuilder` — generates C++ worker source, compiles via Verilator. Optionally takes a `DesignAst` to emit the Phase 3 probe table.
- `SimulationWorkerClient` — JSON IPC over stdin/stdout to the compiled worker. `StartAsync` verifies protocol v3; typed wrappers include chunked `ReadSignalsAsync` while preserving the atomic command/response drain discipline.
- `ProbeTableEnumerator` — walks a `DesignAst` and yields one `ProbeEntry` per hierarchical signal (filters `__V*` tmps, `Width > 64`, unpacked arrays). Path mangler turns `"top.acc.q"` into Verilator's `"top__DOT__acc__DOT__q"` field name.

**Phase 1 additions:**
  - `VerilatorXmlAstReader` — recursive-descent XML → `DesignAst`. Runs alongside the legacy parser.
  - `LegacyDesignFlattener` — `DesignAst` → `ElaboratedDesign`. Compatibility seam; `DesignLoadService` now calls reader + flattener instead of `VerilatorXmlParser` directly.

### `Bistable.Engine` and `Bistable.EngineHost`
- `DesignElaborationService` — validates and elaborates a project independently
  of either UI shell.
- `ElaborationDiagnosticsParser` — Verilator stderr to shared file/line/column
  diagnostics.
- `EngineSchematicProjectionService` — top-module decoder output to an exact
  signal-labelled, layout-agnostic node/edge transport graph.
- `EngineRpcServer` — JSON-line methods `hello`, `loadProject`, `shutdown`, and
  the protocol-v2 `simulation.*` family (`start`/`setInput`/`eval`/`tick`/
  `reset`/`readSignals`/`stop`); stdout is protocol-only and elaboration/
  validation failures carry structured diagnostics/`invalid_value` codes.
- `SimulationSessionService` (`Bistable.Engine`) — owns the native Verilator
  worker for one loaded project via `SimulationWorkerBuilder`. Drives the live
  loop (validate → SetInput → Eval/Tick/Reset → one batched `ReadSignals`), and
  keys each start with a **session generation** so a project reload swaps the
  worker atomically and drops late results from the superseded generation.
- `EngineSimulationWorker` (`Bistable.Engine`) — UI-independent worker transport
  mirroring `SimulationWorkerClient`'s atomic send/drain + `Hello`/`ReadSignals`
  discipline; no simulation math (the compiled worker owns all of it).
- `SimulationValueValidator` — parses bin/hex/dec and range-checks against a
  port width **before** any worker IPC; a bad value never reaches the worker.

### `Bistable.Theia` (Phase 9.5 POC)
- `browser-app` / `electron-app` — browser validation harness and branded
  desktop workbench, pinned to Theia 1.73.1.
- `extensions/bistable-workbench` — closeable/movable product widget plus the
  frontend/backend proxy to `Bistable.EngineHost`.
- The workbench auto-loads the root project and coalesces HDL saves through a
  400 ms latest-save-wins coordinator. RTL schematic is a separate main-area
  document widget; ELK layered/orthogonal layout executes in the Theia backend
  process and the frontend draws typed RTL SVG symbols and pins. The transport
  keeps exact net identity separate from semantic display-pin metadata; the
  shared `schematic-visual-contract.ts` computes bounded text-aware node sizes,
  protected input/output columns, middle elision and overview LOD before ELK
  runs. Instance headers separate instance name from module type. Hierarchical
  module documents and Vivado-style selective expand/collapse remain the next
  migration slice.
- `bistable-project-state.ts` is the single owner of the loaded-project and
  live-simulation state; the schematic widget observes it and refreshes values
  **without reopening** the document. Pin selection carries exact signal +
  hierarchical path (never the display label). Live values overlay the existing
  geometry as SVG text — value changes never re-run ELK — and the visible-probe
  set is computed once per layout. `simulation-state.ts` holds the DOM-free
  state helpers (snapshot/frame/read merge, `pinClasses`, `liveValue`).
- The backend `BistableEngineService` proxy owns the engine-host child process
  and forwards the `simulation.*` methods; it guards protocol v2 at handshake.
  No worker ownership lives in the renderer or the frontend.
- Explorer, Monaco, Problems, Terminal, Settings, document tabs, and dock
  lifecycle come from Theia packages instead of custom Bistable controls.

### `Bistable.App` (retained compatibility frontend)
- `ViewModels/MainWindowViewModel.cs` — the main VM (large; refactor candidate).
- `ViewModels/SignalViewModel.cs`, `HierarchyScopeInstanceViewModel.cs`, etc.
- `Services/DesignLoadService.cs` — compatibility adapter over
  `Bistable.Engine.DesignElaborationService`.
- `Services/ProjectFileWatcherService.cs` + `ProjectReloadCoordinator.cs` —
  debounced project/source/include watching and latest-save-wins reload queue.
- `Services/SimulationWorkerHotSwapService.cs` — prepares a replacement native
  worker while the current simulation remains owned and responsive.
- `Services/VcdTraceDocument.cs` — currently memory-loaded; Phase 6 will stream.
- `Services/Routing/Elk/ElkGraphBuilder.cs` — design → ELK graph.
- `Services/Routing/Elk/ElkSchematicEngine.cs` — LRU-cached layout pipeline.
- `Services/Routing/Elk/ElkRunner.cs` — Node subprocess bridge to elkjs.
- `Views/SchematicPreviewControl.*.cs` — multi-file partial class (rendering, hit-test, ELK draw, viewport).
- `Views/MainWindow.cs` — top-level UI composition.
- `Views/SourceWorkspaceView.cs` — Dock.Avalonia document containing the
  AvaloniaEdit HDL editor, source explorer, live-reload controls, and Problems
  panel. Broad XAML/platform migration remains Phase 14 work.

## 4. Threading model (current)

| Layer | Thread | Notes |
|-------|--------|-------|
| Worker C++ subprocess | own process | spawned by `SimulationWorkerClient` |
| `SimulationWorkerClient.SendAsync` | worker IPC gate | writes+response are one atomic transaction; cancellation drains the pending response (see PHASE-6.5 log, 2026-06-10) |
| `ElkRunner.Layout` | background | `ElkRunner.Layout` itself is still synchronous, but callers reach it through `SchematicLayoutService.LayoutAsync(CancellationToken)`, which serializes on `_layoutGate`, runs off the UI thread, is cancellable, and raises `LayoutStillRunning` on soft timeout |
| `ElkSchematicEngine.Compute` | caller | LRU-cached (8-entry, SHA-1 key); gate hierarchy path adds `GateLevelLayoutCache` fingerprinting |
| `DesignLoadService.LoadAsync` | background (Task) | Verilator XML generation off UI thread |
| `Bistable.EngineHost` | own process | Theia backend owns stdin/stdout RPC and terminates the host with the workbench backend |
| Theia frontend | browser/Electron renderer | Monaco and workbench widgets only; .NET elaboration stays out of the renderer thread |
| `ProjectFileWatcherService` | filesystem callback + async debounce | Coalesces project/source/include events; never mutates UI collections directly |
| `ProjectReloadCoordinator` | background runner | One reload at a time; a newer save cancels the active pass and queues the newest path set |
| `SimulationWorkerHotSwapService` | background subprocess build | Old worker remains live until replacement has started, restored inputs, and produced its first frame |
| `MainWindowViewModel.ApplyFrame` | UI thread | mutates ObservableCollections |

**Status (2026-07-16):** the "Phase 6" async/cancellable layout goal above is
largely met on the ELK path — layout runs off the UI thread, is cancellable, and
LRU-cached. Remaining UI-thread hot spots (batch probe protocol, incremental VCD)
are tracked in `docs/PHASES/PHASE-6.5.md` and `docs/HANDOFFS/PHASE-6.5-GATE-PIN-LABELS-NEXT.md`.

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
        ├──> SchematicDecoderCoverageEvent
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
- `Bistable.Core.Design.Schematic.SchematicPrimitiveList` carries `SchematicDecoderCoverageEvent` entries emitted by `SchematicDecoder`.
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
        │  v3: Hello + ReadSignals batch             │
        │      ReadSignal/WriteSignal/Force/         │
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

### Live development pipeline (Phase 9)

```
file/project save
      │
      ▼
ProjectFileWatcherService ──debounce/coalesce──> ProjectReloadCoordinator
      │                                              │ latest save cancels active
      ▼                                              ▼
DesignLoadService.LoadAsync ──> AstModuleDiff ──> updated schematic/catalog
      │                              │
      │ error                        └── unchanged scope keeps ELK cache key
      ▼
Problems + STALE last-good schematic

successful semantic change ──> SimulationWorkerHotSwapService
                                      │ old worker remains responsive
                                      ▼
                               restore inputs + first frame
                                      │
                                      ▼
                                  atomic swap
```

The project contract stores `liveReload.enabled` and `liveReload.debounceMs`;
global preferences can disable the feature or override debounce. Source edits
live in `SourceDocumentViewModel` until Ctrl+S/Save writes the file, after which
the same watcher path is used as for an external IDE.

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
