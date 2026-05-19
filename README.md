# Bistable

Bistable is an interactive desktop tool for inspecting and testing SystemVerilog
modules through Verilator. The current build focuses on a small playable loop:

- load a JSON project file
- elaborate the design with `verilator --xml-only`
- parse top-level port metadata
- inspect inputs and outputs in a minimal dark UI
- build a generated native Verilator worker
- evaluate, tick, run, and reset the loaded design through the worker
- update outputs live as input values change when `Live` is enabled
- stream native trace samples from the worker into the waveform viewer
- ingest VCD trace files for internal signal browsing and waveform replay

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

Then use the `File` menu to open either a sample or a project file:

```text
samples/alu/alu.bistable.json
```

For native simulation, select the `alu` sample, press `Build` once, then change
`a`, `b`, or `op`. With `Live` enabled, outputs update immediately; `Eval`
still remains available as an explicit step:

- `op = 0`: add
- `op = 1`: subtract
- `op = 2`: bitwise and
- `op = 3`: bitwise or

## Current scope

The toolbar actions are wired for the first native loop. `Build` generates a
Verilator-backed executable under `.bistable/worker`, then `Eval`, `Tick`,
`Run`, and `Reset` talk to that process over the JSON control protocol.
Waveform history is now driven by worker trace samples rather than only UI-side
state reconstruction, so clocked runs carry real intermediate transitions into
the viewer. When project tracing is enabled, the worker also emits a VCD trace
file that Bistable reads back to expose internal hierarchical signals. `Live`
mode auto-runs `Eval` after a short debounce whenever an input value changes.

The current UI also supports a basic IDE-style workspace flow:

- `File > Open Project...` loads a `.bistable.json` project.
- `File > Open Sample` lists bundled samples.
- `View` can hide/show the project and waveform panes and re-dock them left/right/bottom.
- tool panes are tabbed inside dock zones, so project, waveform, and schematic can share the same side.
- left, right, and bottom dock sizes persist across runs.
- toolbar clock selection chooses which 1-bit signal `Tick` and `Run` will pulse.
- `Live` enables immediate combinational/native re-evaluation from the input panel.
- toolbar cycle count controls how many cycles `Run` advances.
- trace-enabled projects expose internal signals under `Project > Trace Signals`.
- internal trace signals can be added to the waveform like top-level ports.
- waveform panel now separates the signal lane list from the timing plot, so selection and reordering stay usable as the trace grows.
- schematic panel shows a top-level symbol prototype built from the elaborated ports.
- schematic panel now also shows instance hierarchy extracted from Verilator XML.
- bundled `samples/hierarchy/hierarchy.bistable.json` demonstrates a multi-level design.
- selecting a signal in the browser or schematic highlights it across the workspace.
- click inside the waveform to select a lane and place the cursor.
- drag inside the waveform to scrub the cursor across history.
- mouse wheel zooms the waveform, `Shift + wheel` pans through history.
- value column follows the waveform cursor, so buses show the value at the selected point.

For a clocked example, select the `counter` sample, then:

1. Press `Build`.
2. Press `Reset`.
3. Set `enable` to `1`.
4. Select `clk` in the toolbar if needed.
5. Press `Tick` or `Run`.

## Live mode notes

`Live` currently starts from the signal editor, not the schematic canvas. That
means:

- combinational designs can behave like Logisim-style direct probing today
- native worker builds also re-evaluate live after input edits
- clock edges are still explicit through `Tick` and `Run`
- direct pin toggling on the schematic surface is the next planned interaction layer
