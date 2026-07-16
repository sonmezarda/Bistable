# Bistable Native Worker Template

This folder contains the historical standalone scaffold only. The production
C++ worker is generated per design by
`src/Bistable.Verilator/SimulationWorkerBuilder.cs`; protocol dispatch changes
must be made there.

The generated worker currently exposes protocol v3 over JSON-line stdin/stdout:

- setting top-level inputs
- evaluating combinational logic
- ticking configured clocks
- running N cycles
- returning output snapshots
- reading one or many live hierarchical probes (`readSignal` / `readSignals`)
- reporting protocol version and capabilities (`hello`)
