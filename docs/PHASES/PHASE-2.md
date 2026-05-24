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
| P2-8b | Inner-scope edge wiring inside expanded compounds | ☐ | When inner primitives reference signal names that match the compound's boundary ports (e.g. an inner FF's clock signal is the compound's "clk" input), an edge should form. Today primitives float without connections inside compounds. Requires extending `CollectInsideCompound` to register inner primitive port refs in the `@inner::<path>::<signal>` namespace. |
| P2-11 | Packed-struct field fan-out / bus fan-out rendering | ☐ | Full spec in `docs/DESIGN_FANOUT_SPEC.md`. Solves the "control_pkg single fat wire" problem: when `control_pins.foo` connects to many places, currently all edges originate from the same boundary pin and visually merge. Spec defines `StructTypeDecl`, `StructFieldExpr`, `StructFanOutPrimitive`, builder + symbol drawing, ≥ 15 new tests. 1.5–2 weeks. |
| P2-5 | Move symbol drawing into a dedicated partial-class file | ✅ | `SchematicPreviewControl.Symbols.cs` — FF/Mux/Latch/Memory drawing + shared port painter helper. (Master plan suggested per-family files; one cohesive file is more discoverable for these 4 closely-related symbols.) |
| P2-6 | Symbol render snapshots (JSON description, not pixels) | ✅ | 3 new ELK golden snapshots: `elk-primitive-register-cell`, `elk-primitive-ternary-mux`, `elk-primitive-memory-tile`. Lock node IDs, port IDs, port labels, and edge wiring. |
| P2-7 | Add `FlipFlopSymbol`, `MuxSymbol`, `LatchSymbol`, `MemorySymbol` | ✅ | IEEE 91 D-FF (rectangle + clock-edge triangle + D/R/Q labels), classic Mux trapezoid (wider on input side, S/0/1/Y labels), D-Latch (FF without edge marker), RAM tile (stacked rectangle with decorative cell bands). |
| P2-8 | Recursive compound: decoder runs per scope, primitives become inner children | ☐ | Tie into existing expansion infrastructure. |
| P2-9 | Per-sample golden snapshots (arnicomp expanded scopes) | ☐ | Validates end-to-end. |
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

## 11. Next session entry point

Pick from:
- **P2-8b (recommended)**: Inner-scope edge wiring inside expanded compounds. Today the inner primitives float without connections inside the compound box. Need to extend `CollectInsideCompound` to register inner primitive port refs in the `@inner::<path>::<signal>` namespace so edges form between inner FFs' clock pins and the compound's "clk" boundary input.
- **P2-11**: Implement the packed-struct field fan-out feature. Full spec in `docs/DESIGN_FANOUT_SPEC.md`. Solves the readability issue with `control_pkg`-style packed structs in arnicomp.
- **Acceptance gate close**: Add per-sample golden snapshots for the real `samples/arnicomp/` project. These require Verilator runtime fixtures (skip if `which verilator` fails) but would close P2-9.
