# RTL Schematic — Visual Issues Backlog

**Opened:** 2026-07-16
**Context:** User reviewed `samples/riscv_single_cycle` after Build → top-level `+`
expand → expanding sub-modules (`u_imem`, `u_dmem`, `u_decoder`, ALU). Top level
renders fine; **defects appear as sub-modules are expanded.**

**Surface:** This is the **RTL schematic** (primitive-expanded), not the
gate-level Yosys viewer. Pipeline: `SchematicDecoder`
(`src/Bistable.Core/Design/Schematic/SchematicDecoder.cs`) → `ElkGraphBuilder`
(`src/Bistable.App/Services/Routing/Elk/ElkGraphBuilder.cs`) → ELK render.

**Design principle (user):** gate-level synthesis is not the schematic's focus,
but the RTL schematic **must present something visually meaningful** — no floating
constants, no half-wired anonymous gates, no unconnected mystery tiles.

---

## Status (2026-07-16) — all five issues closed

| # | Issue | State |
|---|-------|-------|
| 1 | Hide synthetic `__schematic_expr_` names + stop floating constant tie | ✅ done |
| 2 | Prune ConstantTie + dead-output gates | ✅ done |
| 3 | Wire ContAssign ConstantTie (top + expanded scopes) | ✅ done |
| 4 (Stage 1) | Memory tile ports + wiring + distinct RAM symbol | ✅ done |
| 5 | RD-mem label/value-badge overlap | ✅ done |

**Remaining follow-ups (not blocking):**
- **Issue 4 Stage 2** — replace the array-write FF with a `MemoryWritePrimitive`
  so write addr/data/we fold into the tile (full Vivado/Logisim RAM symbol). Plan:
  `~/.claude/plans/functional-shimmying-torvalds.md` §"Stage 2".
- **RTL sample fix (optional)** — `alu_zero` is genuinely dangling in
  `samples/riscv_single_cycle` (see "Not a render bug" below). Not an
  `ElkGraphBuilder` defect.
- **Pending user visual acceptance** on `samples/riscv_single_cycle` (expand
  `u_imem`/`u_dmem`/`u_decoder`/ALU) to confirm the fixes look right end-to-end.

The shared root-cause notes and per-issue detail below are kept as an
implementation record.

---

## Shared root causes (fix these and most symptoms collapse)

1. **`PruneOrphanPrimitives` is too narrow**
   ([ElkGraphBuilder.cs:2696-2715](../src/Bistable.App/Services/Routing/Elk/ElkGraphBuilder.cs#L2696)).
   - `IsPrunablePrimitive` only covers Operator/Gate/Arith/FlipFlop; **ConstantTie
     and Memory are never pruned.**
   - Prune requires the node to have ports AND *all* ports unconnected, so a gate
     with a connected input but dangling output is **kept as a half-wired floater.**

2. **Inner-compound wiring switch omits ConstantTie + Memory**
   ([ElkGraphBuilder.cs:1908](../src/Bistable.App/Services/Routing/Elk/ElkGraphBuilder.cs#L1908)).
   - Expanded sub-scopes route primitives into producer/consumer maps through this
     switch. `ConstantTiePrimitive` and `MemoryPrimitive` are absent, so inside an
     expanded module they get **no edges**. Top scope has separate collectors
     (`CollectTopScopeConstantLiteralEndpoints` etc.), which is why the top level
     looks fine and only children break.

---

## Ordered backlog (close one at a time, in this order)

### [x] Issue 1 — Hide synthetic `__schematic_expr_...` names + stop floating constant tie — DONE 2026-07-16
- Symptom: `32'h0 → __schematic_expr_..._mem_addr_0` text floats mid-canvas
  (photos 1-4).
- Root: constant operands materialize as a `ConstantTiePrimitive` with label
  `"{Literal} → {OutputSignal}"` ([ElkGraphBuilder.cs:249](../src/Bistable.App/Services/Routing/Elk/ElkGraphBuilder.cs#L249));
  synthetic name comes from `CreateSignalName`
  ([SchematicDecoder.cs:1088/1138](../src/Bistable.Core/Design/Schematic/SchematicDecoder.cs#L1088)),
  binary-op operands at [966-967](../src/Bistable.Core/Design/Schematic/SchematicDecoder.cs#L966).
  Label never passes through `PrettifySignalLabel`
  ([ElkGraphBuilder.cs:2730](../src/Bistable.App/Services/Routing/Elk/ElkGraphBuilder.cs#L2730)).
- Fix direction: never show `__schematic_expr_*` to the user (show just the
  literal, e.g. `32'h0`); make the tie connect to its consumer, or render it as a
  compact tie stub attached to the consuming pin instead of a free node.
- **Done:** `AddConstantTieNode` now shows only the literal when the tie output is
  a synthetic expression net (`IsSyntheticExpressionSignal`), otherwise
  `literal → PrettifySignalLabel(name)`
  ([ElkGraphBuilder.cs](../src/Bistable.App/Services/Routing/Elk/ElkGraphBuilder.cs)).
  Test: `ConstantTiePrimitiveTests.Builder_ConstantTie_SyntheticOutputSignal_ShowsLiteralOnly`.
  4 arnicomp goldens regenerated (constant-tie labels only).
  **Still open (moved into Issue 2/later):** the synthetic name still leaks into
  Gate/Arith node titles (`And __schematic_expr_...`) and edge labels — edge
  `labels[0]` doubles as the hover/live-value selection key, so blanking it needs
  care and was deliberately left out of Issue 1. The floating-node problem itself
  (constant tie not connected/pruned) is Issue 2/3.

### [x] Issue 2 — Extend prune to ConstantTie + dead-output gates — DONE 2026-07-16
- Symptom: floating/anonymous gates with one side dangling; stray constants
  (photos 2, 4, 5).
- **Done** ([ElkGraphBuilder.cs](../src/Bistable.App/Services/Routing/Elk/ElkGraphBuilder.cs)):
  - `IsPrunablePrimitive` now includes `ConstantTie` (fully-orphan constants are
    removed). **Memory is intentionally NOT prunable** — an unwired memory still
    signals the module owns storage; that is Issue 4 (visual), not pruning. This
    preserves the existing `MemoryNode_NeverPruned` contract.
  - New `IsDeadIfOutputUnconsumed` (Gate/Arith/Operator, **not** FlipFlop/Constant):
    a combinational node whose output (EAST-side port) is consumed by nobody is
    dead logic and is pruned — this clears the half-wired floaters. Flip-flops are
    kept (real state); constant ties are kept unless fully orphaned (so an Issue-3
    wiring gap doesn't look output-dead and get wrongly deleted).
  - Prune predicate now handles portless prunable nodes and distinguishes
    "any connection" from "connected output" in a single O(ports) pass — no extra
    per-iteration cost in the fixed-point loop.
  - Tests: `OrphanConstantTie_GetsPruned`,
    `HalfWiredOperator_InputsConnectedButOutputUnconsumed_GetsPruned`, plus the
    refactored `ConstantTieLabel` theory. ~10 pre-existing combinational/suppression
    tests were building output-less scopes (unrealistic); each now wires an `Out`
    boundary so the gate/op output is consumed. 4 arnicomp goldens regenerated
    (only dangling `tie___schematic_expr_*` nodes removed; 66 lines removed, 0 added).

### [x] Issue 3 — Wire ContAssign ConstantTie in top + expanded scopes — DONE 2026-07-16
- Symptom: "top fine, children broken" — constants lose edges only when a
  sub-module is expanded.
- **Root found:** ContAssign-derived `ConstantTiePrimitive` (e.g. the `8'h0`
  operand of `a == 8'h0`) was NOT registered as a producer at *either* scope —
  `CollectBufferEndpoints` only iterates `OfType<BufferPrimitive>()`, and the
  inner-compound wiring switch had no `ConstantTie` case. (Direct child-instance
  literal inputs — the other constant path — were already wired.) They only looked
  "top fine" because the top-scope ones were pruned by Issue 2; nothing was ever
  actually connected.
- **Done** ([ElkGraphBuilder.cs](../src/Bistable.App/Services/Routing/Elk/ElkGraphBuilder.cs)):
  - New `CollectConstantTieEndpoints` registers each ContAssign tie as a producer
    on its output net at top scope (mirrors `CollectBufferEndpoints`).
  - New `case ConstantTiePrimitive` in the inner-compound wiring switch registers
    the tie as a scoped producer, so it connects inside expanded modules too.
  - Both reuse the Buffer output key the tie node is built with — O(1) lookup, no
    new port plumbing.
  - Result on real arnicomp: the three previously-floating
    `tie___schematic_expr_*` constants now render **and carry edges** into their
    comparison/arith consumers (e.g. `tie_…mem_wdata….out → arith_…mem_wdata….l`)
    instead of being pruned.
  - Test: `Builder_ContAssignConstantOperand_WiresTieIntoConsumer`; 4 arnicomp
    goldens regenerated (constants + their edges restored).
- **Memory note:** `MemoryPrimitive` inner-scope wiring is deferred to Issue 4 —
  the tile is portless today, so there is nothing to wire until it gets real
  read/write/addr ports.

### [x] Issue 4 (Stage 1) — Wire memory tiles + distinct RAM symbol — DONE 2026-07-16
- Symptom: `MEM mem [31:0]×32` tile had no ports/wires; name overflowed; looked like
  a sub-module (photos 1, 3). RD-mem output label overlapped its value badge.
- **Done** ([ElkGraphBuilder.cs](../src/Bistable.App/Services/Routing/Elk/ElkGraphBuilder.cs),
  [SchematicPreviewControl.Symbols.cs](../src/Bistable.App/Views/SchematicPreviewControl.Symbols.cs)):
  - MEM tile now has a WEST write-in (`.win`) and EAST read-out (`.dout`) port. The
    read-out is the **canonical producer** of the array signal; every RD-mem gained
    a WEST source port (`.src`) consuming it. New roles/keys `MemoryReadOut`/
    `MemoryWriteIn`/`MemoryReadSource`; new `CollectMemoryEndpoints` +
    `CollectMemoryReadEndpoints` source consumer; inner-compound switch handles
    `MemoryPrimitive` + RD source via scoped keys.
  - **Double-producer resolved (Option A):** a memory-write FF (`QSignal ==` array
    name) produces a derived `MemoryWrite(mem)` signal consumed by the tile write-in
    — the array name stays single-producer, so readers connect only to the tile.
    Handled in `CollectFlipFlopEndpoints` + the inner switch via a per-scope
    `MemorySignalNames` set (O(n), built once).
  - Renderer: tile draws its W/D ports, ellipsizes the title (`EllipsizeToWidth`),
    and shows an accent stroke + "RAM" corner tag so it no longer reads as a
    sub-module. RD-mem dropped the redundant third label (memory-name) so it no
    longer overlaps the live-value badge; height 56→64 for the third port.
  - Perf: all O(1) `portRefs` lookups; one O(n) memory-name set per scope; no new
    fixed-point loops. Scales to RV32 register files.
  - Tests: `Memory_HasReadOutAndWriteInPorts`, `Memory_ReadOut_DrivesMemoryReadSource`,
    `MemoryWriteFF_WiresToMemoryWriteIn_AndDoesNotDoubleDriveMem`,
    `ExpandedCompound_WithInnerMemory_WiresReadOutToReadSource`, RD label/port
    assertions; `MemoryNode_NeverPruned` stays green. 1 memory-tile golden
    regenerated (ports added).
- **Stage 2 (deferred):** replace the array-write FF with a `MemoryWritePrimitive`
  so write addr/data/we fold into the tile as ports (Vivado/Logisim RAM symbol).
  See plan `~/.claude/plans/functional-shimmying-torvalds.md` §"Stage 2".

### [x] Issue 5 — Fix RD-mem label/value-badge overlap — DONE 2026-07-16
- Symptom: on `RD mem`, the output net label (`instruction[32b]`) and the live
  value badge (`0x00000000`) overlap.
- Root: two parts. (a) `AddMemoryReadNode` attached a redundant third ElkLabel
  (memory-name) on a 92px node — **removed in Issue 4**. (b) `DrawElkMemoryReadNode`
  also drew a centred in-body value badge via `DrawPrimitiveLiveOutput`, which
  duplicated the value already shown on the outgoing wire's edge live-value label
  and spilled into the net label region.
- **Done** ([SchematicPreviewControl.Symbols.cs](../src/Bistable.App/Views/SchematicPreviewControl.Symbols.cs)):
  dropped the `DrawPrimitiveLiveOutput` call from `DrawElkMemoryReadNode`. The read
  data value is shown once, on the EAST output wire (`DrawEdgeLiveValueLabel`, for
  `bitWidth > 1`); a 1-bit read's value is encoded by wire colour. No more centred
  pill to collide with the net label. Other primitives keep their in-body badge
  (their outputs don't always carry a wide-bit wire label). Verified: build 0/0,
  846 tests + snapshots green. **User to visually confirm on `riscv_single_cycle`.**

---

## Not a render bug — RTL source (track separately)

- **`alu_zero` is genuinely dangling in the sample.** `assign zero = result == 32'h0;`
  ([riscv_single_cycle_top.sv:146](../samples/riscv_single_cycle/riscv_single_cycle_top.sv#L146)),
  bound to `alu_zero` at
  [:315](../samples/riscv_single_cycle/riscv_single_cycle_top.sv#L315), declared at
  [:259](../samples/riscv_single_cycle/riscv_single_cycle_top.sv#L259), but never
  consumed downstream. A single-cycle CPU would normally feed it into branch
  logic. The schematic correctly shows it terminating — fix the RTL sample if
  desired, but this is not an `ElkGraphBuilder` defect.
