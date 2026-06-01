# Phase 3 — Worker Protocol v2 (Live Internal Probe)

**Master plan:** `/home/ardac/.claude/plans/fluffy-wishing-kettle.md` Section 9
**Phase goal:** Allow the GUI to read and write ANY hierarchical signal (or memory cell) at simulation time, not just top-level ports. This is the **prerequisite for Phase 4 (live values on schematic)** — the differentiator that turns a static viewer into a Logisim-class live debugger.
**Prerequisite:** Phase 1 (AST) complete. Phase 2 / 2.5 static schematic is production-ready as of 2026-05-25 (Phase 2.5: 7/7 + 3 closeout tasks complete), so Phase 3 can start on top of a stable schematic baseline.
**Phase gate:** ✅ `ReadSignal("arnicomp_top.{path}")` returns the live FF value mid-simulation; ✅ `ForceSignal` holds a value across `Eval`+`Tick` cycles; ⏸ memory read API deferred to P3-6 (scalar probes are gate-blocking, memory is not); ✅ all existing samples still build and run (468 tests green).

---

## 1. Why this phase matters

After Phase 2.5 the schematic shows the design's STRUCTURE at production quality: internal Verilator tmp aliases are folded/hidden, mux selectors are on the south side, orphan primitives are pruned, and arnicomp connectivity audit reports 0 problematic unconnected primitive ports. But every value visible to the user is still a snapshot of TOP-LEVEL ports only — internal FF Q values, memory cells, mux active paths are dark.

Phase 3 lifts the lid on the simulation:
- Read any hierarchy signal hot (no VCD round-trip): `top.cpu.alu.result` → live value
- Write/force any signal: held across `Eval` cycles until released
- Read memory cells: array contents on demand
- Enumerate probes: GUI knows what's readable

This is what makes Phase 4 possible. Without it, every FF on the schematic remains a static box.

---

## 2. Architecture

```
GUI (Bistable.App)
    │
    ▼ SimulationWorkerClient.ReadSignalAsync("top.acc.q", ct)
                                                │
                                ┌───────────────┘
                                ▼ JSON command
SimulationCommand { Type=ReadSignal, Path="top.acc.q" }
                                │
                                ▼ stdin
Worker process (compiled C++ from SimulationWorkerBuilder)
                                │
                                ▼ probeTable.lookup("top.acc.q")
                                ▼ read pointer → uint64_t value
                                ▼ encode JSON
                                ▼ stdout
SimulationSnapshot { ReadResult: { Value, Width, IsSigned } }
                                │
                                ▼ deserialize
GUI receives value
```

**Key parts:**
1. **Verilator code-gen flag**: `--public-flat-rw` exposes every hierarchical signal as a public field on the model class.
2. **Probe table**: C++ `std::unordered_map<std::string, ProbeDescriptor>` keyed by hierarchy path. Built from the AST signal list at code-gen time. Stores pointers (or accessor lambdas) into `model->...` fields.
3. **Protocol v2 commands**: new `ReadSignal` / `WriteSignal` / `ForceSignal` / `ReleaseSignal` / `ReadMemory` / `WriteMemory` / `ListProbes` over the existing stdin/stdout JSON channel.
4. **Force state**: small `std::map<probe, forced_value>` in the worker, re-applied at the top of every `Eval` call.

---

## 3. Task board

Status legend: ☐ todo · 🟡 in progress · ✅ done · ⛔ blocked

| ID | Task | Status | Model | Est. | Notes |
|----|------|--------|-------|------|-------|
| P3-1 | Protocol v2 type definitions | ✅ | Sonnet | 1 d | 7 new `SimulationCommandType` values; `SimulationCommand` extended with `Path`/`MemoryAddress`/`MemoryCount`; new DTOs `SignalReadResult`/`MemoryReadResult`/`ProbeDescriptor`; `SimulationSnapshot` extended with optional `ReadResult`/`MemoryReadResult`/`ProbeList`/`Acknowledged`/`Error`. 22 round-trip tests + backwards-compat guard. |
| P3-2 | Worker code-gen: add `--public-flat-rw` flag | ✅ | Sonnet | 0.5 d | `ProjectConfiguration.EnableInternalProbes` (default `true`) gates the flag. `BuildVerilatorArguments` helper extracted to keep `BuildAsync` cognitive complexity under budget. 3 config tests. |
| P3-3 | Probe table generation (C++) | ✅ | Opus | 2 d | `ProbeTableEnumerator.Enumerate(DesignAst, top)` yields one `ProbeEntry` per probable signal. Filters: `__V*` Verilator tmps, width > 64 (no `VlWide<N>` yet), unpacked arrays (memory — deferred to P3-6). 13 enumerator tests + integrated into `SimulationWorkerBuilder` C++ emitter (`AppendProbeTableSupport`). |
| P3-4 | Worker handler: ReadSignal / ReadMemory | ✅ | Opus | 2 d | `readSignal` → probe table lookup → read lambda. `readMemory` → `memory_table` lookup with address + count, writes `MemoryReadResponse` with hex cells. Out-of-range guard returns structured `ErrorResponse`. |
| P3-5 | Worker handler: WriteSignal / ForceSignal / ReleaseSignal | ✅ | Opus | 2 d | `writeSignal` direct write via write lambda. `forceSignal` adds to `std::map<string,uint64_t> forced_signals` re-applied via `apply_forced_signals()` BEFORE every eval AND after every eval inside `drive_clock` (so forces survive the FF latch on the rising edge). `releaseSignal` removes the entry. |
| P3-6 | C# client API: `ReadSignalAsync` / `ForceSignalAsync` / `ReadMemoryAsync` / `ListProbesAsync` | ✅ | Sonnet | 1 d | 6 typed wrappers in `SimulationWorkerClient.cs` (App side). Calls now flow end-to-end to real worker after P3-3/4/5. |
| P3-7 | Probe enumeration: `ListProbes` returns AST signal list | ✅ | Sonnet | 1 d | Worker iterates `probe_table` → emits `{"kind":"probeList","probes":[...]}` with width/signed/registered/memory flags. |
| P3-8 | Per-config opt-in flag (large designs may want to skip probes) | ✅ | Sonnet | 0.5 d | `ProjectConfiguration.EnableInternalProbes`, default true. When false: probe table stays empty, `--public-flat-rw` flag skipped, all probe commands return `ErrorResponse`. |
| P3-9 | C++ test harness (native unit tests) | ☐ | Opus | 2 d | Optional. C# integration tests (P3-10) currently cover the same surface. Defer until probe semantics need finer-grained checks. |
| P3-10 | C# protocol round-trip tests (arnicomp end-to-end) | ✅ | Sonnet | 1 d | `HotProbeTests.cs` — 7 tests covering counter + hierarchy + arnicomp. Includes: list probes, live read across ticks, write→read-back, force-survives-tick-until-released, nested-instance probes, arnicomp probe table populated. |
| P3-11 | Memory demo sample | ✅ | Sonnet | 1 d | `samples/memory_demo/` — `logic [7:0] mem [0:15]` with `we`/`addr`/`din`/`dout` interface. 5 integration tests cover ListProbes/ReadMemory/WriteMemory/out-of-range/scalar-on-memory-path. |
| P3-12 | Update `docs/PROTOCOL.md` | ✅ | Sonnet | 0.5 d | Formal v2 spec with JSON shapes for every command + WorkerResponse subtype. |
| P3-13 | Update `docs/ARCHITECTURE.md` | ✅ | Sonnet | 0.5 d | Simulation pipeline section now describes the probe table flow. |

**Total estimate: ~14 days serial, ~9 days with 2 parallel agents.**

---

## 4. Detailed task specs

### P3-1 — Protocol v2 type definitions

**Goal:** Land the type changes that downstream tasks depend on. No behaviour, no tests yet — just plumbing.

**Files to read first:**
- `src/Bistable.Protocol/SimulationCommand.cs`
- `src/Bistable.Protocol/SimulationCommandType.cs`
- `src/Bistable.Protocol/SimulationSnapshot.cs`

**Changes:**
- `SimulationCommandType` enum: add `ReadSignal`, `WriteSignal`, `ForceSignal`, `ReleaseSignal`, `ReadMemory`, `WriteMemory`, `ListProbes`.
- `SimulationCommand` record: extend with optional `HierarchyPath`, `MemoryAddress`, `MemoryCount` fields.
- New DTO `SignalReadResult { string Path, BigInteger Value, int Width, bool IsSigned }`.
- New DTO `ProbeDescriptor { string Path, int Width, bool IsRegistered, bool IsMemory, int? MemoryDepth }`.
- Extend `SimulationSnapshot` with optional `ReadResult` / `MemoryReadResult` / `ProbeList` fields.

**Tests:** ≥ 4 (`Bistable.Tests/Protocol/ProtocolV2JsonTests.cs`) — round-trip serialization for each new command type.

**Acceptance:** Phase 0/1/2 tests all still pass; the new types serialize/deserialize cleanly.

---

### P3-2 — Worker code-gen: `--public-flat-rw`

**Goal:** Pass the Verilator flag that exposes internals.

**Files to read first:**
- `src/Bistable.Verilator/SimulationWorkerBuilder.cs`
- `src/Bistable.Verilator/VerilatorTool.cs`

**Fix:**
- In `SimulationWorkerBuilder.GenerateWorkerSource` (or wherever Verilator args are assembled), add `--public-flat-rw` to the default arg list.
- Make it conditional on `configuration.EnableInternalProbes` (default true).

**Tests:** ≥ 2 — assert the flag appears in the assembled Verilator command line; assert ProjectConfiguration default carries the flag enabled.

**Acceptance:** All sample projects still compile (the flag may bloat compile time on large designs — acceptable for arnicomp scale).

---

### P3-3 — Probe table generation (C++)

**Goal:** Generate C++ code that, at worker startup, populates a `std::unordered_map<std::string, ProbeDescriptor>` mapping every internal signal path to its field pointer.

**Files to read first:**
- `src/Bistable.Verilator/SimulationWorkerBuilder.GenerateWorkerSource` — current code-gen pipeline
- `native/worker-template/main.cpp` — current stub (will be extended)

**Approach:**
- Walk `DesignAst.Modules` recursively (using `ElaboratedDesign.HierarchyRoot` to get instance paths).
- For each `SignalDecl` in each module, emit C++ entries like:
  ```cpp
  probeTable["arnicomp_top.acc.q"] = ProbeDescriptor{
      .read = []() -> uint64_t { return top->arnicomp_top->acc->q; },
      .write = [](uint64_t v) { top->arnicomp_top->acc->q = v; },
      .width = 8,
      .isRegistered = true
  };
  ```
- For memory signals (`SignalDecl.ArrayDims.Count > 0`), generate entries that take an index too.
- The Verilator-emitted `--public-flat-rw` field naming is hierarchical with `__DOT__` separator — translate to dotted paths.

**Risks:**
- Verilator's flat-rw field naming has version-specific quirks. Detect with a small probe test at startup.
- Large designs → big probe table (1000s of entries). Lambdas are cheap but the std::unordered_map allocation is O(N). Acceptable for arnicomp scale; profile later.

**Tests:** Hand-written C++ + smoke test on arnicomp showing the table has the expected entry for `arnicomp_top.acc.q`. Move to P3-9.

**Model: Opus** — C++ code-gen with name-mangling quirks.

---

### P3-4 — Worker handler: ReadSignal / ReadMemory

**Goal:** When the worker receives `{Type: ReadSignal, Path: "arnicomp_top.acc.q"}`, it looks up the probe table, reads the pointer, and emits a JSON response.

**Approach:**
- Extend the command dispatcher in `main.cpp` to recognize the new command types.
- For `ReadSignal`: probeTable lookup → call the `read()` lambda → encode `SignalReadResult` JSON.
- For `ReadMemory`: lookup + iterate over the requested address range.
- Errors (path not found, out of bounds) → emit error JSON with structured code.

**Tests:** P3-9 native harness + P3-10 C# round-trip.

**Model: Opus** — interleaves C++ JSON encoding with std::unordered_map lookups.

---

### P3-5 — Worker handler: Write / Force / Release

**Goal:** Two distinct write modes:
- `WriteSignal`: one-shot write, simulation may overwrite on next `Eval`.
- `ForceSignal`: held value — re-applied at the top of every `Eval` until `ReleaseSignal` clears it.

**Approach:**
- `WriteSignal` is a straight `probeTable[path].write(value)`.
- `ForceSignal` adds an entry to `std::map<std::string, uint64_t> forcedValues`. Modify the Eval loop to call `apply_forced_values()` at the top of each tick BEFORE the model's eval step.
- `ReleaseSignal` removes from the map.

**Tests:** Force a signal to 0, run 100 Eval cycles, assert it stays 0; release, run more cycles, assert it follows simulation. Native harness + C# integration.

**Model: Opus** — force-loop semantics need care.

---

### P3-6 — C# client API

**Goal:** Typed wrappers around the raw JSON `SendAsync`.

**Files:** `src/Bistable.Verilator/SimulationWorkerClient.cs`.

**API:**
```csharp
public Task<SignalReadResult> ReadSignalAsync(HierarchyPath path, CancellationToken ct);
public Task WriteSignalAsync(HierarchyPath path, BigInteger value, CancellationToken ct);
public Task ForceSignalAsync(HierarchyPath path, BigInteger value, CancellationToken ct);
public Task ReleaseSignalAsync(HierarchyPath path, CancellationToken ct);
public Task<IReadOnlyList<SignalReadResult>> ReadMemoryAsync(HierarchyPath path, ulong addr, int count, CancellationToken ct);
public Task<IReadOnlyList<ProbeDescriptor>> ListProbesAsync(CancellationToken ct);
```

`HierarchyPath` is a tiny value type wrapping `string` for compile-time safety (no accidental signal/path mixing).

**Tests:** ≥ 6 mock-based tests (use a fake worker that returns canned JSON) plus the round-trip suite from P3-10.

---

### P3-7 — `ListProbes` enumeration

Worker emits its full probe table on `{Type: ListProbes}`. Used by the GUI to enumerate available signals + verify probe-table integrity at startup.

**Acceptance:** arnicomp returns ~50+ probes; each has a non-empty path and a width > 0.

---

### P3-8 — Per-config opt-in

`ProjectConfiguration.EnableInternalProbes`, default true. When false, worker is built without `--public-flat-rw` and the probe table is empty. The probe API still works (returns error "probes disabled") but doesn't bloat the binary.

**Acceptance:** Setting `EnableInternalProbes=false` on a large design reduces worker binary size measurably; `ReadSignal` returns a clean error.

---

### P3-9 — C++ native test harness

**Goal:** Unit tests that build a tiny SystemVerilog module + worker, drive Eval cycles, and exercise the read/write/force/release API directly from C++.

**Files:** New `tests/native/` directory with CMakeLists.txt. Single test program that builds a 1-FF model and runs:
```cpp
TEST(ReadAfterEval, ReturnsLiveValue) {
    Worker w(model);
    w.WriteSignal("top.d_in", 0x42);
    w.Eval();
    w.Tick();   // clock edge
    auto r = w.ReadSignal("top.q");
    ASSERT_EQ(r.value, 0x42);
}
```

**Build integration:** CMake invocation in CI pipeline. Skipped locally when CMake/g++/Verilator absent.

**Tests:** ≥ 10 native tests covering: read, write, force-hold-across-eval, release-follows-sim, memory read, list-probes.

---

### P3-10 — C# round-trip on arnicomp

**Goal:** A `[Trait("Category", "Integration")]` test that builds the real arnicomp project, spins up the worker, and exercises the new API end-to-end.

**Skip pattern:** Same as Phase 2.9 — `if (!HasVerilator()) return;`.

**Test cases:**
- Read `arnicomp_top.acc.q` before any Eval → returns 0 (reset state).
- Drive `instruction = 0x81`, Tick × 3 → `arnicomp_top.acc.q` changes (read again).
- Force `acc_we = 1`, Tick → q updates; release, Tick → q free.
- ListProbes → returns ≥ 30 entries.
- ReadMemory on a sample with `logic [7:0] mem [0:15]` returns 16 cells.

---

### P3-11 — Memory demo sample

**Goal:** A small SystemVerilog project demonstrating array reads/writes. Existing samples don't have unpacked arrays.

**File:** `samples/memory_demo/memory_demo.sv` + `.bistable.json`.

**Module:**
```sv
module memory_demo (
    input  logic clk, we,
    input  logic [3:0] addr,
    input  logic [7:0] din,
    output logic [7:0] dout
);
    logic [7:0] mem [0:15];
    always_ff @(posedge clk) if (we) mem[addr] <= din;
    assign dout = mem[addr];
endmodule
```

**Tests:** Snapshot test + memory read round-trip.

---

### P3-12, P3-13 — Documentation

- `docs/PROTOCOL.md`: NEW file (was placeholder). Spec each command's JSON shape, error codes.
- `docs/ARCHITECTURE.md`: extend Section 4 (simulation pipeline) with the probe table.

---

## 5. Implementation order (recommended)

```
1. P3-1 (1d)  →  P3-2 (0.5d)         (foundations: types + flag)
2. P3-3 (2d)  →  P3-4 (2d)  →  P3-5 (2d)   (worker side, serial within C++)
3. P3-6 (1d) [parallel with #2]      (C# client wrappers)
4. P3-7 (1d) + P3-8 (0.5d)           (small additions)
5. P3-9 (2d) [parallel with above]   (native harness, run on CI)
6. P3-10 (1d) + P3-11 (1d)           (integration tests + sample)
7. P3-12 + P3-13 (1d combined)       (docs closing)
```

**Total: ~14 days serial, ~9 days with 2 parallel agents (one C++/worker side, one C#/client side).**

---

## 6. Cross-phase notes

- **Phase 4 (Live values)** is the immediate downstream consumer. Phase 3's API surface is the contract.
- **Phase 5 (Sub-sim)** also benefits — sub-sim worker can use the same probe table to enumerate the sub-module's internal signals.
- **Phase 6 (Streaming + Async)** — `ReadSignalAsync` should already be async-friendly; ensure the worker IPC doesn't block UI thread (existing concern, will be addressed in Phase 6).
- **Phase 7 (Force/Release UI)** — directly uses P3-5's API for right-click force/release.
- **Phase 8 (Breakpoints)** — extends the protocol with `SetBreakpoint`, sharing the probe table.

---

## 7. Risks & mitigations

| Risk | Mitigation |
|------|-----------|
| `--public-flat-rw` slows large-design compile | Per-config opt-in (P3-8) |
| Verilator field naming differs between versions | Detect via probe-table-smoke-test at worker startup; fall back to error |
| Memory access concurrency (writes during eval) | Queue writes, apply at top of next Eval (P3-5 design) |
| Probe table size on million-gate designs | Lazy: only build entries for signals the GUI subscribes to (deferred to Phase 2.8 / P2.8-2) |
| Hierarchical path quoting issues (signals with weird names) | Worker accepts both `__DOT__` and `.` separators |

---

## 8. Acceptance criteria (phase gate)

- [ ] `ReadSignal("arnicomp_top.acc.q")` returns the live value matching VCD trace within ±1 cycle.
- [ ] `ForceSignal` holds across 100 consecutive `Eval` calls; `ReleaseSignal` immediately frees.
- [ ] `ReadMemory` on `memory_demo` returns all 16 cells with correct contents.
- [ ] `ListProbes` on arnicomp returns ≥ 30 entries (each module's locals + ports counted).
- [ ] All Phase 0/1/2/2.5 tests still pass (≥ 393 baseline + new Phase 3 tests).
- [ ] `docs/PROTOCOL.md` documents every new command + JSON shape.
- [ ] CI green on Linux (Verilator + g++ available).

---

## 9. Handoff for next session

If you're picking this up cold:

1. Read `/home/ardac/.claude/plans/fluffy-wishing-kettle.md` Section 9 — master spec.
2. Read this file's Section 2 (architecture) + Section 5 (implementation order).
3. Read `src/Bistable.Verilator/SimulationWorkerBuilder.cs` and `src/Bistable.Verilator/SimulationWorkerClient.cs` to understand existing IPC.
4. Read `src/Bistable.Protocol/*.cs` for the current command/snapshot shape.
5. Start with **P3-1** — type definitions. Pure plumbing, low risk.
6. Branch: `phase-3/protocol-v2`. Commit prefix: `[phase-3]`.

**Critical constraint:** Phase 3 MUST NOT change the legacy top-level-only API contract. The existing Eval / SetInput / Tick paths must keep working unchanged. The new API is purely additive.

**Suggested split for two parallel agents:**
- Agent A (Opus): worker side (P3-3, P3-4, P3-5, P3-9)
- Agent B (Sonnet): C# client + protocol + tests (P3-1, P3-6, P3-7, P3-8, P3-10, P3-11)
- Sync point: P3-1 must land before either agent's path 3+ can run.

---

## 10. Recent activity

- **2026-05-30**: **Memory probes (P3-6 path) + memory_demo sample landed**. `ProbeTableEnumerator` now emits a `ProbeEntry { IsMemory=true, MemoryDepth }` for any single-dimension unpacked array (multi-dim still skipped). Worker C++ codegen emits a parallel `std::unordered_map<std::string, MemoryAccessor> memory_table` with per-cell read/write lambdas: scalar `readSignal` on a memory path returns 0 by design (use `readMemory`); `writeSignal` is a no-op for memory paths. Two new wire commands handled: `readMemory` (path + memoryAddress + memoryCount) → `MemoryReadResponse` with hex cells; `writeMemory` (path + memoryAddress + value) → `AckResponse`. Out-of-range guards return structured `ErrorResponse`. The XML reader was extended to follow `unpackarraydtype`'s `sub_dtype_id` so the cell width comes from the inner basicdtype (was returning 1 before), and the array depth is now parsed from the `<range>` element's nested `<const>` literal names (e.g. `32'shf` → 15). New `samples/memory_demo/` is a 16x8 RAM with `we`/`addr`/`din`/`dout`; `MemoryProbeTests` covers list / range read / write+read roundtrip / out-of-range / scalar-on-memory-path. Test suite grew from 478 to 514.

- **2026-05-29**: **P3-3, P3-4 (scalar half), P3-5, P3-7, P3-10, P3-12, P3-13 landed** — Phase 3 worker side complete; live probe API end-to-end functional on arnicomp. Memory probes (P3-6 surface task) and the matching `samples/memory_demo/` (P3-11) are deferred — current enumerator filters unpacked arrays so the path is reserved but not exercised.
  - **P3-3** (`src/Bistable.Verilator/ProbeTableEnumerator.cs` + `SimulationWorkerBuilder.AppendProbeTableSupport`):
    - New `ProbeTableEnumerator.Enumerate(DesignAst, topModuleName)` walks the module hierarchy and yields one `ProbeEntry` per probable signal. Path mangler `MangleFieldName("top.acc.q")` → `"top__DOT__acc__DOT__q"` matches Verilator's `--public-flat-rw` field naming convention.
    - Filters: `__V*` Verilator internals (CSE/DFG temporaries), `Width > 64` (no `VlWide<N>` support yet), `ArrayDims.Count > 0` (memory deferred).
    - `SimulationWorkerBuilder.BuildAsync` now accepts an optional `DesignAst? designAst` parameter; when present + `EnableInternalProbes=true`, the enumerator runs and the resulting `IReadOnlyList<ProbeEntry>` is woven into the C++ codegen.
    - C++ emitter writes: `struct ProbeEntry { std::function<uint64_t()> read; std::function<void(uint64_t)> write; int width; bool is_signed; bool is_registered; bool is_memory; int memory_depth; };` + `std::unordered_map<std::string, ProbeEntry> probe_table` + `init_probe_table(model_t*)` that captures `rootp` and binds read/write lambdas via `r->{mangled_field}`.
    - Includes `V{model}___024root.h` when probes are present (the main header only forward-declares the root class).
  - **P3-4** (worker dispatch):
    - `readSignal`: probe-table lookup → call read lambda → emit `{"kind":"signalRead","result":{"path":"...","value":"0x...","width":N,"isSigned":bool}}`. Unknown path → `ErrorResponse`.
    - `readMemory`: deferred — memory probes filtered until P3-6 lands.
  - **P3-5** (worker dispatch + force-state):
    - `writeSignal`: direct write via probe lambda; emits `AckResponse`. One-shot — next eval may overwrite.
    - `forceSignal`: `forced_signals[path] = value` + immediate write + Ack.
    - `releaseSignal`: `forced_signals.erase(path)` + Ack.
    - `apply_forced_signals()` runs at the top of every `setInput`/`eval`/`tick`/`runCycles` AND after every `model.eval()` inside `drive_clock` — this second call is critical: without it, the forced value gets clobbered by the FF latch on the rising clock edge. The Reset branch now also re-calls `init_probe_table(model.get())` because `unique_ptr` reseat invalidates the captured `rootp`.
  - **P3-7**: `listProbes` iterates `probe_table` → emits `{"kind":"probeList","probes":[{path,width,isSigned,isRegistered,isMemory,memoryDepth}]}`. Memory flag is always false until P3-6.
  - **P3-10**: `tests/Bistable.Tests/Protocol/HotProbeTests.cs` — 7 integration tests covering counter (top-level read/write/force/release across ticks), hierarchy (`system_top.u_core.u_logic.sum` nested-instance probe), and arnicomp (smoke test: probe table populated for the production CPU sample). Uses `VerilatorXmlAstReader` to obtain the AST and feeds it through `BuildAsync(designAst:)`.
  - **Protocol refactor (foundational)**: `WorkerResponse` became an abstract record with `[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]`. Six subtypes: `SimulationFrame` (v1 stepping commands), `SignalReadResponse`, `MemoryReadResponse`, `ProbeListResponse`, `AckResponse`, `ErrorResponse`. `SimulationSnapshot` was deleted entirely (replaced by `SimulationFrame`). `MainWindowViewModel` and the entire test suite migrated: `SendAsync` → `StepAsync` for v1 stepping commands; `ApplySnapshot` → `ApplyFrame`.
  - **P3-12**: New `docs/PROTOCOL.md` — spec for every command shape + every response subtype, with JSON examples and the force/release lifecycle.
  - **P3-13**: `docs/ARCHITECTURE.md` simulation pipeline section now diagrams the probe table flow (AST → enumerator → C++ table → worker dispatch).
  - **Test suite**: 443 → **468** (+25). Zero regressions across all 4 test projects (4 + 2 + 12 + 450 = 468 total).
  - **What this unlocks**: **Phase 4** (live values on schematic) can begin — the GUI-side `LiveProbeService` now has a real worker-side API to call. A snapshot of arnicomp's full FF state is a single `listProbes` + per-path `readSignal` call away.

- **2026-05-25**: **P3-1, P3-2, P3-6 landed** (C# side of Phase 3 is complete; worker side P3-3/4/5 is the remaining critical path).
  - **P3-1** (`src/Bistable.Protocol/`):
    - `SimulationCommandType` gains 7 new enum values: `ReadSignal`, `WriteSignal`, `ForceSignal`, `ReleaseSignal`, `ReadMemory`, `WriteMemory`, `ListProbes`.
    - `SimulationCommand` extended with optional `Path` (hierarchy), `MemoryAddress`, `MemoryCount`. Old (v1) fields preserved unchanged.
    - New DTOs: `SignalReadResult(Path, Value, Width, IsSigned)`, `MemoryReadResult(Path, StartAddress, CellWidth, Cells)`, `ProbeDescriptor(Path, Width, IsSigned, IsRegistered, IsMemory, MemoryDepth)`.
    - `SimulationSnapshot` extended with optional `ReadResult`/`MemoryReadResult`/`ProbeList`/`Acknowledged`/`Error` fields. Only one is non-null per response, chosen by the command that produced it.
    - 22 tests in `tests/Bistable.Tests/Protocol/ProtocolV2JsonTests.cs`: per-command JSON discriminator, payload round-trips for all new types (including 128-bit wide hex value), v1 snapshot still parses (forward-compat).
  - **P3-2** (`Bistable.Core/Projects/ProjectConfiguration.cs` + `Bistable.Verilator/SimulationWorkerBuilder.cs`):
    - New field `ProjectConfiguration.EnableInternalProbes` (default `true`). Existing `.bistable.json` files without the field still load with the default.
    - Worker build pipeline appends `--public-flat-rw` to the Verilator command line when the flag is enabled.
    - `BuildAsync` refactored: the argument-list assembly extracted into a pure `BuildVerilatorArguments` helper to keep `BuildAsync`'s cognitive complexity inside the 15-statement budget.
    - 3 tests in `tests/Bistable.Tests/Protocol/InternalProbesConfigTests.cs`: default value, init-only override, JSON round-trip (with and without the field present).
  - **P3-6** (`Bistable.App/Services/SimulationWorkerClient.cs`):
    - 6 typed async methods: `ReadSignalAsync`, `WriteSignalAsync`, `ForceSignalAsync`, `ReleaseSignalAsync`, `ReadMemoryAsync`, `WriteMemoryAsync`, `ListProbesAsync`.
    - Centralized error handling: every method calls `ThrowIfError` on the snapshot so callers see a clean `InvalidOperationException` instead of silently-null fields.
    - Until P3-3/4/5 land, these calls surface the worker's "unknown command" error — useful for writing GUI/test code against the final API today.
  - **Test suite**: 425 → **443** (+25 across 3 new test files). Zero regressions in any of the 4 test projects (4 + 2 + 12 + 425 = 443 total).
  - **What this unlocks**: the entire Phase 3 C# surface is ready. Phase 4 (live values on schematic) can be written against this API today — the calls just won't return real data until the worker C++ side is done.
  - **Remaining critical path**: **P3-3/P3-4/P3-5** — C++ worker code-gen (probe table from AST + command dispatch + force/release state). Sized for one focused Opus session (~5-6 days serial work, condensable to ~2-3 days with strong focus). Detailed handoff spec follows below.

---

## 11. Opus handoff — P3-3 / P3-4 / P3-5 (C++ worker code-gen)

Cold-start prompt for the next session:

```
Repo: /home/ardac/projects/verilatorGUI
Bugünün tarihi: 2026-05-25 (or later)
Branch: kullanıcının committed olduğu branch

GÖREV
Phase 3'ün worker side'ını tamamla: probe table generation + command handlers +
force/release state. P3-3, P3-4, P3-5 birlikte tek bir tutarlı C++ değişikliği.

ÖNCE OKU
1. docs/PHASES/PHASE-3.md — bu dosyanın TÜMÜ. Özellikle Bölüm 2 (architecture)
   ve Bölüm 4'teki P3-3/P3-4/P3-5 spec'leri.
2. docs/PROTOCOL.md (varsa — yoksa bu görevin parçası olarak oluşturulacak).
3. src/Bistable.Protocol/*.cs — P3-1'de eklenen yeni tiplere bak.
4. src/Bistable.Verilator/SimulationWorkerBuilder.cs — GenerateWorkerSource
   method. Mevcut C++ kod üretimi pipeline'ı + main() dispatcher.
5. src/Bistable.App/Services/SimulationWorkerClient.cs — P3-6'da eklenen
   typed wrapper'lar. Worker'ın produced edeceği JSON'un beklenen şekli.

YAPILACAKLAR

KATMAN A: Probe table generation (P3-3)
1. SimulationWorkerBuilder.BuildAsync'e DesignAst parametresi ekle
   (veya ModuleMetadata'dan elaborated design alınabilirse — kontrol et).
2. GenerateWorkerSource içinde, model class'ından sonra şu C++ kodu ekle:
     - struct ProbeEntry { std::function<uint64_t()> read; std::function<void(uint64_t)> write; int width; bool isSigned; bool isRegistered; bool isMemory; int memoryDepth; };
     - static std::unordered_map<std::string, ProbeEntry> probeTable;
     - Setup function init_probe_table(model_t* m) — AST'ten her SignalDecl
       için bir entry üret:
         probeTable["arnicomp_top.acc.q"] = ProbeEntry{
             .read = [m]() -> uint64_t { return m->arnicomp_top->acc->q; },
             .write = [m](uint64_t v) { m->arnicomp_top->acc->q = v; },
             .width = 8, ...
         };
3. AST yolundan C++ field path'ine çeviri: AST signal path "arnicomp_top.acc.q"
   → Verilator field path "arnicomp_top->acc->q" (DOT → ->).
4. Memory signal'ler (SignalDecl.ArrayDims.Count > 0) için, read/write
   lambda'ları address index alır.
5. main() başında init_probe_table çağrısı.

KATMAN B: Read/Write handlers (P3-4)
1. main() dispatch'ine yeni komutlar ekle:
   - type == "readSignal": probeTable lookup, ProbeEntry.read() çağrı, JSON cevap:
       {"time":..., "signals":[], "readResult":{"path":"...","value":"0x42","width":8,"isSigned":false}}
   - type == "writeSignal": lookup, write() çağrı, ACK cevap: {"acknowledged":true}.
   - type == "readMemory": lookup + memoryAddress + memoryCount; range loop;
     {"memoryReadResult":{"path":"...","startAddress":0,"cellWidth":8,"cells":["0x00","0xA2",...]}}
   - type == "writeMemory": lookup + address + value; write to cell.
   - type == "listProbes": JSON array of {"path","width","isSigned","isRegistered","isMemory","memoryDepth"} for every probeTable entry.
2. Path lookup başarısız: error JSON: {"signals":[],"error":"unknown probe path: ..."}.
3. Out-of-range memory: structured error.

KATMAN C: Force/Release (P3-5)
1. static std::unordered_map<std::string, uint64_t> forcedSignals;
2. type == "forceSignal": probeTable lookup; forcedSignals[path] = value; ack.
3. type == "releaseSignal": forcedSignals.erase(path); ack.
4. apply_forced_signals() function — her EVAL ÖNCESİ çağrılacak. Modify
   "eval"/"tick"/"runCycles" branches: model->eval() çağrısından ÖNCE
   her forced signal için probeTable[path].write(value).
5. Bu sayede simulation bir sonraki cycle'da bile forced değeri ezse,
   apply_forced_signals onu geri yazar.

KATMAN D: Tests
1. tests/Bistable.Tests/Protocol/HotProbeTests.cs (NEW):
   - [Trait("Category", "Integration")] — Verilator gerek
   - HasVerilator() skip pattern
   - arnicomp build → worker spawn → ReadSignal/WriteSignal/Force/Release roundtrip
   - 8-10 test
2. native test harness (tests/native/) — opsiyonel, P3-9 bağımsız task

DİKKAT EDİLECEK NOKTALAR
- Verilator field naming: --public-flat-rw ile hierarchical sinyaller
  "model->__PVT__top->__PVT__acc->q" gibi name-mangled alanlara dönüşebilir
  veya doğrudan "model->top->acc->q" olabilir. Verilator 5.x'te ikincisi yaygın
  ama önce manuel bir arnicomp build çıktısını incele.
- Wide signals (>64 bit) için VlWide<N> tipi var. Probe table read/write
  lambdaları bu tipi handle etmeli (en az 128 bit'e kadar — hex string ile).
- JSON encoding: mevcut json_escape helper var. ReadResult / MemoryReadResult
  şekli ProtocolV2JsonTests.cs'teki C# round-trip ile birebir eşleşmeli.
- forcedSignals re-apply order: eval ÖNCESİ (semantically: at clock edge before
  Verilog assignments execute).

KISITLAR
- Mevcut v1 command'lar (SetInput, Eval, Tick, vs.) DOKUNULMASIN. Sadece
  yeni v2 branch'leri ekle.
- SimulationWorkerBuilder.BuildAsync imzasını değiştirmek gerekirse,
  geriye dönük uyumluluk için overload bırak veya ModuleMetadata yerine
  ElaboratedDesign al.
- Test suite mevcut 443'ten en az 10 yeni HotProbe testi eklenmeli.
- Commit YAPMA. Bittiğinde diff göster.

DOĞRULAMA
- dotnet build temiz
- HasVerilator() varsa: HotProbeTests yeşil
- arnicomp build temiz (uzun sürebilir — --public-flat-rw flag yüzünden)
- Connectivity audit (Phase 2.5 polish'ten): hâlâ "0 problematic unconnected ports"

BİTİNCE
- PHASE-3.md güncelle: P3-3/4/5 ✅, Recent activity'ye detaylı entry
- 8-10 cümlede özetle
- git diff --stat HEAD
- "Phase 4'e geçmeye hazırız" mesajı

ÇALIŞMA STİLİ
- TodoWrite ile her katmanı (A/B/C/D) takip et
- C++ code-gen — her büyük değişiklikten sonra bir arnicomp build dene
- Türkçe konuş ama kod İngilizce
- Verilator name mangling konusunda çok dikkatli ol — yanlış path = silent
  runtime failure
```

---
