# Design IR — AST Specification

**Namespace:** `Bistable.Core.Design.Ast`
**Status:** Approved spec — Phase 1, task P1-1 (written 2026-05-23)
**Next step:** Implement C# records in `src/Bistable.Core/Design/Ast/*.cs` (task P1-2)
**Owner phase:** Phase 1 — Design IR & Parser AST

---

## 1. Purpose and scope

The Design IR (intermediate representation) is a backend-agnostic Abstract Syntax Tree (AST) that captures the full structural and behavioral content of an elaborated hardware design. It is the contract between:

- **Front-end readers** (e.g., `VerilatorXmlAstReader` in `Bistable.Verilator`) that parse tool-specific outputs into this AST.
- **Back-end consumers** (e.g., schematic builder in Phase 2, probe enumerator in Phase 3, FSM detector in Phase 7) that operate on this neutral representation.

**What the AST captures:**
- Module hierarchy (ports, parameters, local signals, sub-instances)
- Continuous assignments (combinational logic, `assign` statements)
- Sequential blocks (`always @(posedge clk)` — flip-flops, latches)
- Combinational blocks (`always_comb`, `always @(*)`)
- Full expression trees (bit-select, concat, ternary, arithmetic, logic)
- Memory declarations (unpacked arrays)

**What the AST does NOT capture:**
- Layout or schematic geometry (handled by `ElkGraphBuilder`)
- Simulation values (handled by `SimulationWorkerClient`)
- Verilator-specific annotation (`loc`, `dtype_id` — parsed for type lookups but not stored in the AST)
- Initial blocks, final blocks (low priority — skipped in Phase 1)
- Function/task declarations (low priority — skipped in Phase 1)

---

## 2. Critical invariant: Verilator-agnostic naming

**Every type name, field name, and enum value in `Bistable.Core.Design.Ast` must be free of tool-specific terminology.**

| Banned term | Why banned | Neutral alternative used instead |
|-------------|------------|----------------------------------|
| `sentree` | Verilator XML element name | `Triggers` (a list of `EdgeTrigger`) |
| `varref` | Verilator XML element name | `SignalRef` (in `ExpressionAst`) |
| `contassign` | Verilator XML keyword | `ContAssignAst` (continuous assignment) |
| `assigndly` | Verilator XML keyword | `AssignAst { IsNonBlocking = true }` |
| `senitem` | Verilator XML element name | `EdgeTrigger` |
| `cond` | Verilator ternary XML element | `CondExpr` (conditional/ternary expression) |
| `concat` (as a type name) | Verilator XML element name | `ConcatExpr`, `ConcatLValue` |

A future Yosys or GHDL reader must be able to emit this same AST without mapping its own vocabulary to Verilator terms. If the mapping feels natural, the naming is neutral enough.

---

## 3. Node reference

### 3.1 Root: `DesignAst`

The top-level container for a parsed design.

```csharp
record DesignAst(
    IReadOnlyList<ModuleAst> Modules);
```

| Field | Type | Description |
|-------|------|-------------|
| `Modules` | `IReadOnlyList<ModuleAst>` | All modules in the design. The top module is the entry with `IsTop = true`. Order matches the Verilator XML `<module>` declaration order. |

**Source:** Verilator XML `<netlist>` element. All `<module>` children become entries in `Modules`.

**Example (arnicomp):** The arnicomp netlist contains ~15 module elements (one top + sub-modules for acc, alu, reg_marl, etc.). `DesignAst.Modules` will have 15 entries, exactly one with `IsTop = true`.

---

### 3.2 `ModuleAst`

One hardware module (Verilog `module` / SystemVerilog `module`).

```csharp
record ModuleAst(
    string Name,
    bool IsTop,
    IReadOnlyList<PortDecl> Ports,
    IReadOnlyList<DesignParameter> Parameters,
    IReadOnlyList<SignalDecl> LocalSignals,
    IReadOnlyList<InstanceDecl> Instances,
    IReadOnlyList<ContAssignAst> ContAssigns,
    IReadOnlyList<SequentialBlockAst> SequentialBlocks,
    IReadOnlyList<CombinationalBlockAst> CombinationalBlocks);
```

| Field | Type | Description |
|-------|------|-------------|
| `Name` | `string` | Module name as declared (e.g., `"arnicomp_top"`, `"acc"`). |
| `IsTop` | `bool` | True if `topModule="1"` appears on the Verilator XML `<module>` element. |
| `Ports` | `IReadOnlyList<PortDecl>` | All boundary signals: inputs, outputs, inout. |
| `Parameters` | `IReadOnlyList<DesignParameter>` | Parameter declarations (from `<var param="true">`). |
| `LocalSignals` | `IReadOnlyList<SignalDecl>` | Internal wire/reg declarations that are not ports. See §3.4 for memory handling. |
| `Instances` | `IReadOnlyList<InstanceDecl>` | Sub-module instantiations (`<instance>`). |
| `ContAssigns` | `IReadOnlyList<ContAssignAst>` | Continuous assignments (`assign target = expr`). |
| `SequentialBlocks` | `IReadOnlyList<SequentialBlockAst>` | Clocked always blocks (Verilator `<always>` with `<sentree>`). |
| `CombinationalBlocks` | `IReadOnlyList<CombinationalBlockAst>` | Combinational always blocks (Verilator `<always>` without `<sentree>`). |

**Source:** Verilator XML `<module name="..." topModule="...">`.

`InitialBlocks` and `FinalBlocks` are intentionally omitted in Phase 1. Unknown block types are skipped with a log warning (see §9).

---

### 3.3 `PortDecl`

A module port (input/output/inout boundary signal).

```csharp
record PortDecl(
    string Name,
    SignalDirection Direction,
    int Width,
    bool IsSigned,
    int PinIndex);
```

**Source:** Verilator XML `<var dir="input|output|inout" pinIndex="..." ...>` inside a `<module>`.

Maps 1:1 to existing `SignalPort`. During flattening, `PortDecl` → `SignalPort` with identical field values.

**Example (arnicomp top module):**
```xml
<var name="clk" dtype_id="bit" dir="input" pinIndex="1" vartype="logic"/>
<var name="pc_out" dtype_id="16" dir="output" pinIndex="2" vartype="logic"/>
```
→
```
PortDecl { Name="clk",   Direction=Input,  Width=1,  IsSigned=false, PinIndex=1 }
PortDecl { Name="pc_out", Direction=Output, Width=16, IsSigned=false, PinIndex=2 }
```

---

### 3.4 `SignalDecl`

A local signal (wire or register) that is not a module port. Includes memories (unpacked arrays).

```csharp
record SignalDecl(
    string Name,
    int Width,
    bool IsSigned,
    IReadOnlyList<BitRange> ArrayDims)
{
    public bool IsRegistered { get; internal set; }
}
```

| Field | Type | Description |
|-------|------|-------------|
| `Name` | `string` | Signal name as declared. |
| `Width` | `int` | Bit width of one element. For a memory, this is the width of a single cell, not the total. |
| `IsSigned` | `bool` | True if declared `signed`. |
| `ArrayDims` | `IReadOnlyList<BitRange>` | Empty for scalar/vector signals. Non-empty for unpacked arrays (memories). Each entry is one array dimension's range `[Hi:Lo]`. Dimensions are ordered outermost-first. |
| `IsRegistered` | `bool` | **Derived.** Set to `true` by a post-parse pass (see §7) if this signal is the target of any `AssignAst` inside a `SequentialBlockAst` in the same module. Always `false` at construction time. |

**Source:** Verilator XML `<var ...>` inside `<module>` where the `dir` attribute is absent and `param != "true"`. For memories: a referenced `<unpackarraydtype>` element determines `ArrayDims`.

**Example — scalar register (arnicomp `acc` module):**
```xml
<var name="acc_q" dtype_id="8" vartype="logic"/>
```
→ `SignalDecl { Name="acc_q", Width=8, IsSigned=false, ArrayDims=[], IsRegistered=false }`
After the IsRegistered pass: `IsRegistered=true` (because `acc_q` is the target of an `assigndly` inside `always @(posedge clk)`)

**Example — memory (hypothetical `memory_demo` module):**
```xml
<var name="mem" dtype_id="arr8x16" vartype="logic"/>
<!-- referenced dtype: <unpackarraydtype id="arr8x16" left="15" right="0">
                          <basicdtype id="8" left="7" right="0"/>
                       </unpackarraydtype> -->
```
→ `SignalDecl { Name="mem", Width=8, IsSigned=false, ArrayDims=[BitRange(Hi=15, Lo=0)], IsRegistered=false }`

---

### 3.5 `BitRange`

An inclusive bit range [Hi:Lo].

```csharp
readonly record struct BitRange(int Hi, int Lo)
{
    public int Width => Hi - Lo + 1;
    public override string ToString() => Hi == Lo ? $"[{Hi}]" : $"[{Hi}:{Lo}]";
}
```

This struct carries the same semantics as existing `DesignBitRange` (same fields, same `Width` computation). The flattener maps `BitRange` → `DesignBitRange` one-to-one when producing the flat compatibility output.

---

### 3.6 `InstanceDecl`

A sub-module instantiation.

```csharp
record InstanceDecl(
    string InstanceName,
    string ModuleName,
    IReadOnlyList<PortConnectionDecl> PortConnections);
```

**Source:** Verilator XML `<instance name="..." defName="...">` containing `<port>` children. `InstanceName` ← `name` attribute, `ModuleName` ← `defName` attribute.

**Example (arnicomp top instantiating acc):**
```xml
<instance name="acc" defName="acc" origName="acc">
  <port name="clk"    direction="in"  portIndex="1"><varref name="clk"/></port>
  <port name="acc_in" direction="in"  portIndex="2"><varref name="alu_out"/></port>
  <port name="acc_out" direction="out" portIndex="3"><varref name="acc_out"/></port>
</instance>
```
→
```
InstanceDecl {
  InstanceName = "acc",
  ModuleName   = "acc",
  PortConnections = [
    PortConnectionDecl { PortName="clk",     SignalName="clk",     Direction="in",  PortIndex=1 },
    PortConnectionDecl { PortName="acc_in",  SignalName="alu_out", Direction="in",  PortIndex=2 },
    PortConnectionDecl { PortName="acc_out", SignalName="acc_out", Direction="out", PortIndex=3 }
  ]
}
```

---

### 3.7 `PortConnectionDecl`

One port binding on a sub-module instance.

```csharp
record PortConnectionDecl(
    string PortName,
    string SignalName,
    string Direction,
    int PortIndex);
```

**Source:** Verilator XML `<port name="..." direction="..." portIndex="...">` inside `<instance>`.

`SignalName` extraction rules (in priority order):
1. Direct `<varref name="..."/>` child → use the `name` attribute.
2. `<sel><varref name="..."/>...</sel>` child → use the base `<varref>` name (packed-struct field access; same fallback as the legacy parser).
3. `<const name="..."/>` child → use the constant literal string.
4. Fallback: `"?"`.

**Example (packed-struct port connection from arnicomp):**
```xml
<port name="ops" direction="in" portIndex="1">
  <sel>
    <varref name="control_pins" dtype_id="struct"/>
    <const name="32'h14" dtype_id="bit32"/>
    <const name="32'h2"  dtype_id="bit32"/>
  </sel>
</port>
```
→ `PortConnectionDecl { PortName="ops", SignalName="control_pins", Direction="in", PortIndex=1 }`

---

### 3.8 `ContAssignAst`

A continuous assignment (`assign target = expression`).

```csharp
record ContAssignAst(
    LValueAst Target,
    ExpressionAst Source);
```

**Source:** Verilator XML `<contassign>`. Children are ordered [RHS expression] [LHS varref/lvalue]. The LHS target is always the last direct `<varref>` child (or a structured lvalue if the target is a bit-select or concat).

**Example 1 — simple wire alias:**
```xml
<contassign dtype_id="8">
  <varref name="instruction"/>
  <varref name="inst_q"/>
</contassign>
```
→ `ContAssignAst { Target=VarRefLValue("inst_q"), Source=SignalRef("instruction") }`

**Example 2 — bit-select splitter:**
```xml
<contassign dtype_id="2">
  <sel dtype_id="2">
    <varref name="bus"/>
    <const name="32'h6"/>
    <const name="32'h2"/>
  </sel>
  <varref name="slice_out"/>
</contassign>
```
→ `ContAssignAst { Target=VarRefLValue("slice_out"), Source=BitSelectExpr(SignalRef("bus"), BitRange(Hi=7, Lo=6)) }`

**Example 3 — concat joiner (from arnicomp):**
```xml
<contassign dtype_id="16">
  <concat dtype_id="16">
    <varref name="prh_out"/>
    <varref name="prl_out"/>
  </concat>
  <varref name="result"/>
</contassign>
```
→ `ContAssignAst { Target=VarRefLValue("result"), Source=ConcatExpr([SignalRef("prh_out"), SignalRef("prl_out")]) }`

**Example 4 — ternary mux in contassign:**
```xml
<contassign dtype_id="8">
  <cond dtype_id="8">
    <varref name="sel"/>
    <varref name="a"/>
    <varref name="b"/>
  </cond>
  <varref name="out"/>
</contassign>
```
→ `ContAssignAst { Target=VarRefLValue("out"), Source=CondExpr(SignalRef("sel"), SignalRef("a"), SignalRef("b")) }`

---

### 3.9 `SequentialBlockAst`

A clocked always block with one or more edge triggers. Represents flip-flops and latches.

```csharp
record SequentialBlockAst(
    IReadOnlyList<EdgeTrigger> Triggers,
    StatementAst Body,
    bool HasAsynchronousReset);
```

| Field | Type | Description |
|-------|------|-------------|
| `Triggers` | `IReadOnlyList<EdgeTrigger>` | One entry per `<senitem>` in the `<sentree>`. |
| `Body` | `StatementAst` | The statement tree (usually a `BeginAst` wrapping an `IfAst` for reset logic). |
| `HasAsynchronousReset` | `bool` | Heuristic: `true` when any trigger has `Edge = Falling` and the trigger signal name contains `"rst"`, `"reset"`, or `"n"` (case-insensitive). Set during parse, not in the IsRegistered post-pass. |

**Identification:** A Verilator XML `<always>` element containing a `<sentree>` child is a `SequentialBlockAst`. A `<always>` without `<sentree>` becomes a `CombinationalBlockAst`.

**Source:** Verilator XML `<always>` with `<sentree>` present.

**Example (from arnicomp — clocked FF with async reset):**

Verilog source:
```verilog
always @(posedge clk or negedge rst_n)
    inst_q <= rst_n ? instruction : 8'h0;
```

Verilator XML:
```xml
<always loc="...">
  <sentree>
    <senitem edgeType="POS"><varref name="clk"/></senitem>
    <senitem edgeType="NEG"><varref name="rst_n"/></senitem>
  </sentree>
  <begin>
    <assigndly dtype_id="8">
      <cond>
        <varref name="rst_n"/>
        <varref name="instruction"/>
        <const name="8'h0"/>
      </cond>
      <varref name="inst_q"/>
    </assigndly>
  </begin>
</always>
```

Resulting AST:
```
SequentialBlockAst {
  Triggers = [
    EdgeTrigger { Edge=Rising,  SignalName="clk"   },
    EdgeTrigger { Edge=Falling, SignalName="rst_n" }
  ],
  HasAsynchronousReset = true,
  Body = BeginAst {
    Statements = [
      AssignAst {
        IsNonBlocking = true,
        Target = VarRefLValue { Name="inst_q" },
        Source = CondExpr {
          Condition = SignalRef { Name="rst_n"      },
          IfTrue    = SignalRef { Name="instruction" },
          IfFalse   = ConstExpr { Value=0, Width=8, IsSigned=false }
        }
      }
    ]
  }
}
```

---

### 3.10 `EdgeTrigger`

A single sensitivity edge in a sequential block's sensitivity list.

```csharp
record EdgeTrigger(
    EdgeKind Edge,
    string SignalName);

enum EdgeKind { Rising, Falling, AnyChange }
```

**Source:** Verilator XML `<senitem edgeType="POS|NEG|BOTH">` containing a `<varref name="..."/>` child.

| `edgeType` attribute | `EdgeKind` |
|---------------------|------------|
| `POS` | `Rising` |
| `NEG` | `Falling` |
| `BOTH` | `AnyChange` |
| *(attribute absent)* | `AnyChange` |

---

### 3.11 `CombinationalBlockAst`

An always block without edge triggers. Represents combinational logic.

```csharp
record CombinationalBlockAst(StatementAst Body)
{
    [JsonIgnore]
    IReadOnlyList<CombinationalProjectionTarget>? ProjectionResults { get; init; }
}
```

**Source:** Verilator XML `<always>` without `<sentree>`, or an `<always>` whose `<sentree>` contains no `<senitem>` children with an `edgeType` attribute (e.g., `always_comb` is sometimes emitted with an empty sentree by Verilator).

After parsing, `CombinationalProjector` records one result per driven signal.
`null` means the block has not passed through the projector; an empty list means
it was processed and had no targets. The metadata is excluded from AST JSON.
Projected targets are also appended to `ModuleAst.ContAssigns` as synthetic
continuous assignments; the original block remains available for diagnostics.

---

## 4. Statement hierarchy

`StatementAst` is an abstract base type with four concrete sealed subtypes. In C#, use a sealed hierarchy (`abstract record StatementAst` + `sealed record X : StatementAst`).

```
StatementAst (abstract record)
├── BeginAst        — begin ... end block (sequence)
├── IfAst           — if/else conditional
├── CaseAst         — case/endcase switch
└── AssignAst       — single assignment (blocking or non-blocking)
```

### 4.1 `BeginAst`

A sequence of statements enclosed in `begin ... end`.

```csharp
sealed record BeginAst(IReadOnlyList<StatementAst> Statements) : StatementAst;
```

**Source:** Verilator XML `<begin>` element. Children are any mix of `<assigndly>`, `<assign>`, `<if>`, `<begin>`, `<case>`, `<casestmt>`.

---

### 4.2 `IfAst`

A conditional statement (`if (cond) ... else ...`).

```csharp
sealed record IfAst(
    ExpressionAst Condition,
    StatementAst Then,
    StatementAst? Else) : StatementAst;
```

**Source:** Verilator XML `<if>`. Children in order: [condition expression] [then branch statement] [optional else branch statement].

**Example (from arnicomp — write-enable gating inside an always block):**
```xml
<if dtype_id="...">
  <varref name="we"/>
  <begin>
    <assigndly dtype_id="8">
      <varref name="data_in"/>
      <varref name="reg_q"/>
    </assigndly>
  </begin>
</if>
```
→
```
IfAst {
  Condition = SignalRef { Name="we" },
  Then = BeginAst {
    Statements = [
      AssignAst { IsNonBlocking=true, Target=VarRefLValue("reg_q"), Source=SignalRef("data_in") }
    ]
  },
  Else = null
}
```

---

### 4.3 `CaseAst`

A case/switch statement (`case (subject) ... endcase`).

```csharp
sealed record CaseAst(
    ExpressionAst Subject,
    IReadOnlyList<CaseArm> Arms,
    StatementAst? Default) : StatementAst;

record CaseArm(ExpressionAst Label, StatementAst Body);
```

**Source:** Verilator XML `<case>` or `<casestmt>`. The first child is the subject expression. Subsequent `<caseitem>` children are arms; each `<caseitem>` with a `<const>` or expression child is a labeled arm. A `<caseitem>` with no expression child before its statement is the default arm (maps to `Default`).

---

### 4.4 `AssignAst`

A single assignment statement, blocking or non-blocking.

```csharp
sealed record AssignAst(
    LValueAst Target,
    ExpressionAst Source,
    bool IsNonBlocking) : StatementAst;
```

| `IsNonBlocking` | Verilator XML element | Verilog syntax |
|---|---|---|
| `false` | `<assign>` | `target = source;` (blocking) |
| `true` | `<assigndly>` | `target <= source;` (non-blocking) |

**Source layout (both `<assign>` and `<assigndly>`):** Children are ordered [RHS expression] [LHS target]. The last direct `<varref>` child (or structured lvalue element) is the LHS target.

**Example (non-blocking assign — the most common form in arnicomp):**
```xml
<assigndly dtype_id="8">
  <varref name="data_in"/>
  <varref name="reg_q"/>
</assigndly>
```
→ `AssignAst { IsNonBlocking=true, Target=VarRefLValue("reg_q"), Source=SignalRef("data_in") }`

---

## 5. LValue hierarchy

`LValueAst` represents the target (left-hand side) of an assignment. It is a sealed hierarchy.

```
LValueAst (abstract record)
├── VarRefLValue        — simple signal name
├── BitSelectLValue     — bit-range slice of a signal
├── ArraySelectLValue   — indexed element of a memory
├── ConcatLValue        — concatenated lvalue {a, b}
└── StructFieldLValue   — packed struct field (low priority)
```

### 5.1 `VarRefLValue`

```csharp
sealed record VarRefLValue(string Name) : LValueAst;
```

**Source:** A bare `<varref name="...">` appearing as the final direct child of `<assign>` or `<assigndly>`.

---

### 5.2 `BitSelectLValue`

```csharp
sealed record BitSelectLValue(string SignalName, BitRange Range) : LValueAst;
```

**Source:** A `<sel>` as the final child of `<assign>` or `<assigndly>`, where the first child `<varref>` names the signal and two `<const>` children give lo offset and width. `Hi = Lo + Width - 1`.

---

### 5.3 `ArraySelectLValue`

```csharp
sealed record ArraySelectLValue(string SignalName, ExpressionAst Index) : LValueAst;
```

**Source:** `<arraysel>` as the LHS of an assignment. The first `<varref>` child names the array signal; the subsequent expression child is the index.

---

### 5.4 `ConcatLValue`

```csharp
sealed record ConcatLValue(IReadOnlyList<LValueAst> Parts) : LValueAst;
```

**Source:** `<concat>` as the LHS of an assignment (e.g., `{a, b} = c;`). Each `<varref>` child of the `<concat>` becomes a `VarRefLValue` entry in `Parts`, listed MSB first.

---

### 5.5 `StructFieldLValue` (low priority)

```csharp
sealed record StructFieldLValue(string SignalName, string FieldName) : LValueAst;
```

Defer implementation until a sample requires it. If a structured LHS is encountered that cannot be decoded by the above rules, fall back to `VarRefLValue` using the base struct signal name, and log a `LogWarning`.

---

## 6. Expression hierarchy

`ExpressionAst` represents any right-hand side value: the source of an assignment, the condition of an `if`, the subject of a `case`, or a sub-expression.

```
ExpressionAst (abstract record)
├── SignalRef           — variable reference
├── ConstExpr           — integer literal
├── BitSelectExpr       — bit-range slice [Hi:Lo]
├── ArraySelectExpr     — memory element access
├── ConcatExpr          — bit concatenation {MSB, ..., LSB}
├── ReplicateExpr       — {N{pattern}}
├── ExtendExpr          — zero- or sign-extend
├── BinaryExpr          — two-operand operation
├── UnaryExpr           — one-operand operation
├── CondExpr            — ternary mux (cond ? ifTrue : ifFalse)
└── FunctionCallExpr    — system/user function call (low priority)
```

### 6.1 `SignalRef`

```csharp
sealed record SignalRef(string Name) : ExpressionAst;
```

**Source:** Verilator XML `<varref name="...">`. The `name` attribute is stored as-is. The `dtype_id` is used for width lookup during parsing but is not stored in the AST.

---

### 6.2 `ConstExpr`

```csharp
sealed record ConstExpr(
    System.Numerics.BigInteger Value,
    int Width,
    bool IsSigned) : ExpressionAst;
```

**Source:** Verilator XML `<const name="N'[h|b|d]value">`. The `name` attribute encodes width (in bits), base (`h`/`b`/`d`), and value. The `dtype_id` is consulted if width is not determinable from the literal alone.

Parsing examples:
- `"8'h0"` → `ConstExpr { Value=0, Width=8, IsSigned=false }`
- `"32'hFF"` → `ConstExpr { Value=255, Width=32, IsSigned=false }`
- `"4'b1010"` → `ConstExpr { Value=10, Width=4, IsSigned=false }`
- `"32'd12"` → `ConstExpr { Value=12, Width=32, IsSigned=false }`

Use `BigInteger` to handle constants wider than 64 bits (e.g., 128-bit bus constants).

---

### 6.3 `BitSelectExpr`

```csharp
sealed record BitSelectExpr(ExpressionAst Base, BitRange Range) : ExpressionAst;
```

**Source:** Verilator XML `<sel>`. Children in order: [base expression] [lo const] [width const]. `Hi = Lo + Width - 1`.

`<sel>` is also used by Verilator for packed-struct field access: the base `<varref>` names the struct variable, and the two `<const>` children give the field's bit offset and width within the struct. The AST treats this identically to a bit-range slice.

**Example (from arnicomp — bus slice):**
```xml
<sel dtype_id="2">
  <varref name="bus"/>
  <const name="32'h6"/>
  <const name="32'h2"/>
</sel>
```
→ `BitSelectExpr { Base=SignalRef("bus"), Range=BitRange(Hi=7, Lo=6) }`

---

### 6.4 `ArraySelectExpr`

```csharp
sealed record ArraySelectExpr(ExpressionAst Base, ExpressionAst Index) : ExpressionAst;
```

**Source:** Verilator XML `<arraysel>`. First child is the base expression (usually a `<varref>` naming the array signal); second child is the index expression.

**Example (memory read at a dynamic address):**
```xml
<arraysel dtype_id="8">
  <varref name="mem"/>
  <varref name="addr"/>
</arraysel>
```
→ `ArraySelectExpr { Base=SignalRef("mem"), Index=SignalRef("addr") }`

Note: arnicomp has 0 `<arraysel>` occurrences. This node type is required for the planned `memory_demo` sample and any design with unpacked arrays.

---

### 6.5 `ConcatExpr`

```csharp
sealed record ConcatExpr(IReadOnlyList<ExpressionAst> Parts) : ExpressionAst;
```

**Source:** Verilator XML `<concat>`. Children are listed MSB first. There are 11 occurrences in arnicomp.

**Example (from arnicomp — two-part bus join):**
```xml
<concat dtype_id="16">
  <varref name="prh_out"/>
  <varref name="prl_out"/>
</concat>
```
→ `ConcatExpr { Parts=[SignalRef("prh_out"), SignalRef("prl_out")] }`

`prh_out` occupies the upper bits; `prl_out` occupies the lower bits.

---

### 6.6 `ReplicateExpr`

```csharp
sealed record ReplicateExpr(int Count, ExpressionAst Pattern) : ExpressionAst;
```

**Source:** Verilator XML `<replicate>`. First child is a `<const>` giving the replication count; second child is the pattern expression.

---

### 6.7 `ExtendExpr`

```csharp
sealed record ExtendExpr(
    ExpressionAst Inner,
    int TargetWidth,
    bool IsSigned) : ExpressionAst;
```

**Source:** Verilator XML `<extend>` (zero-extend) or `<extendS>` (sign-extend). `TargetWidth` is resolved from the `dtype_id` attribute lookup.

---

### 6.8 `BinaryExpr`

```csharp
sealed record BinaryExpr(
    BinaryOp Op,
    ExpressionAst Left,
    ExpressionAst Right) : ExpressionAst;

enum BinaryOp
{
    Add, Sub, Mul, Div, Mod,
    And, Or, Xor,
    LogicAnd, LogicOr,
    Equal, NotEqual,
    LessThan, GreaterThan, LessOrEqual, GreaterOrEqual,
    ShiftLeft, ShiftRight, ShiftRightArithmetic
}
```

**Source:** Verilator XML binary operation elements. Two children: first is `Left`, second is `Right`.

| XML element | `BinaryOp` | Legacy `OperatorSymbol` |
|-------------|------------|------------------------|
| `add` | `Add` | `"+"` |
| `sub` | `Sub` | `"-"` |
| `mul` | `Mul` | `"*"` |
| `div` | `Div` | `"/"` |
| `moddiv` | `Mod` | `"%"` |
| `and` | `And` | `"&"` |
| `or` | `Or` | `"|"` |
| `xor` | `Xor` | `"^"` |
| `logand` | `LogicAnd` | `"&&"` |
| `logor` | `LogicOr` | `"||"` |
| `eq` | `Equal` | `"="` |
| `neq` | `NotEqual` | `"≠"` |
| `lt` | `LessThan` | `"<"` |
| `gt` | `GreaterThan` | `">"` |
| `lte` | `LessOrEqual` | `"≤"` |
| `gte` | `GreaterOrEqual` | `"≥"` |
| `shiftl` | `ShiftLeft` | `"<<"` |
| `shiftr` | `ShiftRight` | `">>"` |
| `shiftrs` | `ShiftRightArithmetic` | `">>>"` |

The "Legacy `OperatorSymbol`" column is the exact string the `LegacyDesignFlattener` must emit for `DesignContAssign.OperatorSymbol`. These strings must match `VerilatorXmlParser.DetectOperatorSymbol` output exactly to avoid breaking existing snapshot and regression tests.

---

### 6.9 `UnaryExpr`

```csharp
sealed record UnaryExpr(UnaryOp Op, ExpressionAst Operand) : ExpressionAst;

enum UnaryOp
{
    Not,        // bitwise NOT  (~)
    LogicNot,   // logical NOT  (!)
    Negate,     // arithmetic negation (unary -)
    ReduceAnd,  // reduction &
    ReduceOr,   // reduction |
    ReduceXor   // reduction ^
}
```

**Source:** Verilator XML unary operation elements. One child: the operand.

| XML element | `UnaryOp` | Legacy `OperatorSymbol` |
|-------------|----------|------------------------|
| `not` | `Not` | `"~"` |
| `lognot` | `LogicNot` | `"!"` |
| `negate` | `Negate` | `"-"` (unary; context distinguishes from `BinaryOp.Sub`) |
| `redand` | `ReduceAnd` | `"&"` |
| `redor` | `ReduceOr` | `"|"` |
| `redxor` | `ReduceXor` | `"^"` |

---

### 6.10 `CondExpr`

```csharp
sealed record CondExpr(
    ExpressionAst Condition,
    ExpressionAst IfTrue,
    ExpressionAst IfFalse) : ExpressionAst;
```

**Source:** Verilator XML `<cond>`. Children in order: [condition] [if-true branch] [if-false branch]. There are 23 occurrences in arnicomp — this is the most frequent non-trivial expression, and every schematic mux in Phase 2 will derive from it.

**Example (from arnicomp — async reset mux inside always block):**
```xml
<cond dtype_id="8">
  <varref name="rst_n"/>
  <varref name="instruction"/>
  <const name="8'h0"/>
</cond>
```
→ `CondExpr { Condition=SignalRef("rst_n"), IfTrue=SignalRef("instruction"), IfFalse=ConstExpr(0,8,false) }`

**Example (nested CondExpr — 3:1 mux, as seen in arnicomp ALU):**
```xml
<cond dtype_id="8">
  <varref name="sel1"/>
  <varref name="a"/>
  <cond dtype_id="8">
    <varref name="sel0"/>
    <varref name="b"/>
    <varref name="c"/>
  </cond>
</cond>
```
→
```
CondExpr {
  Condition = SignalRef("sel1"),
  IfTrue    = SignalRef("a"),
  IfFalse   = CondExpr {
    Condition = SignalRef("sel0"),
    IfTrue    = SignalRef("b"),
    IfFalse   = SignalRef("c")
  }
}
```
This nesting is valid and expected. Phase 2 will collapse nested `CondExpr` chains into an N:1 mux primitive.

---

### 6.11 `FunctionCallExpr` (low priority)

```csharp
sealed record FunctionCallExpr(
    string Name,
    IReadOnlyList<ExpressionAst> Args) : ExpressionAst;
```

Defer: emit only when a `<funcref>` or `<ccall>` element is encountered. If encountered in Phase 1, log a `LogWarning` and substitute `ConstExpr(Value=0, Width=1, IsSigned=false)` as a placeholder so parsing continues.

---

## 7. Registered signal detection policy

`SignalDecl.IsRegistered` is a **derived property** computed in a post-parse pass after all modules are assembled.

### 7.1 Algorithm

After `ModuleAst` is fully constructed (all `SequentialBlocks` populated):

1. For each `SequentialBlockAst` in `module.SequentialBlocks`, recursively walk the `Body` statement tree.
2. Collect the signal name from every `AssignAst.Target`:
   - `VarRefLValue.Name`
   - `BitSelectLValue.SignalName`
   - `ArraySelectLValue.SignalName`
   - `ConcatLValue` → collect all component signal names recursively
   - `StructFieldLValue.SignalName`
3. Place all collected names in a case-insensitive `HashSet<string>`.
4. For each `SignalDecl` in `module.LocalSignals`: set `IsRegistered = true` if `Name` is in the set.

### 7.2 Rules

| Condition | `IsRegistered` |
|-----------|---------------|
| Signal driven only from `ContAssigns` | `false` |
| Signal driven only from `CombinationalBlocks` | `false` |
| Signal driven from any `SequentialBlockAst` | `true` |
| Signal driven from both `SequentialBlockAst` AND `ContAssignAst` | `true` + `LogWarning` (ambiguous drive — likely a design bug) |
| Port signals (`PortDecl`) | Never set; ports are not `SignalDecl` instances |

### 7.3 Downstream usage

| Consumer | How `IsRegistered` is used |
|----------|---------------------------|
| Phase 2 schematic builder | `true` → emit `FlipFlopPrimitive`; `false` + sequential target → latch; otherwise combinational wire |
| Phase 3 probe enumerator | `true` → signal holds a stable Q value, expose via `ReadSignal` |
| Phase 7 FSM detector | `true` state variable → potential FSM state register |

---

## 8. Backwards-compatibility seam: `LegacyDesignFlattener`

`LegacyDesignFlattener` consumes `DesignAst` and produces `ElaboratedDesign` (the existing flat model). This allows `ElkGraphBuilder` and all Phase 0 tests to keep working unchanged through Phase 1.

### 8.1 Type mapping table

| Legacy flat type | Flat field | Source in AST | Notes |
|------------------|------------|---------------|-------|
| `ElaboratedDesign` | *(root)* | `DesignAst` | Flattener returns this. |
| `ModuleMetadata` | `Name`, `Ports`, `Parameters` | `ModuleAst.Name`, `.Ports`, `.Parameters` | `PortDecl` → `SignalPort` (identical fields). |
| `DesignModuleDefinition` | `Metadata`, `LocalSignals`, `Instances`, `ContAssigns` | `ModuleAst` | All four collections; see §8.2 for ContAssign mapping. |
| `DesignLocalSignal` | `Name`, `Width`, `IsSigned` | `SignalDecl.Name`, `.Width`, `.IsSigned` | `IsRegistered` and `ArrayDims` are **not** in the legacy type — silently dropped. |
| `DesignInstanceDefinition` | `Name`, `ModuleName`, `PortConnections` | `InstanceDecl` | 1:1 field mapping. |
| `DesignInstancePortConnection` | `PortName`, `SignalName`, `Direction`, `PortIndex` | `PortConnectionDecl` | 1:1 field mapping. |
| `DesignContAssign` | `TargetName`, `SourceNames`, `OperatorSymbol`, `SourceRange` | `ContAssignAst` | Complex; see §8.2. |

### 8.2 `ContAssignAst` → `DesignContAssign` mapping

The flattener performs a shallow inspection of `ContAssignAst.Source`:

| `Source` expression type | `OperatorSymbol` | `SourceNames` | `SourceRange` |
|--------------------------|-----------------|--------------|---------------|
| `SignalRef(name)` | `null` | `[name]` | `null` |
| `BitSelectExpr(SignalRef(name), range)` | `null` | `[name]` | `DesignBitRange(range.Hi, range.Lo)` |
| `BinaryExpr(op, left, right)` | op string (see §6.8 table column "Legacy OperatorSymbol") | all `SignalRef.Name` values found by recursive DFS | `null` |
| `UnaryExpr(op, operand)` | op string (see §6.9 table) | all `SignalRef.Name` values found by recursive DFS | `null` |
| `ConcatExpr(parts)` | `"{}"` | all `SignalRef.Name` values found in `parts` by recursive DFS | `null` |
| `CondExpr(cond, t, f)` | `"?:"` | all `SignalRef.Name` values in cond+t+f by recursive DFS | `null` |
| `ExtendExpr(inner, ...)` | `null` (treated as wire alias) | all `SignalRef.Name` values in inner | `null` |
| `ConstExpr(...)` | `null` | `[]` (constant driver, no signal source) | `null` |
| anything else | `null` | all `SignalRef.Name` values by recursive DFS | `null` |

**`SourceNames` extraction:** Collect all `SignalRef.Name` values found by recursive DFS over the expression tree. Exclude any name equal to `ContAssignAst.Target`'s signal name (same logic as the legacy parser). Deduplicate case-insensitively. The resulting list must be non-empty; a `ContAssignAst` with no signal sources is dropped (not emitted as a `DesignContAssign`).

**`OperatorSymbol` string values** must match the legacy `VerilatorXmlParser.DetectOperatorSymbol` output character-for-character. The values are: `"+"`, `"-"`, `"*"`, `"/"`, `"%"`, `"&"`, `"|"`, `"^"`, `"~"`, `"&&"`, `"||"`, `"!"`, `"="`, `"≠"`, `"<"`, `">"`, `"≤"`, `"≥"`, `"<<"`, `">>"`, `">>>"`, `"{}"`, `"?:"`.

### 8.3 What the flattener discards

The following AST content has no representation in the legacy flat model:

| Discarded | Reason |
|-----------|--------|
| `SequentialBlockAst` | Not in `DesignModuleDefinition`. Phase 2 consumes from AST directly. |
| `CombinationalBlockAst` | Same. |
| `SignalDecl.IsRegistered` | `DesignLocalSignal` has no such field. |
| `SignalDecl.ArrayDims` | `DesignLocalSignal` has no such field. Memory support is Phase 2+. |
| Expression trees deeper than top-level | Only the top-level expression type determines `OperatorSymbol`. Sub-expressions are collapsed to `SourceNames` via DFS. |

This means that during Phase 1, the schematic does not yet show flip-flop or mux symbols derived from always blocks. Phase 2 consumes the full AST directly and adds those primitives.

---

## 9. Unknown XML element behavior

When `VerilatorXmlAstReader` encounters an XML element it does not recognize:

| Context | Behavior |
|---------|----------|
| Unknown element as a **statement** inside `<begin>` or `<always>` | Skip element + `logger.LogWarning("Skipping unknown statement element '{Name}' at {Location}", ...)` |
| Unknown element as an **expression** | Return `ConstExpr(Value=0, Width=1, IsSigned=false)` + `logger.LogWarning(...)` |
| Unknown element as an **lvalue** | Return `VarRefLValue("__unknown__")` + `logger.LogWarning(...)` |
| Unknown element at **module level** (not statement/expression context) | Skip silently. Module-level unknowns (`<typedef>`, `<typetable>`, `<attrscope>`) are routine. |

**Never throw for unknown elements.** The reader must be tolerant of Verilator version differences and design patterns not present in arnicomp. Callers receive a partially-populated AST with warnings in the log.

**Fatal errors (do throw `InvalidDataException`):**
- `<verilator_xml>` root element is absent.
- `<netlist>` contains zero `<module>` children.
- Expression depth exceeds the 200-level guard (see §10.2).

---

## 10. Reader implementation guide (informational — for P1-3)

This section provides guidance for the agent writing `VerilatorXmlAstReader`. It is not part of the type contract.

### 10.1 Recommended parse order

Implement and unit-test handlers in this order (each builds on the previous):

1. `ParseConst(XElement)` → `ConstExpr`
2. `ParseSignalRef(XElement)` → `SignalRef`
3. `ParseBitSelect(XElement)` → `BitSelectExpr`
4. `ParseConcat(XElement)` → `ConcatExpr`
5. `ParseBinary(XElement)` → `BinaryExpr`
6. `ParseUnary(XElement)` → `UnaryExpr`
7. `ParseCond(XElement)` → `CondExpr`
8. `ParseExpression(XElement, depth)` → dispatches by element name to above; passes `depth+1`
9. `ParseLValue(XElement)` → dispatches to `VarRefLValue`, `BitSelectLValue`, `ArraySelectLValue`, `ConcatLValue`
10. `ParseAssign(XElement, isNonBlocking)` → `AssignAst`
11. `ParseIf(XElement)` → `IfAst`
12. `ParseCase(XElement)` → `CaseAst`
13. `ParseBegin(XElement)` → `BeginAst`
14. `ParseStatement(XElement)` → dispatches to statement handlers
15. `ParseEdgeTrigger(XElement)` → `EdgeTrigger`
16. `ParseSenTree(XElement)` → `IReadOnlyList<EdgeTrigger>`
17. `ParseAlways(XElement)` → `SequentialBlockAst` or `CombinationalBlockAst` (presence of `<sentree>` decides)
18. `ParseContAssign(XElement)` → `ContAssignAst`
19. `ParseSignalDecl(XElement)` → `SignalDecl` (IsRegistered=false at this stage)
20. `ParsePortDecl(XElement)` → `PortDecl`
21. `ParsePortConnection(XElement)` → `PortConnectionDecl`
22. `ParseInstanceDecl(XElement)` → `InstanceDecl`
23. `ParseModule(XElement)` → `ModuleAst`
24. `ParseDesign(XDocument)` → builds all modules, then calls `ComputeIsRegistered(modules)`

### 10.2 Expression depth guard

The expression parser is recursive. Enforce a hard limit to prevent stack overflows on pathological input:

```csharp
private ExpressionAst ParseExpression(XElement element, int depth = 0)
{
    if (depth > 200)
        throw new InvalidDataException(
            $"Expression nesting depth exceeded 200 at element '{element.Name}'. " +
            "Design may contain an unexpanded generate or macro loop.");
    // dispatch ...
}
```

### 10.3 `IsRegistered` computation pass

```csharp
private static void ComputeIsRegistered(IReadOnlyList<ModuleAst> modules)
{
    foreach (ModuleAst module in modules)
    {
        HashSet<string> driven = CollectSequentialTargets(module.SequentialBlocks);
        foreach (SignalDecl signal in module.LocalSignals)
            signal.IsRegistered = driven.Contains(signal.Name, StringComparer.OrdinalIgnoreCase);
    }
}

private static HashSet<string> CollectSequentialTargets(
    IReadOnlyList<SequentialBlockAst> blocks)
{
    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var block in blocks)
        CollectFromStatement(block.Body, names);
    return names;
}
```

`CollectFromStatement` visits `BeginAst`, `IfAst`, `CaseAst` recursively, and for `AssignAst` extracts the signal name(s) from the `Target` lvalue.

### 10.4 Combinational projection pass

`VerilatorXmlAstReader` runs the post-parse chain in this order:

1. compute `IsRegistered`;
2. `TempFolder.Fold` compiler-generated single-consumer temporaries;
3. `CombinationalProjector.Project` procedural combinational blocks.

The projector symbolically executes Begin/Assign/If/Case statements with
last-assignment-wins semantics. Constant case labels become equality conditions;
branches become `CondExpr`. Bit-select lvalue writes are tracked per destination
bit and reconstructed as one whole-bus expression only when every bit is
defined. Partial assignment (latch risk), non-constant case labels, unsupported
lvalues, or expressions deeper than 128 produce projection metadata with
`Unsupported` status instead of a synthetic assignment.

---

## 11. Complete worked example: arnicomp `always` block end-to-end

This traces one `<always>` element from raw XML through the full AST and into the legacy flat output.

**Verilog source:**
```verilog
always @(posedge clk or negedge rst_n)
    inst_q <= rst_n ? instruction : 8'h0;
```

**Verilator XML (from arnicomp generation — master plan §16):**
```xml
<always loc="...">
  <sentree>
    <senitem edgeType="POS"><varref name="clk"/></senitem>
    <senitem edgeType="NEG"><varref name="rst_n"/></senitem>
  </sentree>
  <begin>
    <assigndly dtype_id="8">
      <cond>
        <varref name="rst_n"/>
        <varref name="instruction"/>
        <const name="8'h0"/>
      </cond>
      <varref name="inst_q"/>
    </assigndly>
  </begin>
</always>
```

**AST produced by `VerilatorXmlAstReader`:**
```
SequentialBlockAst
├── Triggers
│   ├── EdgeTrigger { Edge=Rising,  SignalName="clk"   }
│   └── EdgeTrigger { Edge=Falling, SignalName="rst_n" }
├── HasAsynchronousReset = true
└── Body = BeginAst
    └── Statements[0] = AssignAst
        ├── IsNonBlocking = true
        ├── Target = VarRefLValue { Name="inst_q" }
        └── Source = CondExpr
            ├── Condition = SignalRef   { Name="rst_n"      }
            ├── IfTrue    = SignalRef   { Name="instruction" }
            └── IfFalse   = ConstExpr  { Value=0, Width=8, IsSigned=false }
```

**`IsRegistered` post-pass result:**
- `"inst_q"` collected from `AssignAst.Target` inside a `SequentialBlockAst`
- `SignalDecl { Name="inst_q" }.IsRegistered` set to `true`

**`LegacyDesignFlattener` output:**
- This `SequentialBlockAst` produces **no** `DesignContAssign` — sequential blocks are invisible to the flat model.
- `DesignLocalSignal { Name="inst_q", Width=8, IsSigned=false }` is included in `DesignModuleDefinition.LocalSignals` (flattened from `SignalDecl`, `IsRegistered` dropped).

**Phase 2 schematic implication (out of scope for Phase 1):**
- Decoder detects `SequentialBlockAst` with a single `AssignAst` target `"inst_q"` → emits `FlipFlopPrimitive { Clock="clk", AsyncReset="rst_n", D="instruction", Q="inst_q" }`.

---

## 12. Glossary

| Term | Definition |
|------|-----------|
| AST | Abstract Syntax Tree. The Design IR node hierarchy documented here. |
| Continuous assignment | `assign target = expr` — drives a wire outside any `always` block. Becomes `ContAssignAst`. |
| Sequential block | `always @(posedge clk ...)` — clocked logic. Becomes `SequentialBlockAst`. |
| Combinational block | `always @(*)` or `always_comb` — unclocked logic. Becomes `CombinationalBlockAst`. |
| Non-blocking assign | `target <= expr` inside a sequential block. `AssignAst { IsNonBlocking=true }`. |
| Registered signal | A signal driven only inside a sequential block. `SignalDecl.IsRegistered=true`. |
| Flattener | `LegacyDesignFlattener` — converts `DesignAst` → `ElaboratedDesign`. The backwards-compat seam. |
| Verilator-agnostic | No type or field name in this AST refers to a Verilator-specific XML element or keyword. |
| DFS | Depth-first search. Used by the flattener to collect `SignalRef.Name` values from expression trees. |
