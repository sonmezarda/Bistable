export const BistableEngineServicePath = '/services/bistable-engine';
export const BistableEngineService = Symbol('BistableEngineService');

export interface EngineHelloResult {
    protocolVersion: number;
    engineVersion: string;
    capabilities: string[];
}

export interface EngineProjectPort {
    name: string;
    direction: string;
    width: number;
    isSigned: boolean;
}

export interface EngineProjectSummary {
    projectPath: string;
    projectDirectory: string;
    topModule: string;
    moduleCount: number;
    ports: EngineProjectPort[];
    verilatorVersion: string;
    elapsedMs: number;
    schematic: EngineSchematicGraph;
}

export interface EngineSchematicGraph {
    moduleName: string;
    nodes: EngineSchematicNode[];
    edges: EngineSchematicEdge[];
}

export interface EngineSchematicNode {
    id: string;
    kind: string;
    label: string;
    inputs: string[];
    outputs: string[];
    /** Short semantic pin names (A/B/Y, D/Q/CLK, module port names). */
    inputLabels?: string[];
    outputLabels?: string[];
    /** Module type for instance symbols; `label` remains the instance name. */
    typeLabel?: string;
    /**
     * Inline hierarchy expansion: id of the Container node this node is laid
     * out inside (absent at the document root). Expanded internals carry
     * instance-namespaced signals (`u_alu.zero`), so probe identity survives.
     */
    containerId?: string;
}

export interface EngineSchematicEdge {
    id: string;
    signal: string;
    sourceNodeId: string;
    targetNodeId: string;
}

export interface EngineSchematicLayout {
    width: number;
    height: number;
    nodes: EngineSchematicLayoutNode[];
    edges: EngineSchematicLayoutEdge[];
}

export interface EngineSchematicLayoutNode extends EngineSchematicNode {
    x: number;
    y: number;
    width: number;
    height: number;
    pinLabelColumnWidth: number;
    headerHeight: number;
    pins: EngineSchematicPin[];
}

export interface EngineSchematicPin {
    id: string;
    signal: string;
    /** Full semantic label. Exact net identity remains in `signal`. */
    label: string;
    /** Pixel-budgeted label rendered inside the symbol. */
    displayLabel: string;
    direction: 'input' | 'output';
    x: number;
    y: number;
}

export interface EngineSchematicLayoutEdge {
    id: string;
    signal: string;
    points: EngineSchematicPoint[];
}

export interface EngineSchematicPoint {
    x: number;
    y: number;
}

/**
 * Result of `loadModuleSchematic`: the hierarchical instance path is the
 * document identity (two instances of the same module type stay distinct);
 * `moduleName` is display metadata only.
 */
export interface EngineModuleSchematic {
    instancePath: string;
    moduleName: string;
    schematic: EngineSchematicGraph;
}

export interface EngineDiagnostic {
    severity: 'Warning' | 'Error';
    code?: string;
    message: string;
    filePath: string;
    line: number;
    column: number;
}

export interface EngineProjectLoadResult {
    project?: EngineProjectSummary;
    diagnostics: EngineDiagnostic[];
    errorMessage?: string;
}

// ── simulation.* (engine protocol v2) ────────────────────────────────────

export interface EngineSimulationSignal {
    signal: string;
    value: string;
}

export interface EngineSimulationFrame {
    time: number;
    signals: EngineSimulationSignal[];
}

export interface EngineSimulationProbe {
    path: string;
    width: number;
    isSigned: boolean;
    isRegistered: boolean;
    isMemory: boolean;
}

export interface EngineSimulationSnapshot {
    topModule: string;
    ports: EngineProjectPort[];
    probes: EngineSimulationProbe[];
    initialFrame: EngineSimulationFrame;
}

export interface EngineSimulationReadOutcome {
    path: string;
    value?: string;
    width: number;
    isSigned: boolean;
    error?: string;
}

export interface EngineSimulationReadResult {
    results: EngineSimulationReadOutcome[];
}

/**
 * A caller value that failed width/format validation before any worker IPC.
 * Surfaced as a rejected promise carrying the structured engine message.
 */
export class EngineSimulationValidationError extends Error {
}

export interface BistableEngineService {
    hello(): Promise<EngineHelloResult>;
    loadProject(projectPath: string): Promise<EngineProjectLoadResult>;
    loadModuleSchematic(projectPath: string, instancePath: string, expand?: string[]): Promise<EngineModuleSchematic>;
    layoutSchematic(graph: EngineSchematicGraph): Promise<EngineSchematicLayout>;
    startSimulation(projectPath: string): Promise<EngineSimulationSnapshot>;
    setInput(signal: string, value: string): Promise<EngineSimulationFrame>;
    evalDesign(): Promise<EngineSimulationFrame>;
    tick(clock?: string): Promise<EngineSimulationFrame>;
    reset(): Promise<EngineSimulationFrame>;
    readSignals(paths: string[]): Promise<EngineSimulationReadResult>;
    stopSimulation(): Promise<void>;
}
