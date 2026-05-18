# Bistable Native Worker Template

This folder is reserved for the generated C++ Verilator worker.

The first playable build stops at metadata extraction and UI controls. The next
step is to generate a worker executable per design that exposes a stable command
protocol for:

- setting top-level inputs
- evaluating combinational logic
- ticking configured clocks
- running N cycles
- returning output snapshots
