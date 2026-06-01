# Testing Guide

This document is the single source of truth for how tests are organized and run in this project. If you change how tests work, update this file in the same PR.

## 1. Test projects

| Project | Purpose | Framework | Network/Tools needed |
|---------|---------|-----------|-----------------------|
| `tests/Bistable.Tests/` | Unit + algorithmic tests of `Bistable.Core`, `Bistable.Verilator`, ELK builder, routing | xUnit | Verilator (for integration subset) |
| `tests/Bistable.Regression/` | One test per known bug — locks down fixes. **Failing test ships with the fix in the same PR.** | xUnit | None (data-driven from arnicomp + others) |
| `tests/Bistable.Snapshots/` | Golden-file ELK graph snapshots per sample project | xUnit + custom `SnapshotAssert` helper | Verilator |
| `tests/Bistable.UiTests/` | Headless Avalonia tests of MainWindow / VM interactions | xUnit + `Avalonia.Headless.XUnit` | Verilator (some tests) |

Run all: `dotnet test` from the repo root. Run a single project: `dotnet test tests/Bistable.Regression`.

## 2. Conventions

### Naming

- Test methods: `<Subject>_<Condition>_<ExpectedResult>` (e.g. `Concat_TwoSourceAssign_RendersJoinerNode`).
- Test files: `<TypeUnderTest>Tests.cs` (e.g. `ElkGraphBuilderTests.cs`). One test file per production class is the default.
- Snapshot files: `tests/Bistable.Snapshots/golden/<sample>-<scope>.json` (e.g. `golden/arnicomp-top.json`, `golden/arnicomp-arnicomp_top.reg_marl.json`).

### Test categories (xUnit Traits)

```csharp
[Trait("Category", "Integration")]   // touches a real worker or Verilator
[Trait("Category", "Snapshot")]      // compares against golden file
[Trait("Category", "UI")]            // headless Avalonia
[Trait("Category", "Regression")]    // bug-locking
```

In CI: `dotnet test --filter "Category!=UI"` for the fast path; full path runs everything.

## 3. Regression tests (bug-locking)

**Rule:** Every bug a user reports gets a regression test in `tests/Bistable.Regression/` *in the same PR* as the fix.

**Workflow:**

1. Identify the symptom. Write a test that reproduces it (test must fail before the fix).
2. Apply the fix.
3. Confirm the test now passes.
4. Add a row to `docs/PHASES/PHASE-<N>.md` under "regression tests added".

The current list of locked-down bugs is in `docs/PHASES/PHASE-0.md` Section 2.

## 4. Golden snapshot tests

We capture the deterministic JSON output of `ElkGraphBuilder.Build()` for each sample project and compare future runs against the captured snapshot.

### Helper API

```csharp
using Bistable.Snapshots;

SnapshotAssert.MatchesJson(
    snapshotName: "arnicomp-top",
    actual: elkGraph,
    serializerOptions: SnapshotJsonOptions.Default);   // stable keys, sorted dicts
```

On mismatch: writes `<name>.actual.json` next to the golden file and throws with a structured diff. Reviewer compares `actual.json` against `golden/<name>.json` and either fixes the code or accepts the new snapshot.

### Regenerating snapshots

Snapshots are NEVER auto-updated. To accept a new snapshot:

```bash
BISTABLE_REGENERATE_SNAPSHOTS=1 dotnet test tests/Bistable.Snapshots
```

This overwrites the golden files. **Always inspect the diff before committing.** A snapshot churn is a real review event.

### What to snapshot

- Per sample, the root scope's `ElkGraph` (after `Build` but before `Layout` — pre-layout is more deterministic).
- For each expanded scope in arnicomp: one snapshot per non-trivial scope.
- The output of `LegacyDesignFlattener` (Phase 1+).
- The `DesignAst` JSON dump (Phase 1+).

## 5. Headless UI tests (Avalonia)

### Setup

`tests/Bistable.UiTests/Bistable.UiTests.csproj` references:

```xml
<PackageReference Include="Avalonia.Headless.XUnit" Version="..." />
<PackageReference Include="Avalonia.Headless" Version="..." />
```

`App.axaml.cs` is the test application entry. Apps are built via `AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })`.

### Fixtures

`MainWindowHeadlessFixture` loads a sample project end-to-end:

```csharp
public sealed class MainWindowHeadlessFixture : IDisposable
{
    public MainWindowViewModel Vm { get; }
    // ctor: builds workspace, loads sample, awaits design ready
}
```

Tests inherit `[Collection("Headless")]` to share fixtures across the suite.

### What to test

- Selecting a hierarchy node → SchematicPreview ELK graph contains the expected ports.
- Clicking a wire (simulated via direct VM call to `SelectedSchematicSignalName`) → probe panel shows correct value, including for slice/submodule-output signals.
- Entering sub-sim → trace + hierarchy swapped; exiting → restored.
- Expansion: `ToggleSchematicExpansion("path")` → builder emits internal edges for that compound.

## 6. Integration tests (full Verilator)

Existing in `tests/Bistable.Tests/VerilatorIntegrationTests.cs`. These require `verilator` on PATH.

In CI: `apt-get install verilator` is installed. Locally: ensure `which verilator` works (Ubuntu: `apt`, Mac: `brew install verilator`, Windows: WSL recommended).

## 7. Coverage

CI runs `dotnet test --collect:"XPlat Code Coverage"` and uploads the cobertura XML as an artifact. Local: same command, output under `tests/**/TestResults/`.

Target: every public production type has at least one test that touches its happy path.

## 8. Performance tests

Mark slow tests with `[Trait("Speed", "Slow")]`. CI runs `dotnet test --filter "Speed!=Slow"` for the fast path on PRs; `Slow` runs nightly.

## 9. Adding a test — checklist

- [ ] Pick the right project (`Tests`, `Regression`, `Snapshots`, `UiTests`).
- [ ] Use the naming convention.
- [ ] Add `[Trait("Category", ...)]` if it's not a plain unit test.
- [ ] Run the test locally; confirm it fails for the right reason (for regression) or passes (for new feature).
- [ ] Update the relevant `docs/PHASES/PHASE-<N>.md` task table.
- [ ] If you added a fixture or helper, document it here.
