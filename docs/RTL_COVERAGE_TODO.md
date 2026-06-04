# RTL Coverage Follow-Ups (post Phase 2.9)

**Status:** Tracked for future work, NOT a Phase 2.9 reopener. Phase 2.9 closed with `SilentMissCount == 0` across every bundled sample — these items are *explicit* `Unsupported` endpoints surfaced by the analyzer, not silent drops.

**Audit run:** 2026-06-04 against `samples/{arnicomp, tiny_cpu, bus_fabric, memory_demo, riscv_single_cycle}`.
**Aggregate coverage:** 757 / 788 endpoints Routed (96.1 %), 21 IntentionalOmission, **10 Unsupported**, 0 SilentMiss.

When these are knocked down, the schematic engine reaches the "true production-grade" target the master plan demands (≥ 99 % per real-world CPU sample, no blank FFs, every immediate decoded). Until then, **Phase 6 (Yosys synthesis) baseline assumes RTL coverage will improve in parallel**, not block.

---

## Group A — `SequentialBlock` not decoded (8 endpoints)

The FF decoder accepts only the canonical "single non-blocking assign to one signal" shape. Any `always_ff` that does more than that falls through to `Unsupported`. Concretely:

### A.1 — Array-element reset loops (3 endpoints)

```systemverilog
always_ff @(posedge clk or negedge rst_n) begin
    if (!rst_n) begin
        for (int i = 0; i < 32; i++) regs[i] <= 0;
    end else if (enable && reg_write && rd != 5'd0) begin
        regs[rd] <= write_data;
    end
end
```

Affected sites:

- `samples/riscv_single_cycle/riscv_single_cycle_top.sv` — `riscv_register_file.regs` and `riscv_data_memory.mem`.
- `samples/tiny_cpu/.../register_file.acc` (same pattern, different reset shape).

Action: extend the FF / memory-write decoder to recognise:
- multi-statement always_ff with `if (!rst) <loop>; else if (...) <single assign>` shape,
- `for` loop bodies that initialise an unpacked array to a constant,
- single-cell writes to an unpacked array indexed by a signal (lowers to a memory-write primitive).

### A.2 — Halt / FSM-style multi-statement always_ff (4 endpoints)

```systemverilog
always_ff @(posedge clk or negedge rst_n) begin
    if (!rst_n) begin
        pc <= 32'h0;
        halted <= 1'b0;
    end else if (enable && !halted) begin
        pc <= next_pc;
        if (halt_next) halted <= 1'b1;
    end
end
```

Affected sites:

- `riscv_single_cycle_top.halted` (one always_ff drives both pc and halted; pc decodes as FF, halted does not).
- `tiny_cpu/.../status_flags.carry_latched`, `irq_pending`, `halted`.

Action: lift the FF decoder from "single assign" to "multi-target FSM-style always_ff with shared sensitivity list" — emit one `FlipFlopPrimitive` per target, sharing the clock + reset.

### A.3 — Conditional counter increment (1 endpoint)

```systemverilog
always_ff @(posedge clk or negedge rst_n) begin
    if (!rst_n) counter <= 0;
    else if (enable) counter <= counter + 1;
end
```

Affected site: `samples/bus_fabric/.../timer_peripheral.counter`.

Action: recognise the "increment-with-enable" pattern as an FF whose D = `counter + 1` (already a supported Arith primitive) gated by the enable. Probably emerges for free once A.2 lands.

---

## Group B — `PrimitiveEndpoint` could not be resolved (2 endpoints)

Joiner primitives whose input list contains a `Replicate` over a single bit. The renderer treats the replicated bit as a single constituent but emits `Joiner.in.N = ?` for the slot.

Affected sites:

- `samples/riscv_single_cycle/.../riscv_decoder.imm_b` — `{{19{instruction[31]}}, instruction[31], instruction[7], instruction[30:25], instruction[11:8], 1'b0}`.
- Same module, `imm_j` — same pattern with different bit selects.

Action: extend the Joiner decoder to materialise `ReplicateExpr` constituents into N concrete bit references (or a single fan-in stub with replication count metadata).

---

## How to verify after fixing

1. Open `samples/<sample>/...sv` and the corresponding `Unsupported` endpoint in the new `View → Schematic Coverage…` window.
2. Rebuild. The endpoint must move to `Routed` and disappear from the `unsupportedConstructs` list.
3. Visually confirm in the schematic that the previously-blank FF / immediate joiner now renders with wires.
4. `dotnet test --filter "FullyQualifiedName~SampleCoverage"` must stay green (silent-miss baseline still 0).

Aim: drive aggregate coverage from 96.1 % → ≥ 99.5 % across the bundled samples before Phase 6 ships a public preview.
