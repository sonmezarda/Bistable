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
    pins: EngineSchematicPin[];
}

export interface EngineSchematicPin {
    id: string;
    signal: string;
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

export interface BistableEngineService {
    hello(): Promise<EngineHelloResult>;
    loadProject(projectPath: string): Promise<EngineProjectLoadResult>;
    layoutSchematic(graph: EngineSchematicGraph): Promise<EngineSchematicLayout>;
}
