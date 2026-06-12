# ELK Routing Performance Analysis

Date: 2026-06-12

## Implementation Status

The two P0 changes were implemented and verified on 2026-06-12:

- routing presets now apply recursively to every expanded compound parent;
- FastPreview uses `LONGEST_PATH`, while Balanced and Production retain
  `NETWORK_SIMPLEX`;
- gate-edge net identity now uses `bistable.netId` metadata instead of
  layout-visible ELK labels;
- the canvas reads metadata first and retains legacy `net{id}` label parsing
  for compatibility.

The production builder generated the full 5,572-node register-file expansion
with 14,056/14,056 metadata-tagged edges, zero edge labels, and
`LONGEST_PATH` on both graph parents. The real ELK.js route completed in
12.6 seconds, compared with the 94.8-second baseline.

The first P1 item is also complete:

- completed layouts are cached across all gate documents in one synthesis
  session;
- the key includes a structural SHA-256 netlist fingerprint, scope path,
  canonical expanded-instance set, routing quality, auto-downgrade setting,
  and an explicit geometry version;
- the LRU uses both an entry count and graph-complexity budget so several
  multi-megabyte expanded layouts cannot grow memory without bound;
- only successful layouts are cached.

The two-stage hierarchical path is complete for large expanded scopes:

- expanded compounds are routed leaf-first;
- completed child compounds enter parent routing as fixed-size macros with
  fixed port positions;
- child nodes and internal edge sections are restored after the parent route;
- nested compounds are composed recursively;
- independently routed compound stages share the synthesis-session cache.

On the full register-file expansion, first layout completed in 13.8 seconds
with 14,056/14,056 routed edges. Reusing the 5,393-node child stage in a new
parent layout completed in 1.4 seconds.

The P2 high-fanout path is also complete:

- scalar nets above 64 sinks are rewritten as invisible, hierarchy-local,
  balanced splitter trees with branching factor 16;
- every synthetic segment retains the original `bistable.netId`, while
  `bistable.syntheticFanout` makes the rewrite diagnosable;
- wide bus sources are intentionally excluded so bundle geometry, per-bit
  selection, and simulation cross-probe remain unchanged;
- the full register-file expansion now contains 5,732 nodes, 20,516 ports,
  and 14,216 edges. Maximum physical fan-out fell from 992 to 36;
- two cold two-stage routes completed in 10.25 and 10.05 seconds, with
  14,216/14,216 edges receiving routed sections.

## Scope

This analysis uses the real `riscv_single_cycle` gate netlist with
`u_registers` expanded inside the top-level schematic:

- 5,572 nodes
- 20,196 ports
- 14,056 edges
- 14,056 edge labels
- 9.7 MB ELK request JSON
- maximum fan-out: 992

Graph construction takes about 180 ms. The baseline FastPreview ELK layout
takes 95-100 seconds, so C# graph construction is not the bottleneck.

## Root Causes

### 1. FastPreview options stop at the root graph

ELK layered options such as `thoroughness`, layering strategy, and routing
style apply to graph parents. Bistable currently writes the preset only to the
root `ElkGraph`. An expanded module is another parent with its own children,
but its layout options contain only padding and port constraints.

The expanded register file therefore falls back to ELK defaults, including
`NETWORK_SIMPLEX` layering and thoroughness 7.

### 2. Every net edge carries a layout-visible label

`GateNetlistElkBuilder` stores `net123` in `ElkEdge.Labels`. The canvas does not
draw these labels; it parses them back only to recover the net id. ELK still
performs label placement and inserts label/long-edge dummy nodes.

Net identity is application metadata and should instead use an edge layout
property such as `bistable.netId`.

### 3. Expansion reroutes the complete compound graph

Expanding one module sends the top-level nodes, all expanded child primitives,
all ports, and all cross-hierarchy edges through one recursive ELK call. No
gate-level layout cache exists. Collapse/re-expand and equivalent module
instances repeat the full work.

### 4. The graph has extreme fan-out

Two source ports exceed fan-out 64; the maximum is 992. The current builder
emits one edge per source-target pair. ELK Layered 0.9.3 rejects true
multi-target hyperedges, so this cannot be collapsed by simply adding multiple
targets to one edge. A future solution requires synthetic net hubs/splitter
trees or partitioned layout.

## Profile

ELK.js phase logging attributes the baseline work approximately as follows:

| Phase | Relative cost |
|---|---:|
| Network simplex layering | 72% |
| Orthogonal edge routing | 11% |
| Brandes-Koepf node placement | 6% |
| Crossing minimization | 4% |
| Other preprocessing/postprocessing | 7% |

ELK.js reports execution-time values at a different scale than wall time in
this Node environment, but their ratios are stable and identify the hot phases.

## Controlled Measurements

All variants use the same synthesized graph and development machine.

| Variant | Wall time | Result size | Assessment |
|---|---:|---:|---|
| Current FastPreview | 94.8 s | 24,985 x 193,538 | Baseline |
| Root-only `LONGEST_PATH` | 92.2 s | 23,955 x 195,525 | Option did not reach child parent |
| Recursive FastPreview options | 36.1 s | 25,715 x 213,402 | 2.6x faster |
| Recursive + `LONGEST_PATH` | 22.7 s | 24,675 x 210,846 | 4.2x faster |
| Recursive + `LONGEST_PATH`, no edge labels | 12.4 s | 26,505 x 247,674 | 7.7x faster |
| Above + ELK built-in high-degree treatment | 13.0 s | unchanged | No benefit |
| Above + balanced scalar fanout trees | 10.1 s | 26,545 x 246,812 | Retain |
| Above + `SIMPLE` placement | 48.8 s | 408,115 x 178,823 | Reject |
| Polyline preview | 14.1 s | 220,848 x 212,928 | Poor width/readability |
| Register-file child only | 11.0 s | 15,409 x 224,382 | Supports cached two-stage layout |

## Recommended Implementation Order

### P0: Correct option propagation (complete)

Apply the effective layout preset to every compound parent created for an
expanded instance. For very large FastPreview graphs, use
`LONGEST_PATH`; retain `NETWORK_SIMPLEX` for Balanced and Production.

Expected full-expansion routing: about 23 seconds before other changes.

### P0: Remove ELK edge labels (complete)

Store net identity in `ElkEdge.LayoutOptions["bistable.netId"]`.
`GateSchematicCanvas.TryGetEdgeNetId` should read metadata first and retain the
old label parser only as a compatibility fallback. Gate edges should then omit
`Labels`.

Expected combined routing: about 12 seconds. Add connectivity, selection,
bundle, and visual-regression tests before landing.

### P1: Gate-level layout cache (complete)

Add an LRU keyed by:

- synthesis artifact/content hash
- scope path
- expanded instance set
- routing preset
- geometry-affecting settings

Cache successful layouts only. A collapse/re-expand operation should be
effectively immediate.

### P1: Two-stage hierarchical expansion (complete)

Layout expanded child modules independently, cache their relative geometry,
then route the small parent graph around a compound rectangle. Merge child
geometry after the parent route. Cross-boundary stubs need a deterministic
bridge layer.

This avoids making ELK solve 5,000+ child primitives together with the complete
parent on every interaction. Repeated module types can share cached relative
layouts.

### P2: High-fanout net hubs (complete)

For nets above a measured threshold, introduce invisible balanced splitter
trees before ELK and map every synthetic segment back to the original net id.
Do not use unsupported hyperedges. Validate layout quality and selection
semantics on clock/reset/register-enable networks.

The implementation uses threshold 64 and branching factor 16. Synthetic hubs
are inserted under the deepest compound owner shared by the driver and sinks,
so two-stage hierarchical layout continues to classify their edges correctly.
The renderer suppresses hub bodies and ports; users see only the routed logical
net. Cache geometry version `gate-layout-v3` prevents older layouts from being
reused after the graph rewrite.

Verification after this change: solution build 0 warnings/errors and 867/867
tests passing (838 Tests, 14 Snapshots, 4 Regression, 11 UI).

### P2: Instrumentation and version benchmark

- Expose phase timings, request/response bytes, graph metrics, and peak Node
  memory in a diagnostics report.
- Benchmark the current ELK.js 0.9.3 against a controlled 0.10.x upgrade.
- Do not upgrade based on version alone; retain only measured improvements with
  unchanged visual regressions.

## Acceptance Targets

- Full `u_registers` first expansion: under 15 seconds on the reference machine.
- Cached re-expansion: under 250 ms.
- Cancel response: under 500 ms.
- No diagonal wires in the final Production/Balanced result.
- Net selection, bus selection, and simulation cross-probe remain bit-accurate.
- The 2,000-cell render benchmark and deterministic Skia goldens remain green.

## References

- ELK Layered algorithm and supported compound/orthogonal graph features:
  https://eclipse.dev/elk/reference/algorithms/org-eclipse-elk-layered.html
- ELK layered thoroughness:
  https://eclipse.dev/elk/reference/options/org-eclipse-elk-layered-thoroughness.html
- ELK node layering strategies:
  https://eclipse.dev/elk/reference/options/org-eclipse-elk-layered-layering-strategy.html
- ELK high-degree node treatment:
  https://eclipse.dev/elk/reference/options/org-eclipse-elk-layered-highDegreeNodes-treatment.html
- ELK.js releases:
  https://github.com/kieler/elkjs/releases
- Vivado selective hierarchy and cone expansion:
  https://docs.amd.com/r/en-US/ug893-vivado-ide/Expanding-Logic-from-Selected-Cells-and-Pins
