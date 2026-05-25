# Phase 3 — Worker Protocol v2 (Live Internal Probe)

**Master plan:** `/home/ardac/.claude/plans/fluffy-wishing-kettle.md` Section 9
**Phase goal:** Allow the GUI to read and write ANY hierarchical signal (or memory cell) at simulation time, not just top-level ports. This is the **prerequisite for Phase 4 (live values on schematic)** — the differentiator that turns a static viewer into a Logisim-class live debugger.
**Prerequisite:** Phase 1 (AST) complete. Phase 2 / 2.5 static schematic is production-ready as of 2026-05-25 (Phase 2.5: 7/7 + 3 closeout tasks complete), so Phase 3 can start on top of a stable schematic baseline.
**Phase gate:** `ReadSignal("arnicomp_top.acc.q")` returns the live FF value mid-simulation; `ForceSignal` holds a value across `Eval` cycles; memory read API returns sensible array contents; all existing samples still build and run.

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
| P3-1 | Protocol v2 type definitions | ☐ | Sonnet | 1 d | `Bistable.Protocol`: new enum values + DTOs |
| P3-2 | Worker code-gen: add `--public-flat-rw` flag | ☐ | Sonnet | 0.5 d | One-line in `SimulationWorkerBuilder.GenerateWorkerSource` |
| P3-3 | Probe table generation (C++) | ☐ | Opus | 2 d | Generate code from AST that maps "hier.path" → field pointer |
| P3-4 | Worker handler: ReadSignal / ReadMemory | ☐ | Opus | 2 d | C++ command dispatch + JSON encode |
| P3-5 | Worker handler: WriteSignal / ForceSignal / ReleaseSignal | ☐ | Opus | 2 d | Same dispatch, plus force-state re-apply in Eval |
| P3-6 | C# client API: `ReadSignalAsync` / `ForceSignalAsync` / `ReadMemoryAsync` / `ListProbesAsync` | ☐ | Sonnet | 1 d | Strongly-typed wrappers around `SendAsync` |
| P3-7 | Probe enumeration: `ListProbes` returns AST signal list | ☐ | Sonnet | 1 d | Worker emits its known probe paths |
| P3-8 | Per-config opt-in flag (large designs may want to skip probes) | ☐ | Sonnet | 0.5 d | `ProjectConfiguration.EnableInternalProbes`, default true |
| P3-9 | C++ test harness (native unit tests) | ☐ | Opus | 2 d | Small `tests/native/` with CMake; build a tiny module, exercise read/write/force/release |
| P3-10 | C# protocol round-trip tests (arnicomp end-to-end) | ☐ | Sonnet | 1 d | Spin up real worker for arnicomp, read `top.acc.q`, drive clk, read again, assert delta |
| P3-11 | Memory demo sample | ☐ | Sonnet | 1 d | New `samples/memory_demo/` with `logic [7:0] mem [0:15]` — proves memory API |
| P3-12 | Update `docs/PROTOCOL.md` | ☐ | Sonnet | 0.5 d | Spec the v2 commands + JSON shapes |
| P3-13 | Update `docs/ARCHITECTURE.md` | ☐ | Sonnet | 0.5 d | Layer map shows the probe table |

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

(empty — phase has not started)
