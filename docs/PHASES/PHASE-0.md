# Phase 0 — Test & CI Infrastructure (live status)

**Master plan:** `/home/ardac/.claude/plans/fluffy-wishing-kettle.md`
**Phase goal:** Make every subsequent phase safe. Add test infrastructure that catches the UI/integration bugs current xUnit tests miss.
**Started:** 2026-05-23
**Phase gate (acceptance):**
- Total tests ≥ 150 (current baseline: 106).
- Every sample project has at least one golden-snapshot test.
- `.github/workflows/ci.yml` green on a clean clone.
- `docs/TESTING.md` documents the snapshot/regression/headless workflow.
- All previously known bugs from the master plan Section 1 have a regression test (failing → fix → passing).

---

## 1. Task board

Status legend: ☐ todo · 🟡 in progress · ✅ done · ⛔ blocked

| ID | Task | Status | Owner | Notes |
|----|------|--------|-------|-------|
| P0-1 | Create `docs/PHASES/PHASE-0.md` (this file) | ✅ | session-2026-05-23 | Live status doc |
| P0-2 | `docs/TESTING.md` — testing conventions (regression / golden / headless) | ✅ | session-2026-05-23 | Reviewer entry point |
| P0-3 | `docs/ARCHITECTURE.md` — current-state layer map | ✅ | session-2026-05-23 | Onboarding doc |
| P0-4 | `tests/Bistable.Regression/` project + locking tests for known bugs | ✅ | session-2026-05-23 | 4 tests added; sub-sim tests deferred to P0-8 |
| P0-5 | `tests/Bistable.Snapshots/` framework (SnapshotAssert + golden/ folder) | ✅ | session-2026-05-23 | Hand-rolled JSON diff, `BISTABLE_REGENERATE_SNAPSHOTS=1` to update |
| P0-6 | Golden snapshots for alu, counter, hierarchy, tiny_cpu, bus_fabric, arnicomp | 🟡 | — | Synthetic snapshot landed; per-sample snapshots pending (needs Verilator runtime fixtures) |
| P0-7 | `tests/Bistable.UiTests/` (Avalonia.Headless) + smoke tests | ✅ | session-2026-05-23 | 2 smoke tests pass; `MainWindowHeadlessFixture` pending P0-8 |
| P0-8 | Headless UI tests for: sub-sim enter/exit, compound expansion edges, concat join | ☐ | — | Depends on P0-7; needs BistableWorkspace fixture with sample project |
| P0-9 | `.github/workflows/ci.yml` — apt install verilator + dotnet test | ✅ | session-2026-05-23 | Ubuntu 24.04, runs all 4 test projects + uploads .trx + coverage |
| P0-10 | Coverage upload in CI (coverlet → cobertura artifact) | ✅ | session-2026-05-23 | Bundled into ci.yml |
| P0-11 | Logging foundation (Microsoft.Extensions.Logging) | ☐ | — | Parallel to Status property |
| P0-12 | Phase-gate verification run (all tests green + ≥150 count) | 🟡 | — | All 113 tests green; need ≥150 to close gate (see P0-6, P0-8) |

---

## 2. Known bugs to cover with regression tests (P0-4)

Each test must fail today (where applicable) and pass after the matching fix lands. Test names follow `<Symptom>_<Condition>` format.

| Test name | Bug it captures | File the fix belongs in |
|-----------|----------------|------------------------|
| `Concat_TwoSourceAssign_RendersJoinerNode` | `c = {a, b}` was rendered as a generic operator box, not a joiner | `ElkGraphBuilder.AddJoinerNode` (fix landed 2026-05-23) |
| `ExpandedCompound_InternalEdgesAppear_BetweenGrandchildren` | Expanded child showed nested boxes but no internal connections | `ElkGraphBuilder.CollectInsideCompound` (fix landed 2026-05-23) |
| `SubSim_HierarchyAndTraceState_SwapsCleanlyOnEnter` | Sub-sim entry left top-level hierarchy/trace stale → probe showed wrong INTERNAL signal | `MainWindowViewModel.EnterSubSimulationAsync` (fix landed 2026-05-23) |
| `SubSim_TopLevelStateRestoredExactly_OnExit` | After exiting sub-sim, top-level state lost or partial | `MainWindowViewModel.ExitSubSimulation` (fix landed 2026-05-23) |
| `Splitter_ContiguousRanges_StackInMSBOrder` | Splitter ports ordered correctly by MSB | `ElkGraphBuilder.AddSplitterNode` |
| `SubModuleOutputSignal_ProbeValue_TracksLiveTrace` | `reg_a_we` showed `-` despite being driven; needed ResolvedSignalName fallback | `MainWindowViewModel.SelectedSchematicSignalValue` (fix landed 2026-05-23) |
| `ParserSel_PackedStructFieldAccess_ResolvesBaseVarref` | `control_pins.ops` lost the wire because parser ignored `<sel>` wrapper | `VerilatorXmlParser.ParseInstancePortConnection` (fix landed 2026-05-23) |

The 2026-05-23 fixes are already in. The regression tests must **lock them down** so they never regress.

---

## 3. Decisions log

- **2026-05-23**: Phase ordering — infrastructure first (user-confirmed).
- **2026-05-23**: Backend strategy — design IR will be Verilator-agnostic (user-confirmed). This means Phase 1's AST has no Verilator-specific node names; the parser is a reader, the AST is a stable contract.
- **2026-05-23**: Per-module test scope v1 — manual pin drive only. Scripting/scenario replay deferred to v2.
- **2026-05-23**: Snapshot framework — hand-rolled JSON diff rather than Verify library. Rationale: agents picking up the project should not need to learn a third-party API; the helper is ~50 lines.

---

## 4. Open questions

(none yet — record any blockers or design splits here as they arise)

---

## 5. Handoff notes for next session

If you're picking this phase up from a fresh session:

1. Read the master plan first: `/home/ardac/.claude/plans/fluffy-wishing-kettle.md`.
2. Read this file to see which P0-* tasks are done.
3. The most recent commit on `main` should be tagged in the "Recent activity" section below.
4. Run `dotnet test` to confirm the current test count and baseline green.
5. Pick the next ☐ task in order — they're listed by dependency.

---

## 6. Recent activity

- **2026-05-23**: `main` fast-forwarded from `schematic/elk-poc`, last commit `b896e03`. Phase 0 work begins.
- **2026-05-23**: Phase 0 infrastructure landed in one session — docs (TESTING.md, ARCHITECTURE.md, PHASE-0.md), 3 new test projects (Regression, Snapshots, UiTests), CI workflow. Total test count: 106 → 113 (4 regression + 1 snapshot + 2 UI smoke).
- **2026-05-23**: ELK band-aid applied — user reported timeout on arnicomp. Two cheap changes: `ElkRunner.DefaultResponseTimeout` 8 s → 45 s, and `elk.layered.thoroughness` 10 → 3. These are explicit band-aids; the real fix is Phase 2's primitive decoder (smaller graph = faster ELK). Snapshot `synthetic-concat-and-splitter.json` was regenerated to reflect the new layout option.
- **Phase 0 partially closed.** Remaining: per-sample golden snapshots (P0-6), full headless UI integration tests (P0-8), logging (P0-11). Test count needs to reach ≥150 to fully close the gate — those three tasks will push it there. Phase 1 can start before P0-6/P0-8/P0-11 close, since the missing items have no blocking dependency on Phase 1 work.

## 7. Test count contribution

| Project | Tests | Notes |
|---------|-------|-------|
| `Bistable.Tests` | 106 | Pre-existing |
| `Bistable.Regression` | 4 | New (P0-4) |
| `Bistable.Snapshots` | 1 | New (P0-5/P0-6 partial) |
| `Bistable.UiTests` | 2 | New (P0-7 smoke only) |
| **Total** | **113** | Phase 0 baseline. Gate target: ≥150. |
