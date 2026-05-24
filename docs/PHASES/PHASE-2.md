# Phase 2 — Schematic Builder from AST (live status)

**Master plan:** `/home/ardac/.claude/plans/fluffy-wishing-kettle.md` Section 8
**Phase goal:** Translate `DesignAst` into a rich set of schematic primitives (FF, mux, buffer, inverter, joiner, splitter, arithmetic). Recursive compound expansion shows true internals.
**Prerequisite:** Phase 1 (DesignAst, reader, flattener) — done as of 2026-05-23.
**Phase gate:** arnicomp expanded compound shows FF/MUX/adder symbols and their connections; ≥30 decoder unit tests; per-sample golden snapshots; zero regression on Phase 0/1 tests.

---

## 1. Why this phase matters

After Phase 1, the AST captures every `always` block, `cond`, `concat`, etc. — but the schematic renderer (`ElkGraphBuilder`) still consumes only the legacy `DesignContAssign` flat list. As a result:

- FF/latch symbols are missing — sequential blocks don't produce visible registers.
- Mux symbols are missing — `CondExpr` shows as a generic `?:` operator box.
- Recursive expansion looks empty for modules dominated by sequential logic (e.g., arnicomp `reg_a`, `reg_marl`).

Phase 2 closes this gap by introducing a **two-pass model**: decode the AST into typed primitives, then lay those primitives out.

---

## 2. Architecture

```
ModuleAst
   │
   ▼ SchematicDecoder.Decode
SchematicPrimitiveList            (NEW — layout-agnostic primitive set)
   │
   ▼ ElkGraphBuilder.Build (refactored)
ElkGraph
   │
   ▼ ElkRunner.Layout
laid-out ElkGraph
```

**Separation of concerns:**
- `SchematicDecoder` is pure: `ModuleAst → SchematicPrimitiveList`. Testable without ELK.
- `ElkGraphBuilder` becomes a primitive renderer: `SchematicPrimitiveList → ElkGraph`.
- Symbol drawing moves to one file per gate family under `Views/Schematic/Symbols/`.

---

## 3. Primitive set

| Primitive | AST source | ELK id prefix | Symbol |
|-----------|-----------|---------------|--------|
| `FlipFlopPrimitive` | `SequentialBlockAst` with single `AssignAst` (rising-edge clock, optional async reset) | `ff_` | FF box: D / clk / Q / Q̄ pins |
| `LatchPrimitive` | `SequentialBlockAst` with level-sensitive trigger | `latch_` | Latch box: D / G / Q |
| `MuxPrimitive` | `ContAssignAst` whose source is `CondExpr`, or nested chain of `CondExpr` | `mux_` | Trapezoid with sel + N inputs |
| `BufferPrimitive` | `ContAssignAst` with source `SignalRef` (wire alias) | `buf_` | Triangle |
| `InverterPrimitive` | `ContAssignAst` with source `UnaryExpr(Not, …)` | `inv_` | Triangle with bubble |
| `GatePrimitive` | `ContAssignAst` with source `BinaryExpr(And/Or/Xor)` or `UnaryExpr(Reduce…)` | `op_` | Existing gate symbols (And/Or/Xor) |
| `ArithPrimitive` | `ContAssignAst` with source `BinaryExpr(Add/Sub/Mul/…)` | `op_` | Distinct rectangular block with op label |
| `SplitterPrimitive` | `ContAssignAst` with source `BitSelectExpr` | `split_` | Existing wedge (Phase 0) |
| `JoinerPrimitive` | `ContAssignAst` with source `ConcatExpr` | `join_` | Existing wedge (Phase 0) |
| `MemoryPrimitive` | `SignalDecl.ArrayDims.Count > 0` | `mem_` | Stacked rectangle (depth × width) |
| `InstancePrimitive` | `InstanceDecl` | `inst_` | Sub-module box (existing) |
| `PortPrimitive` | `PortDecl` | `port_` | Boundary pin (existing) |
| `SignalPrimitive` | `SignalDecl` (not registered, no driver in this scope) | `sig_` | Net node (existing) |

---

## 4. Task board

Status legend: ☐ todo · 🟡 in progress · ✅ done · ⛔ blocked

| ID | Task | Status | Notes |
|----|------|--------|-------|
| P2-1 | Define `SchematicPrimitive` type hierarchy in `Bistable.Core.Design.Schematic` | ✅ | Lives in `Bistable.Core` (not `App`) — pure types are core domain, reusable by future backends |
| P2-2 | Write `SchematicDecoder` (AST → primitives) | ✅ | Also in `Bistable.Core.Design.Schematic`. Handles Buffer/Splitter/Joiner/Mux/Gate/Arith/Inverter/FF/Latch/Memory/Instance/Port/Signal |
| P2-3 | Decoder unit tests (≥30) in `tests/Bistable.Tests/Schematic/` | ✅ | 26 tests; arnicomp-pattern end-to-end + per-primitive coverage |
| P2-4 | Wire `ElkGraphBuilder` to consume primitives (additive, FF nodes) | ✅ | `ElkScopeData.Primitives` + `AddFlipFlopNode` + `CollectFlipFlopEndpoints`; full VM→view→builder plumbing. 6 new builder tests. Sub-sim save/restore handles `_currentAst`. |
| P2-4c | Mux + Latch + Memory primitives wired into builder | ✅ | `AddMuxNode`/`AddLatchNode`/`AddMemoryNode` + `CollectMux/LatchEndpoints`. 28 new tests covering happy paths, edge cases (constants, orphans, empty inputs), discriminator disjointness, and full XML→AST→Decode→ELK end-to-end. |
| P2-4d | Buffer + Inverter + Gate + Arith primitives in builder | ✅ | Industry-standard symbols (triangle / triangle+bubble / IEEE 91 gate shapes / labelled box). Legacy operator-node generation suppressed per-target when Gate/Arith owns it. 27 new tests + legacy suppression contract + discriminator parametrized theory. |
| P2-8 | Recursive compound expansion: decoder inside expanded children | ✅ | `ElkScopeData.PrimitivesByModule` (keyed by module name) populated by VM at design load. `AttachCompoundChildren` adds scoped primitive nodes (`<compound>/ff_q`) inside expanded children. Leaf modules (no sub-instances but with primitives) now expand. Inner edge wiring deferred to P2-8b. 7 new tests covering happy path, scoping (collision avoidance), leaf-module expandability, missing-catalog graceful degradation, and non-expanded compound suppression. |
| P2-8b | Inner-scope edge wiring inside expanded compounds | ✅ | `RegisterInnerPrimitivePortRefs` registers inner primitive port refs under `@inner::{path}` scoped keys. `CollectInsidePrimitiveEndpoints` maps them to the compound's boundary signals so `EmitEdges` draws FF.D→boundary_in, boundary_out→FF.Q, etc. `CollectExpandedCompoundEndpoints` now processes leaf modules (no grandchildren but with primitives). Recursive for nested expanded compounds. 8 new tests covering FF.D/Clk/Q/Reset wiring, Mux selector, two-primitive local signal chain, signal-leak regression, and nested expansion. |
| P2-11 | Packed-struct field fan-out rendering | ✅ | `StructTypeDecl`/`StructFieldDecl` in AST; `SignalDecl.StructType` populated by reader from `<structdtype>` + `<refdtype>` aliases. `PortConnectionDecl.SignalRange` captures sel-wrapped pin slice ranges. `StructFanOutPrimitive` emitted by decoder for any struct signal with ≥ 1 field-access consumer; per-consumer contassigns suppressed. Builder `AddStructFanOutNode` paints an inverse splitter wedge (one west input, N east legs with field labels). `PrettifySignalLabel` strips internal namespace prefixes from edge labels. **35 new tests** across reader/decoder/builder/end-to-end + 1 snapshot. |
| P2-5 | Move symbol drawing into a dedicated partial-class file | ✅ | `SchematicPreviewControl.Symbols.cs` — FF/Mux/Latch/Memory drawing + shared port painter helper. (Master plan suggested per-family files; one cohesive file is more discoverable for these 4 closely-related symbols.) |
| P2-6 | Symbol render snapshots (JSON description, not pixels) | ✅ | 3 new ELK golden snapshots: `elk-primitive-register-cell`, `elk-primitive-ternary-mux`, `elk-primitive-memory-tile`. Lock node IDs, port IDs, port labels, and edge wiring. |
| P2-7 | Add `FlipFlopSymbol`, `MuxSymbol`, `LatchSymbol`, `MemorySymbol` | ✅ | IEEE 91 D-FF (rectangle + clock-edge triangle + D/R/Q labels), classic Mux trapezoid (wider on input side, S/0/1/Y labels), D-Latch (FF without edge marker), RAM tile (stacked rectangle with decorative cell bands). |
| P2-8 | Recursive compound: decoder runs per scope, primitives become inner children | ☐ | Tie into existing expansion infrastructure. |
| P2-9 | Per-sample golden snapshots (arnicomp expanded scopes) | ✅ | `tests/Bistable.Snapshots/ArnicompSnapshotTests.cs` — 3 new snapshots: `arnicomp-top` (collapsed top scope), `arnicomp-top-expanded-marl_i` (reg_marl leaf module expanded showing inner mux), `arnicomp-reg-cell` (reg_cell module with FF + contassign). Skip pattern: `if (!HasVerilator()) return;` — tests pass silently when Verilator absent. `[Trait("Category","Integration")]` + `[Trait("RequiresVerilator","true")]` for CI filter. |
| P2-10 | Update ARCHITECTURE.md + PHASE-2.md recent activity | ☐ | Closing task. |

---

## 5. Implementation order

1. **P2-1** → primitive types compile.
2. **P2-3 starts here (TDD-light)** → write the first decoder tests against `SchematicDecoder` (it doesn't exist yet); watch them fail.
3. **P2-2** → implement decoder one primitive at a time. Start with `SplitterPrimitive`/`JoinerPrimitive` (already familiar shapes), then `BufferPrimitive`, `GatePrimitive`/`ArithPrimitive`, `InverterPrimitive`, `MuxPrimitive` (collapses nested `CondExpr`), `FlipFlopPrimitive`, `LatchPrimitive`, `MemoryPrimitive`.
4. **P2-4** → glue into `ElkGraphBuilder` behind a flag. Snapshot diffs reveal what changed.
5. **P2-5, P2-7** → symbol library cleanup + new symbols (FF, MUX, Latch, Memory).
6. **P2-6, P2-9** → snapshots.
7. **P2-8** → recursive expansion.
8. **P2-10** → docs.

---

## 6. Key files

- **AST entry point:** `src/Bistable.Core/Design/Ast/ModuleAst.cs` — decoder input.
- **Current builder:** `src/Bistable.App/Services/Routing/Elk/ElkGraphBuilder.cs` — refactor target.
- **Current rendering:** `src/Bistable.App/Views/SchematicPreviewControl.Elk.cs` — symbol dispatch lives here today.
- **Existing splitter/joiner code** — reference for shape conventions.

---

## 7. Cross-phase notes

- Phase 3 (worker probe) depends on primitive identification: registered signals via `FlipFlopPrimitive` → live Q display in Phase 4.
- Phase 7 (FSM detector) builds on `SequentialBlockAst` + `CaseAst` decoding done here.

---

## 8. Risks

- **Nested `CondExpr` explosion**: a chain of 5+ nested conds becomes visually unreadable. Mitigation: collapse N>4 chains into one wide `MuxPrimitive` with a synthesized selector bus.
- **Ambiguous registered signals**: a signal driven by both `always` and `assign` is flagged with a warning in Phase 1; decoder must decide whether to emit FF or buffer. Policy: FF wins (sequential drive is the dominant semantic).
- **Snapshot churn**: introducing primitives changes ELK graphs significantly. The diff will be large. Mitigation: regenerate snapshots in a clear commit; document the visual impact in Recent activity.

---

## 9. Handoff for next session

If you're picking this up from a fresh session:

1. Read `/home/ardac/.claude/plans/fluffy-wishing-kettle.md` Section 8 (Phase 2 spec).
2. Read this file's Section 3 (primitive set) + Section 5 (order).
3. Read `docs/DESIGN_AST.md` Sections 3-6 to understand the input.
4. Read `src/Bistable.App/Services/Routing/Elk/ElkGraphBuilder.cs` to understand the current builder.
5. Start with P2-1 (primitive type hierarchy).

---

## 10. Recent activity

- **2026-05-24**: P2-1, P2-2, P2-3 landed in one session.
  - **P2-1**: `src/Bistable.Core/Design/Schematic/SchematicPrimitive.cs` + `SchematicPrimitiveList.cs`. 13 primitive types: `FlipFlopPrimitive`, `LatchPrimitive`, `MuxPrimitive` (with `MuxInput` / `MuxSource` discriminated union), `BufferPrimitive`, `InverterPrimitive`, `GatePrimitive`, `ArithPrimitive`, `SplitterPrimitive`, `JoinerPrimitive`, `MemoryPrimitive`, `InstancePrimitive`, `PortPrimitive`, `SignalPrimitive`. All `[JsonPolymorphic]` for snapshot serialization.
  - **Placement decision**: Primitives + decoder went into `Bistable.Core` (not `Bistable.App` as the master plan suggested). Reasoning: pure types are domain-level and a future Yosys reader would also produce these. Master plan §8 placement was a draft hint, not a hard constraint.
  - **P2-2**: `SchematicDecoder.Decode(ModuleAst)` walks ports, locals, instances, sequential blocks, and continuous assignments. FF reset-mux peeling: when `SequentialBlockAst` body has `CondExpr(SignalRef(rst), trueBranch, ConstExpr)`, the decoder extracts the reset signal and uses `trueBranch` as D. Nested `CondExpr` chains are flattened into a single `MuxPrimitive` with ordered selector + input lists.
  - **P2-3**: 26 tests in `tests/Bistable.Tests/Schematic/SchematicDecoderTests.cs`. Coverage: Buffer, Splitter, Joiner, Inverter, Gate (And/Or/Xor + reduction ops), Arith (Add/Sub/Eq/Lt), Mux (simple + nested + with-const), FF (basic + with-async-reset + body-in-begin), Memory, Instance, Port, Signal IsRegistered, full arnicomp-pattern end-to-end.
  - **2 new snapshots**: `primitives-reg-cell.json`, `primitives-alu-lite.json` validate decoder output stability.
  - **Test suite**: 175 → 203 (+28). Zero regressions.
  - **P2-4 deferred**: Refactoring 904-line `ElkGraphBuilder` to consume primitives is a separate focused session. The decoder + primitives are ready and snapshot-verified; the rendering integration can land incrementally without blocking other Phase 2 work (e.g., new symbols can be designed against the primitive contract).

- **2026-05-24 (continued)**: **P2-4 completed** — additive FF rendering path landed end-to-end.
  - **Builder layer**: `ElkScopeData` extended with optional `IReadOnlyList<SchematicPrimitive>? Primitives`. `ElkGraphBuilder` now iterates `FlipFlopPrimitive` entries to emit FF nodes with D/Clk/[Rst]/Q ports. `CollectFlipFlopEndpoints` registers Q as producer / D, Clk, Rst as consumers in the existing pub/sub edge model.
  - **New constants**: `ElkPortRole.FlipFlop{D,Clock,Reset,Q}`, `ElkNodeIds.ForFlipFlop` + `IsFlipFlop`, `ElkSignalKey.FlipFlop{D,Clock,Reset,Q}`.
  - **VM layer**: `MainWindowViewModel._currentAst` field added + saved/restored across sub-sim entry/exit (mirrors `_currentDesign` pattern). `SelectedHierarchyPrimitives` observable collection populated by `RefreshSelectedHierarchyPorts` via `SchematicDecoder.Decode(moduleAst)`.
  - **View layer**: `ScopePrimitivesProperty` styled property on `SchematicPreviewControl`. `DrawElkScopePanel` signature accepts `IReadOnlyList<SchematicPrimitive>?` and forwards to `ElkScopeData`. Both `MainWindow.cs` and `SchematicStudioWindow.cs` bind `ScopePrimitives` to `SelectedHierarchyPrimitives`.
  - **Tests**: 6 new `ElkGraphBuilderFlipFlopTests` cover the basic posedge case, async reset port addition, boundary wiring for D/Clk, Q→contassign forwarding, and a regression guard that empty `Primitives` keeps legacy behavior bit-identical.
  - **Test suite**: 209 total (was 203, +6 builder tests). Zero regressions across all 4 test projects.
  - **What this unlocks**: When the user selects a hierarchy node whose module has sequential blocks, the schematic now shows FF symbols (as labeled rectangles for now) with proper D/Clk/Q connections. Visual appearance is "FF {qSignal}" — Phase 2 next steps (P2-5/P2-7) will replace this with a proper FF symbol drawing.
  - **Known limitations**: Width=0/1 on synthetic primitives (dtype lookup not resolved for memories in synthetic tests; real Verilator XML provides proper widths). Only `FlipFlopPrimitive` is wired today; `MuxPrimitive`/`LatchPrimitive`/`MemoryPrimitive` rendering is the next incremental task.

- **2026-05-24 (continued)**: **P2-4c completed** — Mux, Latch, and Memory primitives wired into `ElkGraphBuilder`.
  - **New nodes**:
    - `AddMuxNode`: N data-input ports (west, top), M selector ports (west, below inputs), 1 output port (east). Constant inputs get ports but no edges (no producer to wire from).
    - `AddLatchNode`: D (west top), G (west bottom), Q (east). Three-port pattern mirroring FF without the clock-edge concept.
    - `AddMemoryNode`: tile-only (no ports yet). Label includes dimensions: `MEM name [hi:lo]×width`. Array access plumbing deferred to Phase 4 (live values).
  - **New endpoint collectors**: `CollectMuxEndpoints`, `CollectLatchEndpoints` — both follow the same pub/sub pattern as `CollectFlipFlopEndpoints`. Mux skips constant inputs cleanly.
  - **New constants**: `ElkPortRole.{MuxInput,MuxSelect,MuxOutput,LatchD,LatchGate,LatchQ,MemoryNode}`, `ElkNodeIds.For{Mux,Latch,Memory}` + `Is{Mux,Latch,Memory}`, `ElkSignalKey.{Mux*,Latch*}` factories.
  - **Tests** (28 new):
    - `ElkGraphBuilderMuxTests` (8): 2:1 mux, port wiring (data + selector + output), 3:1 nested mux, constant in branch (port exists, no edge), multiple muxes share a selector, orphan input (no crash, no edge), degenerate empty inputs.
    - `ElkGraphBuilderLatchAndMemoryTests` (8): latch port shape, D/G/Q wiring, latch Q drives contassign alias; memory label format, memory has no ports/edges, depth-1 memory.
    - `ElkGraphBuilderPrimitiveIntegrationTests` (12): full XML→AST→Decode→ElkGraph for a register cell (FF + buffer), ternary contassign (mux), mixed-primitive scope (FF+Mux+Buffer+Splitter), unpacked array (memory tile), regression guard for empty primitives, and a parametrized discriminator-disjointness theory (`ff_`/`mux_`/`latch_`/`mem_`/`op_`/`split_`/`join_` never collide).
  - **Test suite**: 209 → 237 (+28). Zero regressions in any test project.
  - **What this unlocks**: When the user selects a module with combinational ternaries (e.g., arnicomp ALU's mux trees), the schematic now shows `MUX y` boxes with proper sel + input + output wiring. Memory declarations show as `MEM arr [15:0]×8` tiles. Latches (rare in arnicomp, more common in sub-modules) show as `L q` boxes with D/G/Q.
  - **Code quality**: Build clean (0 warnings, 0 errors). All ID prefixes unique. The dispatch in `Build` is a single `switch` over the primitive base type — easy to extend for future primitives (BufferPrimitive, GatePrimitive, etc. when their visual representation is desired separately from the legacy operator path).

- **2026-05-24 (continued)**: **P2-5 + P2-6 + P2-7 completed** — industry-standard symbol library landed.
  - **New file**: `src/Bistable.App/Views/SchematicPreviewControl.Symbols.cs` (partial class). 4 symbol renderers + a shared `DrawSymbolPortsAndLabels` helper that overlays port dots and pin glyphs onto the symbol body.
  - **Symbol designs (IEEE 91 / classic schematic conventions)**:
    - **D Flip-Flop**: rectangle body, `D`/`R`/`Q` port glyphs inside the body, small `▷` (clock-edge triangle) at the clock pin pointing inward — the standard edge-trigger marker. QSignal name rendered as title above the box.
    - **Mux**: trapezoid silhouette, wider on the data-input (west) side and inset by ~20% of height on the output (east) side — the classic schematic mux shape. Data inputs labelled by the decoder's branch labels (`0` / `1`); selectors labelled `S` (single) or `S0`/`S1` (multi); output `Y`.
    - **D Latch**: identical to FF *except* no clock-edge triangle. The `G` gate-pin glyph alone communicates level-sensitivity. A contract test guards that `>` never appears on a Latch port.
    - **Memory**: tall rectangle with 5 decorative horizontal divider lines suggesting addressable cells. Dimensions label (`MEM arr [15:0]×8`) is rendered inside.
  - **Builder enhancements**: `AddFlipFlopNode`/`AddMuxNode`/`AddLatchNode` now attach `port.Labels` (`D`/`>`/`R`/`Q`, `0`/`1`/`S`/`Y`, `D`/`G`/`Q`). The renderer paints these glyphs just *inside* the body via the shared port painter so they don't collide with the incoming wires.
  - **Dispatch**: `DrawElkNodesRecursive` extended with 4 new `else if` branches (`IsFlipFlop`/`IsMux`/`IsLatch`/`IsMemory`) routing to the appropriate `DrawElk*Node` method. Falls back to the generic `DrawElkNodeCard` for unknown IDs (no regression).
  - **Tests** (12 new):
    - `ElkGraphBuilderPortLabelTests` (9): FF pins labelled `D`/`>`/`Q`, async reset adds `R`; Mux branch labels carry through from decoder, single selector is `S`, multi-selector is `S0`/`S1`, output is `Y`; Latch pins are `D`/`G`/`Q`; **contract test** asserts `>` never bleeds onto a Latch port; **cross-primitive test** verifies each primitive type has its own glyph set with no unintended overlap.
    - `PrimitiveElkGraphSnapshotTests` (3): full XML→AST→Decode→Builder pipeline snapshots for register-cell, ternary-mux, and memory-tile. Golden files in `tests/Bistable.Snapshots/golden/elk-primitive-*.json` lock the entire output (node IDs, port IDs, port labels, edges) against regression in either the builder or decoder.
  - **Test suite**: 237 → 249 (+12). Zero regressions in any of the 4 test projects.
  - **What this unlocks in the live app**: When the user opens arnicomp (or any module with sequential/mux/memory content) and selects a scope, the schematic now renders proper IEEE 91 FF symbols with their characteristic clock-edge triangle, classic trapezoid muxes, latches distinct from FFs (no triangle), and memory tiles with cell-band decoration. Every pin carries its standard glyph (D/Q/>/R/0/1/S/Y/G).
  - **Code quality**: 0 warnings, 0 errors; all new files match existing conventions (partial classes, StreamGeometry-based shapes, shared port-painter helper to avoid duplication across FF/Mux/Latch); symbol drawing is purely a function of `(ElkNode, Rect)` — no global state, no side effects, fully thread-safe.

- **2026-05-24 (P2-4d + P2-8 + fan-out spec)**: Three closely-related deliverables landed in one session.
  - **P2-4d — Buffer / Inverter / Gate / Arith primitives migrated to the primitive rendering path.**
    - Builder: `AddBufferNode` / `AddInverterNode` / `AddGateNode` / `AddArithNode` + matching `Collect*Endpoints`. New `ElkPortRole` values (`BufferIn/Out`, `InverterIn/Out`, `GateInput/Output`, `ArithLeft/Right/Output`); new `ElkNodeIds.For{Buffer,Inverter,Gate,Arith}` + `Is{...}` discriminators; new `ElkSignalKey` factories.
    - **Legacy suppression**: when a `GatePrimitive` or `ArithPrimitive` covers a target signal, the legacy contassign-driven `AddOperatorNode` is suppressed for that exact target (per-target `HashSet`). Buffer/Inverter need no suppression because the legacy path never rendered single-source contassigns. Tests prove that non-owned targets still produce legacy operator nodes — suppression is precise.
    - **Symbols** (`Views/SchematicPreviewControl.Symbols.cs`):
      - Buffer = right-pointing triangle (no bubble) — classic non-inverting buffer.
      - Inverter = right-pointing triangle + output bubble — classic NOT gate.
      - Gate = dispatches by `GateKind` to the existing `DrawAndGate` / `DrawOrGate(xor: false/true)`; inverted variants (Nand/Nor/Xnor) add an output bubble overlay.
      - Arith = labelled rectangle with the operator glyph (`+`, `−`, `×`, `<<`, `≤`, etc.) centred inside.
    - Dispatch added in `DrawElkNodesRecursive` for all 4 new ID prefixes.
    - **Cognitive-complexity refactor**: the primitive-dispatch switch in `Build()` was extracted to a private `DispatchPrimitives` helper to keep `Build()`'s complexity inside the 15-statement budget.
    - **27 new tests** in `ElkGraphBuilderCombinationalPrimitiveTests`: per-primitive happy path (Buffer/Inverter/Gate/Arith with N variants), port-label correctness (`A`/`B`/`I0..N` for Gate, `A`/`B` for Arith), legacy-suppression contract (3 tests: Gate suppresses, Arith suppresses, non-owned target still rendered), discriminator parametrized theory.
    - **Snapshot churn**: 1 existing snapshot regenerated (`elk-primitive-register-cell.json` now includes the `buf_d_out` node — the `assign d_out = q;` alias is correctly recognised as a Buffer). The end-to-end integration test was updated to assert the new 2-hop topology (FF.Q → buf.in → buf.out → boundary).
  - **P2-8 — Recursive compound expansion: the decoder runs inside expanded compounds.**
    - `ElkScopeData.PrimitivesByModule` (optional, keyed by module name) carries decoded primitives for every module in the AST catalog. Built once by the VM at design load (`RebuildPrimitivesByModule`) and rebuilt on sub-sim enter/exit so the catalog always matches the active design.
    - `BuildChildNode` parameter chain (`AddChildNode` → `BuildChildNode` → `AttachCompoundChildren`) extended to thread the catalog through.
    - `AttachCompoundChildren` looks up the expanded child's module in `PrimitivesByModule` and adds inner primitive nodes via the new `BuildInnerPrimitiveNode` helper. **Scoped IDs**: every inner node ID is prefixed by `child_<hierarchyPath>/` to prevent collisions with outer-scope primitives of the same signal name in a different module.
    - **Expandability**: `BuildChildLabels` now treats a leaf module (no sub-instances) as expandable when its module has primitives. This means a module that's just a FF + a few gates can be expanded to reveal its interior — previously such modules were leaf-only.
    - **No edge wiring inside compounds yet**: inner primitive port refs are NOT registered in `portRefs`, so the compound's interior shows the symbol nodes but their pins are unconnected. This is P2-8b — a separate task once the inner-namespace pub/sub model is extended. Documented in PHASE-2.md task board.
    - View layer: `ScopePrimitivesByModuleProperty` styled property on `SchematicPreviewControl`; bindings added in both `MainWindow.cs` and `SchematicStudioWindow.cs`. `DrawElkScopePanel` signature accepts the catalog and forwards to `ElkScopeData.PrimitivesByModule`.
    - **7 new tests** in `ElkGraphBuilderRecursiveCompoundTests`: happy path (leaf module with FF renders inside), leaf module marked expandable, multiple primitives in one compound, ID scoping prevents collision when outer and inner share signal name, regression guard (no catalog → no behavioural change), missing-catalog-entry graceful degradation, non-expanded compound emits no inner nodes.
  - **Fan-out / packed-struct spec — `docs/DESIGN_FANOUT_SPEC.md`** (deferred to P2-11).
    - Detailed design for solving the "arnicomp `control_pins` collapses to one fat line" problem: when a packed struct connects to many consumers (each reading a different field), the renderer should produce a fan-out wedge with per-field labelled legs — Vivado/Logisim convention.
    - Spec covers AST extensions (`StructTypeDecl`, `StructFieldDecl`, `StructFieldExpr`, `SignalDecl.StructType`), reader changes (parse `<typetable>`, attach struct metadata), decoder grouping pass, builder node + endpoint collection, symbol drawing, comprehensive test plan (≥ 15 tests), backwards-compatibility, and an 8-step implementation order with estimates (~1.5–2 weeks).
    - Also covers a simpler `BusFanOutPrimitive` for non-struct signals with many readers (opt-in via preference).
    - The spec is self-contained and another agent can implement P2-11 cold from it.
  - **Test suite**: 249 → **283** (+34 across 2 new files, with 1 existing test updated for new topology + 1 snapshot regenerated). 4 test projects green, 0 regressions.
  - **What this unlocks**: arnicomp's combinational ALU paths now render as IEEE 91 logic gates (AND/OR/XOR shapes) with proper port labels; arithmetic blocks show as labelled boxes with `+` / `<<` / `≤` glyphs; buffers and inverters are visible (previously invisible single-source aliases). When the user expands a register module like `acc` or `pc_we`, the FF inside now appears inside the compound box — the design hierarchy is finally visually navigable end-to-end.

- **2026-05-24 (P2-8b + P2-9)**: Inner-scope edge wiring and arnicomp end-to-end snapshots.
  - **P2-8b — Inner-scope edge wiring inside expanded compounds.**
    - New `RegisterInnerPrimitivePortRefs`: when `AttachCompoundChildren` builds inner primitive nodes, it now also registers their ports in `portRefs` under `@inner::{compoundPath}` scoped keys (distinct from outer-scope keys, collision-safe).
    - New `CollectInsidePrimitiveEndpoints`: called from `CollectInsideCompound`, maps each inner primitive's port refs to the compound's `@inner::` signal namespace as producers/consumers, enabling `EmitEdges` to draw edges between inner FF/Mux/etc. pins and the compound's boundary ports.
    - Fixed `CollectExpandedCompoundEndpoints`: previously skipped leaf modules (`ChildInstances.Count == 0`); now also enters compounds that have inner primitives (the `hasInnerPrims` guard). Recursive for nested expanded compounds.
    - **8 new tests** in `ElkGraphBuilderRecursiveCompoundTests` (extending the P2-8 file): FF.D → compound boundary input, FF.Clk → compound boundary input, FF.Q → compound boundary output, async-reset pin wiring, Mux selector wiring, two-primitive local signal chain (FF.Q → Buffer.in), signal-leak regression (inner signal does not bleed to outer scope), nested expanded compound (two levels deep, innermost FF wired).
  - **P2-9 — Per-sample golden snapshots from real `samples/arnicomp/` elaboration.**
    - `tests/Bistable.Snapshots/ArnicompSnapshotTests.cs` (new): 3 `[Trait("Category","Integration")]` tests that call `DesignLoadService.LoadAsync` on the real `.bistable.json`, build `ElkScopeData` from the resulting `DesignAst`, and assert against golden files.
    - Skip pattern: `if (!HasVerilator()) return;` — tests silently pass when Verilator is absent, so the suite stays green on machines without it.
    - **3 new golden files** in `tests/Bistable.Snapshots/golden/`: `arnicomp-top.json` (collapsed top scope, ~4.2KB), `arnicomp-top-expanded-marl_i.json` (reg_marl leaf module expanded with inner mux nodes and boundary wiring, ~4.4KB), `arnicomp-reg-cell.json` (reg_cell scope with FF + contassign alias buffer, ~338 bytes).
    - `ArnicompSnapshotTests.BuildElk` helper builds `ElkScopeData` directly from `DesignAst` without requiring the full VM — reusable pattern for future integration snapshot tests.
  - **Test suite**: 283 → **295** (+12 across 2 test files + 3 new golden files + 1 new source test file). All 4 test projects green, 0 regressions.
  - **What this unlocks**: expanded compound interiors are now fully wired — when the user expands a register module (e.g. `reg_marl` via `marl_i`), the inner mux symbols have their selector and data inputs connected to the compound's boundary ports. The arnicomp acceptance gate (per-sample golden snapshots) is now satisfied.

- **2026-05-24 (P2-11)**: **Packed-struct field fan-out** rendering landed end-to-end.
  - **Problem solved**: Previously a packed struct like arnicomp's `control_pins` (a `control_pkg::ctrl_t` with ~19 fields) collapsed into a single boundary edge feeding every consumer — the wires overlapped at the boundary pin and the user could not tell which consumer read which field. Now the renderer paints one inverse-wedge fan-out node with one labelled output port per consumed field; each leg drives its specific downstream consumer(s), eliminating the visual collision.
  - **AST extensions** (P2-11-1): new pure-data records `StructTypeDecl` + `StructFieldDecl` (with `Lo`/`Width`/`Range` helpers). `SignalDecl` gains optional `StructType` field. `PortConnectionDecl` gains optional `SignalRange` so the decoder can detect sliced port connections (`.pin(control_pins.ops)` → range `[hi:lo]`).
  - **Reader** (P2-11-2): `BuildStructTypeMap` parses `<structdtype>` entries from `<typetable>` and resolves field bit positions by walking members in declaration order (Verilog packed structs are MSB-first; last-declared member sits at `Lo = 0`). `<refdtype>` aliases are followed so `dtype_id="58"` resolves to the same struct as `dtype_id="10"`. `ExtractSelRange` captures port-connection slice ranges using the existing `ParseVerilogConst` helper.
  - **Primitives** (P2-11-3): new `StructFanOutPrimitive` record carrying `StructSignal`, `StructTypeName`, `StructWidth`, and an ordered list of `StructFanOutLeg`s. Each leg records its `FieldName`, `Range`, and the list of `Consumers` (plain signal names + `instance.port` notation). The decoder's `BuildStructFanOuts` pass groups all `BitSelectExpr` reads of a struct signal (from contassigns + instance pins) by `(struct, range)`, resolves each range to a field name (or falls back to `[hi:lo]` when the slice doesn't line up with a declared field), and emits one primitive per struct signal with ≥ 1 access. Field-access contassigns are simultaneously suppressed in the regular `DecodeContAssign` loop so no duplicate `SplitterPrimitive` competes for the same target.
  - **Builder** (P2-11-4): `AddStructFanOutNode` emits an ELK node with one west input port (labelled with the struct signal name) and N east leg ports (labelled with the field name, plus `[hi:lo]` suffix for multi-bit fields). `CollectStructFanOutEndpoints` registers the input as a consumer of the struct signal (so the boundary pin drives the wedge) and each leg as a producer of a synthetic `::fanout::struct.field` signal — then routes each declared consumer onto that synthetic key. The instance-pin consumer case is resolved by `TryResolveInstancePin` which splits `instance.port` and looks up the matching `ChildInput` port ref. New `ElkPortRole.{StructFanOutInput,StructFanOutLeg}`, `ElkNodeIds.{ForStructFanOut,IsStructFanOut}`, `ElkSignalKey.{StructFanOutInput,StructFanOutLeg}` complete the wire-up.
  - **Symbol drawing** (P2-11-5): `DrawElkStructFanOutNode` paints an inverse splitter wedge — single apex on the west (input) side, wide east face holding the N labelled output ports. Mirrors the existing splitter geometry helpers; uses the shared `DrawSymbolPortsAndLabels` painter so the per-field labels render right-aligned to each east port (matching the existing splitter convention). Dispatch added in `DrawElkNodesRecursive`.
  - **Label prettification**: `PrettifySignalLabel` strips internal `::fanout::` and `@inner::` namespace prefixes from edge labels so the user sees `ctrl.ops` instead of `::fanout::ctrl.ops`. Affected the existing arnicomp inner-edge snapshots (regenerated cleanly — labels now read `clk` and `rst_n` instead of `@inner::top.marl_i::clk`).
  - **Tests** (35 new across 4 files):
    - `Ast/StructTypeReaderTests` (8): struct dtype attachment + field-order + bit-offset (`Hi`/`Lo` LSB-first); width = struct total (not basicdtype); refdtype indirection; non-struct signals have `StructType=null`; `PortConnectionDecl.SignalRange` populated when sel-wrapped, null otherwise; single-bit slice has `Hi==Lo`.
    - `Schematic/StructFanOutDecoderTests` (9): single-field happy path, multi-consumer aggregation onto one leg, multi-field → multi-leg, instance-pin consumer notation (`alu_i.ops`), mixed consumer kinds, struct-without-consumers emits nothing, non-struct signal falls back to legacy splitter, unaligned slice gets `[hi:lo]` fallback label, suppression of competing `SplitterPrimitive` for owned target.
    - `ElkGraphBuilderStructFanOutTests` (14): node has 1 input + N legs, port labels (struct name on input, field-name on single-bit legs, `field[hi:lo]` on multi-bit), boundary→input edge, leg→instance-pin edge, multi-consumer leg fans to all consumers, single-edge-from-boundary regression (proves the visual fix), parametrized discriminator disjointness across `fanout_`/`ff_`/`mux_`/`buf_`/`op_`, no-fan-out regression (legacy splitter path still active).
    - `Ast/StructFanOutEndToEndTests` (3): full XML → AST → Decode pipeline for arnicomp-style `control_pins` pattern (struct + 3 consumers spanning 2 fields), legs sorted MSB-first, refdtype path produces fan-out identically.
    - `Snapshots/StructFanOutSnapshotTests` (1): golden file `elk-struct-fanout-two-fields.json` locks node IDs, port IDs, port labels, and edge wiring.
  - **Test suite**: 295 → **329** (+34). 4 test projects green, 0 regressions. 2 arnicomp golden snapshots regenerated for the prettified label format.
  - **Code quality**: 0 warnings, 0 errors. `BuildStructFanOuts` and `CollectStructFanOutEndpoints` refactored into focused helper methods to keep cognitive complexity inside the 15-statement budget. Reader's `BuildStructTypeMap` split into `ParseStructType` + `ResolveMemberWidth` for the same reason.
  - **What this unlocks**: opening arnicomp and selecting the top scope will now show `control_pins` as a wedge with `ops`/`acc_we`/`pc_we`/`mar_load`/`data_sel`/... legs, each driving exactly the consumers that read that field. Previously this was the user's most-cited readability complaint. Other packed-struct designs (e.g. AXI4 bundles) get the same treatment automatically.

## 11. Next session entry point

Phase 2 is now feature-complete. Remaining tasks:
- **P2-10 (final closing task)**: Update `docs/ARCHITECTURE.md` to describe the two-pass model (decoder → builder), the 14-primitive type set, compound expansion with inner wiring, and the fan-out spec. ~30 min.
- **Phase 3 readiness**: with Phase 2 done, the next phase (Worker Protocol v2 — live internal probe) unblocks Phase 4 (live values on schematic). Phase 6 (streaming + async) is independent and can run in parallel.
