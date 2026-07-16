# Professional Tool Capability Analysis

**Date:** 2026-06-02  
**Scope:** Re-evaluate the roadmap against the real product target: load complex RTL such as RV32/out-of-order CPU cores, render complete schematics, simulate meaningful programs, inspect live state, and eventually synthesize to gate-level netlists.

> **HISTORICAL SNAPSHOT — partially superseded (reviewed 2026-07-16).**
> This is a point-in-time capability audit from 2026-06-02, before the
> synthesis/gate-level and ELK-async work landed. Several "missing" findings
> below have since been delivered; read it as roadmap rationale, not current
> state. Corrections:
> - "No synthesis backend exists" (§2.4) → **delivered.** Yosys integration
>   (`Bistable.Yosys`), netlist import, gate-level schematic, and RTL-vs-gate
>   comparison are in place (Phase 6 / Phase 6.5, `samples/riscv_single_cycle`).
> - "No RTL vs gate-level comparison" (§2.3) → **delivered** (Comparator + Synthesis Settings).
> - "ELK layout is cached but still synchronous" (§3) → **now async + cancellable**
>   via `SchematicLayoutService.LayoutAsync`.
> - RV32I execution target (item 3) → **delivered** (`samples/riscv_single_cycle`, Phase 5).
> For current architecture see `docs/ARCHITECTURE.md` §0/§5, and for phase status
> `docs/PHASES/PHASE-6.5.md`.

## 1. Executive conclusion

The current project is no longer blocked primarily by visual polish. Phase 2.7 items such as search, breadcrumbs, theme presets, mini-map, and export improve usability, but they do **not** move the product to the capability level of a professional HDL tool.

The current plan can become a good schematic/debugger UI, but it will not, by itself, reach the stated target:

- load a non-trivial RV32I/RV32IM core,
- explain every rendered and unrendered signal,
- run real instruction streams,
- inspect CPU architectural/microarchitectural state,
- synthesize RTL into gate-level netlists,
- compare RTL vs gate-level behavior.

The roadmap must pivot from UX polish to a capability track:

1. **RTL completeness and missing-signal audit**: no silent missing wires, no silent unsupported constructs.
2. **Construct completion for real RTL**: generate clusters, interfaces/modports, parameter arrays, memory patterns.
3. **RV32I execution target**: program loader, clock/reset presets, pass/fail probes, ISA smoke tests.
4. **Gate-level synthesis backend**: Yosys integration, netlist import, standard-cell schematic, gate-level simulation.
5. **Scale/performance**: only after real large-design samples expose actual bottlenecks.

Phase 2.7 should be paused except for support tasks that directly enable these capabilities.

## 2. Current capability inventory

### 2.1 RTL loading and elaboration

Current state:

- Project files (`ProjectConfiguration`) support:
  - top module,
  - source list,
  - include dirs,
  - defines,
  - parameters,
  - Verilator options,
  - clock/reset hints,
  - trace config,
  - internal probe opt-in.
- `DesignLoadService` runs Verilator XML generation, reads `DesignAst`, and flattens to `ElaboratedDesign`.
- `VerilatorXmlAstReader` is the main AST frontend.

Strength:

- Verilator gives real SystemVerilog elaboration rather than a handwritten parser pretending to support the language.
- The AST path is backend-agnostic enough that a Yosys frontend can be added later.

Main gaps:

- No first-class frontend diagnostics report: unsupported XML nodes, fallback parse paths, `?` signal names, skipped expressions, skipped modules.
- No design coverage summary: how many ports/signals/assignments/primitives were recognized vs dropped.
- No stress samples beyond arnicomp/tiny_cpu/bus_fabric scale.
- No explicit target for modern CPU constructs: interfaces, generated arrays of instances, register files, packed/unpacked arrays, parameter-heavy modules.

### 2.2 Static schematic generation

Current state:

- Phase 2, 2.5, 4.5 closed many schematic issues.
- Primitive support includes FF, latch, mux, buffer, inverter, gates, arith, splitter, joiner, memory tile, struct fan-out, tri-state, constant ties.
- Recursive compound expansion and concat-bound port joiner/fanout are implemented.

Strength:

- The schematic is now good for arnicomp-class modules and many hand-built samples.
- The architecture has a clear `DesignAst -> SchematicPrimitiveList -> ElkGraph` path.

Main gaps:

- The tool still lacks a formal **schematic completeness metric**.
- Missing wires are discovered visually by the user instead of being reported by the system.
- Unsupported or intentionally skipped signals are not surfaced in a professional diagnostics panel.
- Generate block visual collapsing is not complete.
- SystemVerilog interfaces/modports are still not supported.
- Wide buses and memory internals are partly out of scope in live probing.
- Some fallback paths still use legacy `DesignContAssign` simplifications.

Professional requirement:

The schematic engine must be able to say:

> This design has 12,480 ports, 91,204 local signals, 37,112 primitive endpoints, 36,998 routed endpoints, 114 unsupported endpoints, and here is the reason for each unsupported endpoint.

Without that, a large design can never be trusted even if it looks correct.

### 2.3 Live simulation and probing

Current state:

- Phase 3 protocol is implemented.
- Worker supports internal probes through Verilator `--public-flat-rw`.
- `ReadSignal`, `WriteSignal`, `ForceSignal`, `ReleaseSignal`, `ReadMemory`, `WriteMemory`, `ListProbes` are present.
- `LiveProbeService` and schematic value rendering exist in app code.

Strength:

- This is the strongest differentiator of the project: not just drawing RTL, but reading live internal state.

Main gaps:

- Probe width is limited by current worker assumptions for scalar values; wide values and structured values need a mature representation.
- Program/memory loading is not a product feature yet.
- There is no CPU run preset: reset, clock selection, run N cycles, stop on pass/fail.
- There is no architectural state model: PC, instruction, x registers, memory, trap/pass/fail are not first-class concepts.
- There is no ISA smoke/regression runner.
- No RTL vs gate-level comparison.

Professional requirement:

For an RV32I core, the user should be able to load a program and see:

- current PC,
- current instruction,
- decoded mnemonic if available,
- register file values,
- memory region,
- branch/jump target,
- active pipeline stage if the core exposes it,
- pass/fail status.

### 2.4 Gate-level synthesis

Current state:

- No synthesis backend exists.
- No Yosys integration exists.
- No Yosys JSON/netlist reader exists.
- No standard-cell/generic-cell schematic layer exists.

This is the largest capability gap relative to the user's target.

Professional requirement:

The tool needs a synthesis path:

```text
RTL sources
  -> Yosys synthesis
  -> generic netlist / cell-mapped netlist
  -> gate-level schematic
  -> gate-level Verilator simulation
  -> RTL vs gate-level comparison
```

Minimum first target:

- Yosys generic cells such as `$_AND_`, `$_OR_`, `$_XOR_`, `$_MUX_`, `$_DFF_*`, `$_NOT_`, `$_BUF_`.
- No foundry liberty file required initially.
- Later: optional Liberty cell libraries, timing metadata, and cell area summaries.

### 2.5 Performance and scale

Current state:

- ELK layout is cached but still synchronous.
- Phase 2.8 is deferred.
- There is no large real design benchmark.

This deferral is correct **only until** the first real CPU target lands. Performance work should not be guessed from arnicomp.

Professional requirement:

Performance work should be driven by:

- a real RV32I sample,
- a generated stress sample,
- one open-source complex core when feasible,
- measured load/build/layout/sim timings.

## 3. Why the current Phase 2.7 plan is insufficient

Phase 2.7 improves navigation and polish. It does not address:

- missing wires,
- unsupported constructs,
- program loading,
- CPU-level simulation workflows,
- synthesis,
- gate-level schematics,
- gate-level simulation,
- large design correctness metrics.

Therefore Phase 2.7 cannot be the main next phase if the product goal is a Vivado-class/RISC-V-capable tool. It can continue later as a UX layer on top of the capability engine.

Recommended status:

- Keep completed Phase 2.7 items: theme presets, breadcrumb/history, pinned signals.
- Pause remaining Phase 2.7 items unless they directly serve capability work.
- Start Phase 2.9: RTL Completeness and Coverage Audit.
- Then run Phase 5 and Phase 6 as capability phases:
  - Phase 5: RV32I Execution Target.
  - Phase 6: Gate-Level Synthesis Backend.

## 4. Proposed capability levels

### Level 0 — Existing small designs

Examples:

- `samples/counter`
- `samples/alu`
- `samples/tiny_cpu`
- `samples/arnicomp`

Required state:

- render static schematic,
- expand nested modules,
- run worker,
- inspect top/internal signals,
- no silent missing wires.

The project is close to this level, but not formally certified because no coverage audit exists.

### Level 1 — Real RV32I single-cycle or simple pipelined core

Required state:

- load a small RV32I core,
- load hex program into instruction/data memory,
- run reset/clock sequence,
- detect pass/fail,
- inspect PC/instruction/register file,
- render major datapath modules.

This is the next meaningful product target.

### Level 2 — Non-trivial pipelined RV32 core

Required state:

- pipeline stages,
- hazards/forwarding,
- multi-cycle memory interface,
- generate blocks,
- register-file arrays,
- branch/flush signals,
- waveform + schematic correlation.

### Level 3 — Out-of-order core exploration

Required state:

- large hierarchy,
- many generated structures,
- queues, rename tables, ROB, issue queues,
- very wide buses,
- memory arrays and packed structs,
- search/trace/debug navigation,
- scalable rendering.

Phase 2.7 UX and Phase 2.8 performance become essential here, but only after Levels 1-2 work.

### Level 4 — Gate-level backend

Required state:

- synthesize RTL,
- import netlist,
- render cells,
- simulate synthesized design,
- compare behavior with RTL.

This is separate from RTL schematic completeness and must be a first-class backend.

## 5. Critical blockers

### Blocker A — No missing-signal certification

The tool cannot be trusted on larger RTL until it can report completeness.

Needed:

- `SchematicCoverageAnalyzer`,
- unsupported AST/XML node collector,
- endpoint coverage report,
- unrouted endpoint report,
- per-module summaries,
- CI assertions on samples.

### Blocker B — No CPU execution workflow

Running `Eval`/`Tick` manually is not enough for CPU designs.

Needed:

- program image config,
- memory loader,
- reset/run presets,
- pass/fail probe config,
- CPU state panel,
- ISA smoke tests.

### Blocker C — No synthesis backend

Gate-level is impossible without a synthesis/import path.

Needed:

- Yosys runner,
- Yosys JSON reader,
- netlist IR,
- generic cell primitives,
- netlist schematic builder,
- gate-level worker build.

### Blocker D — Remaining construct gaps

Critical for real cores:

- generate block visual grouping,
- SystemVerilog interfaces/modports,
- packed/unpacked arrays,
- parameterized module arrays,
- wide values beyond 64-bit,
- memory initialization.

## 6. Recommended immediate roadmap

### Step 1 — Phase 2.9: RTL Completeness and Coverage Audit

Do this before adding another UI feature.

Outcome:

- The tool can tell us exactly what it cannot draw.
- arnicomp/tiny_cpu/bus_fabric become certified baselines.
- Every future large-design test has a measurable pass/fail gate.

Doc: `docs/PHASES/PHASE-2.9.md`.

### Step 2 — Phase 5: RV32I Execution Target

Outcome:

- Add a small RV32I core sample or import one.
- Load a program.
- Run it.
- Validate pass/fail.
- Inspect PC/register/memory live.

Doc: `docs/PHASES/PHASE-5.md`.

### Step 3 — Close remaining real-RTL construct gaps

At minimum:

- generate block visual grouping from Phase 2.6,
- interface/modport support from Phase 2.6,
- memory initialization/reporting.

These should be driven by the RV32I target, not implemented abstractly.

### Step 4 — Phase 6: Gate-Level Synthesis Backend

Outcome:

- Yosys synthesis from RTL to generic netlist.
- Import netlist.
- Render standard generic cells.
- Simulate netlist.
- Compare RTL vs gate-level smoke behavior.

Doc: `docs/PHASES/PHASE-6.md`.

### Step 5 — Resume Phase 2.7 / Phase 2.8 selectively

After the capability track has real designs:

- Search becomes important for RV32I and OoO.
- Mini-map matters after layout is large.
- Export matters after schematic is complete.
- Performance work uses measured bottlenecks.

## 7. Acceptance definition for "professional enough"

The project reaches the target when all of these are true:

1. A real RV32I sample can be loaded from source with no manual code changes.
2. The schematic coverage report has no silent drops.
3. A program image can be loaded and run.
4. The GUI can show PC, instruction, register file, and memory state live.
5. The same RTL can be synthesized with Yosys.
6. The synthesized gate-level netlist can be rendered.
7. The synthesized gate-level netlist can be simulated.
8. RTL and gate-level runs agree on a smoke program.
9. Unsupported constructs are reported explicitly with file/module/signal context.
10. Large-design performance bottlenecks are measured and tracked.

## 8. Testing strategy shift

Current tests are mostly unit/regression/snapshot oriented. Keep them, but add capability tests:

- **Coverage tests**: assert every sample has zero silent unresolved endpoints.
- **Program tests**: run a tiny instruction stream and assert final state.
- **Probe tests**: read PC/regfile/memory after cycles.
- **Synthesis tests**: run Yosys on a small module and import generic cells.
- **Equivalence smoke tests**: RTL vs gate-level final state on the same small program.
- **Stress tests**: generated hierarchy/large netlist, marked slow.

## 9. Documentation changes made from this analysis

New/updated docs:

- `docs/PHASES/PHASE-2.9.md` — RTL completeness and coverage audit.
- `docs/PHASES/PHASE-5.md` — RV32I execution target.
- `docs/PHASES/PHASE-6.md` — gate-level synthesis backend.
- `docs/PHASES/PHASE-2.7.md` — marked as paused/deprioritized for capability pivot.

