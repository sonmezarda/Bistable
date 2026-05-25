# Phase 2.5 — Static Schematic Polish (live status)

**Master plan:** `/home/ardac/.claude/plans/fluffy-wishing-kettle.md`
**Phase goal:** Address the 7 visual issues found during user review of arnicomp rendering. These are all *quick wins* — small focused changes per task, each producing a noticeable visual improvement. No new constructs, no big refactors.
**Prerequisite:** Phase 2 complete (static schematic infrastructure in place).
**Phase gate:** All 7 user-reported visual issues resolved; ≥ 15 new tests; arnicomp golden snapshots regenerated cleanly; zero functional regression.

---

## 1. Why this phase matters

Phase 2 produced a feature-complete static schematic. User review of arnicomp surfaced 7 specific visual issues that erode professional perception. Each is small but compound — together they make the difference between "research prototype" and "production tool". Fixing them now (before adding Phase 3 live values) avoids polishing on top of broken visuals.

---

## 2. Task board

Status legend: ☐ todo · 🟡 in progress · ✅ done · ⛔ blocked

| ID | Task | Status | Model | Est. | Notes |
|----|------|--------|-------|------|-------|
| P2.5-1 | Output boundary pin alignment (Issue 1) | ✅ | Sonnet | 30 min | Extracted `ComputeBoundaryPinLayout` helper, mirrored output to match input; 11 tests |
| P2.5-2 | Sub-instance title overlap (Issue 2) | ✅ | Sonnet | 30 min | `ModuleHeaderHeight` 36 → 48, title y+4 → y+8, title ellipsize + port label ellipsize; 4 tests |
| P2.5-3 | Legacy primitive suppression (Issue 5) | ✅ | Sonnet | 30 min | Extended `primitiveOwnedTargets` to cover Mux/Buffer/Inverter. Joiner deliberately excluded — legacy is the only joiner renderer. 6 tests |
| P2.5-4 | VdfgTmp filter — level 1 (Issue 7) | ✅ | Sonnet | 1 h | `IsVerilatorInternalSignal` helper filters at decoder + builder layers (signals + contassigns + sequential + splitters); 17 tests |
| P2.5-5 | Inner cluster node dispatch (Issue 6) | ✅ | Opus | 3 h | Inner IDs now `ff_<scope>__<sig>` (prefix-first) so `IsFlipFlop`/`IsMux`/… StartsWith dispatch fires. Refactored `Add{FlipFlop,Mux,Latch,Memory,Buffer,Inverter,Gate,Arith}Node` to take `(target, nodeIdOverride, portRefKeyPrefix)`; deleted obsolete `BuildInnerPrimitiveNode`/`MakeInnerNode`/`RegisterInnerPrimitivePortRefs`. Compound min-size grows with inner-content count. 8 new tests. |
| P2.5-6 | Mux nested-cond label clarity (Issue 4) | ✅ | Sonnet+Opus | 3 h | Decoder produces semantic labels: 2-input mux keeps "1"/"0"; chained mux uses bit-aware branch labels (e.g. "ctrl[2]"/"ctrl[1]"/"ctrl[0]"/"else"). Orphan sources → `MuxConstantSource("X")` with `·X` label suffix. Constant sources → `·<value>` suffix. Selector port label = bit-aware display name; `SelectSignals` kept BARE for wire endpoint resolution (caught + fixed bug where readable selector labels broke wire-up). 14 new tests + connectivity audit confirms zero unlabelled empty ports. |
| P2.5-7 | Mux selector port on south side (Issue 3) — *optional* | ☐ | Sonnet | 1 h | Layout change to `PortSideSouth` for selectors; verify ELK crossing count doesn't regress |

**Acceptance criteria** for each task is defined in its detailed section below.

---

## 3. Detailed task specs (per-agent prompts)

Each task is sized for a single fresh-agent session. The "Files to read first" + "Files to modify" + "Test additions" tuples are self-contained.

### P2.5-1 — Output boundary pin alignment

**Problem:** On the top-level module symbol, OUTPUT pins render as `[module] → wire → label → ......gap...... → value-box`. Inputs render as `[label] [value-box] → wire → [module]`. The asymmetry breaks the eye-line: output wires end visually disconnected from the value box.

**Files to read first:**
- `src/Bistable.App/Views/SchematicPreviewControl.Rendering.cs` lines 106–163 (`DrawPins`)

**Fix:**
- In the `leftSide=false` branch (line 147+), change `badgeX` to sit RIGHT NEXT to `pinEndX` (6px gap, mirroring inputs).
- Move the label to the RIGHT of the badge (`labelX = badge.Right + 10`).
- Result: `[module] → wire → [value-box] [label]`.

**Tests to add** in `tests/Bistable.UiTests/`:
- `BoundaryPinLayout_Output_ValueBoxAdjacentToWire` — render fixture, assert badge x-position is within ±10px of pin end.
- `BoundaryPinLayout_InputAndOutput_Mirror` — assert symmetric distances.

**Acceptance:**
- Wire end visually touches the value box on output pins.
- Test count ≥ 2 new.
- No regression in existing UI tests.

---

### P2.5-2 — Sub-instance title overlap

**Problem:** Sub-instance child boxes (e.g. `jump_decoder`) have a header height of 36px. With a 14pt title and the first port label sometimes spanning 100+px wide (`jmp_cond[3b]`), the title and the port collide vertically.

**Files to read first:**
- `src/Bistable.App/Services/Routing/Elk/ElkGraphBuilder.cs` lines 11–25 (constants), 1015–1030 (BuildChildNode height calc)
- `src/Bistable.App/Views/SchematicPreviewControl.Elk.cs` — search for `DrawElkNodeCard` or compound child rendering

**Fix:**
- Bump `ModuleHeaderHeight` from 36 to 48.
- In the child-node renderer, add 4–6px more top padding to the title baseline.
- Ensure port labels longer than (child.Width - 24px) are ellipsized via the existing `Ellipsize` helper.

**Tests:**
- `BuildChildNode_HeaderHeight_AccommodatesTitleAndFirstPortRow` — synthetic child with long port label, assert `node.Height >= ModuleHeaderHeight + portRows * PortRowHeight`.
- `LongPortLabel_GetsEllipsized` — port label > node.Width truncated with `…`.

**Acceptance:**
- Title never overlaps first port row.
- Long port labels truncated, not clipped/wrapped.

---

### P2.5-3 — Legacy primitive suppression for Mux/Buffer/Inverter/Joiner

**Problem:** When a `CondExpr` contassign is decoded into a `MuxPrimitive`, the LEGACY `AddOperatorNode` path STILL runs (because suppression only covers `GatePrimitive` and `ArithPrimitive`). Result: every mux shows a duplicate `?:` operator box next to its proper mux trapezoid.

**Files to read first:**
- `src/Bistable.App/Services/Routing/Elk/ElkGraphBuilder.cs` lines 40–80 (Build → primitiveOwnedTargets)

**Fix:** Extend the suppression set to cover Mux, Buffer, Inverter, Joiner, StructFanOut (defensive):
```csharp
.Select(static p => p switch
{
    GatePrimitive g              => g.OutputSignal,
    ArithPrimitive a             => a.OutputSignal,
    MuxPrimitive mux             => mux.OutputSignal,
    BufferPrimitive buf          => buf.OutputSignal,
    InverterPrimitive inv        => inv.OutputSignal,
    JoinerPrimitive join         => join.OutputSignal,
    _ => null
})
```

**Tests** in `tests/Bistable.Tests/`:
- `MuxPrimitive_SuppressesLegacyCondOperator` — pass both a MuxPrimitive and a `DesignContAssign` for the same target with `OperatorSymbol="?:"`; assert no `op_*` node.
- Repeat for `BufferPrimitive` (legacy single-source wire) → no op node.
- `InverterPrimitive_SuppressesLegacyUnaryNot`.
- `JoinerPrimitive_SuppressesLegacyConcat`.

**Acceptance:**
- arnicomp snapshot regenerates with FEWER nodes (no `?:` duplicates).
- No two nodes ever target the same signal.

---

### P2.5-4 — VdfgTmp internal name filtering (level 1)

**Problem:** Verilator's DFG-based common sub-expression elimination produces signals like `__VdfgTmp_h1814ef32__0` and `__Vlvbound_h1234__1`. These appear as operator nodes with garbage names + sometimes-empty ports. They're internal optimizer artifacts and shouldn't render.

**Files to read first:**
- `src/Bistable.Core/Design/Schematic/SchematicDecoder.cs` — Decode method
- `src/Bistable.App/Services/Routing/Elk/ElkGraphBuilder.cs` — Build method, contassign loops

**Fix (level 1 — hide):**
- Add helper `IsVerilatorInternalSignal(string name) => name.StartsWith("__V", StringComparison.Ordinal)`.
- In `SchematicDecoder.Decode`, skip emitting primitives whose target or source name matches.
- In `ElkGraphBuilder.Build`, skip contassigns whose target name matches.
- Keep the signals in the AST (other tools may want them), just filter at the rendering layer.

**Tests:**
- `VerilatorInternalSignals_AreFilteredFromPrimitives` — module with `__VdfgTmp_xx` contassign → no primitive emitted.
- `VerilatorInternalSignals_AreFilteredFromBuilder` — pass `DesignContAssign` with `__V*` target → no node.
- `NonInternalSignals_StillRender` — defensive guard.

**Acceptance:**
- arnicomp snapshot loses the `__VdfgTmp_*` operator nodes.
- Test count ≥ 3 new.

**Note for future:** Level-3 fix (tmp folding — substituting the tmp's expression into its consumers) is **deferred to P2.6-1**. Level 1 just hides them.

---

### P2.5-5 — Inner cluster node dispatch + sizing

**Problem (Issue 6):** When user expands a compound (e.g. `reg_d`), the inner FF/Mux nodes appear as TINY generic boxes with no proper symbol rendering, and the wires don't visually connect. Root cause: inner primitive node IDs use `child_<path>/ff_q` format. The dispatch in `DrawElkNodesRecursive` checks `nodeId.StartsWith("ff_")` etc. — but the `child_<path>/` prefix breaks the check, so the proper `DrawElkFlipFlopNode` is never called. Falls back to `DrawElkNodeCard` (generic small box).

**Files to read first:**
- `src/Bistable.App/Services/Routing/Elk/ElkGraphBuilder.cs` lines 329–500 (`RegisterInnerPrimitivePortRefs`, `BuildInnerPrimitiveNode`, `MakeInnerNode`)
- `src/Bistable.App/Services/Routing/Elk/ElkGraphBuilder.cs` lines 1830–1870 (`ElkNodeIds.For*` + `Is*`)
- `src/Bistable.App/Views/SchematicPreviewControl.Elk.cs` lines 100–160 (`DrawElkNodesRecursive` dispatch)

**Fix strategy:**

Option A (recommended): change the inner-primitive ID format so the prefix stays at the start.
- Current: `child_top_reg_d/ff_reg_q`
- New: `ff_reg_d__reg_q` (sanitize hierarchy path with `__` separator)
- Add `ElkNodeIds.ForInnerPrimitive(scopePath, kind, signal)` helper that prepends the right prefix.
- Update `BuildInnerPrimitiveNode`, `RegisterInnerPrimitivePortRefs`, `CollectInsidePrimitiveEndpoints` to use the new ID format.

Option B (fallback if A is too invasive): change the dispatch checks to use `Contains("/ff_")` etc. — more fragile but smaller diff.

**Recommendation:** Option A.

**Additional sub-fix:** Inner primitives currently use `MakeInnerNode` which is a simplified version. Replace with calls to the existing `AddFlipFlopNode`-style builders so port labels (`D`/`Q`/`>`/`R`) and proper symbol drawing match outer-scope quality. This means refactoring those Add* methods to take an optional ID-prefix argument.

**Sizing fix:** Compound `parent.Width`/`Height` should grow based on inner primitive content. Compute `requiredInnerSize` and bump compound min-size.

**Tests** in `tests/Bistable.Tests/ElkGraphBuilderRecursiveCompoundTests.cs`:
- `InnerFlipFlop_GetsProperSymbolDispatch` — after building, the inner FF node's ID matches `IsFlipFlop`.
- `InnerMux_PortLabels_MatchOuterMux` — inner mux port labels include `0/1/S/Y`.
- `ExpandedCompound_MinimumSize_FitsInnerPrimitives` — assert parent.Width >= sum of inner widths + padding.

**Acceptance:**
- Expanding a compound shows inner FF/Mux/etc with **proper symbols** (clock triangle, trapezoid, pin labels).
- Wires between inner primitives + to compound boundary visually connect.
- arnicomp `marl_i` snapshot regenerates with inner nodes correctly dispatched.

**Model recommendation:** Opus — the ID format change touches 4 files and requires care with all the helper builders. Sonnet doable but risk of missing edge cases.

---

### P2.5-6 — Mux nested-cond label clarity

**Problem (Issue 4):** Nested `CondExpr` like `s2 ? a : s1 ? b : s0 ? c : d` decodes to a 4-input mux with selector labels `S2/S1/S0`. But these labels suggest bits of a single selector signal, which they're NOT (they're 3 separate 1-bit signals). Also, some input ports show empty (no wire) because `ExpressionToSignalName` returns null for complex sub-expressions and the resulting `MuxSignalSource("?")` has no producer.

**Files to read first:**
- `src/Bistable.Core/Design/Schematic/SchematicDecoder.cs` lines 170–235 (`DecodeMux`, `FlattenCondChain`, `ToMuxSource`)
- `src/Bistable.App/Services/Routing/Elk/ElkGraphBuilder.cs` lines 198–235 (`AddMuxNode` port labels)

**Fix:**

1. **Selector label semantics**: For chained-ternary muxes (each branch has its own selector), use priority labels:
   - 2-input mux (1 selector): `S` (current behavior — keep)
   - N-input chain: `if-1`, `elif-1`, ..., `else` for INPUTS; `c1`, `c2`, ..., `cN-1` for SELECTORS. Indicates priority encoder semantics.
   - Alternatively, label inputs by their selector NAME: e.g., if `s2 ? a : ...`, label that input port `s2=1` and the final else `default`.

2. **Orphan input fix**: `ToMuxSource` for unrecognized expressions should return `MuxConstantSource("X", width)` instead of `MuxSignalSource("?")`. The renderer then shows "X" (don't-care) instead of an empty port.

3. **8-way mux detection**: Add a separate decoder pass that recognizes `case` statements with a single state-var subject — these are TRUE 1-of-N muxes (not priority chains). Different visual treatment: single labelled multi-bit selector + N inputs with case-value labels.

**Tests:**
- `Mux_ThreeWayPriorityChain_LabelsIndicatePriority` — verify input labels are `if-1`/`elif-1`/`else` (or equivalent).
- `Mux_UnrecognizedSubExpression_ResolvesToConstantX` — `s ? (a & b) : c` → input 1 = `MuxConstantSource("X", w)`.
- `Mux_CaseStatement_DecodesAsTrueNwayMux` (depends on CaseAst support in decoder — may be a follow-up).

**Acceptance:**
- No empty mux ports.
- Selector labels accurately reflect semantics (priority chain vs single-bit-bus).

**Decision deferred to implementer:** Whether to handle CaseAst decoding here or in P2.6. If complex, split.

---

### P2.5-7 — Mux selector port on south side *(optional / decision needed)*

**Problem (Issue 3):** Logisim / industry convention places mux select pins on the BOTTOM (south) of the trapezoid, not the west side. Current implementation uses west for all (data + selectors).

**Files to read first:**
- `src/Bistable.App/Services/Routing/Elk/ElkGraphBuilder.cs` lines 198–235 (`AddMuxNode`)
- `src/Bistable.App/Views/SchematicPreviewControl.Symbols.cs` — `DrawElkMuxNode` (trapezoid geometry)

**Fix:**
- Change selector port `LayoutOptions` from `PortSideWest` to `PortSideSouth`.
- Adjust trapezoid Y-extent to leave room for selector pins on the bottom edge.
- Update the symbol drawer to anchor selector labels on the south side (below the trapezoid body).

**Tests:**
- `Mux_Selector_PortSide_IsSouth` — assert layoutOptions has `elk.port.side = SOUTH`.
- Regenerate ELK snapshots and verify no INCREASE in edge crossings.

**Risk:** ELK layered algorithm sometimes increases crossings when ports straddle multiple sides. If snapshot regeneration shows worse crossings, revert and document as "deferred to a future ELK config tune".

**Status:** OPTIONAL — only do if ELK behaves well. Otherwise leave for Phase 2.7 routing-polish.

---

## 4. Implementation order

Recommended order (for a single agent or split across agents):

1. **P2.5-3** (5 min) — One-line fix, biggest visual impact (kills duplicate `?:` boxes).
2. **P2.5-4** (1 h) — Filters Verilator internal noise.
3. **P2.5-1** (30 min) — Output pin alignment.
4. **P2.5-2** (30 min) — Title overlap.
5. **P2.5-6** (2 h) — Mux label cleanup.
6. **P2.5-5** (3 h) — Inner cluster dispatch (biggest, leave last).
7. **P2.5-7** (1 h) — South-side selectors (optional).

Total: ~8 h focused work. Splittable across 2–3 sessions.

---

## 5. Cross-phase notes

- All 7 fixes touch existing snapshot files (`arnicomp-*.json`, `elk-primitive-*.json`). Regenerate as part of each task.
- The Mux label changes (P2.5-6) may affect Phase 3 probe display: Mux primitive's structure becomes the canonical "what does the user see" — Phase 4 live-value overlay will read from it.
- Inner cluster dispatch (P2.5-5) is **prerequisite** for Phase 4's live-values-on-compound display.

---

## 6. Decisions log

- 2026-05-24: Phase 2.5 split from Phase 2 closing tasks. Static schematic quality fixes consolidated here so Phase 2 can be marked "feature-complete" and Phase 3 (Worker Protocol v2) can start in parallel without polish blocking it.

---

## 7. Recent activity

- **2026-05-24**: **P2.5-3, P2.5-1, P2.5-2, P2.5-4 completed** in single session (~3 hours).
  - **P2.5-3** (`ElkGraphBuilder.cs`): `primitiveOwnedTargets` extended to suppress legacy operator nodes for `Mux`/`Buffer`/`Inverter` (in addition to existing `Gate`/`Arith`). **Joiner deliberately excluded** because the builder's primitive switch never renders `JoinerPrimitive` — legacy `AddJoinerNode` is the canonical concat renderer. 6 tests in `ElkGraphBuilderPrimitiveSuppressionTests.cs`.
  - **P2.5-1** (`SchematicPreviewControl.Rendering.cs`): extracted `ComputeBoundaryPinLayout` as a public static helper returning a `BoundaryPinLayout` record (Badge, LabelX, WireStart, WireEnd, Hit). Output side now mirrors input exactly — badge 6px past wire end, label 10px past badge. Eliminated the 64px gap that made output wires look disconnected. 11 tests in `BoundaryPinLayoutTests.cs` covering symmetry, anchoring, hit-rect coverage, long-label handling.
  - **P2.5-2** (`ElkGraphBuilder.cs` + `SchematicPreviewControl.Elk.cs`): `ModuleHeaderHeight` bumped 36 → 48 with a comment marking the change. Title `y+4` → `y+8` for additional baseline clearance. Port label rendering now ellipsizes when label width > half of node width minus margin. Title itself ellipsizes when node is narrow. 4 tests in `ChildNodeHeaderHeightTests.cs`.
  - **P2.5-4** (`SchematicDecoder.cs` + `ElkGraphBuilder.cs`): `IsVerilatorInternalSignal(name)` matches any `name.StartsWith("__V")` — covers `__VdfgTmp_*`, `__Vlvbound_*`, `__Vfunc_*`, etc. Filtered at decoder (signals, memories, sequential blocks, contassigns) AND at builder (legacy operator nodes, legacy splitter loop on both target and source). `IsPrimitiveOnInternalSignal` discriminator handles all 10 primitive types. 17 tests in `VerilatorInternalSignalFilterTests.cs` covering precise pattern matching, each decoder/builder layer, and user-signal pass-through guard.
  - **Snapshot impact** (all 5 affected snapshots regenerated cleanly):
    - `arnicomp-top.json`: **2148 → 860 lines** (60% shrink). All `?:` operator boxes gone (Mux suppression), all `__VdfgTmp_*` operator nodes gone, header height bump applied. Final node mix: **0 legacy `op_` nodes, 29 `mux_` primitives**, proper FFs, properly-labelled fan-out.
    - `arnicomp-top-expanded-marl_i.json`: 2146 → 864 lines (same fixes applied to expanded compound).
    - `arnicomp-reg-cell.json`: 89 → 80 lines (minor: header bump).
    - `elk-primitive-ternary-mux.json`: 102 → 50 lines (Mux suppression killed the duplicate `?:` operator box that previously sat next to the mux primitive — proves the fix).
    - `synthetic-concat-and-splitter.json`: 2-line touch (header bump propagated).
  - **Test suite**: 332 → **370** (+38 across 4 new test files). All 4 test projects green, 0 regressions.
  - **Code quality**: 0 warnings, 0 errors. Linter-driven cleanups: XML doc comments instead of multi-line `//` (linter flagged as commented code), removed unused locals.
  - **Visual impact**: of the 7 user-reported issues, 4 are now fully resolved:
    - ❶ Output pin alignment ✅
    - ❷ Title overlap ✅
    - ❺ `?:` boxes everywhere ✅ (this was the biggest visual win — duplicate operator nodes for every mux/buffer/inverter gone)
    - ❼ `__VdfgTmp_*` equality boxes ✅
  - **Remaining in P2.5**: ❹ (mux label clarity — P2.5-6), ❻ (inner cluster dispatch — P2.5-5), ❸ (optional south selectors — P2.5-7).

- **2026-05-25**: **P2.5-6 completed** — mux label clarity (Issue ❹) end-to-end. ~3 hours including the wire-up bug fix discovered during careful connectivity audit.
  - **Decoder** (`SchematicDecoder.cs`):
    - `DecodeMux` rewritten with iterative chain walk (replaces the previous recursive `FlattenCondChain` + depth-counting hack). For 2-input ternary, keeps clear "1"/"0" branch labels; for chained ternaries, branch labels are the bit-aware selector display name ("ctrl[2]" / "ctrl[1]" / "ctrl[0]") plus "else" for the default. Communicates priority-encoder semantics without lying about a non-existent multi-bit selector.
    - `ToMuxSource` extended: complex sub-expressions (BinaryExpr, ConcatExpr, etc.) → `MuxConstantSource("X", 1)` don't-care. Also promotes `__V*` internal-tmp signal references to `X` since P2.5-4 hides their drivers — without this promotion the mux input would silently lose its wire.
    - Branch labels get a `·<value>` suffix when the source is a constant or `·X` for don't-care. **Contract**: every unconnected mux input port now carries a label suffix explaining why it's empty.
    - New helper `ExpressionToReadableLabel` returns the bit-aware variant (`"ctrl[3:2]"`) used only for DISPLAY. The wire-up name (`"ctrl"`) is preserved via the existing `ExpressionToSignalName`.
  - **Primitive** (`SchematicPrimitive.cs`): `MuxPrimitive` gains optional `SelectorLabels` field. `SelectSignals` stays as the BARE wire-up name for endpoint resolution; `SelectorLabels` carries the bit-aware display variant. **Bug** caught during connectivity audit: an initial single-list design corrupted wire endpoints — selectors named `"control_pins[8]"` had no producer match (only `"control_pins"` was a producer), leaving 4 selector ports + 3 input ports orphaned. Split fixed all 7 orphans.
  - **Builder** (`ElkGraphBuilder.cs`): `AddMuxNode` uses `SelectorLabels[i]` for port glyphs (falls back to `SelectSignals[i]` when null). Empty selector name → `"S{i}"` defensive fallback.
  - **Tests** (14 new in `MuxLabelClarityTests.cs`, 3 existing updated):
    - Decoder happy paths: 2-input "1"/"0", chained selector-name+"else", select-signals list in declaration order.
    - Orphan handling: BinaryExpr → constant X, ConcatExpr → constant X, BitSelectExpr → preserved as signal (reduce works), `__V*` tmp reference → constant X (promotion).
    - Constant branch: literal value flows to MuxConstantSource AND appears as `·<value>` suffix in label.
    - Wire-name vs display-label separation: `SelectSignals` = bare "ctrl", `SelectorLabels` = "ctrl[2]" / "ctrl[1]" / "ctrl[0]".
    - Builder uses SelectorLabels for port glyph.
    - **Contract test** `UnconnectedMuxPort_AlwaysHasLabelSuffix_NeverAmbiguous`: 3 orthogonal cases (orphan, tmp, constant) all produce labelled empty ports.
    - Existing tests updated: `Mux_SingleSelector_LabeledWithSignalName`, `Mux_MultipleSelectors_LabeledWithEachSignalName`, `EachPrimitive_PortLabels_DoNotOverlapAcrossTypes`.
  - **Connectivity audit** (Python-driven on regenerated arnicomp-top snapshot):
    - Before: 4 unconnected selector ports + 3 unlabelled empty input ports.
    - After: **0 unconnected selectors**, **0 unlabelled empty inputs**. 6 empty input ports remain — ALL labelled with `·X` (don't-care) or `·<value>` (constant) suffix. Zero ambiguous ports.
  - **Test suite**: 370 → **393** (+23). All 4 test projects green, zero regressions. Snapshots regenerated for arnicomp-top, arnicomp-top-expanded-marl_i, elk-primitive-ternary-mux, synthetic-concat-and-splitter.
  - **Visual impact**: every mux selector now shows its actual signal name (with bit detail when applicable); every empty input port shows WHY it's empty (`·X` / `·0` / `·1`). Issue ❹ closed.

- **Phase 2.5 status: 6/7 tasks complete.** Only P2.5-7 (optional south-side selectors) remains — explicitly marked optional in original spec.

- **2026-05-25**: **P2.5-5 completed** (~2 hours, Opus).
  - **Root cause**: inner primitive node IDs used `child_<scope>/ff_<sig>` format. The drawing dispatch in `DrawElkNodesRecursive` ([SchematicPreviewControl.Elk.cs:140-175](src/Bistable.App/Views/SchematicPreviewControl.Elk.cs#L140)) checks `ElkNodeIds.IsFlipFlop(node.Id)` which is `StartsWith("ff_", …)`. The `child_<scope>/` prefix broke the check, so inner primitives fell through to `DrawElkNodeCard` (generic small box) instead of `DrawElkFlipFlopNode` / `DrawElkMuxNode` / etc. Additionally, the inner port IDs were `.in.<i>` (generic), so even when dispatched the FF symbol drawer's `port.Id.EndsWith(".clk")` clock-triangle logic missed.
  - **Fix (ID format)**: added 8 `ElkNodeIds.ForInner*` helpers in [ElkGraphBuilder.cs:2031-2042](src/Bistable.App/Services/Routing/Elk/ElkGraphBuilder.cs#L2031) that produce `ff_<sanitized-scope>__<sanitized-sig>` — prefix at the START, scope as a `__` suffix. Existing `Is*` discriminators now fire correctly.
  - **Fix (port labels + dispatch)**: refactored `Add{FlipFlop,Mux,Latch,Memory,Buffer,Inverter,Gate,Arith}Node` to take `(IList<ElkNode> target, …, string? nodeIdOverride = null, string? portRefKeyPrefix = null)`. Outer-scope calls pass `graph.Children` (defaults preserve previous behaviour); inner-scope calls pass `parent.Children`, an `ElkNodeIds.ForInner*` id, and `"@inner::<compoundPath>"` key prefix. New `AddInnerPrimitiveNode` dispatcher (~50 lines) replaces the ~170 lines of obsolete `BuildInnerPrimitiveNode` / `MakeInnerNode` / `RegisterInnerPrimitivePortRefs`. Inner FF/Mux/etc. now have identical port id format (`.d`/`.clk`/`.rst`/`.q` for FF, `.in.<i>`/`.sel.<i>`/`.out` for Mux) and identical labels (D/>/R/Q) to outer-scope, so the symbol drawers fire correctly.
  - **Fix (compound sizing)**: `AttachCompoundChildren` now computes `requiredWidth = 320 + grandchildCount*80 + innerCount*40` and `requiredHeight = 200 + max(0, innerCount-4)*24` so compounds grow when they hold many inner primitives.
  - **Tests**: extended [ElkGraphBuilderRecursiveCompoundTests.cs](tests/Bistable.Tests/ElkGraphBuilderRecursiveCompoundTests.cs) with 8 new P2.5-5 tests:
    - `InnerFlipFlop_NodeId_StartsWithFfPrefix_SoDispatchFires`
    - `InnerMux_NodeId_StartsWithMuxPrefix_SoDispatchFires`
    - `InnerPrimitive_PortLabels_MatchOuterPrimitive`
    - `InnerPrimitive_PortIdSuffix_MatchesOuterFormat_NotGenericIndex`
    - `ExpandedCompound_MinimumSize_GrowsWithInnerPrimitiveCount`
    - `ScopedInnerIds_DoNotCollide_AcrossDifferentCompoundsWithSameSignal`
    - `InnerInverter_DispatchesAsInverter_NotGenericCard`
    - `InnerPrimitive_HasFfTitleLabel_LikeOuterPrimitive`
    - Updated all 8 existing P2-8b tests to reference the new port id format (`ff_<scope>__<q>.d` instead of `child_<path>/ff_q.in.0`).
  - **Snapshot impact**: only `arnicomp-top-expanded-marl_i.json` shows inner-primitive ID changes (one inner mux `mux_arnicomp_top_marl_i__mar_step` with `.sel.0` selector port — identical to outer mux format). Other arnicomp snapshots match prior P2.5-3/4 output unchanged in node mix.
  - **Test suite**: 370 → **378** (+8 in `Bistable.Tests`). All 4 test projects green (378 + 12 snapshots + 4 regression + 2 ui = 396 total). 0 warnings, 0 errors.
  - **Visual impact (Issue 6)**: expanding a compound child now shows inner FFs with clock-triangle + D/>/Q labels, inner muxes as trapezoids with 0/1/S labels, inner inverters with the output bubble, etc. — instead of tiny generic boxes. Compound box auto-grows to fit.
  - **Remaining in P2.5**: ❹ (mux label clarity — P2.5-6), ❸ (optional south selectors — P2.5-7).
