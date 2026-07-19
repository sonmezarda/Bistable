import {
    EngineProjectPort,
    EngineSchematicLayoutNode,
    EngineSimulationFrame,
    EngineSimulationProbe,
    EngineSimulationReadResult,
    EngineSimulationSnapshot
} from '../common/bistable-engine-protocol';

/**
 * Identity of a signal the user selected on the schematic. Carries the exact
 * module-local signal name plus the hierarchical probe path — we never infer a
 * bus from a display label, and per-bit identity is preserved end to end.
 */
export interface SelectedSignal {
    /** Module-local signal name as it appears on the schematic pin/edge. */
    signal: string;
    /** Hierarchical probe path, e.g. `top.acc.q`. */
    path: string;
    /** Kind of the owning node (Port / FlipFlop / …). */
    nodeKind: string;
}

export type PokeAction = 'toggle' | 'edit' | 'select';

/**
 * The single choke point deciding whether a selection may drive
 * `simulation.setInput`. Only the root (top-module) schematic document may
 * resolve a drive port: a child module's boundary port is *not* a top-level
 * input even when its module-local name matches one, so hierarchical documents
 * must always get `undefined` here (poke-safety regression contract).
 */
export function topLevelDrivePort(
    ports: readonly EngineProjectPort[] | undefined,
    selected: SelectedSignal,
    documentIsRoot: boolean
): EngineProjectPort | undefined {
    if (!documentIsRoot || !ports) {
        return undefined;
    }
    return ports.find(candidate => candidate.name === selected.signal);
}

/**
 * Only an exact top-level input Port is mutable in Poke mode. A scalar toggles,
 * a bus opens the value editor, and every other signal remains selection-only.
 */
export function pokeAction(selected: SelectedSignal, port: EngineProjectPort | undefined): PokeAction {
    if (selected.nodeKind !== 'Port'
        || !port
        || port.name !== selected.signal
        || port.direction.toLowerCase() !== 'input') {
        return 'select';
    }
    return port.width === 1 ? 'toggle' : 'edit';
}

/** Normalize supported worker scalar renderings without guessing for X/Z. */
export function logicBitValue(raw: string | undefined): '0' | '1' | undefined {
    if (raw === undefined) {
        return undefined;
    }
    switch (raw.trim().toLowerCase().replaceAll('_', '')) {
        case '0':
        case '0x0':
        case "1'h0":
        case "1'b0":
        case "1'd0":
            return '0';
        case '1':
        case '0x1':
        case "1'h1":
        case "1'b1":
        case "1'd1":
            return '1';
        default:
            return undefined;
    }
}

export function nextBinaryToggleValue(raw: string | undefined): '0' | '1' | undefined {
    const current = logicBitValue(raw);
    return current === undefined ? undefined : current === '0' ? '1' : '0';
}

/**
 * Click target for node bodies whose complete symbol represents one signal.
 * Ports and constants qualify; gates/muxes do not because their body spans
 * multiple independent inputs and outputs.
 */
export interface NodeBodySelectionTarget {
    selected: SelectedSignal;
    x: number;
    y: number;
    width: number;
    height: number;
}

export function nodeBodySelectionTarget(
    node: EngineSchematicLayoutNode,
    topModule: string
): NodeBodySelectionTarget | undefined {
    if (node.kind !== 'Port' && node.kind !== 'Constant') {
        return undefined;
    }

    // A pass-through boundary port of an inline-expanded instance carries a
    // direction hint and both sides; its identity is the *inner* namespaced
    // net (`u_alu.y`), never the parent net it connects to.
    const signal = node.kind === 'Port' && node.typeLabel === 'output'
        ? node.inputs[0] ?? node.outputs[0]
        : node.outputs[0] ?? node.inputs[0];
    if (!signal) {
        return undefined;
    }

    if (node.kind === 'Constant') {
        // Keep the interaction geometry identical to renderConstant(). The
        // layout node may be taller for routing, but only the visible literal
        // box should behave as the click target.
        const height = Math.min(node.height, 26);
        return {
            selected: { signal, path: probePath(topModule, signal), nodeKind: node.kind },
            x: 2,
            y: (node.height - height) / 2,
            width: Math.max(0, node.width - 4),
            height
        };
    }

    return {
        selected: { signal, path: probePath(topModule, signal), nodeKind: node.kind },
        x: 0,
        y: 0,
        width: node.width,
        height: node.height
    };
}

/**
 * The frontend-side, presentation-only simulation state. A superseded session
 * (older `generation`) must never overwrite a newer one — the widget checks
 * `generation` before applying any late frame or read.
 */
export interface SimulationState {
    generation: number;
    topModule: string;
    /** Probe path → scalar probe metadata, for width/direction lookups. */
    probes: Map<string, EngineSimulationProbe>;
    /** signal name / probe path → current value string. */
    values: Map<string, string>;
    selected?: SelectedSignal;
    /** Ports the user has explicitly driven this session. */
    driven: Set<string>;
    status: 'idle' | 'starting' | 'ready' | 'stale' | 'error';
    errorMessage?: string;
}

export function emptySimulationState(): SimulationState {
    return {
        generation: 0,
        topModule: '',
        probes: new Map(),
        values: new Map(),
        driven: new Set(),
        status: 'idle'
    };
}

/** Full hierarchical probe path for a top-module signal. */
export function probePath(topModule: string, signal: string): string {
    return `${topModule}.${signal}`;
}

/**
 * Seed a fresh state from a session snapshot. Increments the generation so a
 * late frame from a previous worker cannot be applied afterwards.
 */
export function applySnapshot(previous: SimulationState, snapshot: EngineSimulationSnapshot): SimulationState {
    const probes = new Map(snapshot.probes.map(probe => [probe.path, probe]));
    const values = new Map<string, string>();
    seedFrame(values, snapshot.topModule, snapshot.initialFrame);
    return {
        generation: previous.generation + 1,
        topModule: snapshot.topModule,
        probes,
        values,
        driven: new Set(),
        status: 'ready'
    };
}

/** Apply a stepping frame's top-level output values in place (returns a new map holder). */
export function applyFrame(state: SimulationState, frame: EngineSimulationFrame): SimulationState {
    const values = new Map(state.values);
    seedFrame(values, state.topModule, frame);
    return { ...state, values, status: 'ready' };
}

/** Merge a batched read result; a per-path error leaves the prior value untouched. */
export function applyReadResult(state: SimulationState, result: EngineSimulationReadResult): SimulationState {
    const values = new Map(state.values);
    for (const outcome of result.results) {
        if (outcome.error || outcome.value === undefined || outcome.value === null) {
            continue;
        }
        values.set(outcome.path, outcome.value);
    }
    return { ...state, values };
}

function seedFrame(values: Map<string, string>, topModule: string, frame: EngineSimulationFrame): void {
    for (const sample of frame.signals) {
        // Frame samples are top-level outputs keyed by module-local name; store
        // under both the local name and the hierarchical path so an overlay can
        // key on whichever the layout exposes.
        values.set(sample.signal, sample.value);
        values.set(probePath(topModule, sample.signal), sample.value);
    }
}

/**
 * Compute the CSS classes for a schematic pin given the current selection,
 * driven set, and whether a live value is known. Pure and DOM-free so it can be
 * unit-tested directly (mandatory schematic-state test).
 *
 * `documentIsRoot` guards the module-local shortcuts: the driven set and the
 * bare-name value map hold top-level names, so in a hierarchical document a
 * same-named child signal must not light up as driven/live from them.
 */
export function pinClasses(
    signal: string,
    path: string,
    state: SimulationState,
    documentIsRoot = true
): string {
    const classes = ['bistable-pin-overlay'];
    if (state.selected && state.selected.path === path) {
        classes.push('bistable-pin-selected');
    }
    if (documentIsRoot && state.driven.has(signal)) {
        classes.push('bistable-pin-driven');
    }
    if (state.values.has(path) || (documentIsRoot && state.values.has(signal))) {
        classes.push('bistable-pin-live');
    }
    return classes.join(' ');
}

/**
 * Resolve the live value for a schematic signal, preferring the exact path.
 * The module-local name fallback exists for top-level frame outputs only —
 * hierarchical documents must pass `documentIsRoot = false` so a child net
 * never borrows the value of a same-named top-level signal.
 */
export function liveValue(
    signal: string,
    path: string,
    state: SimulationState,
    documentIsRoot = true
): string | undefined {
    return state.values.get(path) ?? (documentIsRoot ? state.values.get(signal) : undefined);
}

/**
 * Deduplicated union of every open document's visible probe paths. The live
 * loop reads this union in one batched `ReadSignals`, so parent and child
 * schematics refresh from a single worker round-trip per action.
 */
export function mergeVisiblePaths(registry: ReadonlyMap<string, readonly string[]>): string[] {
    const union = new Set<string>();
    for (const paths of registry.values()) {
        for (const path of paths) {
            union.add(path);
        }
    }
    return [...union];
}
