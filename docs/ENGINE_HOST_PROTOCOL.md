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
| `hello` | — | `{ protocolVersion, engineVersion, capabilities[] }` | v2 advertises `simulation.start`, `simulation.step`, `simulation.readSignals`. |
| `loadProject` | `projectPath` | project summary + top-module schematic graph | Elaboration only; no worker build. |
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

## Error codes

| Code | Cause |
|------|-------|
| `invalid_request` | Malformed line, or missing `id`/`method`. |
| `method_not_found` | Unknown method. |
| `invalid_value` | A `setInput` value failed width/format validation (worker untouched). |
| `elaboration_failed` | Verilator elaboration failed; `data.diagnostics[]` carries file/line/column. |
| `engine_error` | IO / invalid-data / invalid-operation from the engine. |

## Session semantics

`SimulationSessionService` keys every started worker with a monotonic
**generation**. A project reload (`simulation.start` again) builds the
replacement worker, swaps it in atomically, and disposes the previous one, so a
late frame or read from a superseded generation is dropped rather than applied
to the new session. Disposing the host tears down its worker subprocess tree.
