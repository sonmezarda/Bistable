# Schematic Coverage Report

**Phase 2.9 deliverable, Phase 7 contract expansion.** A machine-readable view
of *what the renderer actually understood about your design.*

The schematic engine has always been allowed to skip constructs it couldn't render. The problem isn't skipping — the problem is **silent skipping**: a wire vanishes from the picture and you have no way to tell whether it was hidden on purpose, hidden because the renderer doesn't support that construct yet, or genuinely missing because of a bug.

The coverage report makes those three cases distinguishable by name.

---

## 1. Where it lives

- **In-memory model:** `Bistable.Core.Design.Schematic.SchematicCoverageReport`.
- **JSON serialization:** `SchematicCoverageReportJson.WriteAsync(path, report, …)` / `Deserialize(json)`.
- **Analyzer entry points:**
  - `SchematicCoverageAnalyzer.Analyze(DesignAst)` — full design.
  - `SchematicCoverageAnalyzer.Analyze(ModuleAst)` — single module (runs the decoder for you).
  - `SchematicCoverageAnalyzer.Analyze(ModuleAst, SchematicPrimitiveList)` — when you already have the decoded primitives.
- **GUI surface:** `View → Schematic Coverage…` opens a window with header pills (Routed / Intentional / Unsupported / Silent miss), a module list, a per-module endpoint table, and a per-construct diagnostic strip.

---

## 2. Status taxonomy

Every `EndpointCoverage` carries one of:

| Status | Meaning | How to react |
|---|---|---|
| `Routed` | A schematic primitive owns this endpoint (FF, mux, contassign-derived buffer, memory tile, boundary port…). | None — this is the happy path. |
| `IntentionalOmission` | The renderer hides this endpoint on purpose (Verilator internal `__V…` signals, literal constants tied to a primitive input, etc.). | None unless you find a case that's incorrectly classified as intentional. |
| `Unsupported` | The renderer recognised the construct but can't draw it yet. A `UnsupportedConstructDiagnostic` is also emitted. | If the construct matters for your design, file an issue or extend the decoder. |
| `SilentMiss` | The analyzer expected an endpoint, the decoder produced no primitive, and no Unsupported diagnostic explains why. | **Bug.** This is the load-bearing guarantee — silent misses must stay at 0 on every sample. |

`SchematicCoverageReport.SilentMissCount == 0` is asserted across every bundled sample in `SampleCoverageTests` (arnicomp, tiny_cpu, bus_fabric, memory_demo, riscv_single_cycle).

---

## 3. Lifecycle

```
DesignAst ─► TempFolder ─► CombinationalProjector ─► SchematicDecoder.Decode(module)
                              │                            │
                              └─ synthetic ContAssignAst ─┘
                                                        │
                                                        ▼
                                       (Logic + Ports + Signals + CoverageEvents)
                                                        │
                                                        ▼
                          SchematicCoverageAnalyzer.Analyze(module, primitives)
                                                        │
                                                        ▼
                                       SchematicCoverageReport
                                       ├── ModuleCoverage[] (per-module endpoint tables)
                                       └── UnsupportedConstructDiagnostic[]
                                                        │
                                                        ▼
                              ┌─────────────────────────┴─────────────────────────┐
                              ▼                                                   ▼
                  SchematicCoverageReportJson.WriteAsync                DiagnosticsWindow (GUI)
                  (CI artifact / external tooling)                      (interactive triage)
```

**Two paths into the analyzer:**

1. **Decoder-driven (preferred).** When the decoder emits `SchematicDecoderCoverageEvent`s (`SchematicPrimitiveList.CoverageEvents`), the analyzer consumes them directly. This is the path that decoder-level instrumentation (P2.9-2) feeds, and it's how new primitive types stay honest without the analyzer having to re-derive what they did.
2. **Fallback analysis.** When the decoder doesn't emit events (older sites,
   simpler tests), the analyzer walks the AST's contassigns + sequential blocks
   itself and matches them against the primitives' output signals.

Phase 7 adds a mandatory third layer: every `CombinationalBlockAst` carries
projector results. The analyzer emits `CombinationalTarget` and
`CombinationalRead` endpoints from those results. A raw/unprojected block is a
pipeline violation and is reported as `Unsupported`, never ignored.

Both paths produce the same shape of report.

---

## 4. Reading a report (CLI / JSON)

```jsonc
{
  "topModule": "riscv_single_cycle_top",
  "modules": [
    {
      "moduleName": "riscv_single_cycle_top",
      "endpoints": [
        {
          "moduleName": "riscv_single_cycle_top",
          "endpointId": "port:clk",
          "signalName": "clk",
          "kind": "BoundaryPort",
          "status": "Routed",
          "reason": "Boundary port decoded."
        },
        {
          "moduleName": "riscv_single_cycle_top",
          "endpointId": "contassign:7:mem_write",
          "signalName": "mem_write",
          "kind": "ContAssignTarget",
          "status": "Unsupported",
          "reason": "Unsupported contassign source expression 'CondExpr'."
        }
      ]
    }
  ],
  "unsupportedConstructs": [
    {
      "moduleName": "riscv_single_cycle_top",
      "constructId": "contassign:7:mem_write",
      "constructKind": "ContAssign",
      "reason": "Unsupported contassign source expression 'CondExpr'."
    }
  ]
}
```

The flat `endpointId` is stable across runs as long as the source's structural ordering doesn't change — useful as a CI baseline (`schematic-coverage-baseline.json` checked into the repo, regenerate when intentionally accepting changes).

---

## 5. Adding coverage for a new primitive

When you add a new decoder rule (FFType / SpecialMux / …):

1. Emit a `SchematicDecoderCoverageEvent` for every endpoint the new primitive owns. Set `Status = Routed` for the wires it draws, `Status = Unsupported` (with `UnsupportedConstructKind` populated) when you bail out, and `Status = IntentionalOmission` for things you deliberately hide.
2. Register the primitive's output signal in `CollectRoutedTargets` if you keep the fallback-analyzer code path alive (it's a small switch).
3. Add a fixture test under `tests/Bistable.Tests/Schematic/` along the pattern of `SchematicCoverageAnalyzerTests` (one routed case + one negative case at minimum).
4. Run `dotnet test --filter "FullyQualifiedName~Coverage"` — the 47-test suite must stay green, plus your new ones.

---

## 6. The load-bearing guarantee

The single rule the analyzer enforces, repeated for emphasis:

> An endpoint may be unsupported, but it must never disappear silently.

For procedural logic this is stronger: every combinational/sequential driver
target, and every combinational read that contributes to a projected target,
must be owned by a schematic primitive or have an explicit
`UnsupportedConstructDiagnostic`. Verilator-internal `__V*` endpoints are the
only intentional-omission exception.

If `SilentMissCount > 0` for any bundled sample, the corresponding `SampleCoverageTests` fixture fails CI. That's the gate that keeps every later phase (Phase 5 CPU run, Phase 6 synthesis comparison, OoO core renderer) from drifting into invisible-wire territory.
