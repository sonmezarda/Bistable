# Phase 2.8 — Performance & Scale (DEFERRED — live status)

> **2026-07-16 — superseded by the vision roadmap.** Incremental layout/caching
> concerns moved to [PHASE-9](PHASE-9.md); render/measure performance and VCD
> tailing to [PHASE-12](PHASE-12.md). See [docs/ROADMAP.md](../ROADMAP.md).
> This file stays as a historical record.

**Master plan:** `/home/ardac/.claude/plans/fluffy-wishing-kettle.md`
**Phase goal:** Make the schematic renderer handle Linux-class CPU designs (500K – 2M gates, 1000+ instance hierarchies). Without this, the tool is limited to teaching/research scale (~10K gates max).
**Prerequisite:** Phase 2.5 + 2.6 + 2.7 complete (full feature set in place).
**Phase gate:** arnicomp scales to 100× equivalent (synthetic stress test); pan/zoom remains > 30 fps on 100K-gate design; full layout of 500K-gate design completes within 10 s.

**STATUS: DEFERRED**

User explicitly decided this phase comes AFTER functional completeness (2.5-2.7) and after Phase 3 (worker probe). Phase 4 (live values) may even precede this. Rationale: small-to-medium designs work fine today; performance optimization without real-world large-design feedback risks premature optimization.

---

## 1. Why this phase matters (and why we defer)

**Matters because:** Production designs (e.g. open-source CVA6, BOOM, OpenPiton) have 100K-2M gates per core. Today's ELK call would time out (current 45 s band-aid).

**Defer because:**
1. Phase 2.5-2.7 deliver more user-visible value per week.
2. Phase 3 (probe) and Phase 4 (live values) are blocking for the "live debugger" UX which is the tool's main differentiator.
3. Real performance fixes require profiling against REAL large designs — premature optimization without that risks wrong choices.

**Trigger to start Phase 2.8:** Either user explicitly requests, OR a user reports a freezing/unresponsive symptom on their own design.

---

## 2. Task board

Status legend: ☐ todo · 🟡 in progress · ✅ done · ⛔ blocked · 💤 deferred

| ID | Task | Status | Model | Est. | Notes |
|----|------|--------|-------|------|-------|
| P2.8-1 | Level-of-Detail (LOD) rendering | 💤 | Opus | 1 wk | Zoom-out = less detail; 4 LOD bands |
| P2.8-2 | Lazy / on-demand rendering | 💤 | Opus | 2 wk | Viewport-clipped render; spatial index |
| P2.8-3 | Edge bundling | 💤 | Sonnet | 1 wk | Multi-bit busses as fat edges |
| P2.8-4 | ELK sub-graph caching (dirty regions) | 💤 | Opus | 2 wk | Per-compound layout cache; partial re-layout |
| P2.8-5 | Streaming primitive enumeration | 💤 | Opus | 1 wk | Decoder lazily yields primitives; don't materialise all upfront |
| P2.8-6 | Headless layout worker | 💤 | Opus | 1 wk | Move ELK calls to a separate process/worker; cancellable |

---

## 3. Detailed task specs

### P2.8-1 — Level-of-Detail (LOD) rendering

**Problem:** At 0.1× zoom, all primitive symbols (clock triangles, pin glyphs, value badges) still render — burns CPU on stuff users can't see anyway.

**Approach:**
- 4 LOD bands based on `transform.Scale`:
  - `< 0.3`: Boxes only with module names; no pins, no symbols
  - `< 0.6`: Boxes + port positions (as dots); no symbols, no labels
  - `< 1.0`: Boxes + simplified symbols (rectangles for FF/Mux); no pin glyphs
  - `>= 1.0`: Full detail (current rendering)
- Per-LOD render path in `DrawElkNodesRecursive`.

**Files:** `src/Bistable.App/Views/SchematicPreviewControl.Elk.cs`, `Symbols.cs`.

**Tests:** ≥ 6 (each LOD band, transition smoothness, no regression at full zoom).

**Expected impact:** 5-10× pan/zoom FPS at low zoom on large designs.

---

### P2.8-2 — Lazy / on-demand rendering

**Problem:** `DrawElkNodesRecursive` iterates EVERY node every frame, even ones off-screen. With 50K nodes that's 50K work per frame.

**Approach:**
- Build a spatial R-tree from ELK output positions.
- On render, query R-tree for nodes intersecting viewport.
- Skip rendering for off-viewport nodes.
- For edges: skip if both endpoints off-viewport.
- Hit-testing also uses the R-tree.

**Files:**
- `src/Bistable.App/Services/Routing/Elk/ElkSpatialIndex.cs` (new) — R-tree wrapper (use `RBush` NuGet or hand-roll)
- `src/Bistable.App/Views/SchematicPreviewControl.Elk.cs` — query-based render loop
- `src/Bistable.App/Views/SchematicPreviewControl.cs` — hit-test uses spatial index

**Tests:** ≥ 8 (visible-only rendering, hit-test accuracy, edge clipping, scroll-into-view bypass, performance benchmark).

**Expected impact:** 10-100× render time reduction on large designs.

---

### P2.8-3 — Edge bundling

**Problem:** A 32-bit bus produces 32 parallel edges. Visually overwhelming and slow.

**Approach:**
- Detect "bundle candidates": multiple edges between same producer/consumer pair with consecutive bit indices.
- Render as ONE thick edge with `[hi:lo]` label.
- On hover, "explode" to show individual lines (optional, post-Phase-2.8).

**Files:**
- `src/Bistable.App/Services/Routing/Elk/EdgeBundler.cs` (new) — bundle detection post-ELK
- `src/Bistable.App/Views/SchematicPreviewControl.Elk.cs` — render bundle vs individual

**Tests:** ≥ 5 (bundle detection, non-contiguous bits not bundled, single-bit-bus not bundled, label correctness).

**Expected impact:** 32× edge count reduction for bus-heavy designs.

---

### P2.8-4 — ELK sub-graph caching with dirty regions

**Problem:** Any change → full re-layout. arnicomp = 500 ms. Linux-CPU class = 30+ s.

**Approach:**
- Per-compound layout cache keyed on `(compound-id, structure-hash)`.
- Expanding a sub-compound triggers only that compound's re-layout.
- Outer layout uses CACHED compound bounding box as a "black box" until next change.
- Cache invalidation: structure hash changes when AST changes.

**Files:**
- `src/Bistable.App/Services/Routing/Elk/ElkLayoutCache.cs` (new)
- `src/Bistable.App/Services/Routing/Elk/ElkSchematicEngine.cs` — split into "top-level" and "compound" cache lookups

**Tests:** ≥ 10 (cache hit/miss, structure-hash determinism, partial invalidation, multi-level compounds).

**Expected impact:** Sub-second response when expanding a compound on 100K-gate design.

---

### P2.8-5 — Streaming primitive enumeration

**Problem:** `SchematicDecoder.Decode` materialises ALL primitives into a list. For 50K primitives = 50K allocations + traversal up front.

**Approach:**
- Convert `Decode` to return `IAsyncEnumerable<SchematicPrimitive>` or chunked.
- Builder consumes incrementally, can yield to UI between chunks.
- Cancellable via CancellationToken.

**Files:**
- `src/Bistable.Core/Design/Schematic/SchematicDecoder.cs` — refactor to yield
- `src/Bistable.App/Services/Routing/Elk/ElkGraphBuilder.cs` — accept streaming input
- `src/Bistable.App/ViewModels/MainWindowViewModel.cs` — async-aware

**Tests:** ≥ 6 (streaming correctness, cancellation, partial-results, no-deadlock).

---

### P2.8-6 — Headless layout worker

**Problem:** ELK runs in-process via Node subprocess. Each call blocks the C# thread until subprocess responds. UI freezes during layout.

**Approach:**
- Long-running Node worker process maintained across layout calls.
- Layout requests queued; cancellable.
- Worker can run on a background CPU core; UI stays at 60 fps.
- Existing `ElkRunner.cs` infrastructure → upgrade to persistent worker.

**Files:**
- `src/Bistable.App/Services/Routing/Elk/ElkRunner.cs` — refactor to persistent process
- `tools/elk-router/server.js` — long-running HTTP-like server over stdio
- Robust process lifecycle (restart on crash, etc.)

**Tests:** ≥ 8 (worker startup, multiple requests, cancellation, crash recovery, shutdown).

---

## 4. When to actually start

**Defer triggers (any of):**
- User reports a freezing symptom on their own design.
- arnicomp + 4-iteration generate block + interface example shows > 3 s layout time.
- A new sample is added that has > 1000 primitives.

**Approval gate:** Whoever picks up this phase MUST justify the timing in this doc's "Recent activity" section.

---

## 5. Recent activity

(empty — phase DEFERRED, not yet started)
