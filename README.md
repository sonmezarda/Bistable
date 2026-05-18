# Bistable

Bistable is an interactive desktop tool for inspecting and testing SystemVerilog
modules through Verilator. The current build focuses on a small playable loop:

- load a JSON project file
- elaborate the design with `verilator --xml-only`
- parse top-level port metadata
- inspect inputs and outputs in a minimal dark UI
- build a generated native Verilator worker
- evaluate, tick, run, and reset the loaded design through the worker

The waveform engine is intentionally kept as the next step after the native
worker loop is stable. The bundled ALU sample also includes a small preview
evaluator, so `Eval` still gives feedback before a worker is built.

## Requirements

- .NET SDK 10
- Verilator 5.x
- Linux desktop session for the Avalonia UI

## Try the sample

Build and test:

```bash
dotnet build Bistable.slnx
dotnet test Bistable.slnx --no-build
```

Run the app:

```bash
dotnet run --project src/Bistable.App/Bistable.App.csproj
```

Then open from the `Samples` list or via `Open Project`:

```text
samples/alu/alu.bistable.json
```

For native simulation, select the `alu` sample, press `Build` once, then change
`a`, `b`, or `op` and press `Eval`:

- `op = 0`: add
- `op = 1`: subtract
- `op = 2`: bitwise and
- `op = 3`: bitwise or

## Current scope

The toolbar actions are wired for the first native loop. `Build` generates a
Verilator-backed executable under `.bistable/worker`, then `Eval`, `Tick`,
`Run 10`, and `Reset` talk to that process over the JSON control protocol.

For a clocked example, select the `counter` sample, then:

1. Press `Build`.
2. Press `Reset`.
3. Set `enable` to `1`.
4. Press `Tick` or `Run 10`.
