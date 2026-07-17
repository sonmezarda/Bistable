# Bistable Theia Workbench POC

This directory is the isolated Phase 9.5 product-shell spike. It does not
replace or delete `Bistable.App`; both frontends coexist until the measured
go/no-go decision in `docs/PHASES/PHASE-9.5.md`.

## Prerequisites

- Node.js 22+
- npm 10+
- The existing .NET 10, Verilator, Yosys, and elkjs requirements

## Build and run

```bash
cd src/Bistable.Theia
npm ci --ignore-scripts
npm run build
npm run start:browser -- ../../../samples/riscv_single_cycle
```

Open `http://127.0.0.1:3010` after the backend reports that it is listening.

The browser target is the dependency-light visual/interaction harness. The
desktop target uses the same extension and is built with `npm run build:all`;
on Ubuntu it additionally requires the standard Electron native development
prerequisites such as `libxkbfile-dev` and `libsecret-1-dev`.

The browser build explicitly rebuilds its required `drivelist` native module
after the script-free install. This keeps the browser POC runnable on hosts
that have not yet installed Electron's desktop-only native prerequisites.

The application package is intentionally small: Theia supplies Explorer,
Monaco, Problems, Terminal, Settings, commands, menus, and workbench layout.
Product-specific UI belongs in `extensions/bistable-workbench`; HDL and
simulation ownership remains in the .NET engine host.

The workbench automatically discovers a root-level `.bistable.json`, reloads
HDL saves with a 400 ms latest-save-wins debounce, publishes Verilator failures
to Problems, and exposes the engine's top-level schematic transport graph as a
separate main document tab. ELK layout runs in the Theia backend process and
the frontend draws typed RTL symbols with explicit pins and orthogonal routes.
Hierarchical module navigation is the next schematic slice.

The public Open VSX installer is deliberately not bundled in this first POC.
Theia 1.73.1 currently reaches a critical archive-extraction advisory through
its marketplace dependency chain. Re-enable VS Code extension installation
only after the pinned Theia line resolves that advisory; built-in Bistable
Theia extensions do not use the affected package path.

All Theia package versions are pinned to one exact release. Upgrade them as one
atomic dependency change and rebuild from `package-lock.json`.
