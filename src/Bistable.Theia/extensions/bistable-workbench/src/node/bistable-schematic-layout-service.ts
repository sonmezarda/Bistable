import ELK, {
    ElkExtendedEdge,
    ElkNode,
    ElkPort
} from 'elkjs/lib/elk.bundled';
import {
    EngineSchematicGraph,
    EngineSchematicLayout,
    EngineSchematicLayoutEdge,
    EngineSchematicLayoutNode,
    EngineSchematicNode,
    EngineSchematicPin
} from '../common/bistable-engine-protocol';

const elk = new ELK();
const PinSize = 8;

export async function layoutSchematicWithElk(graph: EngineSchematicGraph): Promise<EngineSchematicLayout> {
    const sourceById = new Map(graph.nodes.map(node => [node.id, node]));
    const signalByEdgeId = new Map(graph.edges.map(edge => [edge.id, edge.signal]));
    const elkGraph: ElkNode = {
        id: 'bistable-schematic-root',
        layoutOptions: {
            'elk.algorithm': 'layered',
            'elk.direction': 'RIGHT',
            'elk.edgeRouting': 'ORTHOGONAL',
            'elk.hierarchyHandling': 'INCLUDE_CHILDREN',
            'elk.layered.spacing.nodeNodeBetweenLayers': '110',
            'elk.spacing.nodeNode': '42',
            'elk.spacing.edgeNode': '24',
            'elk.layered.nodePlacement.strategy': 'NETWORK_SIMPLEX',
            'elk.layered.crossingMinimization.strategy': 'LAYER_SWEEP',
            'elk.padding': '[top=36,left=36,bottom=36,right=36]'
        },
        children: graph.nodes.map(toElkNode),
        edges: graph.edges.map(edge => {
            const source = sourceById.get(edge.sourceNodeId);
            const target = sourceById.get(edge.targetNodeId);
            if (!source || !target) {
                throw new Error(`Schematic edge '${edge.id}' refers to a missing node.`);
            }
            return {
                id: edge.id,
                sources: [outputPinId(source, edge.signal)],
                targets: [inputPinId(target, edge.signal)]
            } satisfies ElkExtendedEdge;
        })
    };

    const result = await elk.layout(elkGraph);
    const nodes = (result.children ?? []).map(child => toLayoutNode(child, sourceById));
    const edges = (result.edges ?? []).map(edge => toLayoutEdge(edge, signalByEdgeId));
    return {
        width: Math.max(1, result.width ?? 1),
        height: Math.max(1, result.height ?? 1),
        nodes,
        edges
    };
}

function toElkNode(node: EngineSchematicNode): ElkNode {
    const size = symbolSize(node);
    return {
        id: node.id,
        width: size.width,
        height: size.height,
        layoutOptions: {
            'elk.portConstraints': 'FIXED_ORDER'
        },
        ports: [
            ...node.inputs.map((signal, index) => toElkPort(inputPinId(node, signal), 'WEST', index)),
            ...node.outputs.map((signal, index) => toElkPort(outputPinId(node, signal), 'EAST', index))
        ]
    };
}

function toElkPort(id: string, side: 'WEST' | 'EAST', index: number): ElkPort {
    return {
        id,
        width: PinSize,
        height: PinSize,
        layoutOptions: {
            'elk.port.side': side,
            'elk.port.index': String(index)
        }
    };
}

function symbolSize(node: EngineSchematicNode): { width: number; height: number } {
    const pinRows = Math.max(node.inputs.length, node.outputs.length, 1);
    switch (node.kind) {
        case 'Port': return { width: 86, height: 32 };
        case 'Mux': return { width: 92, height: Math.max(82, pinRows * 18 + 24) };
        case 'Gate':
        case 'Inverter':
        case 'Buffer': return { width: 96, height: Math.max(64, pinRows * 18 + 16) };
        case 'FlipFlop':
        case 'Latch': return { width: 112, height: Math.max(86, pinRows * 18 + 24) };
        case 'Instance': return { width: 168, height: Math.max(82, pinRows * 18 + 34) };
        case 'Memory':
        case 'MemoryRead': return { width: 132, height: Math.max(86, pinRows * 18 + 28) };
        default: return { width: 112, height: Math.max(66, pinRows * 18 + 20) };
    }
}

function toLayoutNode(
    node: ElkNode,
    sourceById: Map<string, EngineSchematicNode>
): EngineSchematicLayoutNode {
    const source = sourceById.get(node.id);
    if (!source) {
        throw new Error(`ELK returned unknown schematic node '${node.id}'.`);
    }
    const ports = new Map((node.ports ?? []).map(port => [port.id, port]));
    const pins: EngineSchematicPin[] = [
        ...source.inputs.map(signal => toLayoutPin(ports, inputPinId(source, signal), signal, 'input')),
        ...source.outputs.map(signal => toLayoutPin(ports, outputPinId(source, signal), signal, 'output'))
    ];
    return {
        ...source,
        x: node.x ?? 0,
        y: node.y ?? 0,
        width: node.width ?? 1,
        height: node.height ?? 1,
        pins
    };
}

function toLayoutPin(
    ports: Map<string, ElkPort>,
    id: string,
    signal: string,
    direction: 'input' | 'output'
): EngineSchematicPin {
    const port = ports.get(id);
    if (!port) {
        throw new Error(`ELK omitted schematic pin '${id}'.`);
    }
    return {
        id,
        signal,
        direction,
        x: (port.x ?? 0) + (port.width ?? PinSize) / 2,
        y: (port.y ?? 0) + (port.height ?? PinSize) / 2
    };
}

function toLayoutEdge(
    edge: ElkExtendedEdge,
    signalByEdgeId: Map<string, string>
): EngineSchematicLayoutEdge {
    const points = (edge.sections ?? []).flatMap(section => [
        section.startPoint,
        ...(section.bendPoints ?? []),
        section.endPoint
    ]);
    return {
        id: edge.id,
        signal: signalByEdgeId.get(edge.id) ?? '',
        points
    };
}

function inputPinId(node: EngineSchematicNode, signal: string): string {
    const index = node.inputs.findIndex(candidate => sameSignal(candidate, signal));
    if (index < 0) {
        throw new Error(`Node '${node.id}' has no input for signal '${signal}'.`);
    }
    return `${node.id}:input:${index}`;
}

function outputPinId(node: EngineSchematicNode, signal: string): string {
    const index = node.outputs.findIndex(candidate => sameSignal(candidate, signal));
    if (index < 0) {
        throw new Error(`Node '${node.id}' has no output for signal '${signal}'.`);
    }
    return `${node.id}:output:${index}`;
}

function sameSignal(left: string, right: string): boolean {
    return left.toLocaleLowerCase() === right.toLocaleLowerCase();
}
