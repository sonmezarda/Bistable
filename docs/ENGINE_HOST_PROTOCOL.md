# Engine Host RPC Protocol (v2)

The versioned JSON-line boundary between a non-.NET frontend (the Theia
workbench) and `Bistable.EngineHost`. Distinct from the worker protocol in
`docs/PROTOCOL.md` — this sits one layer higher: the host owns the
`SimulationSessionService`, which in turn owns the native Verilator worker.

- **Transport:** newline-delimited JSON over the host process's stdin/stdout.
- **Direction:** frontend writes an `EngineRpcRequest`; the host replies with
  exactly one `EngineRpcResponse` per request, correlated by `id`.
- **stdout is protocol-only.** All logs go to stderr.
- **Version:** `EngineRpcProtocol.Version = 2`. The frontend guards on the
  `hello` result and rejects a mismatched host.

## Request / response

```jsonc
// request
{ "id": "<uuid>", "method": "<method>", "params": { ... } }

// success
{ "id": "<uuid>", "result": { ... } }

// error
{ "id": "<uuid>", "error": { "code": "<code>", "message": "…", "data": { ... } } }
```

## Methods

| Method | Params | Result | Notes |
|--------|--------|--------|-------|
| `hello` | — | `{ protocolVersion, engineVersion, capabilities[] }` | v2 advertises `simulation.start`, `simulation.step`, `simulation.readSignals`, and the additive `schematic.module`. |
| `loadProject` | `projectPath` | project summary + top-module schematic graph | Elaboration only; no worker build. Refreshes the host's design cache. |
| `loadModuleSchematic` | `projectPath`, `instancePath`, `expand?[]` | `{ instancePath, moduleName, schematic }` | Layout-agnostic graph for one **hierarchical instance path** (`top.u_core.u_alu`). Optional `expand` lists document-relative instance paths (`u_core`, `u_core.u_alu`) to compose inline as Container nodes (capability `schematic.expand`). Served from the cached elaboration of the latest `loadProject`/`simulation.start` for the same project; only a cache miss re-elaborates. |
| `simulation.start` | `projectPath` | `{ topModule, ports[], probes[], initialFrame }` | Builds/attaches the worker; advances the session generation. |
| `simulation.setInput` | `signal`, `value` | frame | Validates width/format **before** any worker IPC. |
| `simulation.eval` | — | frame | Combinational settle. |
| `simulation.tick` | `clock?` | frame | One clock edge (defaults to first scalar input). |
| `simulation.reset` | — | frame | Model reseat + reset drive. |
| `simulation.readSignals` | `paths[]` | `{ results: [{ path, value?, width, isSigned, error? }] }` | One batched worker round-trip (chunked past 4096). |
| `simulation.stop` | — | `{ accepted: true }` | Disposes the worker. |
| `shutdown` | — | `{ accepted: true }` | Disposes the session and ends the loop. |

### Schematic node display metadata

Each `loadProject` schematic node keeps `inputs[]`/`outputs[]` as the exact net
identities used for edge routing, probing and selection. Optional parallel
`inputLabels[]`/`outputLabels[]` arrays contain semantic display names only
(`A/B/Y`, `S/I0/Y`, `D/CLK/Q`, or an instance's HDL port names). Instance nodes
also carry `typeLabel` while `label` remains the instance name. Frontends must
never use display labels as connection keys; generated `__schematic_*` names
remain inspectable without being painted across symbol bodies.

### Hierarchical schematic documents

`loadModuleSchematic` identifies a document by its **instance path**, never by
module type: `top.u_alu` and `top.u_core.u_alu` are distinct documents even
when both instantiate the same module. Segments match instance names
case-sensitively; the first segment must be the top module (elaborated or
original source name). The result echoes the `instancePath` back as the
document identity and adds `moduleName` (display metadata only). All node/edge
signals in the returned graph are module-local exact nets; a frontend derives
probe paths by prefixing the instance path (`top.u_alu` + `result` →
`top.u_alu.result`). Signals of a child document are read-only over
`simulation.readSignals`; `simulation.setInput` remains valid only for exact
top-level input ports.

### Selective inline expansion

With `expand`, each listed relative instance is composed in place instead of
appearing as a collapsed Instance symbol:

- The instance becomes a node of kind `Container` (`id` chain
  `container:u_core`, `u_core/container:u_alu`); every composed node carries a
  `containerId` naming the Container it is laid out inside. Layout nests
  containers; net identity is unaffected.
- Expanded internals keep exact per-bit identity through instance
  **namespacing**: child net `zero` appears as `u_alu.zero`, so the document
  prefix still yields the true probe path (`top.u_alu.zero`). Nothing is
  aliased and no bus is derived from names.
- The child's boundary ports become pass-through `Port` nodes inside the
  container: one side is the parent net, the other the namespaced internal
  net, with a `typeLabel` of `input`/`output` as a render-direction hint.
  Namespaced boundary nets never match a top-level input, so Poke safety is
  preserved structurally.
- Expansion is selective and recursive; siblings not listed stay collapsed.
  Unknown relative paths fail with `invalid_path`.

## Error codes

| Code | Cause |
|------|-------|
| `invalid_request` | Malformed line, or missing `id`/`method`. |
| `method_not_found` | Unknown method. |
| `invalid_value` | A `setInput` value failed width/format validation (worker untouched). |
| `invalid_path` | A `loadModuleSchematic` instance path does not resolve in the elaborated design (missing/empty segment, wrong root, or a module type name used as a path). |
| `elaboration_failed` | Verilator elaboration failed; `data.diagnostics[]` carries file/line/column. |
| `engine_error` | IO / invalid-data / invalid-operation from the engine. |

## Session semantics

`SimulationSessionService` keys every started worker with a monotonic
**generation**. A project reload (`simulation.start` again) builds the
replacement worker, swaps it in atomically, and disposes the previous one, so a
late frame or read from a superseded generation is dropped rather than applied
to the new session. Disposing the host tears down its worker subprocess tree.
