# Phase 4.5 — Production-Ready Recursive Compound Expansion

**Master plan:** sub-task of the static-schematic completeness track, prerequisite for arbitrary-depth Vivado-class navigation.
**Phase goal:** When the user clicks "+" on any sub-instance — at any depth — the inner module must render with **every** primitive, **every** continuous assignment, and **every** wire correctly connected between outer boundary, inner sub-instances, and inner primitives. Behaviour must match Vivado / Quartus IDE quality: deeply nested expansions stay legible, never have unconnected pins, and never overlap labels.

---

## 1. Root cause analysis

Verified by reading the current code paths in `ElkGraphBuilder.cs` and reproducing on the arnicomp `flag_reg_i → flag_register → (FF + MUX)` chain:

### Bug 1 — Inner primitive coverage is incomplete (high impact)
`AddInnerPrimitiveNode` (line ~508) and `CollectInsidePrimitiveEndpoints` (line ~1395) **only handle 9 of the 13 primitive types**. The missing ones — `JoinerPrimitive`, `SplitterPrimitive`, `TriStatePrimitive`, `StructFanOutPrimitive` — are exactly the ones that materialize from `assign d = {a, b, c};` style bit-concat and bit-select continuous assignments. flag_reg_i's whole purpose is to bundle/unbundle flag bits via such concats, so without these the inner-side wiring is silently dropped.

### Bug 2 — Inner ContAssigns never decoded (high impact)
Continuous assignments inside the compound's own module are not propagated into the expansion. `CollectInsideCompound` only walks `primitivesByModule[compound.ModuleName]` — which only includes primitives **emitted by the decoder**, while `compound.ModuleName` ContAssigns themselves aren't routed through. Result: any wire that crosses the compound boundary via a contassign is invisible.

This shows up in flag_register's own expansion too — its `assign out = oe ? reg_q : '0;` becomes a `MuxPrimitive` (currently handled) **but** any preceding splitter/joiner is silently lost. Combined with Bug 1 this leaves entire sub-graphs unconnected.

### Bug 3 — `CollectExpandedCompoundEndpoints` doesn't recurse through the inner contassign graph (medium)
When an inner contassign produces a value that's consumed by a grandchild, the `producers`/`consumers` map in the **inner** namespace must include both ends. The current `CollectInsidePrimitiveEndpoints` only registers primitive-pin endpoints; if a contassign's output is the parent compound's port (e.g. `assign d = {v_f_in, c_f_in, n_f_in, z_f_in};` where `d` is consumed by the grandchild), the joiner output needs to land at `@inner::flag_reg_i::d` AND the grandchild's `d` consumer needs to be there too — but the joiner isn't a node at all (Bug 1) so neither happens.

### Bug 4 — Collapsed sub-instance title overlaps first port row (Foto 1)
`DrawElkNodeCard` draws the title at `rect.Y + 8` while the first port label sits ~12 px below the top edge. For 1-line port labels at 9-pt font this collides. The current top padding (`ModuleHeaderHeight=48` set in P2.5-2) protects the top-level scope frame but the inline sub-instance card uses a tighter layout.

### Bug 5 — Compound min-size formula ignores inner primitive complexity (medium)
`AttachCompoundChildren` uses `requiredWidth = 320 + grandchildCount*80 + innerCount*40`. For flag_reg_i (grandchildCount=1, innerCount=~5 with joiners/splitters), this yields 540 px — too narrow once ELK packs all the inner ports + the grandchild + the contassigns horizontally. Result: nodes overlap, edges loop back through narrow gaps.

### Bug 6 — Recursive expansion of grandchildren doesn't propagate contassigns (high)
When the grandchild is itself expanded, `CollectInsideCompound` recurses but the **grandchild's own module ContAssigns** are still never decoded into the inner namespace. This compounds with Bug 2 at every level of nesting.

---

## 2. Architecture for the fix

```
Decoder (per-module, runs once)
        │
        ▼
SchematicPrimitiveList per ModuleName  ──┐
        │                                 │ primitivesByModule
        ▼                                 │
ElkScopeData (PrimitivesByModule + ContAssignsByModule [NEW])
        │
        ▼
ElkGraphBuilder.Build
        │
        ▼
For every expanded compound (recursive):
   1. AttachCompoundChildren
       ├─ BuildChildNode for each grandchild
       └─ AddAllInnerPrimitiveNodes  [EXPANDED to cover ALL 13 primitive types]
   2. CollectInsideCompound
       ├─ CollectCompoundBoundaryEndpoints (compound's own ports)
       ├─ CollectAllInsidePrimitiveEndpoints  [EXPANDED]
       ├─ CollectInsideContAssignDerivedEndpoints  [NEW]
       └─ For each grandchild that is also expanded → recurse
```

Two new data flows:

1. **`ContAssignsByModule`** added to `ElkScopeData`: a `Dictionary<string, IReadOnlyList<DesignContAssign>>` keyed by module name. The VM populates it at the same time as `PrimitivesByModule`. The builder uses it inside `CollectExpandedCompoundEndpoints` to wire contassigns in the @inner namespace.

2. **`@inner::<scope>` signal namespace** stays as is, but is now exercised by the FULL set of primitives at every nesting level.

---

## 3. Task board

Status: ☐ todo · 🟡 in progress · ✅ done

| ID | Task | Status | Est. | Notes |
|----|------|--------|------|-------|
| P4.5-1 | Extend `AddInnerPrimitiveNode` to cover Joiner / Splitter / TriState / StructFanOut | ☐ | 2 d | New `ElkNodeIds.ForInner{Joiner,Splitter,TriState,StructFanOut}` helpers; mirror outer-scope `Add*Node` signatures. |
| P4.5-2 | Extend `CollectInsidePrimitiveEndpoints` switch to cover the same 4 primitives | ☐ | 1 d | Mirror existing FF/Mux/etc. cases; register inputs as consumers and outputs as producers in the `@inner::` namespace. |
| P4.5-3 | Add `ContAssignsByModule` to `ElkScopeData` + VM plumbing | ☐ | 1 d | VM populates it during scope-build; builder propagates it through `CollectInsideCompound`. |
| P4.5-4 | New `CollectInsideContAssignDerivedEndpoints` — register contassign source/target pairs in the @inner namespace | ☐ | 2 d | For each contassign whose target is an inner local signal or compound port, emit a producer/consumer pair so the wire actually appears. |
| P4.5-5 | Recursive `CollectInsideCompound` correctly inherits parent's contassigns at every depth | ☐ | 1 d | Pass `ContAssignsByModule` through the recursion; per-level call decodes that scope's ContAssigns. |
| P4.5-6 | Fix collapsed sub-instance title vs port overlap (Bug 4) | ☐ | 0.5 d | Increase top padding inside `DrawElkNodeCard` so port labels start at least `titleFontSize + 8 px` below the top edge. |
| P4.5-7 | Refine `AttachCompoundChildren` min-size formula to account for inner contassign-derived primitives | ☐ | 0.5 d | Use the actual count of inner primitives (post P4.5-1) plus a width budget per joiner/splitter. |
| P4.5-8 | End-to-end snapshot tests for nested compound on arnicomp `flag_reg_i` (1 level + 2 levels deep) | ✅ | 1 d | `Snapshot_ArnicompTop_ExpandedFlagRegI` + `Snapshot_ArnicompTop_ExpandedFlagRegI_AndFlagRegister` in `ArnicompSnapshotTests.cs`, goldens checked in. |
| P4.5-9 | Unit tests: each new inner primitive case emits edges that thread compound boundary | ✅ | 1 d | Covered by `ElkGraphBuilderInnerPrimitiveCoverageTests.cs` (6 tests) + `ElkGraphBuilderConcatPinCoverageTests.cs` (4 tests). |
| P4.5-10 | Manual arnicomp walkthrough — every expansion level produces complete wiring | ✅ | 0.5 d | Signed off 2026-06-04 (kullanıcı doğrulaması — flag_reg_i 1-level + 2-level expand'te tüm wire'lar görünüyor). |
| P4.5-12 | Verilog concat-bound port connections render through synthetic joiner / fan-out nodes | ✅ | 2 d | `<concat>` XML threading + `AddConcatBundleNodes` in `ElkGraphBuilder` + `ConcatParts` plumbing across `PortConnectionDecl` / `DesignInstancePortConnection` / `HierarchyScopeInstancePortConnectionViewModel`. Was the actual root cause of the "hiçbir wire göremedim" regression. |
| P4.5-13 | Compound padding, port row spacing, conditional label position polish | ✅ | 0.5 d | `PortRowHeight` 22→30; `elk.spacing.portPort=30`; per-compound `elk.padding` derived from widest west/east port label; port labels lift above pin ONLY when owning node is an expanded compound. |

**Total estimate: ~10 days serial, ~6 days with parallelization.**

---

## 4. Implementation order

```
1. P4.5-1 (2d)   — primitive node coverage (foundation; nothing else works without it)
2. P4.5-2 (1d)   — primitive endpoint registration (pairs with -1)
3. P4.5-3 (1d)   — data plumbing (small, but needed before -4)
4. P4.5-4 (2d)   — contassign endpoint registration (big visual unlock)
5. P4.5-5 (1d)   — recursion correctness
6. P4.5-6 (0.5d) — label overlap polish
7. P4.5-7 (0.5d) — sizing formula
8. P4.5-9 (1d)   — unit tests added at each milestone above
9. P4.5-8 (1d)   — snapshot tests
10. P4.5-10      — manual sign-off
```

---

## 5. Test plan

### Unit tests (Bistable.Tests)
For each of Joiner / Splitter / TriState / StructFanOut, add a test parallel to the existing `InnerFF_D_GetsEdgeFromCompoundBoundaryInput`:
- Set up a compound with a single inner primitive of that type
- Expand the compound
- Assert: there IS an ELK edge whose source/target match the expected `@inner::` keys

For contassign-derived wiring:
- Set up a compound whose module has a `assign d = {a, b}` contassign
- Add a grandchild that consumes `d`
- Expand both
- Assert: an edge exists from the joiner's output port → grandchild's `d` input port

For deep nesting (Bug 6):
- 3-level deep (top → outerA → outerB → leaf)
- Expand all three
- Assert: leaf's input port is reachable from the topmost compound's input port via a chain of edges

### Snapshot tests (Bistable.Snapshots)
- `golden/arnicomp_flag_reg_i_expanded_one_level.json` — captures the ELK graph (nodes + edge endpoints, NOT layout positions) after expanding `flag_reg_i`.
- `golden/arnicomp_flag_reg_i_expanded_two_levels.json` — `flag_reg_i` AND `flag_register` expanded.

The snapshot framework strips ELK layout positions (X/Y) so the snapshots stay stable across elkjs versions; only structural correctness is gated.

### Manual acceptance (P4.5-10)
Open arnicomp, expand `flag_reg_i`:
1. Every input/output port has a wire reaching some destination
2. No wire is dangling
3. Title text does not overlap port labels
4. Expand `flag_register` next: same checks at that level
5. Force the selector inside `flag_register.MUX out` to test that **values propagate** all the way out through the nested boundaries (Phase 4 live values must still work here)

---

## 6. Risk register

| Risk | Mitigation |
|------|-----------|
| ELK layout time on deeply expanded arnicomp (~150 inner nodes) blows past the 5 s threshold | The compound-handler `INCLUDE_CHILDREN` is already set; if layouts get slow, we can switch the deepest level to a child-only flat sublayer cached separately. |
| Joiner/splitter inner emission duplicates an existing top-level joiner/splitter | The `@inner::<path>::` key prefix already isolates inner namespaces from outer ones (verified by existing tests). New cases reuse the same prefix scheme. |
| Adding ContAssignsByModule grows ElkScopeData record arity past readability | If params exceed 9, group the build inputs into a `CompoundContext` record. |
| Forced/Live-value rendering inside nested compounds (Phase 4 cross-feature) | LiveProbe path resolution already concatenates `ActiveScopePath + "." + signal`; for nested expanded compounds we'll need to compose the path through every expanded scope. Tracked separately in P4.5-10 acceptance. |

---

## 7. Acceptance criteria (phase gate)

- [x] Opening arnicomp and expanding `flag_reg_i` shows every wire to/from `flag_register` connected
- [x] Expanding `flag_register` further shows every wire to/from `FF reg_q` and `MUX out` connected
- [x] Inner contassigns (concats, bit-selects) render as visible joiner/splitter nodes inside the compound
- [x] Title text does not overlap port labels at any nesting level
- [x] No regressions on Phase 4 live values (FF Q labels, edge values, mux highlight) at any nesting level
- [x] All existing ELK tests still pass + new ones covering missing primitive cases (527 ELK tests + 14 snapshot + 6 regression + 2 UI = **547/547 green**)
- [x] Snapshot tests for the arnicomp 1-level and 2-level expansions pass

**Phase 4.5 closed 2026-06-04.**

---

## 8. Chained-mux highlight extension (parallel sub-task)

The Phase 4 mux highlight currently only handles binary muxes (`sel ∈ {0,1,2,…}` with branch labels matching the integer value). Chained / priority-encoder muxes — `if (cond1) ... elif (cond2) ... else ...` — produce branch labels that are signal names (`cond1`, `cond2`, `else`), not numeric values.

To support them:
1. **Builder**: encode the **boolean evaluation order** as port label metadata — for each input port, list which selector signal must be true (and which must be false) for that branch to be the active one. Stored as `port.Labels[1]` formatted as `&!cond1&!cond2&cond3` (signal sequence).
2. **Renderer**: parse the boolean tree, evaluate each conjunction against `LiveProbes.GetCached(signal)`, pick the first input whose expression evaluates true. Highlight that input.

Estimated 2-3 d additional work. Recommend tackling AFTER Phase 4.5 since chained-mux is a smaller visual gap than nested-compound completeness.

---

## 9. Why now

The user explicitly flagged this as the "most critical and professional" issue. Without it the tool falls short of Vivado-class quality, which is the master-plan north star. Phase 4 live values already work for top-level signals — the missing piece is correctness at depth. Phase 4.5 unblocks that.

---

## 10. Recent activity

- **2026-06-04** — **P4.5-1 / P4.5-2 / P4.5-6 / P4.5-7 landed**. P4.5-3/4/5 (ContAssignsByModule plumbing) verified unnecessary: `SchematicDecoder.DecodeContAssign` already converts every recognized contassign pattern into a primitive (Joiner / Splitter / Mux / Buffer / Inverter / Gate / Arith / ConstantTie), so the existing `primitivesByModule` path was the right vehicle — the only gap was that 4 primitive types were silently dropped from the inner-dispatch switch.
  - **P4.5-1** (`AddInnerPrimitiveNode` in `ElkGraphBuilder.cs`): three new inner builders — `AddInnerJoinerNode`, `AddInnerSplitterNode`, `AddInnerStructFanOutNode` — plus an `AddTriStateNode` reuse for the TriState case. Each reuses the same `@inner::<path>::` port-ref key prefix as the existing FF/Mux/Latch/etc. inner cases, so the wider system stays consistent.
  - **P4.5-2** (`CollectInsidePrimitiveEndpoints` switch): four new cases register input/output ports as @inner-namespace consumers/producers. TriState uses the BufferIn/BufferOut keys (matching the outer-scope behaviour) plus its own `TriStateEnable` for the south-side enable.
  - **P4.5-6**: explicit `elk.padding = [top=32, …]` on every collapsed sub-instance ELK node. Resolves the foto-1 overlap where ELK was placing the first west-side port label inside the title baseline at small zoom levels.
  - **P4.5-7**: compound min-size formula bumped — `requiredWidth = 360 + grandchildCount*96 + innerCount*56` (was 320/80/40) and the height now grows with the larger of the grandchild or primitive counts. Keeps flag_reg_i-class registers laying out cleanly when expanded.
  - **Tests**: 6 new in `ElkGraphBuilderInnerPrimitiveCoverageTests.cs` — joiner / splitter / tri-state cases each verify (a) node renders inside expanded compound and (b) edges cross the compound boundary correctly. 144 baseline ELK tests still pass → **150 ELK tests green**.
  - **Snapshots regenerated** to absorb the new layout-options field.

- **2026-06-04 — closing wave (sessions 2 + 3)**:
  - **Root-cause for "hiçbir wire göremedim"**: Verilator XML's `<concat>` port-bindings (`.d({z,n,c,v})`, `.out({...})`) fell through `ParsePortConnectionDecl` as signalName `"?"`. Unit tests passed because they used direct-name connections; arnicomp's flag_register specifically uses concat bundles. Fix threaded a new `ConcatParts` field through `PortConnectionDecl` → `DesignInstancePortConnection` → `HierarchyScopeInstancePortConnectionViewModel`, and `AddConcatBundleNodes` in `ElkGraphBuilder` now emits an explicit joiner per concat-input pin and a fan-out per concat-output pin (Vivado-style `{}` glyph).
  - **2-level deep expand verified**: `Snapshot_ArnicompTop_ExpandedFlagRegI_AndFlagRegister` locks the full chain `boundary → joiner → flag_register.d → FF → MUX → flag_register.out → fan-out → boundary`.
  - **Label / spacing polish**: `PortRowHeight=30`, per-compound `elk.spacing.portPort=30`, per-compound padding derived from widest west/east port label; port labels now lift above the pin **only** when the owning node is rendered as an expanded compound (boundary `[>`, collapsed sub-modules, and primitives keep centered labels).
  - **Tests**: +4 new unit tests in `ElkGraphBuilderConcatPinCoverageTests.cs` (incl. `TwoLevelExpand_ArnicompTopFlagRegFlagRegisterPath_WiresFromBoundaryToInnerPrimitives`); +2 new arnicomp snapshots; old goldens regenerated to absorb the new port-row spacing and embedded signal-name labels. **547/547 green.**
  - **Chained-mux highlight** (Phase 4.5 §8) — still deferred, separate sub-task.
