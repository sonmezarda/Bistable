# Worker Protocol v3

This document specifies the JSON line-protocol between the GUI process
(`Bistable.App`) and the Verilator-compiled worker process (`bistable-worker`).

> **Note:** this is the low-level GUI↔worker protocol. The higher-level
> frontend↔engine-host RPC used by the Theia workbench (which wraps this worker
> protocol behind `simulation.*` methods) is specified in
> `docs/ENGINE_HOST_PROTOCOL.md`.

- **Transport:** newline-delimited JSON over the worker's stdin/stdout.
- **Direction:** GUI writes a `SimulationCommand`; worker replies with exactly
  one `WorkerResponse` per command.
- **Encoding:** UTF-8. One JSON object per line (no embedded newlines).
- **Serialization:** `System.Text.Json` with `PropertyNamingPolicy = camelCase`.

The worker is a pure stdin/stdout filter — no sockets, no shared memory. The
GUI launches one worker per build artifact; multiple commands may be queued
sequentially over the same worker.

---

## 1. Command shape

Every command is a `SimulationCommand` record with a required `type`
discriminator and a small set of optional payload fields:

```json
{
  "type": "<command>",
  "signal": "<port name, for setInput / tick>",
  "value": "<hex or decimal string>",
  "cycles": <int, for runCycles>,
  "path": "<dotted hierarchy path, for probe commands>",
  "paths": ["<path 1>", "<path 2>", "... for readSignals"],
  "memoryAddress": <ulong, for memory commands>,
  "memoryCount": <int, for memory commands>
}
```

Unused fields are omitted by the GUI and ignored by the worker.

### 1.1 Command catalog

| `type`          | Purpose                                              | Required payload                    | Response               |
|-----------------|------------------------------------------------------|-------------------------------------|------------------------|
| `hello`         | Negotiate protocol version and capabilities          | —                                   | `WorkerHelloResponse`  |
| `setInput`      | Write a value to a top-level input port, then eval   | `signal`, `value`                   | `SimulationFrame`      |
| `eval`          | Run the combinational settle loop                    | —                                   | `SimulationFrame`      |
| `tick`          | One clock edge: drive clk low → high → low + eval    | `signal` (clock name)               | `SimulationFrame`      |
| `runCycles`     | `cycles` consecutive ticks                           | `signal`, `cycles`                  | `SimulationFrame`      |
| `reset`         | Re-construct the model + drive the reset port        | —                                   | `SimulationFrame`      |
| `getSnapshot`   | Re-eval + report current state (no input change)     | —                                   | `SimulationFrame`      |
| `pause`         | Report current state without re-eval                 | —                                   | `SimulationFrame`      |
| `readSignal`    | Probe-table read of any hierarchical signal          | `path`                              | `SignalReadResponse`   |
| `readSignals`   | Batch-read hierarchical signals in one IPC turn      | `paths`                             | `SignalsReadResponse`  |
| `writeSignal`   | One-shot write to a hierarchical signal              | `path`, `value`                     | `AckResponse`          |
| `forceSignal`   | Pin a hierarchical signal across subsequent evals    | `path`, `value`                     | `AckResponse`          |
| `releaseSignal` | Release a previously-forced signal                   | `path`                              | `AckResponse`          |
| `readMemory`    | (Reserved — P3-6) range-read of an unpacked array    | `path`, `memoryAddress`, `memoryCount` | `MemoryReadResponse` |
| `writeMemory`   | (Reserved — P3-6) write a single memory cell         | `path`, `memoryAddress`, `value`    | `AckResponse`          |
| `listProbes`    | Enumerate the worker's probe table                   | —                                   | `ProbeListResponse`    |

The v1 stepping commands all return `SimulationFrame`, so the existing GUI
snapshot flow is unchanged. Probe commands return typed responses with their
own `kind` discriminator. Protocol v3 adds `hello` and `readSignals`; the
single-path commands remain available for explicit one-off operations.

---

## 2. Response shape

Every response is a `WorkerResponse` — an abstract record with a `kind`
discriminator selecting one of eight subtypes via
`System.Text.Json` polymorphism.

### 2.1 `WorkerHelloResponse` — `"kind": "hello"`

Emitted by `hello`. The GUI requires the exact current protocol version and
the `readSignals` capability before attaching a newly-built worker.

```json
{
  "kind": "hello",
  "protocolVersion": 3,
  "capabilities": ["readSignals"]
}
```

### 2.2 `SimulationFrame` — `"kind": "frame"`

Emitted by every v1 stepping command. Contains the current simulation time,
the top-level output ports' values, and optionally the trace events recorded
during the command.

```json
{
  "kind": "frame",
  "time": 7,
  "signals": [
    { "signal": "y",    "value": "52", "time": 7 },
    { "signal": "zero", "value": "0",  "time": 7 }
  ],
  "trace": [
    { "signal": "clk", "value": "1", "time": 1 },
    { "signal": "clk", "value": "0", "time": 2 }
  ]
}
```

- `signals` lists every top-level output port's decimal-string value.
- `trace` is optional; populated by `setInput`, `tick`, `runCycles` with the
  per-step events the worker observed during the command.

### 2.3 `SignalReadResponse` — `"kind": "signalRead"`

Emitted by `readSignal`. The value is hex with `0x` prefix.

```json
{
  "kind": "signalRead",
  "result": {
    "path":     "arnicomp_top.acc.q",
    "value":    "0xA2",
    "width":    8,
    "isSigned": false
  }
}
```

### 2.4 `SignalsReadResponse` — `"kind": "signalsRead"`

Emitted by `readSignals`. Every requested path has its own outcome, so one
unknown path does not discard successful reads from the same frame.

```json
{
  "kind": "signalsRead",
  "result": {
    "results": [
      { "path": "top.a", "value": "0x1", "width": 1, "isSigned": false, "error": null },
      { "path": "top.missing", "value": null, "width": 0, "isSigned": false,
        "error": "unknown probe path: top.missing" }
    ]
  }
}
```

One worker command accepts at most 4,096 paths. `ReadSignalsAsync` transparently
chunks larger caller lists; a normal visible frame below that limit remains
exactly one stdin/stdout round-trip.

### 2.5 `MemoryReadResponse` — `"kind": "memoryRead"`

(Reserved — emitted by `readMemory` once P3-6 lands.)

```json
{
  "kind": "memoryRead",
  "result": {
    "path":         "memory_demo.mem",
    "startAddress": 0,
    "cellWidth":    8,
    "cells":        ["0x00", "0xA2", "0x55", "0xFF"]
  }
}
```

### 2.6 `ProbeListResponse` — `"kind": "probeList"`

Emitted by `listProbes`. Every entry currently exposed by the probe table.
Order is unspecified.

```json
{
  "kind": "probeList",
  "probes": [
    { "path": "counter.clk",     "width": 1, "isSigned": false, "isRegistered": false, "isMemory": false, "memoryDepth": 0 },
    { "path": "counter.count",   "width": 8, "isSigned": false, "isRegistered": false, "isMemory": false, "memoryDepth": 0 }
  ]
}
```

`memoryDepth` is `0` for scalar probes. (`null` on the C# side is encoded
as `0` here for JSON simplicity; the GUI's `ProbeDescriptor` exposes it
as `int? MemoryDepth` after deserialization.)

### 2.7 `AckResponse` — `"kind": "ack"`

Emitted by `writeSignal`, `forceSignal`, `releaseSignal` on success.

```json
{ "kind": "ack" }
```

### 2.8 `ErrorResponse` — `"kind": "error"`

Emitted whenever the worker rejects a command. The GUI's typed wrappers in
`SimulationWorkerClient` raise `InvalidOperationException` on receipt.

```json
{ "kind": "error", "message": "unknown probe path: counter.does_not_exist" }
```

Common error messages:

| Message                                  | Cause                                                              |
|------------------------------------------|--------------------------------------------------------------------|
| `unknown command type: <type>`           | The worker doesn't recognise the `type` discriminator.             |
| `unknown probe path: <path>`             | The path is not in the probe table (typo, unknown module, etc.).   |
| `invalid binary value`                   | A `value` field had `0b` prefix but contained non-`0`/`1` chars.   |
| Any `std::exception` thrown during eval  | Wrapped verbatim (e.g. Verilator runtime errors).                  |

---

## 3. Value encoding

| Field         | Encoding                                                                   |
|---------------|----------------------------------------------------------------------------|
| GUI → worker  | Decimal (no prefix), `0x` hex, or `0b` binary string. All parsed to uint64. |
| Worker → GUI  | Decimal string for `SimulationFrame.signals`; `0x` hex for `SignalReadResponse.value` and `MemoryReadResponse.cells`. |

The GUI's `SignalReadResult.Value` is typed `string` to accommodate wide
values (>64 bits) once `VlWide<N>` support lands. Currently the enumerator
filters such signals out, so reads always fit in 64 bits.

---

## 4. Probe table semantics

The probe table is built once at worker startup from the design AST. Each
entry binds a read/write closure pair to a hierarchical signal path:

- **`readSignal`**: calls the read lambda → returns the current value.
- **`readSignals`**: loops over the requested paths and calls each available
  read lambda, emitting one JSON response line with per-path success/error.
- **`writeSignal`**: calls the write lambda → next eval may overwrite if a
  driver is computing the same signal.
- **`forceSignal`**: adds the path → value pair to `forced_signals` and
  writes immediately. The worker calls `apply_forced_signals()` at the top
  of every `setInput` / `eval` / `tick` / `runCycles` AND after every
  `model.eval()` inside `drive_clock`. The second site is what makes force
  survive the FF latch on the rising clock edge.
- **`releaseSignal`**: removes the path from `forced_signals`. The signal
  immediately resumes simulation-driven behaviour on the next eval.

Force state persists across `reset` by design (the GUI's "this pin is held"
semantic should survive a model reseat). The Reset command does re-call
`init_probe_table(model.get())` because reseating the `unique_ptr` invalidates
the captured `rootp` pointer; the same applies to forced-signal lambdas.

### 4.1 Filters

The enumerator currently skips:

- **Verilator internal helpers** (signal names starting with `__V`). These
  are CSE/DFG temporaries with mangled names — never user-meaningful.
- **Wide signals** (`Width > 64`). `VlWide<N>` requires hex-string handling
  on the C++ side; not yet implemented.
- **Unpacked arrays** (`ArrayDims.Count > 0`). Memory probes are reserved
  for P3-6.

---

## 5. Lifecycle

```
GUI launches worker
        │
        ▼
Worker constructs V<top>() + init_probe_table(model.get())
        │
        ▼  hello → exact version + capabilities check
        │
        ▼  (loop)
   <─── stdin: SimulationCommand JSON line
        │
        ▼ dispatch on `type`
        │
   ───> stdout: WorkerResponse JSON line  (always exactly one per command)
        │
        ▼
   <─── stdin: next command…
        │
GUI closes stdin → worker exits cleanly
```

The worker is single-threaded and processes one command at a time. The GUI's
`SimulationWorkerClient` enforces this via an `await` queue.

---

## 6. Wire-format guarantees

- Every response line is a single self-contained JSON object terminated by
  `\n`. The GUI's reader splits on `\n` and parses each line independently.
- The worker MUST NOT emit non-JSON to stdout. Verilator runtime diagnostics
  (`%Error`, `%Warning`) are redirected to stderr via the
  `#define VL_PRINTF(...) fprintf(stderr, __VA_ARGS__)` shim in the
  generated worker source.
- If the worker catches a C++ exception, it emits an `ErrorResponse` and
  continues the loop. A SIGSEGV / abort terminates the worker; the GUI
  detects the closed pipe and surfaces the error to the user.

---

## 7. Versioning

This is protocol **v3**. v3 adds the versioned `hello` handshake and batch
`readSignals` response. `SimulationWorkerBuilder` embeds
`WorkerProtocol.CurrentVersion` in every generated C++ source and the normal
GUI Build path always regenerates/recompiles the executable before
`SimulationWorkerClient.StartAsync` verifies it. An old executable therefore
cannot be attached silently: it is replaced by Build or rejected with an
explicit rebuild error. Existing fields remain append-only.
