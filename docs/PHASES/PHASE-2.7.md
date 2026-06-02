# Phase 2.7 — Schematic UX & Persistence (live status)

**Master plan:** `/home/ardac/.claude/plans/fluffy-wishing-kettle.md`
**Phase goal:** Make the schematic feel like a professional EDA tool — search, breadcrumbs, mini-map, drill-down navigation, hover/selection feedback, manual layout overrides, view-state persistence, theme presets, export.
**Prerequisite:** Phase 2.5 + 2.6 complete (polish + constructs).
**Phase gate:** All 10 UX features functional with keyboard shortcuts where appropriate; user session state persists across app restarts; export produces valid SVG/PNG/PDF.

---

## 1. Why this phase matters

After 2.5 (polish) and 2.6 (constructs), the schematic RENDERS correctly. But the user can't navigate it. Vivado/Quartus/ModelSim have decades of UX investment in EDA navigation; matching the basics here is what makes the tool *usable* on real designs.

Without these:
- 10K-gate design = scroll-wheel hunting
- "Where is signal `pc_d`?" = no answer
- Layout shifts every time → muscle memory broken
- No way to share screenshots with colleagues

---

## 2. Task board

Status legend: ☐ todo · 🟡 in progress · ✅ done · ⛔ blocked

| ID | Task | Status | Model | Est. | Notes |
|----|------|--------|-------|------|-------|
| P2.7-1 | Search & navigate (Cmd/Ctrl+F) | ☐ | Sonnet | 3 d | Find signal/instance, highlight + scroll-into-view |
| P2.7-2 | Breadcrumb navigation | ✅ | Sonnet | 2 d | Clickable path + back/forward buttons + Alt+Left/Alt+Right — landed 2026-06-04. |
| P2.7-3 | Mini-map | ☐ | Sonnet | 3 d | Bottom-right overlay showing full schematic + viewport rect |
| P2.7-4 | Drill-down (zoom-into-module) | ☐ | Opus | 5 d | Double-click instance → enter its scope as new view |
| P2.7-5 | Hover state extensions | ✅ | Sonnet | 2 d | Ctrl+click multi-select + chip strip — landed 2026-06-04. Hover dimming infrastructure was already in place from Phase 4; this task added the pinned (sticky) multi-selection on top. |
| P2.7-6 | Routing aesthetics (junctions, bridges) | ☐ | Sonnet | 3 d | Junction dots at 3-way intersections; bridge notation for crossings |
| P2.7-7 | Layout overrides (drag-to-position) | ☐ | Opus | 1 wk | Drag instance → persist position; ELK respects FIXED_POSITION |
| P2.7-8 | View state persistence | ☐ | Sonnet | 3 d | Per-project: expanded paths, zoom, pan, last-selected, last-scope |
| P2.7-9 | Theme presets | ✅ | Sonnet | 2 d | Dark / Light / High-contrast / Print-friendly — landed 2026-06-04. |
| P2.7-10 | Export (PNG / SVG / PDF) | ☐ | Sonnet | 3 d | Selected scope → file or clipboard |

---

## 3. Detailed task specs

### P2.7-1 — Search & navigate

**UX:** `Cmd/Ctrl+F` opens a search overlay (TextBox at top of schematic). Typing filters live; matching signal/instance highlights yellow + scroll-into-view. `Enter` → next match. `Esc` → close.

**Files:**
- `src/Bistable.App/Views/SchematicSearchOverlay.cs` (new) — Avalonia control
- `src/Bistable.App/Services/SchematicSearch.cs` (new) — index + match logic
- `src/Bistable.App/Views/SchematicPreviewControl.cs` — key binding + highlight render
- `src/Bistable.App/Services/Routing/Elk/ElkGraphBuilder.cs` — search index built from primitive list

**Tests:** ≥ 5 (signal name match, instance name match, no-match handling, case-insensitive, regex toggle).

**Acceptance:** Find any signal/instance in any sample in < 1 s.

---

### P2.7-2 — Breadcrumb navigation

**UX:** Top of schematic panel shows clickable path `arnicomp_top > reg_a > reg_marl > ff_q`. Each segment is a button. Alt+Left = go up one level. Alt+Right = re-enter.

**Files:**
- `src/Bistable.App/Views/SchematicBreadcrumbControl.cs` (new)
- `src/Bistable.App/ViewModels/MainWindowViewModel.cs` — scope navigation history stack
- Wire to existing scope-selection commands

**Tests:** ≥ 4 (build breadcrumb from path, click → navigate, Alt+Left, history depth).

---

### P2.7-3 — Mini-map

**UX:** Bottom-right corner, ~200×150px overlay. Shows full schematic at scale + a viewport rectangle. Click → pan to that location. Drag the rectangle → scroll.

**Files:**
- `src/Bistable.App/Views/SchematicMiniMap.cs` (new)
- `src/Bistable.App/Views/SchematicPreviewControl.cs` — composes mini-map overlay

**Implementation notes:**
- Re-use ELK's laid-out positions; render at 1:N scale.
- Re-render only when ELK output changes (cheap).

**Tests:** ≥ 3 (scaling correctness, viewport rect tracking, click-pan).

---

### P2.7-4 — Drill-down (zoom-into-module)

**UX:** Double-click a sub-instance → that module becomes the new top-level view. URL-style navigation: each drill-down pushes onto history. Back button (or Esc) returns.

**Files:**
- `src/Bistable.App/ViewModels/MainWindowViewModel.cs` — `EnterModuleScope(string moduleName)` command + history
- `src/Bistable.App/Views/SchematicPreviewControl.cs` — double-click handler
- May need separate `_scopeStack` distinct from sub-sim (Phase 5)

**Decision:** Coexist with current `+` expand (inline) — drill-down is for DEEP navigation where inline doesn't fit.

**Tests:** ≥ 6 (enter scope, history push/pop, back across multi-level drill, state restoration).

**Model:** Opus — touches navigation state model.

---

### P2.7-5 — Hover state extensions

**UX:**
- Hover over a signal/wire → highlight all consumers + driver (orange tint)
- Ctrl+click → add to multi-selection
- Selection panel shows count + clear button

**Files:**
- `src/Bistable.App/Views/SchematicPreviewControl.cs` — pointer-move/down handlers
- New `HighlightContext` field for transient hover overlay
- Render pass: dim non-highlighted edges to 30% opacity when something is hovered

**Tests:** ≥ 4 (hover triggers, multi-select state, clear, no flicker on quick moves).

---

### P2.7-6 — Routing aesthetics (junctions + bridges)

**UX:**
- Where 3+ wires meet a single point → small dot (current renderer omits this).
- Where two wires CROSS without connecting → hop arc (bridge notation).

**Files:**
- `src/Bistable.App/Views/SchematicPreviewControl.Elk.cs` — post-edge-draw pass
- Detect crossings via line-intersection algorithm
- ELK already emits junction points in `ElkEdge.JunctionPoints` — verify and use

**Tests:** ≥ 4 (3-way junction dot, 4-way junction dot, crossing without junction = bridge, perpendicular crossings).

---

### P2.7-7 — Layout overrides (drag-to-position)

**UX:** User drags a child instance to a new position. The position persists. Subsequent re-layouts respect that position (ELK `org.eclipse.elk.position`).

**Files:**
- `src/Bistable.App/Views/SchematicPreviewControl.cs` — pointer drag handler on child nodes
- `src/Bistable.App/Services/Routing/Elk/LayoutOverrideStore.cs` (new) — persists to `.bistable/layout/<scope>.json`
- `src/Bistable.App/Services/Routing/Elk/ElkGraphBuilder.cs` — apply overrides to child nodes

**Decisions:**
- Override = per-scope, per-hierarchy-path.
- Reset menu item per scope.
- Overrides survive design re-elaboration.

**Tests:** ≥ 6 (drag persists, override file format, multiple per-scope overrides, reset, partial overrides).

**Model:** Opus — interaction + persistence + ELK config.

---

### P2.7-8 — View state persistence

**UX:** Re-opening a project restores: expanded compound paths, zoom level, pan position, selected hierarchy node, last-active scope.

**Files:**
- `src/Bistable.App/Services/ViewStateStore.cs` (new) — JSON in `.bistable/viewstate.json`
- `src/Bistable.App/ViewModels/MainWindowViewModel.cs` — load on open, save on relevant changes (debounced)

**Tests:** ≥ 4 (state save/load roundtrip, missing file graceful, version migration).

---

### P2.7-9 — Theme presets

**UX:** Settings panel (or `Cmd+,`): theme dropdown — Dark (current default), Light, High-contrast, Print-friendly. Live preview.

**Files:**
- `src/Bistable.App/Views/SchematicTheme.cs` — existing theme record, add presets
- `src/Bistable.App/ViewModels/SettingsViewModel.cs` (new or extend)
- Persistence via existing settings infrastructure

**Print-friendly preset specs:**
- White background, black strokes, gray gates, dashed clock lines (for monochrome printer-friendly).

**Tests:** ≥ 3 (theme selection persists, all 4 presets render without crash, palette swap is atomic).

---

### P2.7-10 — Export (PNG / SVG / PDF)

**UX:** `File > Export Schematic` → choose format → save dialog. Or Ctrl+Shift+E → copy SVG to clipboard.

**Files:**
- `src/Bistable.App/Services/SchematicExporter.cs` (new)
- SVG: re-render the laid-out ELK graph with SVG primitives (own pass, since Avalonia's RenderTargetBitmap is raster).
- PNG: RenderTargetBitmap of the schematic at 2x DPI.
- PDF: use a library like `QuestPDF` (free for OSS) or generate PostScript directly.

**Tests:** ≥ 5 (SVG well-formed XML, PNG dimensions, all 14 primitive types export, theme respected, clipboard copy works).

**Model:** Sonnet for SVG/PNG, may need Opus help if PDF library integration gets tricky.

---

## 4. Implementation order

Small-to-large for incremental shipping:

1. **P2.7-9** (2 d) — Themes: trivial, no dependencies.
2. **P2.7-2** (2 d) — Breadcrumb: small, high user-value.
3. **P2.7-5** (2 d) — Hover state.
4. **P2.7-1** (3 d) — Search.
5. **P2.7-3** (3 d) — Mini-map.
6. **P2.7-6** (3 d) — Junctions/bridges.
7. **P2.7-8** (3 d) — View state persistence.
8. **P2.7-10** (3 d) — Export.
9. **P2.7-4** (5 d) — Drill-down (touches nav state).
10. **P2.7-7** (1 wk) — Layout overrides (biggest, leave last).

Total: ~5 weeks serial; ~3 weeks parallel with 2 agents.

---

## 5. Cross-phase notes

- P2.7-7 (overrides) interacts with P2.6-2 (generate blocks) — overrides keyed on hierarchy path must handle `g[0..7]` cluster expansion.
- P2.7-4 (drill-down) needs Phase 5 sub-sim coordination — "drill" vs "isolate" are distinct semantics.
- P2.7-10 (export) and Phase 4 (live values) — export should capture current values snapshot too.

---

## 6. Recent activity

- **2026-06-04 — P2.7-9 (theme presets) landed.**
  - `SchematicTheme.HighContrast` (WCAG-AAA black/white/saturated) and `SchematicTheme.Print` (monochrome grayscale for printer-friendly screenshots) added to `Services/SchematicTheme.cs`. New `SchematicThemePreset` enum + `SchematicThemePresets.Get / DisplayName` switchers.
  - New `Services/UserPreferencesStore.cs` — JSON at `~/.bistable/preferences.json` (or `%APPDATA%/Bistable/preferences.json` on Windows). Corrupted-file fallback returns defaults instead of throwing.
  - `MainWindowViewModel` gained `SchematicThemePreset` (two-way) + `SchematicTheme` (resolved record). Constructor accepts an optional `UserPreferencesStore` for tests; loads the persisted preset on construction.
  - UI wiring: theme combo box in the schematic viewport toolbar (`BuildSchematicThemeComboBox` in `MainWindow.cs`); `SchematicPreviewControl.Palette` binding points to the ViewModel's `SchematicTheme` so swaps repaint instantly.
  - Tests: +11 new in `SchematicThemePresetsTests.cs` covering preset resolution, display names, preferences roundtrip, missing-file + corrupt-file fallback, VM-level preset change persists, and VM startup loads persisted preset. **558/558 green** (538 ELK + 14 snapshot + 4 regression + 2 UI).
- **Implementation order**: next per §4 is **P2.7-2 (breadcrumb navigation)** — small, high user-value, no deps.

- **2026-06-04 — P2.7-2 (breadcrumb + back/forward) landed.**
  - New `Services/ScopeNavigationHistory.cs` — browser-style past/future stacks. Records each navigation; same-path repeats ignored; `GoBack` / `GoForward` round-trip with proper future-clearing on fresh navigations.
  - `MainWindowViewModel` gained `NavigateScopeBackCommand` / `NavigateScopeForwardCommand` (RelayCommands with live CanExecute) plus `CanNavigateScopeBack` / `CanNavigateScopeForward` properties. The `SelectedHierarchyNode` setter pushes onto history unless `_suppressScopeHistoryPush` (set during Back/Forward) is true.
  - UI: new `BuildSchematicBreadcrumbBar` in `MainWindow.cs` between header and toolbar. ← / → buttons (with tooltips) for back/forward + clickable segments bound to `SelectedHierarchyBreadcrumbs` (already populated by an existing `BuildHierarchyBreadcrumbs` helper). Segments fire `SelectHierarchyScopeCommand` with the segment's `HierarchyPath`.
  - Keyboard shortcuts: `Alt+Left` / `Alt+Right` bound at the Window scope via `KeyBindings`.
  - Tests: +11 new in `SchematicBreadcrumbAndHistoryTests.cs` — initial disabled state, no-op when empty, `RecordNavigation`/`GoBack`/`GoForward` round-trip, fresh navigation clears future, same-path ignored, deep 4-level path round-trip. **569/569 green** (549 ELK + 14 snapshot + 4 regression + 2 UI).
- **Implementation order**: next per §4 is **P2.7-1 (search & navigate)** — Cmd/Ctrl+F overlay, scroll-into-view of matches.

- **2026-06-04 — settings home refactor (VS Code / JetBrains pattern).**
  Theme + router selectors were initially landed inline in the schematic toolbar (P2.7-9 first wave). The user flagged that the panel toolbar isn't the right home for global rendering preferences — frequently-used viewport controls and rarely-changed settings shouldn't compete. Refactor adopted a **hybrid model**:
  - **Schematic panel toolbar** now holds only viewport actions (Fit / 1:1 / + / − / Studio) + the breadcrumb bar.
  - **Top-level View menu** gained "Schematic Theme" and "Routing Engine" submenus — radio-style items bound to the VM's `SchematicThemePreset` / `SchematicRouter` properties through a small `EnumEqualsConverter`. Fast keyboardable access for power users.
  - **Preferences window** (new) opens via `File → Preferences...` or `Ctrl+,`. VS Code-style: category list on the left (just "Schematic" today; future phases append more), the focused category's form on the right with labeled fields and inline descriptions. DataContext is the same `MainWindowViewModel` so edits live-propagate.
  - VM persists both `SchematicTheme` + `SchematicRouter` together via the extended `UserPreferences` schema. The `SchematicPreviewControl.RoutingEngine` property is now bound to the VM (was a UI-only side channel before).
  - Tests: +2 in `SchematicThemePresetsTests.cs` (`UserPreferencesStore_RoundtripsSchematicRouter`, `MainWindowViewModel_SchematicRouterChange_PersistsAndKeepsTheme`). Combined suite **571/571 green** (551 ELK + 14 snapshot + 4 regression + 2 UI).
  - **Why now**: future P2.7 sub-tasks (mini-map defaults, hover behaviour, layout-override management, export defaults, keymap) all need a home. Setting up the Preferences scaffold once means every future setting lands in the right place instead of accreting on the toolbar.

- **2026-06-04 — P2.7-5 (hover state extensions / multi-select) landed.**
  Hover-dim infrastructure was already present from Phase 4. This task added the **sticky multi-selection** layer on top so users can build up a comparison set instead of needing to keep the cursor over each wire.
  - `SchematicPreviewControl` gained `_pinnedSignalNames` (case-insensitive HashSet) + public `PinnedSignalNames` / `TogglePinnedSignal` / `ClearPinnedSignals` + `PinnedSignalsChanged` event.
  - `HandleSignalReferenceHit` now branches on `KeyModifiers.Control`: Ctrl+click toggles pin and bypasses the single-selection path, so the inspector + drive panel keep their existing semantics.
  - Edge renderer (`SchematicPreviewControl.Elk.cs:BuildElkEdgeStyle` and `SchematicPreviewControl.Routing.cs`) treats pinned wires as highlighted alongside the hovered net. Non-highlighted edges dim when either hover OR any pin is active.
  - `MainWindowViewModel` exposes a live `PinnedSignals` `ObservableCollection<string>` mirror + `ClearPinnedSignalsCommand` + `ClearPinnedSignalsRequested` event. `MainWindow` wires `preview.PinnedSignalsChanged → vm.RefreshPinnedSignals(preview.PinnedSignalNames)` and routes `vm.ClearPinnedSignalsRequested → preview.ClearPinnedSignals()`.
  - UI: new chip strip row in the schematic panel under the breadcrumb. Bound to `PinnedSignals`; collapses to zero height when empty via a `CountGreaterThanZeroConverter`. Each chip is a rounded accent-bordered label; trailing "Clear" button fires `ClearPinnedSignalsCommand`.
  - Tests: +5 in `PinnedSignalsTests.cs` covering initial-empty, refresh mirrors exactly, refresh replaces previous content (not additive), Clear command fires the event, empty refresh wipes the collection. Combined suite **576/576 green** (556 ELK + 14 snapshot + 4 regression + 2 UI).
