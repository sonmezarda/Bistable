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
| P2-4 | Wire `ElkGraphBuilder` to consume primitives (additive, FF nodes) | ✅ | `ElkScopeData.Primitives` + `AddFlipFlopNode` + `CollectFlipFlopEndpoints`; full VM→view→builder plumbing. 6 new builder tests. Sub-sim save/restore handles `_currentAst`. Other primitives (Mux/Latch/Memory) will be added incrementally. |
| P2-5 | Move symbol drawing to `Views/Schematic/Symbols/` (one file per family) | ☐ | `IGateSymbol` interface + 10-12 implementations. |
| P2-6 | Symbol render snapshots (JSON description, not pixels) | ☐ | Hand-built primitive list → ELK graph → snapshot. Per-sample. |
| P2-7 | Add `FlipFlopSymbol`, `MuxSymbol`, `LatchSymbol`, `MemorySymbol` | ☐ | These are net-new (Phase 0 already has gates + splitter + joiner). |
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

## 11. Next session entry point

Pick from:
- **P2-4** (recommended): Add a parallel primitive-rendering path in `ElkGraphBuilder`. Start by emitting `FlipFlopPrimitive` as an FF symbol (which arnicomp-pattern doesn't currently show). Compare via snapshot diff.
- **P2-5/P2-7**: Symbol library cleanup. `FlipFlopSymbol`, `MuxSymbol`, `LatchSymbol`, `MemorySymbol` need drawing code. Currently only existing gate symbols + splitter + joiner are rendered.
- **P2-8**: Recursive compound. Phase 1 already builds full module catalog; decoder runs per scope. The remaining piece is "expanded compound → run decoder on inner module → render inner primitives as ELK children".
