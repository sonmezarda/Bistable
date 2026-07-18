import {
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

    const signal = node.outputs[0] ?? node.inputs[0];
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
 */
export function pinClasses(
    signal: string,
    path: string,
    state: SimulationState
): string {
    const classes = ['bistable-pin-overlay'];
    if (state.selected && state.selected.path === path) {
        classes.push('bistable-pin-selected');
    }
    if (state.driven.has(signal)) {
        classes.push('bistable-pin-driven');
    }
    if (state.values.has(path) || state.values.has(signal)) {
        classes.push('bistable-pin-live');
    }
    return classes.join(' ');
}

/** Resolve the live value for a schematic signal, preferring the exact path. */
export function liveValue(signal: string, path: string, state: SimulationState): string | undefined {
    return state.values.get(path) ?? state.values.get(signal);
}
