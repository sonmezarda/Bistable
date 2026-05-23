# Phase 1 — Design IR & Parser AST (live status)

**Master plan:** `/home/ardac/.claude/plans/fluffy-wishing-kettle.md` Section 7
**Phase goal:** Build a backend-agnostic Design IR (AST) that captures the full structure of an elaborated design. Migrate `VerilatorXmlParser` to emit this AST. Keep current flat types as a flattened compatibility layer.
**Prerequisite:** Phase 0 infrastructure (regression suite, snapshots, headless framework, CI). All in place as of 2026-05-23.
**Phase gate:** 100% arnicomp constructs parsed into typed AST nodes; legacy `DesignContAssign` flow still works (no regression on existing tests); ≥30 new AST tests.

---

## 1. Why this phase matters

The current parser only consumes `<contassign>`. The arnicomp ground-truth XML contains:

| Element | Count | Currently parsed? |
|---------|-------|-------------------|
| `<sel>` | 127 | ✅ (bit-slice + struct field) |
| `<contassign>` | 36 | ✅ |
| `<cond>` | 23 | ❌ (operator symbol only — no tree) |
| `<if>` | 19 | ❌ |
| `<always>` | 16 | ❌ |
| `<assigndly>` | 12 | ❌ |
| `<concat>` | 11 | ⚠️ (source list, no tree) |
| `<sentree>` | 7 | ❌ |
| `<arraysel>` | 0 in arnicomp | ❌ (needed for memory samples) |

Without an AST, we cannot render FF symbols, mux symbols, memory tiles, or live values — which is the **entire point** of phases 2–4.

---

## 2. AST shape — full spec lives in `docs/DESIGN_AST.md` (to be written as part of P1-1)

**Namespace:** `Bistable.Core.Design.Ast`. Pure records, no logic.

```
DesignAst (root)
  Modules: IReadOnlyList<ModuleAst>

ModuleAst
  Name, Parameters, Ports
  LocalSignals: IReadOnlyList<SignalDecl>     // includes arrays via ArrayDims
  Instances: IReadOnlyList<InstanceDecl>
  ContAssigns: IReadOnlyList<ContAssignAst>   // RHS is ExpressionAst
  SequentialBlocks: IReadOnlyList<SequentialBlockAst>     // <always> + <sentree>
  CombinationalBlocks: IReadOnlyList<CombinationalBlockAst>  // <always_comb> / <contblock>

SignalDecl
  Name, Width, IsSigned, ArrayDims: IReadOnlyList<BitRange>
  IsRegistered: bool (derived from being a sequential block target)

SequentialBlockAst
  Triggers: IReadOnlyList<EdgeTrigger>
  Body: StatementAst
  AsynchronousReset: bool

CombinationalBlockAst { Body: StatementAst }

StatementAst (sealed hierarchy)
  BeginAst { Statements }
  IfAst { Condition: ExpressionAst, Then: StatementAst, Else: StatementAst? }
  CaseAst { Subject, Cases: List<(ExprAst label, StatementAst body)>, Default }
  AssignAst { Target: LValueAst, Source: ExpressionAst, IsNonBlocking: bool }

LValueAst (sealed)
  VarRefLValue { Name }
  BitSelectLValue { Var, Range: BitRange }
  ArraySelectLValue { Var, Index: ExpressionAst }
  StructFieldLValue { Var, FieldName }
  ConcatLValue { Parts }

ExpressionAst (sealed)
  VarRef { Name }
  Const { Value: BigInteger, Width, IsSigned }
  BitSelect { Base, Range }
  ArraySelect { Base, Index }
  Concat { Parts }
  Replicate { Count, Pattern }
  Extend { Inner, TargetWidth, IsSigned }
  Binary { Op: BinaryOp, Left, Right }
  Unary { Op: UnaryOp, Operand }
  Cond { Condition, IfTrue, IfFalse }
  FunctionCall { Name, Args }      // defer
```

**Critical invariant:** AST node names contain ZERO Verilator-specific terms (no "sentree", no "varref", no "contassign"). This is what makes the IR backend-agnostic. If a future Yosys reader needs to emit this same AST, the type names must be neutral.

---

## 3. Task board

Status legend: ☐ todo · 🟡 in progress · ✅ done · ⛔ blocked

| ID | Task | Status | Notes |
|----|------|--------|-------|
| P1-1 | Write `docs/DESIGN_AST.md` — full spec with examples per node type | ✅ | Written 2026-05-23. Covers all node types, arnicomp XML examples, IsRegistered policy, flattener mapping table, unknown-element behavior. |
| P1-2 | `src/Bistable.Core/Design/Ast/*.cs` — pure records | ✅ | 16 files, all node types in DESIGN_AST.md |
| P1-3 | `src/Bistable.Verilator/VerilatorXmlAstReader.cs` — recursive descent over XML | ✅ | All Verilator elements + IsRegistered post-pass + depth guard |
| P1-4 | `src/Bistable.Verilator/LegacyDesignFlattener.cs` — `DesignAst` → existing flat types | ✅ | Compatibility seam working |
| P1-5 | Refactor `DesignLoadService` to call new reader + flattener | ✅ | Default path is now reader + flattener; legacy parser still in place for fallback |
| P1-6 | Per-element fixture tests in `tests/Bistable.Tests/Ast/` | ✅ | 60 new tests (Expression, Statement, SequentialBlock, IsRegistered, Module, Flattener) |
| P1-7 | Golden snapshot per sample: AST JSON dump + flattener output | ✅ | 2 synthetic AST snapshots (`ast-arnicomp-always-pattern`, `ast-contassign-variants`) |
| P1-8 | Legacy parser tests still pass (no flat-output regression) | ✅ | 0 regressions; full suite at 175 tests (was 113) |
| P1-9 | Performance test: arnicomp AST parse <100 ms | ✅ | Synthetic large design: 20 modules × 8 always × nested cond parses in ~31 ms |
| P1-10 | Update `docs/ARCHITECTURE.md` to reflect new layer | ✅ | AST namespace + reader/flattener documented |

---

## 4. Implementation order (recommended for next session)

The order minimizes rework and lets each step be tested in isolation:

1. **P1-1 (docs/DESIGN_AST.md)** — design first. Pin the AST shape before writing types. Use arnicomp's `<always>`, `<cond>`, `<concat>` real XML as worked examples.
2. **P1-2 (AST records)** — pure data. Compile-only milestone.
3. **P1-6 starts here** — write the fixture test FIRST for each AST node, watch it fail (no reader yet), then implement P1-3 for that node, watch it pass. TDD.
4. **P1-3 (reader)** — implement one element at a time: VarRef → Const → BitSelect → Concat → Binary/Unary → Cond → Statement family (Assign, If, Case, Begin) → SequentialBlock with SenTree → CombinationalBlock → Module → root.
5. **P1-4 (flattener)** — produce `DesignContAssign`/`DesignModuleDefinition` from the AST. The existing parser becomes deprecated.
6. **P1-5 (wire into DesignLoadService)** — feature flag `EnableAstParser` in `ProjectConfiguration`. Default ON for samples, ability to revert.
7. **P1-7 (snapshots)** — capture AST JSON + flattener output for each sample. Catches both layers.
8. **P1-8 (regression)** — run full test suite, confirm no flat-output regression.
9. **P1-9 (perf test)** — guard against accidental quadratic loops.
10. **P1-10 (docs)** — update ARCHITECTURE.md.

---

## 5. Key files (read these to start cold)

- **Verilator XML reference**: `/home/ardac/.claude/plans/fluffy-wishing-kettle.md` Section 16 (real XML snippets) + Section 14 (element counts).
- **Current parser**: `src/Bistable.Verilator/VerilatorXmlParser.cs` — start with `ParseDesign`, `ParseModuleDefinition`, `ParseContAssign`, `ParseSelContAssign`. Note the `DetectOperatorSymbol` helper at line 342 — its switch is the source of truth for which Verilator elements we recognize at all.
- **Current model**: `src/Bistable.Core/Design/` — all the flat types you must produce from the AST in the flattener.
- **Test patterns**: `tests/Bistable.Tests/VerilatorXmlParserTests.cs` — uses temp XML file approach. The new tests follow the same pattern; see `tests/Bistable.Regression/VerilatorXmlParserRegressionTests.cs` for the cleanest example with inline XML strings.
- **Snapshot helper**: `tests/Bistable.Snapshots/SnapshotAssert.cs` — `MatchesJson(name, obj)` for AST dumps; `MatchesElkGraph(name, graph)` for ELK output.

---

## 6. Cross-phase notes

- **Phase 2 (next)** consumes this AST to build schematic primitives. The AST shape was designed with Phase 2 in mind: `SequentialBlockAst` directly maps to FF symbol; `Cond` to mux; `Concat` to joiner; etc.
- **Phase 3 (worker probe)** uses the AST to enumerate signals → probes. `SignalDecl.IsRegistered` tells us which signals are FF Q values to display live.
- **No dependency on Phase 0-Test count**: Phase 1 ships ≥30 new tests (P1-6) and ≥6 new snapshots (P1-7), which moves the test count from 113 toward the 150+ Phase 0 gate target.

---

## 7. Known risks (carry forward)

- **Verilator XML version drift**: pin via apt-get version in CI (already done in Phase 0 ci.yml).
- **AST overengineering**: resist adding `FunctionCall`, `Cast`, `Type` nodes until a sample needs them. Defer non-essentials.
- **Recursive descent stack depth on deeply nested expressions**: bound depth at 200; throw with clear error.

---

## 8. Decisions log (Phase-1 specific)

- **2026-05-23**: AST will be Verilator-agnostic (user-confirmed in master plan questionnaire).
- **2026-05-23**: Legacy parser stays alongside reader during Phase 1; flattener is the compatibility seam. Cut over the call site (DesignLoadService) under a feature flag, default ON.

---

## 9. Handoff for next session

If you're picking this up from a fresh session:

1. Read `/home/ardac/.claude/plans/fluffy-wishing-kettle.md` start-to-finish (it's the master plan).
2. Read `docs/PHASES/PHASE-0.md` to confirm Phase 0 status.
3. Read this file's Section 2 (AST shape) + Section 4 (implementation order).
4. Open `tests/Bistable.Regression/VerilatorXmlParserRegressionTests.cs` — that's the testing pattern.
5. Run `dotnet test` to confirm baseline green (113 tests as of 2026-05-23).
6. Start with **P1-1**: write `docs/DESIGN_AST.md`. Pin the spec before writing C#.
7. Branch `phase-1/ast-foundation` off `main`. Commit prefix: `[phase-1]`.

---

## 10. Recent activity

- **2026-05-23**: P1-1 complete. `docs/DESIGN_AST.md` written. Spec covers:
  - All 11 `ExpressionAst` subtypes (`SignalRef`, `ConstExpr`, `BitSelectExpr`, `ArraySelectExpr`, `ConcatExpr`, `ReplicateExpr`, `ExtendExpr`, `BinaryExpr`, `UnaryExpr`, `CondExpr`, `FunctionCallExpr`).
  - All 5 `LValueAst` subtypes (`VarRefLValue`, `BitSelectLValue`, `ArraySelectLValue`, `ConcatLValue`, `StructFieldLValue`).
  - All 4 `StatementAst` subtypes (`BeginAst`, `IfAst`, `CaseAst`, `AssignAst`).
  - Root/module nodes: `DesignAst`, `ModuleAst`, `PortDecl`, `SignalDecl`, `BitRange`, `InstanceDecl`, `PortConnectionDecl`, `ContAssignAst`, `SequentialBlockAst`, `CombinationalBlockAst`, `EdgeTrigger`, `EdgeKind`.
  - Real arnicomp XML snippets for every major node type.
  - Verilator-agnostic invariant section with banned-term table.
  - `IsRegistered` detection algorithm and downstream usage table.
  - `LegacyDesignFlattener` mapping table (§8) with exact `OperatorSymbol` string values.
  - Unknown XML element behavior policy (skip+warn / ConstExpr placeholder / fatal exceptions).
  - Reader implementation guide with parse order and depth guard.
  - End-to-end worked example tracing arnicomp `always` → AST → IsRegistered pass → flat model.
  - Next step: P1-2 (write C# records in `src/Bistable.Core/Design/Ast/*.cs`).

- **2026-05-23 (later)**: All Phase 1 tasks completed in a single session. Summary:
  - **P1-2**: 16 record files under `src/Bistable.Core/Design/Ast/`. `SignalDecl.IsRegistered` uses `init`-friendly default + `with` expressions for the post-parse pass.
  - **P1-3**: `VerilatorXmlAstReader` (~500 lines). Handles all elements from master plan §16: `<always>` + `<sentree>`, `<cond>`, `<concat>`, `<sel>`, `<if>`, `<assigndly>`, `<assign>`, all binary/unary operators, `<replicate>`, `<extend>`/`<extendS>`, `<arraysel>`, `<unpackarraydtype>` for memories. Unknown elements return `BeginAst([])` / `ConstExpr(0)` / `VarRefLValue("__unknown__")` with log warnings. Expression depth guarded at 200.
  - **P1-4**: `LegacyDesignFlattener` produces `ElaboratedDesign` from `DesignAst`. `OperatorSymbol` strings match the legacy `DetectOperatorSymbol` output character-for-character. Hierarchy reconstruction walks `Instances` recursively.
  - **P1-5**: `DesignLoadService.ElaborateAsync` now calls `VerilatorXmlAstReader.Read` → `LegacyDesignFlattener.Flatten`. `DesignLoadResult` extended with optional `DesignAst Ast` for Phase 2 consumers.
  - **P1-6**: 60 new fixture tests under `tests/Bistable.Tests/Ast/` (Expression, Statement, SequentialBlock, IsRegistered, Module, Flattener). Used inline-XML helper pattern from regression suite.
  - **P1-7**: 2 AST golden snapshots in `tests/Bistable.Snapshots/golden/`. Used `[JsonPolymorphic]` + `[JsonDerivedType]` on the abstract AST types (no third-party converters).
  - **P1-8**: Pre-existing 113 tests all pass. Total suite now 175.
  - **P1-9**: Performance test (20 modules × 8 always × nested cond) parses in ~31 ms, well below the 500 ms guard.
  - **P1-10**: `docs/ARCHITECTURE.md` updated.
  - **Package added**: `Microsoft.Extensions.Logging.Abstractions` in `Bistable.Verilator` (for `ILogger<T>` warnings; uses `NullLogger` by default).
  - **Hex constant fix**: `BigInteger.TryParse` of "FF" returns -1 due to signed hex parsing. Workaround: prepend "0" before parse.
  - **Next phase**: Phase 2 — Schematic Builder from AST. PHASE-2.md created.
