# Phase 2.9 — RTL Completeness and Coverage Audit

**Status:** In progress — coverage model, ELK telemetry, sample gates, negative tests, and diagnostics UI landed  
**Reason:** Phase 2.7 UX work is useful but not sufficient for professional RTL tooling. Before adding more polish, the tool must be able to prove which signals, ports, primitives, and wires were successfully understood and rendered.  
**Phase goal:** No silent missing wires. Every skipped, unsupported, unresolved, or intentionally hidden endpoint must appear in a machine-readable coverage report and, later, a UI diagnostics panel.

---

## 1. Why this phase matters

The user has already found real cases where wires were missing or visually misleading. The concat-bound port issue was fixed, but the discovery mechanism was manual: open arnicomp, expand modules, zoom in, visually inspect wires.

That workflow does not scale to:

- RV32I cores,
- generated register files,
- pipeline stages,
- out-of-order queues,
- wide control/data buses,
- SystemVerilog interfaces,
- synthesized netlists.

Professional tooling cannot rely on visual inspection alone. It must report completeness:

> "This module has 2,418 expected endpoints. 2,411 are routed. 7 are unsupported. Here are the exact reasons."

This phase introduces that capability.

---

## 2. Definitions

### Expected endpoint

Any semantic connection point that should be represented in the schematic or explicitly marked as intentionally omitted:

- boundary input/output/inout port,
- child instance input/output/inout port,
- primitive input/output pin,
- memory read/write port if represented,
- struct/interface fan-out leg,
- concat joiner/splitter leg,
- generated block proxy port,
- gate-level cell pin in the synthesis backend later.

### Routed endpoint

An expected endpoint that resolves to:

- an ELK node port,
- at least one producer/consumer entry,
- and, if connected, at least one emitted edge.

### Intentional omission

An endpoint that is intentionally not routed and has an explicit reason:

- constant-only input,
- high-Z placeholder,
- Verilator internal temporary intentionally hidden,
- unused output,
- unsupported memory internals deferred,
- probe-only signal.

### Silent miss

An endpoint that should be represented but is neither routed nor explained. Silent misses are phase-gate failures.

---

## 3. Task board

Status legend: `todo`, `in_progress`, `done`, `blocked`

| ID | Task | Status | Est. | Notes |
|----|------|--------|------|-------|
| P2.9-1 | Define coverage data model | done | 1 d | `SchematicCoverageReport`, `ModuleCoverage`, `EndpointCoverage`, `UnsupportedConstructDiagnostic` landed in Core. |
| P2.9-2 | Instrument `SchematicDecoder` | in_progress | 2 d | Initial `SchematicCoverageAnalyzer` inspects decoder output for unsupported contassign/sequential targets and `?` primitive pins; direct decoder event instrumentation still pending. |
| P2.9-3 | Instrument `ElkGraphBuilder` endpoint resolution | done | 2 d | `ElkBuildResult` now carries routing telemetry; graph/PortRef and dangling consumer diagnostics are test-covered. |
| P2.9-4 | Add Verilator XML/AST fallback diagnostics | in_progress | 2 d | Initial reader fallback diagnostics landed for unknown expressions/l-values/statements; broader `?`, dtype, range, memory diagnostics still pending. |
| P2.9-5 | Generate per-sample reports | done | 1 d | Added `SchematicCoverageReportJson` writer/reader and sample coverage JSON artifact roundtrip tests. |
| P2.9-6 | Add sample gates | done | 2 d | Generated-XML silent-miss gates now cover arnicomp, tiny_cpu, bus_fabric, memory_demo, and riscv_single_cycle. |
| P2.9-7 | Add negative tests | done | 1 d | Added 6 negative coverage tests, including concat l-value unknown segment reporting as explicit `ContAssignLValue` diagnostic. |
| P2.9-8 | Add diagnostics UI entry point | done | 2 d | Added `DiagnosticsWindow` and `View > Schematic Coverage...` command over the current in-memory report. |
| P2.9-9 | Update docs/architecture/testing | done | 0.5 d | Documented coverage report lifecycle in `docs/ARCHITECTURE.md` and `docs/TESTING.md`. |

---

## 4. Proposed data model

Location:

- `src/Bistable.Core/Design/Schematic/SchematicCoverageReport.cs`

Candidate records:

```csharp
public sealed record SchematicCoverageReport(
    string TopModule,
    IReadOnlyList<ModuleCoverage> Modules,
    IReadOnlyList<UnsupportedConstructDiagnostic> UnsupportedConstructs)
{
    public int SilentMissCount => Modules.Sum(m => m.SilentMissCount);
}

public sealed record ModuleCoverage(
    string ModuleName,
    int ExpectedEndpointCount,
    int RoutedEndpointCount,
    int IntentionalOmissionCount,
    IReadOnlyList<EndpointCoverage> Endpoints)
{
    public int SilentMissCount => Endpoints.Count(e => e.Status == EndpointCoverageStatus.SilentMiss);
}

public sealed record EndpointCoverage(
    string ModuleName,
    string? HierarchyPath,
    string EndpointId,
    string SignalName,
    EndpointKind Kind,
    EndpointCoverageStatus Status,
    string Reason);

public enum EndpointCoverageStatus
{
    Routed,
    IntentionalOmission,
    Unsupported,
    SilentMiss
}
```

The exact shape can change during implementation, but the invariant cannot:

**Every non-routed endpoint has a reason.**

---

## 5. Coverage sources

### 5.1 XML/AST reader coverage

Track:

- port connection parsed as `?`,
- `<concat>` parts parsed or failed,
- `<sel>` ranges parsed or failed,
- dtype width unknown,
- struct dtype unresolved,
- unsupported XML expression node,
- expression reduced to fallback signal name,
- memory dimensions unknown,
- wide value unsupported.

### 5.2 Decoder coverage

Track:

- every `ContAssignAst`,
- whether it became a primitive,
- whether it was suppressed because another primitive owns it,
- whether it became an intentional no-op,
- whether it was unsupported.

Examples:

- `CondExpr` -> `MuxPrimitive`: routed.
- `ConcatExpr` -> `JoinerPrimitive`: routed.
- expression with unsupported op -> unsupported diagnostic.
- Verilator internal temp -> intentional omission if hidden by policy.

### 5.3 Builder coverage

Track:

- every boundary port,
- every child instance port,
- every primitive port,
- every concat synthetic port,
- every struct fan-out leg,
- producer/consumer registration,
- emitted edge count,
- dangling consumers,
- dangling producers.

Important distinction:

- A dangling output can be intentional if no consumer exists.
- A dangling input is suspicious unless constant-driven or explicitly unsupported.

---

## 6. Acceptance criteria

The phase closes only when:

- `SchematicCoverageReport` exists and is generated for every loaded design.
- arnicomp has zero silent misses.
- tiny_cpu has zero silent misses.
- bus_fabric has zero silent misses.
- memory_demo has zero silent misses or documented intentional memory limitations.
- At least three synthetic unsupported constructs produce explicit diagnostics.
- Tests fail if a new `?` signal silently reaches the builder.
- Tests fail if a primitive input has no producer and no omission reason.
- The UI exposes at least a minimal diagnostics list.

---

## 7. Tests

### Unit tests

New files:

- `tests/Bistable.Tests/Schematic/SchematicCoverageReportTests.cs`
- `tests/Bistable.Tests/Schematic/SchematicCoverageDecoderTests.cs`
- `tests/Bistable.Tests/ElkGraphBuilderCoverageTests.cs`
- `tests/Bistable.Tests/Ast/VerilatorXmlAstDiagnosticsTests.cs`

Cases:

- direct input-to-child input is routed,
- concat-bound child input has all legs routed,
- constant input is intentional omission,
- unused output is intentional omission,
- unknown `?` connection is unsupported or silent miss depending source,
- unsupported expression produces diagnostic,
- Verilator internal temp is intentional omission,
- wide signal probe limitation is diagnostic, not silent.

### Sample coverage tests

New file:

- `tests/Bistable.Tests/Schematic/SampleCoverageTests.cs`

Cases:

- arnicomp coverage silent miss count is zero,
- tiny_cpu coverage silent miss count is zero,
- bus_fabric coverage silent miss count is zero,
- memory_demo coverage silent miss count is zero or approved intentional omissions only.

### Regression tests

Bug-lock examples:

- concat-bound port cannot become `?` silently,
- struct field slice cannot collapse to base signal silently,
- generated instance array cannot render as unrelated duplicate boxes without diagnostic.

---

## 8. Non-goals

This phase does not:

- add search,
- add minimap,
- improve colors,
- add export,
- implement synthesis,
- fix every unsupported construct.

It creates the measurement system that tells us what to fix next.

---

## 9. Why this must precede RV32I and synthesis work

Without coverage reporting, an RV32I core may appear to load while silently dropping:

- branch control signals,
- register-file write enables,
- generated array instances,
- memory ports,
- interface fields,
- wide status buses.

That would waste time debugging the wrong layer. Coverage makes the tool honest.

---

## 10. Next phase dependency

Phase 5 (RV32I Execution Target) depends on this phase enough to identify unsupported constructs in the candidate RV32I sample. It does not need every unsupported construct fixed, but it needs complete visibility into what is unsupported.

---

## 11. Recent activity

- **2026-06-02 — P2.9-1 landed, P2.9-2 initial slice started.**
  - Added Core coverage model in `SchematicCoverageReport.cs`: `SchematicCoverageReport`, `ModuleCoverage`, `EndpointCoverage`, `UnsupportedConstructDiagnostic`, endpoint/status enums.
  - Added `SchematicCoverageAnalyzer` entry points for `Analyze(ModuleAst)` and `Analyze(ModuleAst, SchematicPrimitiveList)`.
  - The analyzer now catches:
    - contassign targets decoded into primitives (`Routed`),
    - unsupported contassign source expressions (`Unsupported` diagnostic, not silent),
    - primitive pins whose signal name resolved to `?` (`SilentMiss`),
    - Verilator internal targets (`IntentionalOmission`),
    - sequential assignments that did not decode into FF/latch primitives (`Unsupported` diagnostic).
  - Added `SchematicCoverageAnalyzerTests` with 5 cases covering routed, unsupported, silent-miss, intentional omission, and unsupported sequential behavior.
  - Validation: `dotnet build Bistable.slnx` green; `dotnet test tests/Bistable.Tests/Bistable.Tests.csproj -v minimal` green (`561/561`).

- **2026-06-02 — P2.9-3 ELK graph and routing telemetry landed.**
  - Added App-level `ElkGraphCoverageReport`, `ElkGraphCoverageDiagnostic`, and `ElkGraphCoverageAnalyzer`.
  - The analyzer walks nested `ElkGraph` nodes recursively and checks:
    - every `ElkPortRef.NodeId` resolves to an actual graph node,
    - every `ElkPortRef.PortId` resolves to an actual graph port,
    - every `ElkPortRef` owner node matches the graph port owner,
    - every edge has at least one source and target,
    - every edge endpoint resolves to a graph port rather than a missing ID or node ID,
    - duplicate graph shape IDs are reported as deterministic failures.
  - `ElkBuildResult` now carries `ElkRoutingTelemetry` with immutable producer/consumer maps, emitted edge count, and dangling signal counts.
  - `ElkGraphBuilder` now removes pruned orphan primitive ports from final `PortRefs` and telemetry, so coverage checks operate on the final graph rather than stale pre-prune state.
  - Dangling consumer signals are reported as warnings, not structural errors, so unsupported/missing inputs become visible without incorrectly failing legitimate unused outputs.
  - Added `ElkGraphBuilderCoverageTests` with 7 cases covering clean builder output, missing edge ports, node-ID edge endpoints, missing PortRef ports, recursive nested compound ports, dangling consumer warnings, and prune/PortRef consistency.
  - Validation: `dotnet build Bistable.slnx` green; `dotnet test tests/Bistable.Tests/Bistable.Tests.csproj -v minimal` green (`568/568`); `dotnet test Bistable.slnx -v minimal` green across Tests, Snapshots, Regression, and UiTests.

- **2026-06-02 — P2.9-4 initial fallback diagnostics and constant literal rendering landed.**
  - Added `VerilatorXmlAstDiagnostic` and `VerilatorXmlAstDiagnosticKind` plus `VerilatorXmlAstReader.LastDiagnostics`.
  - `VerilatorXmlAstReader` now records diagnostics when unsupported XML statement/expression/l-value elements fall back to skipped body, zero constant, or `__unknown__`.
  - Added `VerilatorXmlAstDiagnosticsTests` covering unknown expression and unknown statement fallback reporting with module context.
  - Updated schematic coverage for mux inputs rendered as `X`: they are now `Unsupported` diagnostics, not mislabeled as literal constants.
  - Literal constants connected directly to instance input ports now render as real ELK constant tie nodes and wires:
    - top-scope child input constants,
    - expanded-compound grandchild input constants.
  - Constant literal routing is width-aware, so different-width literal uses do not accidentally share one producer key.
  - Updated Arnicomp ELK snapshots because `1'h0` constant tie nodes are now present in the graph instead of being visually omitted.
  - Validation: `dotnet build Bistable.slnx` green; targeted coverage/constant/XML tests green (`26/26`); `dotnet test tests/Bistable.Tests/Bistable.Tests.csproj -v minimal` green (`574/574`); `dotnet test Bistable.slnx -v minimal` green across Tests, Snapshots, Regression, and UiTests.

- **2026-06-02 — P2.9-5/P2.9-6 initial Arnicomp sample gate landed.**
  - Added `SchematicCoverageAnalyzer.Analyze(DesignAst)` so coverage can be computed across every module in a loaded design, not only one selected module.
  - Added `SampleCoverageTests` for `samples/arnicomp/.bistable/metadata/arnicomp_top.xml`.
  - Fixed `VerilatorXmlAstReader.ParseCase`: a `<caseitem>` containing only an expression label is now parsed as an empty labelled arm, not as an unknown `<const>` statement.
  - Current Arnicomp coverage gate:
    - `SilentMissCount = 0`
    - unsupported constructs, when present, are reported explicitly rather than hidden.
  - Validation: `dotnet build Bistable.slnx` green; targeted sample/coverage/XML tests green (`12/12`); `dotnet test tests/Bistable.Tests/Bistable.Tests.csproj -v minimal` green (`577/577`); `dotnet test Bistable.slnx -v minimal` green across Tests, Snapshots, Regression, and UiTests.

- **2026-06-03 — P2.9-7/P2.9-8/P2.9-9 landed.**
  - Added `SchematicCoverageNegativeTests` with 6 regression-style negative cases:
    - concat l-value containing `__unknown__`,
    - unsupported combinational block target handling,
    - multi-driver visibility,
    - boundary port routed baseline,
    - empty module baseline,
    - sequential array-cell write visibility.
  - Tightened concat l-value coverage so `{__unknown__, y}` produces:
    - `EndpointCoverageStatus.Unsupported`,
    - `UnsupportedConstructDiagnostic.ConstructKind = "ContAssignLValue"`,
    - `SilentMissCount = 0`.
  - Added `DiagnosticsWindow`, opened through `View > Schematic Coverage...`, with module filtering, endpoint status table, status totals, and unsupported construct diagnostics.
  - Documented the coverage report lifecycle and status semantics in `docs/ARCHITECTURE.md` and `docs/TESTING.md`.

- **2026-06-03 — P2.9-5/P2.9-6 completed across samples.**
  - Added `SchematicCoverageReportJson` in Core for stable, readable JSON coverage artifacts.
  - Extended `SampleCoverageTests` to generate fresh Verilator XML from sample project configs before analyzing coverage, so gates do not depend on stale checked-in metadata.
  - Sample silent-miss gates now cover:
    - `samples/arnicomp/`
    - `samples/tiny_cpu/`
    - `samples/bus_fabric/`
    - `samples/memory_demo/`
    - `samples/riscv_single_cycle/`
  - Added per-sample JSON artifact write/read roundtrip tests under temp output.
