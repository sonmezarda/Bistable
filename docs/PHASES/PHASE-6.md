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
| P6-1 | Add synthesis config model | done | 1 d | `Bistable.Core.Projects.SynthesisConfiguration` opt-in `Synthesis` field on `ProjectConfiguration`. JSON round-trips with sensible defaults. |
| P6-2 | Add `Bistable.Yosys` project | done | 1 d | Solution + App + Tests now reference `Bistable.Yosys`. Tool + script-builder live here; JSON parser will land in P6-4. |
| P6-3 | Implement `YosysTool` runner | done | 2 d | `YosysTool.IsAvailableAsync` / `GetVersionAsync` / `RunScriptAsync`. `YosysScriptBuilder.Build(project, synth, dir)` emits the default `read_verilog → hierarchy → proc → opt → fsm → opt → memory → opt → [flatten] → [techmap] → write_json` pipeline. |
| P6-4 | Parse Yosys JSON to `GateNetlist` | done | 4 d | `Bistable.Core.Synthesis.GateNetlist` data model + `Bistable.Yosys.YosysJsonReader.Read(string|JsonElement)` / `ReadFileAsync(path, ct)`. Handles port directions, ordered bit vectors, integer net ids vs string-encoded constants (`"0"/"1"/"x"/"z"`), cell connections + `port_directions` + `parameters`, and the top-module attribute. |
| P6-5 | Add generic-cell primitive mapping | done | 4 d | `Bistable.Yosys.GateCellLibrary` maps `$_AND_`/`$_OR_`/`$_XOR_`/`$_NAND_`/`$_NOR_`/`$_XNOR_`/`$_NOT_`/`$_BUF_`/`$_MUX_`/`$_DFF_P_`/`$_DFF_N_`/`$_DLATCH_P_`/`$_DLATCH_N_` to renderer symbol families (Gate/Inverter/Buffer/Mux/FlipFlop/Latch) + pin role metadata (Inputs / Output / ClockPin / EnablePin). Unknown cells fall back to a generic descriptor instead of being dropped. |
| P6-6 | Build gate-level schematic graph | done | 5 d | `GateNetlistElkBuilder.Build(netlist)` emits one ELK node per cell with the matching prefix (`gate_` / `ff_` / `mux_` / `inv_` / `buf_` / `latch_`) so the existing `SchematicPreviewControl` symbol dispatchers fire; boundary anchors per port; edges keyed off Yosys's shared bit ids with constants skipped. |
| P6-7 | Gate-level GUI + worker build path | partial | 4 d | GUI side landed: VM `SynthesizeCommand` runs Yosys → parses JSON → raises `GateNetlistReady`. Toolbar **Synthesize** button (visible only when `Synthesis.Enabled`); single-instance `GateLevelSchematicWindow` renders the laid-out graph on a Canvas. Gate-level worker simulation deferred to a follow-up pass. |
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

---

## 14. Recent activity

- **2026-06-04 — P6-1 / P6-2 / P6-3 landed (foundation wave).**
  - `Bistable.Core.Projects.SynthesisConfiguration` is the new opt-in `Synthesis` field on `ProjectConfiguration`. Designs without a synthesis section behave exactly as before; CPU samples can add a small block to participate. JSON tests pin round-trip + missing-field + partial-field semantics.
  - New `src/Bistable.Yosys/` project. `Bistable.App` and `Bistable.Tests` both depend on it; solution updated. Will host the JSON parser (P6-4), generic-cell mapping (P6-5), and the gate-level schematic graph builder (P6-6).
  - `YosysTool` mirrors `VerilatorTool`'s shape: `IsAvailableAsync` (graceful return when yosys isn't on PATH), `GetVersionAsync`, `RunScriptAsync(scriptPath, workingDir)` capturing full stdout/stderr.
  - `YosysScriptBuilder.Build(project, synthesis, projectDir)` emits the default `read_verilog → hierarchy → proc → opt → fsm → opt → memory → opt → [flatten] → [techmap when GenericCells] → opt → write_json` pipeline. Output path resolves relative to the project dir; the output directory is created so yosys can write into it.
  - **Tests**: +11 in `tests/Bistable.Tests/Synthesis/` — `SynthesisConfigurationTests` (3, JSON round-trip / optional / partial defaults), `YosysScriptBuilderTests` (6, every stage toggle + path resolution), `YosysToolTests` (2, missing-binary + missing-script paths). Combined suite **672/672 green** (652 Tests + 14 Snapshots + 4 Regression + 2 UI).
  - **Side note**: `docs/RTL_COVERAGE_TODO.md` lists 10 RTL endpoints (8 SequentialBlock multi-statement / array-loop reset, 2 PrimitiveEndpoint replicate-concat) that the analyzer still flags as `Unsupported` on the bundled samples. Phase 6 work assumes those improvements happen in parallel — the gate-level renderer's correctness doesn't depend on them.
  - **Next P6 milestones**: P6-4 (Yosys JSON → `GateNetlist` reader), then P6-5 (gate cell symbol library), then P6-6 (gate-level schematic graph builder).

- **2026-06-04 — P6-4 (Yosys JSON → GateNetlist parser) landed.**
  - `Bistable.Core.Synthesis.GateNetlist` data model: `GateNetlist { TopModule, Modules }` → `GateModule { Name, Ports[], Cells[], Nets[] }` → `GateCell { Name, Type, Connections, PortDirections, Parameters }`. Bit identity is `GateBit` (struct) carrying either a net id (≥ 2) or one of four constant flags (`0`/`1`/`x`/`z`). Deliberately separate from `DesignAst` — RTL and post-synthesis are different semantic layers.
  - `Bistable.Yosys.YosysJsonReader.Read(json)` and `ReadFileAsync(path, ct)` parse Yosys 0.33's `write_json` output. Handles: integer net ids alongside string constants in the same `bits` array, optional `port_directions`/`parameters`/`netnames`, the `top` attribute's 32-char binary encoding, and the "single module, no top flag" fallback.
  - Tests: +7 fixture-driven in `YosysJsonReaderTests` against real `yosys 0.33` output (and-gate, 4-bit DFF bus, const-bit concat, ternary mux, netnames preservation, error-on-missing-modules, top-fallback). Fixtures live under `tests/Bistable.Tests/Synthesis/fixtures/` and are copied to the test output dir via `CopyToOutputDirectory="PreserveNewest"`.
  - End-to-end integration test (`YosysRoundTripIntegrationTests`, `Category=Integration`) writes a tiny SV file, runs the real `yosys` binary through `YosysScriptBuilder` + `YosysTool`, then parses the resulting JSON. Skips gracefully when yosys isn't on PATH so CI without the binary stays green.
  - Combined suite **680/680 green** (660 Tests + 14 Snapshots + 4 Regression + 2 UI).
  - **Next P6 milestones**: P6-5 (generic-cell symbol library + pin metadata for `$_AND_` / `$_OR_` / `$_XOR_` / `$_NOT_` / `$_MUX_` / `$_DFF_*`), then P6-6 (gate-level schematic graph builder backed by ELK).

- **2026-06-04 — P6-5 / P6-6 / P6-7 (GUI side) landed.**
  - **GateCellLibrary** (`src/Bistable.Yosys/GateCellLibrary.cs`) — explicit table from every supported Yosys cell type to a `GateCellDescriptor { Shape, GateKind?, Inputs[], Output, ClockPin?, EnablePin? }`. The schema lets the graph builder honour FF pin ordering (D / C / Q) and mux south-side selectors, and lets the renderer dispatch on the existing `GateKind` enum without any new symbol code.
  - **GateNetlistElkBuilder** (`src/Bistable.App/Services/Routing/Elk/GateNetlistElkBuilder.cs`) — static graph builder. One ELK node per cell (prefix follows `Shape`: `gate_` / `ff_` / `mux_` / `inv_` / `buf_` / `latch_`), two boundary anchors for input / output module ports, edges generated per shared net id (constants are silently skipped — they have no driver). All endpoints land in `PortRefs` so a future live-values pass can attach.
  - **Synthesize VM command** in `MainWindowViewModel` runs `YosysTool.IsAvailableAsync` → writes a script from `YosysScriptBuilder` → runs Yosys → parses the JSON via `YosysJsonReader` → raises `GateNetlistReady`. Status text (`SynthesisStatus`) mirrors to the global status bar so long synths surface progress. `IsSynthesizing` re-evaluates the command so double-presses are suppressed.
  - **Toolbar Synthesize button** + **GateLevelSchematicWindow**. Window builds the netlist's ELK graph, runs the local `ElkRunner`, and paints nodes + polylined edges on a `Canvas` (deliberately simpler than the RTL preview — the goal is "user sees real gates and wires" before lifting the full symbol painter over).
  - **RISC-V sample** (`samples/riscv_single_cycle/riscv_single_cycle.bistable.json`) gained a `"synthesis": { "enabled": true, "outputJson": ".bistable/synthesis/riscv_single_cycle_top.json", "genericCells": true }` block — one-click demo path.
  - **Tests**: +9 in `GateCellLibraryTests` (every supported cell type + unknown fallback), +6 in `GateNetlistElkBuilderTests` (boundary + cell nodes, label kind token, edge count, multi-FF bus, constants don't emit edges, missing-top-throws). Combined suite **700/700 green** (680 Tests + 14 Snapshots + 4 Regression + 2 UI).
  - **Remaining P6 work**: P6-7's worker-build path (gate-level Verilator compilation), P6-8 (RTL vs gate-level smoke compare), P6-9 (synthesis reports surfaced in the UI), P6-10 (sample synthesis flow tests). The current GUI surface is enough for the user to drive `Synthesize` against the RISC-V sample and see gates.

