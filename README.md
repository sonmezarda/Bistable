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
- Windows, Linux, or macOS desktop

**Windows**: Install Verilator via [MSYS2](https://www.msys2.org/) (`pacman -S mingw-w64-ucrt-x86_64-verilator`).
Make sure the MSYS2 `ucrt64/bin` directory (which contains `verilator`, `g++`, and `make`) is on your `PATH`.

**Linux/macOS**: Install Verilator from your package manager or build from source.

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
samples/hierarchy/hierarchy.bistable.json
samples/tiny_cpu/tiny_cpu.bistable.json
samples/bus_fabric/bus_fabric.bistable.json
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
- tool panes can now also move to `Center` or `Floating`, so the workspace no longer assumes everything must live on the left/right/bottom rails.
- the main workspace now runs on `Dock.Avalonia`, with document/tool panes hosted by a real docking model instead of letter-based pane buttons.
- the main Dock workspace now keeps Dock's built-in root/layout templates enabled, so the app renders panes instead of showing raw Dock model type names.
- left, right, and bottom dock sizes persist across runs.
- toolbar clock selection chooses which 1-bit signal `Tick` and `Run` will pulse.
- `Live` enables immediate combinational/native re-evaluation from the input panel.
- schematic panel now includes a live probe/drive surface for the selected top-level signal.
- clicking a 1-bit input pin on the top-level symbol toggles it immediately.
- clicking a bus input pin now opens a dedicated value editor dialog with radix selection and per-bit toggles.
- selecting a hierarchy node now exposes its exact-scope traced internal signals in a dedicated explorer list.
- hierarchy graph nodes now show exact-scope and descendant trace counts.
- exact-scope internal signals are now also visible on the schematic canvas as selectable scope probes.
- selected hierarchy instances now render as a focused scope panel on the schematic canvas instead of only a side probe strip.
- the focused scope panel now shows parent/child instance neighborhood and lets you navigate hierarchy directly from the schematic canvas.
- the focused scope panel now uses module port metadata from Verilator XML to draw input/output stubs for the active instance.
- child instance cards now use elaborated instance-port connections from Verilator XML, and the schematic routes those connections through current-scope ports and local nets.
- the schematic canvas now has an inspect-style viewport with wheel zoom, drag pan, fit, and 1:1 reset controls so dense hierarchy views remain navigable.
- the main schematic pane now exposes zoom percentage and a direct `Studio` action for opening a larger schematic inspector window.
- the main schematic pane now has splitters between schematic/probe, schematic/hierarchy, and hierarchy/graph areas so dense views can be resized in-place.
- the dedicated `Schematic Studio` window runs the same live schematic surface in a non-compact layout, so port stubs, child instance cards, and routed nets get more space for inspection.
- the schematic viewport now keeps user zoom/pan stable across live updates and clips strictly to its own pane instead of bleeding into neighboring docks.
- waveform also has a dedicated `Waveform Studio` window, so the two densest surfaces can be inspected outside the dock constraints.
- docked tool panes now support a fuller tool-window cycle: `left / right / bottom / center / float / hide`, plus a `Reset Tool Layout` action.
- the center workspace is now a real document host: `Inspector`, `Waveform`, and `Schematic` start as documents, while `Project` opens as a tool pane.
- schematic scope layout is now driven by a dedicated layout engine, so current node, child instances, local nets, and probe sections use explicit non-overlap regions instead of ad-hoc drawing math.
- schematic connection routing is now driven by a dedicated route planner, so inline and stacked hierarchy views use deterministic lane assignment instead of simple midpoint elbows.
- schematic routing now builds an internal net graph before routing, so fanout, boundary ports, child ports, and local nets share one canonical connectivity model.
- the default route planner is net-aware: different nets avoid sharing the same collinear route segment, while same-net fanout exposes junction points for clearer branch inspection.
- local child-output to child-input nets now route through side corridors instead of dropping below the involved module cards.
- route lanes use wider spacing in readability-first schematic views, so parallel nets remain easier to distinguish while panning/zooming.
- compact schematic scope layout now reserves wider port zones, route corridors, and child cards to keep labels and stubs out of each other.
- child output routes now avoid crossing through child instance cards by using exterior bridge lanes.
- bus routes now render with stronger strokes and compact inline labels, while selected routes expose a label hit target for easier inspection.
- route labels are now clamped inside the scope panel and spaced apart by the router to avoid obvious label collisions.
- repeated routes for the same bus now draw one grouped label with a fanout count instead of repeating the same label on every branch.
- repeated routes for the same net now share a routed bundle lane, so fanout is drawn as one trunk with short branches instead of independent overlapping paths.
- focused schematic worlds and scope panels now reserve more horizontal routing space, making dense hierarchy inspection favor readability over fitting everything into a tiny box.
- schematic module pins now place labels before the bit/value badge, keeping the badge adjacent to the connector line instead of drifting away from the pin.
- child instance cards now keep port labels and width badges inside the symbol body, with short edge stubs instead of long internal lines crossing text bands.
- focused scope symbols now keep their own port labels and width badges inside the active module body, reducing cable/label collisions around scope outputs.
- focused hierarchy views now avoid drawing the parent scope as a small side module; parent navigation remains in breadcrumbs/tree while the canvas concentrates on the active scope and its child instances.
- schematic hierarchy now starts collapsed: the selected scope renders as a symbol until the `+` affordance or double-click expands that scope.
- expanded schematic scopes render boundary port glyphs plus child instances, instead of drawing the parent module as a competing box next to its children.
- child instance expansion is now in-place: expanding `u_core` from inside `system_top` keeps the current canvas context and opens `u_core` as a nested scope panel with its own boundary ports and child instances.
- expanded hierarchy panels now resize their world and local layout around nested scopes so open modules remain pannable and do not collapse into neighboring cards.
- schematic connection routing now accepts layout obstacles and performs bounded orthogonal detours around module cards/local regions, reducing routes that cut through symbols in dense hierarchy views.
- schematic routes now use a small net-class palette: boundary input routes, local/internal net routes, and child output routes are visually separated.
- orthogonal route crossings now carry bridge metadata and render with a small visual gap/bridge cue instead of looking like an electrical junction.
- expanded schematic scopes now use the outer scope frame as the hierarchy boundary; the extra inner current-scope symbol was removed and boundary ports attach directly to the frame edges.
- schematic route labels are no longer drawn by default, reducing bus-label clutter; route hit testing remains on the wire and details appear in the live probe.
- schematic module cards no longer change the active hierarchy selection when clicked; hierarchy selection stays owned by the hierarchy pane while the canvas keeps wire/probe selection and `+/-` expansion.
- schematic split panes now keep practical minimum sizes during resize so the probe and hierarchy regions do not collapse into unusable strips.
- child instance cards now use an internal card layout engine, with explicit header/body/footer regions and bounded port rows so dense cards stay readable.
- local net chips and routed schematic connections are now selectable inspection surfaces, not only passive drawing primitives.
- `Schematic Studio` now exposes breadcrumb scope navigation, active-scope framing, and keyboard viewport controls for faster hierarchy inspection.
- `Schematic Studio` header breadcrumbs now stay in their own grid column, long signal lists trim cleanly, and scope probe rows no longer draw connector lines through labels.
- toolbar cycle count controls how many cycles `Run` advances.
- trace-enabled projects expose internal signals under `Project > Trace Signals`.
- internal trace signals can be added to the waveform like top-level ports.
- waveform panel now separates the signal lane list from the timing plot, so selection and reordering stay usable as the trace grows.
- schematic panel shows a top-level symbol prototype built from the elaborated ports.
- schematic panel now also shows instance hierarchy extracted from Verilator XML.
- bundled `samples/hierarchy`, `samples/tiny_cpu`, and `samples/bus_fabric` demonstrate progressively denser multi-level designs.
- native worker builds now report Verilator/make progress in the status bar and fail with a timeout instead of appearing indefinitely stuck.
- selecting a signal in the browser or schematic highlights it across the workspace.
- hierarchy scope signals can be added to the waveform one-by-one or as a whole scope.
- selecting an internal trace signal updates the live probe without changing the active hierarchy context.
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

`Live` now works from both the signal editor and the top-level schematic
surface. That means:

- combinational designs can support immediate direct probing today
- native worker builds also re-evaluate live after input edits
- 1-bit top-level inputs can be toggled directly on the symbol
- wider inputs can be driven directly from a schematic dialog or from the live probe panel
- exact-scope internal signals can be selected directly on the schematic canvas and added to the waveform with a double-click
- the schematic canvas now shows the selected hierarchy instance as an internal probe panel with module/path context and live probe values
- child instances on the focused scope panel expose explicit `+/-` expansion controls while hierarchy selection remains owned by the hierarchy tree
- active hierarchy instances now expose their elaborated module ports on the schematic canvas, even before full routed nets exist
- current-scope local signals now appear as named nets, and child instance connections route against those nets or the selected scope ports
- schematic inspection no longer assumes a fixed fitted view; you can zoom and pan through dense hierarchy layouts directly in the pane
- dense hierarchy inspection can now move into a dedicated `Schematic Studio` window with a larger world canvas and a less compact placement mode
- waveform inspection can now move into a dedicated `Waveform Studio` window instead of competing with docked pane width
- dense scope routing now uses explicit lane planning for current-port, child-port, and local-net connections, which reduces line overlap in wider hierarchy views
- fanout routing is bundle-aware, so repeated child connections for the same current-scope signal reuse one trunk lane and one primary label
- child instance cards now compress their internal port rows to stay within the card body instead of spilling into footer space
- child instance port rows now follow a compact digital-symbol treatment: the route terminates at the card edge and pin metadata stays inside the symbol
- active scope views now behave more like a descend-into-schematic view: the parent is not drawn as a competing mini-symbol on the canvas
- schematic expansion state is separate from hierarchy selection, so selecting a scope for inspection does not automatically explode its internals on the canvas
- local net chips and routed connections can now be clicked to select the matching traced signal and double-clicked to push it into the waveform
- `Schematic Studio` now supports scope breadcrumbs, `Scope` framing, and keyboard navigation (`F`, `S`, `1`, `+`, `-`)
- clock edges are still explicit through `Tick` and `Run`
- richer instance-aware geometry, net probing directly on routed connections, and more professional schematic presentation polish are still the next planned interaction layers
