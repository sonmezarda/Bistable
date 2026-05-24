# Signal Fan-out & Packed-Struct Field Rendering — Design Spec

**Status:** Draft spec for a future Phase 2 sub-task (`P2-11`, deferred from 2026-05-24)
**Driver:** Real-world readability when packed structs (e.g., arnicomp's `control_pkg::control_t`) carry many fields and connect to many consumers. The current renderer collapses every consumer of `control_pins` into edges that all originate from the same boundary pin, which visually merges into one fat line — losing the per-field structure.
**Owner phase:** Phase 2 (Schematic Builder).
**Depends on:** Phase 1 AST (`StructFieldLValue`, `BitSelectExpr` on packed structs), Phase 2 primitives + decoder, P2-4d/P2-8 rendering pipeline.

---

## 1. The problem we are solving

### 1.1 What the user sees today

Open `samples/arnicomp/` and select the top scope. The `control_pins` boundary input
(a packed `control_pkg::control_t` struct) connects to many consumers:

- `alu_i.ops`        ← `control_pins.ops`       (2 bits)
- `acc_we.en`        ← `control_pins.acc_we`    (1 bit)
- `pc_we.en`         ← `control_pins.pc_we`     (1 bit)
- `mar_load.en`      ← `control_pins.mar_load`  (1 bit)
- `mar_step.en`      ← `control_pins.mar_step`  (1 bit)
- `mux_data_sel`     ← `control_pins.data_sel`  (1 bit)
- ... and so on

The Verilator XML serialises each connection as `<sel><varref name="control_pins"/>...</sel>`
(the legacy parser already handles this via the `control_pins`-as-base fallback).
The flattener emits a `DesignContAssign` whose source is `control_pins` for each
consumer.

`ElkGraphBuilder.AddEdges` then creates one edge per consumer, all sourced at
`boundary_in.control_pins` — they bunch up on the boundary pin and render as a
single thick "horse shoe" of overlapping wires. The user cannot tell which
consumer reads which field.

### 1.2 What the user should see

A familiar Vivado / Logisim convention:

```
            ┌──────── ops[1:0] ───────────► alu_i.ops
            │
control_pins├──────── acc_we ────────────► acc_we.en
   (struct) │
            ├──────── pc_we  ────────────► pc_we.en
            │
            ├──────── mar_load ──────────► mar_load.en
            │
            └──────── data_sel ──────────► mux_data_sel
```

A small **fan-out / splitter wedge** sits next to the boundary port. Each leg
of the wedge is labelled with the field name (and bit-range when applicable),
making it obvious which consumer reads which slice of the struct.

The same treatment applies to non-struct buses that simply have many readers —
e.g. a 16-bit `pc` bus driving 4 sinks should fan out at a junction, not all
overlap at the producer.

---

## 2. Scope

### 2.1 In scope

- **Packed struct field fan-out**: connections of the form `consumer ← struct.field`
  (Verilator XML `<sel><varref name="struct"/><const offset/><const width/></sel>`)
  should render with a per-field fan-out wedge labelled with the field name.
- **Bus fan-out (multi-reader)**: any signal with ≥ 2 consumers in the scope
  should optionally render with a fan-out junction node — even if the producer
  is a simple boundary pin or operator output.
- **Field name recovery**: the AST currently loses the SystemVerilog struct
  field name (it only knows the bit range). The decoder must consult `<typetable>`
  / `<packarraydtype>` / `<structdtype>` metadata to recover field names.

### 2.2 Out of scope

- Live values on fan-out legs (Phase 4 will add per-wire value display).
- Unpacked-struct fan-out (rare; defer to a follow-up).
- Generate-block fan-out (Phase 7 territory).
- Auto-collapsing fan-out wedges that become too tall (future polish).

---

## 3. AST extensions needed

### 3.1 Struct type metadata

Today the AST has no first-class concept of struct types. `BitSelectExpr` on a
packed struct works (the parser already recovers `control_pins` as the base),
but the **field name** is dropped — only `(lo, width)` survive.

Add to `Bistable.Core.Design.Ast`:

```csharp
public sealed record StructTypeDecl(
    string Name,                             // "control_pkg::control_t"
    int TotalWidth,
    IReadOnlyList<StructFieldDecl> Fields);

public sealed record StructFieldDecl(
    string FieldName,                        // "acc_we"
    int Lo,                                  // bit offset (LSB)
    int Width,                               // bit width
    string? TypeName);                       // forward-decl support

public sealed record SignalDecl(
    string Name,
    int Width,
    bool IsSigned,
    IReadOnlyList<BitRange> ArrayDims,
    bool IsRegistered = false,
    StructTypeDecl? StructType = null);      // NEW — set when the signal is a packed struct
```

`StructTypeDecl` lives at module-scope or netlist-scope (TBD — Verilator emits
typedefs at netlist root via `<typetable>`).

### 3.2 Reader changes (`VerilatorXmlAstReader`)

1. Parse `<typetable>` once at netlist load. Build a lookup
   `Dictionary<string, StructTypeDecl>` keyed by `dtype_id`.
2. When parsing a `<var>` whose `dtype_id` references a struct type, attach
   the resolved `StructTypeDecl` to `SignalDecl.StructType`.
3. When parsing `<sel>` over a struct-typed `<varref>`, instead of emitting
   `BitSelectExpr`, emit a new `StructFieldExpr(SignalRef baseRef, string fieldName)`.
   The field name is resolved by matching `(lo, width)` against the struct's
   `Fields`.

### 3.3 New expression node

```csharp
public sealed record StructFieldExpr(
    ExpressionAst Base,
    string FieldName,
    BitRange Range) : ExpressionAst;
```

`Range` is kept so the legacy flattener can still emit a `DesignContAssign`
with `SourceRange` populated — backwards-compatible.

The flattener maps `StructFieldExpr` → `DesignContAssign { SourceNames=[base], SourceRange=range, OperatorSymbol="." + FieldName }` (or a similar discriminator that the renderer can recognize as "this is a field access, render with the field name").

---

## 4. Decoder extensions (`SchematicDecoder`)

Add a new primitive:

```csharp
public sealed record StructFanOutPrimitive(
    string Id,
    string StructSignal,                     // "control_pins"
    StructTypeDecl Type,                     // resolved type metadata
    IReadOnlyList<StructFanOutLeg> Legs)     // one per field that is actually consumed
    : SchematicPrimitive(Id);

public sealed record StructFanOutLeg(
    string FieldName,
    BitRange Range,
    IReadOnlyList<string> Consumers);        // signal names of downstream sinks
```

Decoder workflow when processing a scope:

1. After collecting all `ContAssignAst` and `InstanceDecl` entries, scan for
   any expression / port connection that is `StructFieldExpr(SignalRef(s), field)`.
2. Group these by `(StructSignal, FieldName)`.
3. For each struct signal with ≥ 1 field access, emit a `StructFanOutPrimitive`
   listing every field used in the scope and its consumers.
4. The primitive replaces the per-consumer edges in the legacy contassign list
   (suppression similar to P2-4d's Gate/Arith pattern).

### 4.1 Bus fan-out without struct typing

For non-struct signals with ≥ 2 consumers, optionally emit a simpler
`BusFanOutPrimitive` (no field names; just labelled with the bit-range each
consumer reads):

```csharp
public sealed record BusFanOutPrimitive(
    string Id,
    string SourceSignal,
    int SourceWidth,
    IReadOnlyList<BusFanOutLeg> Legs) : SchematicPrimitive(Id);

public sealed record BusFanOutLeg(
    BitRange Range,                          // full width when no slice
    string ConsumerLabel);                   // e.g. "alu_i.a", or a wire name
```

This is gated on a user preference (`SchematicTheme.ShowBusFanOut`) since some
designs benefit from collapsed wires.

---

## 5. Renderer (`ElkGraphBuilder` + `SchematicPreviewControl.Symbols`)

### 5.1 New ELK node type

`AddStructFanOutNode` produces an ELK node similar to `SplitterPrimitive`'s
wedge, but with:

- **N labelled output ports** (one per consumed field), each port label =
  `"<field>[hi:lo]"` (e.g. `"ops[15:14]"`).
- **1 input port** on the west side, named after the struct.
- A tall body so the legs spread vertically without overlapping.

ID prefix: `fanout_<struct>`.

Endpoint registration (`CollectStructFanOutEndpoints`):

- Input → consumes the struct signal (so the boundary input drives it).
- Each output → produces the unique fan-out leg key
  `"fanout::<struct>.<field>"`, and `ExpandConsumersThroughContAssigns` is
  extended so that any consumer reading `struct.field` is rewritten to consume
  this leg key instead.

### 5.2 Symbol drawing

`DrawElkStructFanOutNode` in `SchematicPreviewControl.Symbols.cs`:

- Trapezoid silhouette (wider on the output side — the inverse of the existing
  splitter wedge).
- Field labels written next to each east-side port (right-aligned inside the
  trapezoid; same convention as the existing splitter).
- The struct name written above the node.

Visual reference:

```
control_pins ▷───┐
                  ╲   ops[1:0]    ►
                   ╲  acc_we      ►
                    ▶ pc_we       ►
                   ╱  mar_load    ►
                  ╱   data_sel    ►
                 ╱
```

### 5.3 Bus fan-out node (BusFanOutPrimitive)

Renders as a simple junction dot with N labelled legs — much smaller than the
struct fan-out wedge. Used when the producer is a single signal and consumers
just need to be distinguished.

---

## 6. Backwards-compatibility

- When `StructType` is null on a `SignalDecl`, behaviour is exactly as today
  (struct fan-out primitive is not emitted; legacy `<sel>`-as-bit-select path
  runs).
- The fan-out primitives are gated behind two preferences:
  - `SchematicTheme.EnableStructFanOut` (default ON when struct metadata is
    available)
  - `SchematicTheme.EnableBusFanOut`    (default OFF; opt-in for now)
- All existing snapshot golden files remain valid because no test currently
  exercises a struct type; they will need regeneration only when `arnicomp`
  snapshot tests are added with the feature enabled.

---

## 7. Testing plan

### 7.1 Unit tests

- **Decoder**:
  - `StructFanOutPrimitive_GroupsFieldAccesses_PerStructSignal`
  - `StructFanOutPrimitive_LegsListAllConsumers_PerField`
  - `StructFanOutPrimitive_NotEmitted_WhenStructTypeIsNull`
  - `BusFanOutPrimitive_EmittedWhenSignalHasMultipleConsumers_AndPrefEnabled`

- **Builder**:
  - `StructFanOutNode_OneInputManyOutputs_PortLabelsMatchFieldNames`
  - `StructFanOutNode_LegSuppressesLegacyPerConsumerContAssign` (mirror of
    P2-4d's Gate suppression)
  - `BusFanOutNode_PortsCountEqualsConsumerCount`

- **Symbol drawing**: snapshot-based (port-label content + geometry placement
  asserted via the existing `ElkGraphSnapshotProjector`).

### 7.2 Integration tests

- End-to-end XML → AST → Decode → Build snapshot for a synthetic struct
  example (`tests/Bistable.Snapshots/golden/elk-primitive-struct-fanout.json`).
- Regression snapshot for `arnicomp`-style control_pkg pattern.

### 7.3 Headless UI

- `MainWindowHeadlessFixture` test: load arnicomp, select the top scope,
  assert that `boundary_in.control_pins` has exactly one outgoing edge
  (to the fan-out node) instead of N edges (one per consumer).

---

## 8. Implementation order (estimate: 1.5–2 weeks)

1. **AST extensions** (P2-11-1, ~1 day): `StructTypeDecl`, `StructFieldDecl`,
   `StructFieldExpr`, `SignalDecl.StructType`. Pure data; tests for the
   record types.
2. **Reader**: parse `<typetable>` + struct metadata (P2-11-2, ~2 days). Cover
   the dtype-id forward-reference case.
3. **Flattener**: emit a discriminating `DesignContAssign.OperatorSymbol`
   (e.g. `".acc_we"`) when source is `StructFieldExpr` (P2-11-3, ~1 day).
4. **Decoder**: emit `StructFanOutPrimitive` from grouped struct field accesses
   (P2-11-4, ~2 days). Unit tests.
5. **Builder**: `AddStructFanOutNode` + `CollectStructFanOutEndpoints` +
   suppression logic (P2-11-5, ~2 days). Builder tests.
6. **Symbol drawing**: `DrawElkStructFanOutNode` (P2-11-6, ~1 day).
7. **Bus fan-out** (P2-11-7, ~2 days): optional, gated by preference. Tests.
8. **Integration + snapshots** (P2-11-8, ~1 day): per-sample golden files.
9. **Docs + PHASE-2.md update** (~half day).

---

## 9. Open design questions

- **Where do struct typedefs live in `DesignAst`?** Options:
  - On `DesignAst` itself (`IReadOnlyDictionary<string, StructTypeDecl> Types`).
  - Inlined into each `SignalDecl.StructType` (denormalized, simpler).
  - Recommendation: **inlined** for simplicity, with a dedup pass during decode
    if memory becomes a concern.
- **What if the same field is consumed both directly (`x.f`) and as part of a
  wider slice (`x[hi:lo]`)?** The fan-out leg should list both consumers under
  the appropriate field; the wider-slice consumer is treated as a multi-field
  consumer (rendered with a comma-separated label).
- **Should the fan-out wedge be expandable** (collapse to single line when
  there are < 3 consumers)? Defer to a follow-up; default is always show when
  enabled.
- **Verilator `--public-flat-rw` interaction with struct probes (Phase 3)**:
  needs verification that struct-field hot reads work for the fan-out legs.

---

## 10. Pointers for the implementing agent

- The legacy parser already handles `<sel><varref name="control_pins"/></sel>`
  in `VerilatorXmlParser.ParseInstancePortConnection` (line ~225). The new
  reader work is to *add* field-name recovery on top of that fallback.
- The current `SchematicDecoder` lives at
  [src/Bistable.Core/Design/Schematic/SchematicDecoder.cs](src/Bistable.Core/Design/Schematic/SchematicDecoder.cs).
  Adding the struct-fan-out grouping pass is a new function called from
  `Decode` after the existing per-contassign loop.
- The legacy splitter rendering already handles the inverse wedge geometry
  ([DrawElkSplitterNode](src/Bistable.App/Views/SchematicPreviewControl.Elk.cs)
  near line 306). The new fan-out wedge can reuse the geometry helpers.
- The pub/sub edge model in `ElkGraphBuilder.AddEdges` already supports
  redirecting consumers via `ExpandConsumersThroughContAssigns`. Adding a new
  consumer-rewrite pass for struct fan-out legs follows the same pattern.

---

## 11. Acceptance criteria

- Opening `samples/arnicomp/` and selecting the top scope shows `control_pins`
  with a fan-out wedge to its consumers (no overlapping wires at the boundary
  pin).
- Each fan-out leg carries the field name in its label (e.g. "ops[1:0]",
  "acc_we", "pc_we", "data_sel").
- Existing tests (currently 283) continue to pass without regression.
- ≥ 15 new tests covering decoder, builder, and integration paths.
- `docs/PHASES/PHASE-2.md` task board updated with a closed P2-11 row.
