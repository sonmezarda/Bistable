# Phase 6 — Gate-Level Synthesis Backend

**Status:** Proposed capability phase  
**Prerequisite:** Phase 2.9 coverage audit; Phase 5 RV32I execution target started or completed.  
**Phase goal:** Add a synthesis backend so the tool can convert RTL to a gate-level netlist, import that netlist, render gate-level schematics, simulate the synthesized design, and compare RTL vs gate-level smoke behavior.

---

## 1. Why this phase matters

The current project is primarily an RTL schematic + Verilator simulation tool. That is valuable, but it is not enough for the stated professional target.

A professional HDL tool must eventually answer:

- What does this RTL become after synthesis?
- Which gates implement this datapath?
- Does gate-level behavior match RTL behavior for a smoke program?
- Which logic feeds this register after synthesis?
- How large is the design at the cell/net level?

This requires a new backend. Verilator XML elaboration is not a synthesis result.

---

## 2. Backend choice

Recommended first backend:

- **Yosys** for synthesis.
- **Yosys JSON** for netlist import.
- Generic cells first, not foundry cells.

Initial cell family:

- `$_AND_`
- `$_OR_`
- `$_XOR_`
- `$_XNOR_`
- `$_NAND_`
- `$_NOR_`
- `$_NOT_`
- `$_BUF_`
- `$_MUX_`
- `$_DFF_*`
- `$_DLATCH_*`
- `$_SDFF_*` where needed
- memory cells deferred unless required by sample.

Later:

- Liberty-backed cell libraries,
- area/timing metadata,
- technology mapping reports,
- SDC constraints,
- STA integration.

---

## 3. Architecture

```text
RTL ProjectConfiguration
    |
    +--> Verilator path (current)
    |       -> DesignAst
    |       -> RTL schematic
    |       -> RTL worker simulation
    |
    +--> Yosys path (new)
            -> Yosys JSON netlist
            -> GateNetlist IR
            -> gate-level schematic
            -> gate-level worker simulation
            -> RTL vs gate-level compare
```

New project recommended:

```text
src/Bistable.Yosys/
    YosysTool.cs
    YosysJsonReader.cs
    GateNetlist.cs
    GateNetlistFlattener.cs
```

Dependency rule:

- `Bistable.Yosys` references `Bistable.Core`.
- `Bistable.App` references `Bistable.Yosys`.
- `Bistable.Core` must not depend on Yosys.

---

## 4. Data model

Add core netlist types:

```csharp
public sealed record GateNetlist(
    string TopModule,
    IReadOnlyDictionary<string, GateModule> Modules);

public sealed record GateModule(
    string Name,
    IReadOnlyList<GatePort> Ports,
    IReadOnlyList<GateNet> Nets,
    IReadOnlyList<GateCell> Cells);

public sealed record GateCell(
    string Name,
    string Type,
    IReadOnlyDictionary<string, GateConnection> Connections,
    IReadOnlyDictionary<string, string> Parameters);

public sealed record GateConnection(
    string PortName,
    IReadOnlyList<GateBit> Bits);
```

Important:

- Keep this separate from `DesignAst`.
- RTL AST and synthesized netlist are different semantic layers.
- Do not force gate-level cells into RTL primitive types too early.

---

## 5. Project configuration extensions

Add synthesis section:

```json
{
  "synthesis": {
    "enabled": true,
    "backend": "yosys",
    "script": "synth.ys",
    "topModule": "rv32i_top",
    "outputJson": ".bistable/synthesis/rv32i_top.json",
    "genericCells": true,
    "flatten": false
  }
}
```

Initial implementation can generate a default Yosys script automatically:

```yosys
read_verilog -sv <sources>
hierarchy -top <top>
proc
opt
fsm
opt
memory
opt
techmap
opt
write_json <output>
```

For CPU designs, memory handling must be explicit. Some flows may preserve memories; some may map them. The report must state which happened.

---

## 6. Task board

Status legend: `todo`, `in_progress`, `done`, `blocked`

| ID | Task | Status | Est. | Notes |
|----|------|--------|------|-------|
| P6-1 | Add synthesis config model | todo | 1 d | `SynthesisConfiguration` in project config. |
| P6-2 | Add `Bistable.Yosys` project | todo | 1 d | Tool runner + JSON reader shell. |
| P6-3 | Implement `YosysTool` runner | todo | 2 d | Locate `yosys`, run script, capture logs/errors. |
| P6-4 | Parse Yosys JSON to `GateNetlist` | todo | 4 d | Modules, ports, cells, netnames, bit vectors. |
| P6-5 | Add generic-cell primitive mapping | todo | 4 d | Gate cell symbol families and pin metadata. |
| P6-6 | Build gate-level schematic graph | todo | 5 d | Separate from RTL builder; use shared renderer where safe. |
| P6-7 | Gate-level worker build path | todo | 4 d | Verilator can compile synthesized Verilog or Yosys output flow; choose safest first. |
| P6-8 | RTL vs gate-level smoke comparison | todo | 4 d | Same inputs/program, compare outputs/final state. |
| P6-9 | Synthesis reports | todo | 2 d | Cell count, net count, unsupported cells, memory treatment. |
| P6-10 | Tests and sample synthesis flow | todo | 5 d | Start with tiny combinational/sequential modules, then RV32I smoke. |

---

## 7. Gate-level schematic design

### 7.1 Visual conventions

Generic cells should render with standard symbols:

- AND/OR/XOR/NAND/NOR/XNOR as logic gates,
- NOT/BUF as triangle/bubble,
- MUX as mux trapezoid,
- DFF/SDFF as flip-flop with reset/set annotations,
- unknown cells as explicit black-box cells with warning label.

### 7.2 Net rendering

Gate-level netlists can have many anonymous nets. The schematic must avoid making the graph unreadable:

- default: group gates by module/hierarchy if not flattened,
- allow "show only cone of influence" later,
- support fan-in/fan-out exploration,
- detect high-fanout nets such as clocks/resets and render them specially.

### 7.3 Cell pin metadata

Yosys generic cell ports must map to semantic pin names:

- `A`, `B`, `Y` for binary gates,
- `S`, `A`, `B`, `Y` for mux,
- `D`, `C`, `Q`, reset/set pins for DFF variants.

This mapping should live in a central table, not scattered through renderer code.

---

## 8. Simulation strategy

Two possible paths:

### Strategy A — synthesize to Verilog and run Verilator

Yosys emits synthesized Verilog:

```yosys
write_verilog .bistable/synthesis/top_synth.v
```

Then existing Verilator worker path builds it.

Pros:

- reuses current worker infrastructure,
- simpler initial gate-level simulation.

Cons:

- probe paths differ,
- generated names can be ugly,
- cell-level mapping may not match JSON import exactly unless controlled.

### Strategy B — simulate from imported netlist

Build a custom netlist simulator.

Pros:

- direct control over cells/probes.

Cons:

- much larger implementation,
- easy to get sequential semantics wrong.

Recommended:

- Start with Strategy A.
- Use Yosys JSON for schematic/report.
- Use synthesized Verilog for worker simulation.
- Add name mapping report between JSON cells and Verilog signals when needed.

---

## 9. RTL vs gate-level comparison

Minimum compare:

- same top-level inputs,
- same clock/reset sequence,
- same program image,
- compare selected outputs at end of run,
- compare pass/fail status.

Later:

- cycle-by-cycle output compare,
- internal correspondence map,
- register equivalence,
- waveform overlay.

First acceptance:

> The RV32I smoke program passes in both RTL and synthesized gate-level simulation and final externally visible state matches.

---

## 10. Diagnostics

Synthesis must produce reports:

- Yosys command line,
- Yosys version,
- script used,
- warnings/errors,
- module count,
- cell count by type,
- net count,
- unsupported cell types,
- memory treatment,
- whether design was flattened,
- output files.

No synthesis result should be accepted silently if unsupported cells exist.

---

## 11. Tests

### Unit tests

New test project or files:

- `tests/Bistable.Tests/Yosys/YosysJsonReaderTests.cs`
- `tests/Bistable.Tests/Yosys/GateCellMetadataTests.cs`
- `tests/Bistable.Tests/Yosys/GateNetlistSchematicTests.cs`

Cases:

- parse simple AND netlist,
- parse DFF netlist,
- parse mux netlist,
- parse vector nets,
- unknown cell creates diagnostic,
- generic cell pin mapping is stable.

### Integration tests

Traits:

- `[Trait("Category", "Integration")]`
- `[Trait("RequiresYosys", "true")]`
- `[Trait("RequiresVerilator", "true")]` for simulation compare.

Cases:

- synthesize counter,
- import JSON,
- render gate-level graph,
- simulate synthesized counter,
- compare RTL vs gate-level counter outputs,
- synthesize RV32I smoke target later.

---

## 12. Phase gate

The phase closes only when:

- project config can request synthesis,
- Yosys can synthesize a simple sample,
- Yosys JSON imports into `GateNetlist`,
- generic cells render in schematic,
- unsupported cells are explicitly reported,
- synthesized Verilog can be simulated through the worker,
- RTL vs gate-level smoke comparison passes for at least one sequential sample,
- RV32I smoke target has a documented synthesis path or documented blockers.

---

## 13. Non-goals

Do not start with:

- timing analysis,
- Liberty cell mapping,
- placement/routing,
- FPGA vendor flows,
- SDF back-annotation,
- full formal equivalence.

Those are future professional features after generic gate-level works.

