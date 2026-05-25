# Phase 2.6 — Construct Completeness (live status)

**Master plan:** `/home/ardac/.claude/plans/fluffy-wishing-kettle.md`
**Phase goal:** Support every SystemVerilog construct that appears in production designs — generate blocks, SystemVerilog interfaces, tri-state buses, bidirectional ports, multi-driver detection. Goal is to render any Linux-class CPU (out-of-order, multi-core) without falling back to generic operator boxes.
**Prerequisite:** Phase 2.5 complete (visual polish done; primitive suppression correct).
**Phase gate:** All eight construct families render with dedicated symbols/treatment; ≥ 40 new tests; arnicomp + at least 3 other real samples have golden snapshots; no construct in current samples falls back to generic box.

---

## 1. Why this phase matters

Phase 2 supports flat combinational + sequential + struct designs. But real chips use:
- **`generate for`** loops that unroll into 8/16/64 instance copies
- **SystemVerilog interfaces** (AXI4, AHB, custom) — bundle 20+ signals across modports
- **Tri-state buses** for external memory / I2C / shared resources
- **Bidirectional `inout`** for DDR data lanes
- **Multi-driver paths** (intentional in OR-tied configurations, accidental in bugs)
- **Parameterized widths** (8-bit ALU vs 64-bit ALU should look different)

Without these, the tool stops being useful past the toy-CPU level. This phase closes that gap.

---

## 2. Task board

Status legend: ☐ todo · 🟡 in progress · ✅ done · ⛔ blocked

| ID | Task | Status | Model | Est. | Notes |
|----|------|--------|-------|------|-------|
| P2.6-1 | VdfgTmp fold (level 3) | ☐ | Opus | 1 wk | CSE undo — substitute tmp expressions into consumers |
| P2.6-2 | Generate block clusters | ☐ | Opus | 1 wk | Detect `[i]` suffix → group `inst[0..N-1]` as one visual cluster |
| P2.6-3 | Tri-state buffer primitive | ☐ | Sonnet | 3 d | New `TriStatePrimitive` for `'z` literals; tri-state symbol (triangle + enable pin) |
| P2.6-4 | Bidirectional `inout` ports | ☐ | Sonnet | 2 d | Double-arrow port glyph; edge endpoints handle both directions |
| P2.6-5 | Multi-driver detection | ☐ | Sonnet | 2 d | Decoder warns when same signal driven by ≥ 2 sources; yellow triangle overlay |
| P2.6-6 | SystemVerilog interface fan-out | ☐ | Opus | 1.5 wk | Like struct fan-out but with modport direction tinting + nested interface support |
| P2.6-7 | Parameterized width labels on primitives | ☐ | Sonnet | 2 d | Show `[32b]`/`[64b]` on Arith/Gate/FF/Mux node titles |
| P2.6-8 | Constant tie wires | ☐ | Sonnet | 2 d | Show `assign x = 8'h00;` as a ground/Vdd-style tie symbol |

---

## 3. Detailed task specs

### P2.6-1 — VdfgTmp fold (Verilator CSE undo)

**Problem:** Verilator's DFG optimization extracts common sub-expressions into `__VdfgTmp_*` signals. Phase 2.5-4 hid these from rendering, but the EXPRESSIONS they substituted are also lost — consumers of `__VdfgTmp_xx` look like they read from nowhere. To fully recover readability, FOLD the tmp's defining expression back into its consumers.

**Approach:**
- New AST pass `TempFolder.Fold(DesignAst) → DesignAst`. Run between reader and decoder.
- For each module:
  1. Collect all `__V*` signals with exactly ONE contassign driver and EXACTLY ONE referencing consumer (multi-consumer tmps are real CSE wins — leave alone).
  2. Substitute the tmp's source expression into the consumer's expression tree.
  3. Remove the original contassign + the tmp signal decl.
- AST stays well-formed; decoder/builder see no tmp at all.

**Files to create:**
- `src/Bistable.Core/Design/Ast/TempFolder.cs` — new pure function
- `tests/Bistable.Tests/Ast/TempFolderTests.cs` — fold scenarios

**Risks:**
- Folding loops (tmp refs tmp refs tmp) — bound iteration with depth limit.
- Multi-bit slices: `__V[hi:lo]` substitution must preserve slice semantics.
- Width mismatches — don't fold if the consumer's width differs from the tmp's.

**Tests:**
- `Fold_SingleConsumerTmp_RemovedFromAst`
- `Fold_MultiConsumerTmp_PreservedAsCseWin`
- `Fold_NestedTmps_FoldsRecursivelyWithBound`
- `Fold_BitSelectOnTmp_PreservesSemantics`
- `Fold_WidthMismatch_LeavesTmpUnfolded`
- `Fold_DoesNotAffectUserSignals` (only `__V*` touched)
- End-to-end: arnicomp `__VdfgTmp_*` signals all folded; resulting equality primitives have non-empty operand sources.

**Model:** Opus — substitution into nested expression trees requires careful case handling.

---

### P2.6-2 — Generate block clusters

**Problem:** `generate for (i=0; i<8; i++) begin: g; my_inst inst (...); end endgenerate` produces 8 separate instances named `g[0].inst`, `g[1].inst`, ..., `g[7].inst`. Currently the schematic shows 8 identical boxes side by side. For a 256-entry register file generate block, this becomes unreadable.

**Approach:**
- New AST node `GenerateBlockAst` in `Bistable.Core.Design.Ast` carrying `BlockName`, `IterationRange`, `IReadOnlyList<InstanceDecl>` (the unrolled instances).
- Reader detects `[N]` suffix pattern in cell names; groups consecutive instances with same module + bracket prefix.
- New primitive `GenerateBlockPrimitive(BlockName, IterationCount, RepresentativeInstance, AllInstances)`.
- Builder emits one ELK node labelled `gen g[0..7]` with port multiplicity. Expanding the generate block (via `+`) reveals the 8 individual instances.

**Files:**
- `src/Bistable.Core/Design/Ast/GenerateBlockAst.cs` — new record
- `src/Bistable.Verilator/VerilatorXmlAstReader.cs` — group generate cells
- `src/Bistable.Core/Design/Schematic/SchematicDecoder.cs` — emit GenerateBlockPrimitive
- `src/Bistable.App/Services/Routing/Elk/ElkGraphBuilder.cs` — `AddGenerateBlockNode`
- `src/Bistable.App/Views/SchematicPreviewControl.Symbols.cs` — `DrawElkGenerateBlockNode` (stacked-rectangle "deck of cards" effect)

**Tests:** ≥ 8 (detection, grouping, single-vs-multi, expand semantics, snapshot).

**Sample:** Add `samples/generate_demo/` with a 4-iteration generate block.

**Model:** Opus — touches AST + reader + decoder + builder + drawing; cross-layer correctness needed.

---

### P2.6-3 — Tri-state buffer primitive

**Problem:** `assign bus = en ? data : 'z;` uses 'z' (high-impedance) literal. Currently this renders as a generic `?:` operator box, losing the tri-state semantics.

**Approach:**
- Decoder recognizes `CondExpr` whose IfFalse (or IfTrue) is a constant with the special 'z' value.
- New `TriStatePrimitive(OutputSignal, DataSignal, EnableSignal, EnableActive)`.
- Symbol: classic tri-state triangle (BufferPrimitive shape) with a perpendicular enable pin coming in from the side.

**Files:**
- `src/Bistable.Core/Design/Ast/ConstExpr.cs` — add `IsHighImpedance` flag (true when value parsed as 'z')
- `src/Bistable.Verilator/VerilatorXmlAstReader.cs` — recognize `'z` const literal
- `src/Bistable.Core/Design/Schematic/SchematicPrimitive.cs` — new TriStatePrimitive
- `src/Bistable.Core/Design/Schematic/SchematicDecoder.cs` — detection pass
- `src/Bistable.App/Services/Routing/Elk/ElkGraphBuilder.cs` — AddTriStateNode
- `src/Bistable.App/Views/SchematicPreviewControl.Symbols.cs` — DrawElkTriStateNode

**Tests:** ≥ 6 (literal parse, primitive decoding, both polarities of enable, multi-driver tri-state bus).

**Sample:** Add tri-state bus to a sample (or use bus_fabric if applicable).

**Model:** Sonnet — straightforward extension of existing primitive pattern.

---

### P2.6-4 — Bidirectional `inout` ports

**Problem:** `inout [7:0] sda;` (I2C data line). Currently SignalDirection.InOut exists in the enum but rendering treats it like a generic port — no visual distinction.

**Approach:**
- Boundary pin glyph: double-arrow pentagon (current input pentagon points right; inout would be a hexagon with both flat sides flattened).
- Edge routing: for `inout` signals, EmitEdges produces TWO edges (one in each direction) OR one bidirectional-marked edge with arrowheads on both ends.

**Files:**
- `src/Bistable.App/Views/SchematicPreviewControl.Rendering.cs` — `BuildInputPentagon`/`BuildOutputPentagon` + add `BuildInoutPentagon`
- `src/Bistable.App/Views/SchematicPreviewControl.Elk.cs` — DrawBoundaryPinGlyph dispatches on direction
- `src/Bistable.App/Services/Routing/Elk/ElkGraphBuilder.cs` — `CollectBoundaryEndpoints` handles InOut

**Tests:** ≥ 4 (inout pentagon shape, inout edge polarity, inout-to-inout connection, inout signal width handling).

**Model:** Sonnet — drawing + small builder change.

---

### P2.6-5 — Multi-driver detection

**Problem:** When a signal has ≥ 2 drivers (both `assign x = ...;` and `always @ ... x <= ...;`, OR two contassigns), it's usually a bug (or OR-tied tri-state bus). User has no visual warning today.

**Approach:**
- Decoder pass: scan all primitives + contassigns, build map `Dictionary<string, List<source>>`. Any signal with > 1 driver gets flagged.
- Rendering: highlight target signal's wire in yellow + add a small warning triangle near the convergence point.
- Optionally: add a "warnings" panel listing all flagged signals with click-to-navigate.

**Files:**
- `src/Bistable.Core/Design/Schematic/SchematicDecoder.cs` — add `MultiDriverDiagnostic` to decode result
- `src/Bistable.Core/Design/Schematic/SchematicPrimitiveList.cs` — add `Warnings` field
- `src/Bistable.App/Services/Routing/Elk/ElkGraphBuilder.cs` — annotate flagged edges
- `src/Bistable.App/Views/SchematicPreviewControl.Elk.cs` — paint warning marker

**Tests:** ≥ 5 (detection of 2-driver case, single-driver no false-positive, tri-state OR-tie expected pattern recognized as valid).

**Model:** Sonnet — pattern detection + small render addition.

---

### P2.6-6 — SystemVerilog interface fan-out

**Problem:** Modern designs (AXI4, AHB, custom AMBA) use SV interfaces to bundle 20+ signals. Each modport defines which signals are inputs/outputs for that view.

**Example:**
```sv
interface axi4_lite_if;
    logic [31:0] awaddr;
    logic        awvalid;
    logic        awready;
    // ... 20+ signals total
    modport master (output awaddr, output awvalid, input awready, ...);
    modport slave  (input  awaddr, input  awvalid, output awready, ...);
endinterface
```

Currently interface signals appear as a fat bundled wire (worse than struct because each modport has its own direction view).

**Approach:**
- Extend struct fan-out machinery to interfaces.
- AST: `InterfaceTypeDecl` + `ModportDecl(name, signalDirections: Dict<string, SignalDirection>)`.
- Reader parses Verilator's `<ifacedtype>` / `<modport>` XML elements.
- Decoder emits `InterfaceFanOutPrimitive` similar to struct fan-out but with **per-leg direction**: input legs paint differently from output legs.
- Renderer color-codes legs (input = green, output = orange) and uses correct arrowhead direction.

**Files:** Multiple AST + Reader + Decoder + Builder + Symbol files.

**Tests:** ≥ 15 (parsing, modport direction recovery, fan-out emission, master vs slave view, edge direction correctness, golden snapshot).

**Sample:** Add `samples/axi4_lite_demo/` with master + slave + 2 signals.

**Model:** Opus — biggest task in the phase, multi-layer.

---

### P2.6-7 — Parameterized width labels

**Problem:** `Add y` is shown identically whether y is 8 bits or 64 bits. Width is critical for understanding datapath sizing.

**Approach:**
- All primitive node titles get a `[Nb]` suffix when the relevant width > 1.
- FlipFlop: `FF q [8b]`
- Mux: `MUX y [32b]`
- Gate: `And y [4b]` (width = output width)
- Arith: `Add y [64b]`
- Buffer: `BUF y [8b]`

**Files:**
- `src/Bistable.App/Services/Routing/Elk/ElkGraphBuilder.cs` — title format
- `src/Bistable.App/Views/SchematicPreviewControl.Symbols.cs` — title rendering can wrap if too long

**Tests:** ≥ 4 (width in title for each primitive type, 1-bit suppression, very-wide design rendering).

**Model:** Sonnet — small touch.

---

### P2.6-8 — Constant tie wires

**Problem:** `assign x = 8'h00;` produces a buffer primitive but the buffer's input is a constant — visually shows up as a buffer with NOTHING connected to its input. Industry convention: a small "0" or "1" or "GND/VDD" tie symbol.

**Approach:**
- Decoder: ContAssignAst where source is `ConstExpr` → new `ConstantTiePrimitive(OutputSignal, Literal, Width)`.
- Symbol: small triangle pointing down (ground/Vdd convention) with the literal value below.
- Suppress the BufferPrimitive that would otherwise be generated for constant-source contassigns.

**Files:**
- `SchematicDecoder.cs` — add case `ContAssignAst { Source: ConstExpr c }` → `ConstantTiePrimitive`
- `SchematicPrimitive.cs` — new primitive type
- `ElkGraphBuilder.cs` — AddConstantTieNode (no input port, just output)
- `SchematicPreviewControl.Symbols.cs` — DrawElkConstantTieNode (downward triangle + literal)

**Tests:** ≥ 5 (literal types: 0, 1, hex constants; multi-bit constants; suppression of buffer fallback).

**Model:** Sonnet.

---

## 4. Implementation order (recommended)

Order for splitting across agents:

1. **P2.6-7** (2 d, Sonnet) — Width labels: zero-risk visual improvement.
2. **P2.6-8** (2 d, Sonnet) — Constant ties: small isolated.
3. **P2.6-3** (3 d, Sonnet) — Tri-state: builds on existing primitive pattern.
4. **P2.6-4** (2 d, Sonnet) — Bidirectional: small visual fix.
5. **P2.6-5** (2 d, Sonnet) — Multi-driver detection.
6. **P2.6-1** (1 wk, Opus) — VdfgTmp fold: cross-cutting AST work.
7. **P2.6-2** (1 wk, Opus) — Generate blocks: cross-cutting + new AST node.
8. **P2.6-6** (1.5 wk, Opus) — Interfaces: biggest, last.

Total ~5 weeks if serial. Parallel across 2 agents: ~3 weeks.

---

## 5. Cross-phase notes

- P2.6-1 (tmp fold) makes P2.6-5 (multi-driver detection) easier — folded AST is simpler to scan.
- P2.6-6 (interfaces) reuses struct fan-out plumbing — refactor opportunities to dedupe.
- P2.6-2 (generate blocks) needs the hierarchy navigator (Phase 2.7's breadcrumb) to be navigable — coordinate.

---

## 6. Recent activity

(empty — phase has not started)
