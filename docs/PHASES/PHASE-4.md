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
| P4-1 | Edge live values — annotate wires with current value | ☐ | Sonnet | 2 d | Mid-edge label showing hex value; auto-suppress for 1-bit (already colored). Use existing `signalValues` lookup, just extend to query `LiveProbeService` for internal signals. |
| P4-2 | FF Q values on FlipFlop symbol body | ☐ | Sonnet | 2 d | Render the live Q value inside the FF body. Polling via subscription model. |
| P4-3 | Mux active-path highlight | ☐ | Sonnet | 1 d | Read selector value, render the selected input edge with thicker pen + accent color. |
| P4-4 | Memory inline grid on RAM tiles | ☐ | Sonnet | 2 d | Like Logisim Evolution: draw a small NxN hex grid inside the memory primitive. Subset of cells visible at viewport zoom; click → opens viewer window. |
| P4-5 | Visible-probes tracker + batched refresh | ☐ | Sonnet | 1 d | Track which paths the renderer accessed in the last frame. After each Eval/Tick, batch-refresh just those paths instead of all 100s. |
| P4-6 | Symbol-body value rendering (Latch/Buffer/Inverter/Gate/Arith outputs) | ☐ | Sonnet | 1 d | Same as FF but for the other primitive types. Output value shown near the output port. |
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

(empty — phase starting)
