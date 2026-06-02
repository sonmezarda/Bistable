# Phase 5 — RV32I Execution Target

**Status:** Proposed capability phase  
**Replaces vague priority:** This phase makes the simulator useful for real CPU workflows. It is more important than remaining Phase 2.7 UX polish.  
**Prerequisite:** Phase 2.9 coverage audit started; Phase 3 worker protocol available; Phase 4 live values usable enough for probes.  
**Phase goal:** Load and run a small RV32I processor design from RTL, execute a program image, inspect CPU state live, and verify pass/fail behavior from the GUI and tests.

---

## 1. Why this phase matters

The product target is not "draw arnicomp nicely." The target is:

> A user can point the tool at a non-trivial CPU design, run a program, inspect live internal state, and understand what happened.

For a professional HDL tool, a CPU is the first serious integration target. It stresses:

- hierarchy,
- generated structures,
- memories,
- registers,
- wide buses,
- instruction/data flow,
- reset sequencing,
- clocking,
- program loading,
- live probes,
- waveform correlation.

If this phase succeeds, the project becomes a real simulator/debugger. If it does not, more schematic polish is premature.

---

## 2. Target definition

### Minimum target

Start with a small RV32I core, not an out-of-order core.

Requirements:

- single clock,
- active reset,
- instruction memory path,
- data memory path or simple memory-mapped pass/fail,
- exposed PC,
- exposed register file or enough probes to infer architectural state.

Candidate approaches:

1. Add a small in-repo `samples/rv32i_minimal`.
2. Import a small open-source RV32I core into `samples/rv32i_minimal` with license preserved.
3. Generate a tiny test core only if a real one is impractical.

The phase should not start with BOOM/CVA6/out-of-order. Those are Level 3 targets after basic CPU workflow exists.

### Program target

First program:

- reset,
- execute a few ALU instructions,
- store pass marker to memory-mapped address or set a `pass` output,
- halt or loop.

Later program set:

- arithmetic,
- load/store,
- branch,
- jump,
- register file write/read,
- simple exception/trap if supported.

---

## 3. Project configuration extensions

Current `ProjectConfiguration` is enough for compile/build, but not enough for CPU execution.

Add a runtime section:

```json
{
  "runtime": {
    "clock": "clk",
    "reset": {
      "signal": "rst_n",
      "activeLevel": 0,
      "cycles": 4
    },
    "programImages": [
      {
        "path": "programs/smoke.hex",
        "format": "hex",
        "target": "instructionMemory",
        "probePath": "rv32i_top.imem.mem",
        "baseAddress": 0
      }
    ],
    "runPresets": [
      {
        "name": "Run smoke",
        "clock": "clk",
        "maxCycles": 1000,
        "stopWhen": "rv32i_top.pass == 1 || rv32i_top.fail == 1"
      }
    ],
    "cpuState": {
      "pc": "rv32i_top.pc",
      "instruction": "rv32i_top.instr",
      "registerFile": "rv32i_top.regfile.regs",
      "pass": "rv32i_top.pass",
      "fail": "rv32i_top.fail"
    }
  }
}
```

Implementation can start simpler than this, but the schema should point toward this shape.

---

## 4. Task board

Status legend: `todo`, `in_progress`, `done`, `blocked`

| ID | Task | Status | Est. | Notes |
|----|------|--------|------|-------|
| P5-1 | Select/add RV32I sample | todo | 2 d | In-repo sample with license clarity. Must compile with Verilator. |
| P5-2 | Add runtime config model | todo | 2 d | Clock/reset/program/pass-fail/probe paths. |
| P5-3 | Program image loader | todo | 3 d | Start with `.hex`/`.mem`; ELF later. |
| P5-4 | Memory initialization path | todo | 3 d | Either Verilator parameter/`$readmemh` or worker memory writes via probe API. |
| P5-5 | Reset/run preset engine | todo | 2 d | Apply reset cycles, then run until max cycles or stop condition. |
| P5-6 | CPU state probe model | todo | 2 d | PC, instruction, registers, memory, pass/fail. |
| P5-7 | RV32I smoke tests | todo | 4 d | Program-level integration tests. |
| P5-8 | GUI CPU run panel | todo | 3 d | Minimal: load program, reset, run, status, PC, pass/fail. |
| P5-9 | Register file and memory viewer integration | todo | 3 d | Reuse memory/probe infrastructure. |
| P5-10 | Schematic/probe correlation | todo | 2 d | Selecting PC/register probes highlights schematic paths. |
| P5-11 | Documentation and sample guide | todo | 1 d | How to add a CPU target. |

---

## 5. Program image support

### First implementation

Support simple hex files:

- one word per line,
- comments allowed,
- configurable word width,
- configurable base address.

Use cases:

- load instruction memory,
- optionally load data memory.

### Later implementation

Support ELF:

- parse ELF sections,
- map loadable sections into memory probes,
- symbol table for labels,
- optional disassembly.

ELF is important, but it should not block the first RV32I smoke.

---

## 6. Memory loading strategies

### Strategy A — `$readmemh`

If the RTL already has:

```systemverilog
initial $readmemh(MEM_FILE, mem);
```

then pass `-GMEM_FILE=...` or equivalent parameter through project config.

Pros:

- simple,
- Verilator-native,
- matches many RTL projects.

Cons:

- not all designs expose this parameter,
- hard to reload without rebuilding.

### Strategy B — worker memory writes

Use Phase 3 `WriteMemory`/probe API to initialize memory after worker startup.

Pros:

- no rebuild for new program,
- GUI-controlled,
- works for more interactive workflows.

Cons:

- current memory probe support may need stronger wide/unpacked array handling,
- needs reliable memory path config.

Recommended:

- implement Strategy A for first sample if fastest,
- add Strategy B as the professional path.

---

## 7. CPU state model

Add a domain model separate from generic probes:

```csharp
public sealed record CpuStateProbeMap(
    string? Pc,
    string? Instruction,
    string? RegisterFile,
    string? DataMemory,
    string? Pass,
    string? Fail);
```

This model lets the UI show CPU state without hardcoding RV32I internals.

For RV32I, also add optional decode:

- raw instruction hex,
- opcode,
- rd,
- rs1,
- rs2,
- immediate,
- mnemonic for common instructions.

Do not overbuild a full disassembler initially. Add only enough for smoke/debug value.

---

## 8. Integration tests

New test file:

- `tests/Bistable.Tests/Protocol/Rv32iExecutionTests.cs`

Traits:

- `[Trait("Category", "Integration")]`
- `[Trait("RequiresVerilator", "true")]`
- possibly `[Trait("Speed", "Slow")]` for longer runs.

Minimum tests:

1. RV32I sample elaborates.
2. Worker builds.
3. Probe list contains PC.
4. Program image loads.
5. Reset drives PC to expected value.
6. Running smoke program reaches pass condition.
7. Register file contains expected final value.
8. Fail condition remains false.

Acceptance:

- test can skip gracefully when Verilator is not installed,
- test must not silently pass if the sample exists but probes are missing.

---

## 9. GUI acceptance

The GUI must provide:

- visible runtime target status,
- load program button,
- reset button,
- run preset button,
- current cycle/time,
- pass/fail indicator,
- PC display,
- instruction display,
- register/memory viewer entry point.

This is not a polish feature. It is the minimum workflow for CPU simulation.

---

## 10. Relationship to Phase 2.7

Phase 2.7 search and navigation become useful after this phase because there will be real CPU state to navigate.

Do not block this phase on:

- mini-map,
- theme presets,
- export,
- layout overrides.

Do reuse:

- breadcrumb/history if helpful,
- pinned signals for PC/instruction/register state,
- Preferences scaffold only for real runtime settings.

---

## 11. Phase gate

The phase closes only when:

- an RV32I sample exists in `samples/`,
- it elaborates through Verilator XML,
- it builds a native worker,
- a smoke program can be loaded,
- reset/run can be executed through tests,
- pass/fail is detected,
- PC and at least one architectural register can be read through probes,
- the GUI exposes a minimal CPU run workflow,
- docs explain how to add another CPU target.

