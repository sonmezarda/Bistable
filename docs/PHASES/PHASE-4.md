# Phase 4 — Live Schematic Visualization

**Master plan:** `/home/ardac/.claude/plans/fluffy-wishing-kettle.md` Section 10
**Phase goal:** Show every live value directly on the schematic — FF Q values on flip-flop symbols, current value labels on wires, mux active-path highlighting, and memory tile contents inline. Turns the static schematic into a true live debugger.
**Prerequisite:** Phase 3 (worker protocol v2) complete with full probe + memory API. `LiveProbeService` subscription infrastructure already in place.
**Phase gate:** User opens arnicomp, clicks Eval/Tick, sees Q values on every FF, current values on every wire, the active mux input visually highlighted, and memory contents inline on RAM tiles — all updating in near-real-time without manual selection.

---

## 1. Why this phase matters

After Phase 3, the GUI _can_ read any internal value via the worker, but the **schematic itself doesn't show them**. The user has to click a wire to see its value. For a 100-signal design that's a lot of clicking. Live values on the schematic turn it from a static structural view into a true live simulator, comparable to Logisim Evolution / Digital.

Phase 3's `LiveProbeService` already handles caching, subscription, and change-detection. Phase 4 wires those subscriptions to schematic rendering.

---

## 2. Architecture

```
LiveProbeService                        SchematicPreviewControl
  │ ValueUpdated event                    │
  │ MemoryUpdated event                   ▼
  └─ subscribed by ─► VisibleProbesTracker ─► InvalidateVisual()
                       (NEW)                   │
                                               ▼
                                          DrawElkEdges / DrawElkFFNode / DrawElkMemoryNode
                                          (read cached values from service)
```

**Key idea:** the renderer consults `LiveProbeService.GetCached(path)` while drawing. A separate `VisibleProbesTracker` keeps track of which paths are currently visible on screen and issues batched refresh requests after each Eval/Tick.

---

## 3. Task board

Status legend: ☐ todo · 🟡 in progress · ✅ done · ⛔ blocked

| ID | Task | Status | Model | Est. | Notes |
|----|------|--------|-------|------|-------|
| P4-1 | Edge live values — annotate wires with current value | ✅ | Sonnet | 2 d | Mid-edge value chip rendered via `DrawEdgeLiveValueLabel`; `LookupLiveValue` blends snapshot table + `LiveProbeService.GetCached`. |
| P4-2 | FF Q values on FlipFlop symbol body | ✅ | Sonnet | 2 d | Q label inside the FF body, fed by `LiveProbeService` cache via the bare-Q signal name label on the FF node. |
| P4-3 | Mux active-path highlight | ✅ | Sonnet | 1 d | `BuildActiveMuxInputSet` reads the selector value each frame, the active input edge is painted accent-cyan with thicker pen. |
| P4-4 | Memory inline grid on RAM tiles | ☐ | Sonnet | 2 d | Like Logisim Evolution: draw a small NxN hex grid inside the memory primitive. Subset of cells visible at viewport zoom; click → opens viewer window. |
| P4-5 | Visible-probes tracker + batched refresh | ✅ | Sonnet | 1 d | `SchematicPreviewControl._visibleProbePaths` records every probe touched per frame; `LiveProbeService.RefreshScalarsAsync(paths)` re-reads only those. ViewModel falls back to `RefreshAllScalarsAsync` when the set is empty (first frame / no view bound). |
| P4-6 | Symbol-body value rendering (Latch/Buffer/Inverter/Gate/Arith outputs) | ✅ | Sonnet | 1 d | `RenderPrimitiveBodyValue` shared between FF/Latch/Buffer/Inverter/Gate/Arith via the per-node bare-output-signal label. |
| P4-7 | Wire value tooltip on hover | ☐ | Sonnet | 0.5 d | Hover a wire → small tooltip with full hex value + width + signed interpretation. |
| P4-8 | Schematic legend / value formatting toggle | ☐ | Sonnet | 0.5 d | Bottom-right corner: hex / decimal / binary toggle for display values. |

**Total estimate: ~10 days serial, ~6 days with focus.**

---

## 4. Implementation order (recommended)

```
1. P4-5 (1d)         — Visible-probes tracker first; everything else depends on it.
2. P4-1 (2d)         — Edge values; biggest visible win.
3. P4-2 (2d)         — FF Q values; biggest "wow" moment.
4. P4-3 (1d)         — Mux highlight.
5. P4-6 (1d)         — Other primitive output values.
6. P4-4 (2d)         — Memory inline grid; nice-to-have on top.
7. P4-7 (0.5d)       — Hover tooltip.
8. P4-8 (0.5d)       — Format toggle.
```

---

## 5. Cross-phase notes

- **Phase 5 (Sub-Sim Maturation)** consumes the same `VisibleProbesTracker` — sub-sim mode just swaps the worker; the tracker re-attaches.
- **Phase 6 (Streaming/Async)** will make sure live-value updates don't block the UI thread.
- **Phase 7 (Force/Release UI)** — right-click a wire with live value → "Force this value" → uses Phase 3 force API.

---

## 6. Recent activity

- **2026-06-04 — status reconciliation.** Code-level audit shows P4-1 (edge live values), P4-2 (FF Q on body), P4-3 (mux active path), and P4-6 (other primitive output values) all landed earlier in code but were not reflected in this doc; updated to ✅ accordingly. Remaining work:
  - **P4-5 (visible-probes tracker)** — `RefreshAllScalarsAsync` still re-reads every scalar every Eval/Tick. RV32I core (~70 probes) is fine; OoO target (1000+ probes) will need the visible-only narrowing.
  - **P4-4 (memory inline grid)**, **P4-7 (hover tooltip)**, **P4-8 (format toggle)** — UX add-ons; pick after P4-5 to avoid premature optimization without the tracker.
  - User-visible state on RISC-V single-cycle sample: live FF values, mid-edge value chips, mux active-path all render in real-time after each Tick. The recent `MemoryFileLoader` 0x-prefix bug fix unblocked actually loading + executing a program through the Memory Viewer.

- **2026-06-04 — P4-5 (visible-probes tracker) landed.**
  - `SchematicPreviewControl` now tracks every probe path the renderer reads in a frame (`_visibleProbePaths`) — every wire value lookup (`LookupLiveValue`), every primitive body value (`DrawPrimitiveLiveOutput`), every mux selector resolve (`MatchActiveMuxInput`) registers its path.
  - `LiveProbeService.RefreshScalarsAsync(IEnumerable<string> paths, …)` reads only the requested probes from the worker, dedupes case-insensitively, and respects cancellation. The original `RefreshAllScalarsAsync` now delegates to the same core helper.
  - `MainWindowViewModel.VisibleProbePathsProvider` callback is set by `MainWindow.CreateBoundSchematicPreview` so the post-Tick refresh prefers the visible set; legacy "refresh all" stays as fallback when the set is empty.
  - Tests: +4 in `LiveProbeServiceRefreshScalarsTests.cs` (no-worker, empty list, duplicate paths, cancellation). Combined suite **639/639 green**.
  - Next P4 work: P4-4 (memory inline grid), P4-7 (hover tooltip), P4-8 (format toggle).
