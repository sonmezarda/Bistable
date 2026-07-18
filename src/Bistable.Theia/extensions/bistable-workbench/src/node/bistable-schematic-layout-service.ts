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
import {
    computeSymbolMetrics,
    pinDisplayLabel,
    pinLabel,
    pinPositionY
} from '../common/schematic-visual-contract';

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
    const metrics = computeSymbolMetrics(node);
    const layoutOptions: Record<string, string> = {
        // Every pin receives an explicit position inside the body region. This
        // keeps captions clear and centers a lone output against many inputs.
        'elk.portConstraints': 'FIXED_POS'
    };
    // Pin boundary ports to the outer layers so inputs sit at the far left and
    // outputs at the far right, visually separated from internal logic.
    const constraint = layerConstraint(node);
    if (constraint) {
        layoutOptions['elk.layered.layering.layerConstraint'] = constraint;
    }
    return {
        id: node.id,
        width: metrics.width,
        height: metrics.height,
        layoutOptions,
        ports: [
            ...node.inputs.map((signal, index) => fixedPort(
                inputPinId(node, signal),
                0,
                pinPositionY(metrics, index, node.inputs.length))),
            ...node.outputs.map((signal, index) => fixedPort(
                outputPinId(node, signal),
                metrics.width,
                pinPositionY(metrics, index, node.outputs.length)))
        ]
    };
}

function fixedPort(id: string, x: number, y: number): ElkPort {
    return {
        id,
        x: x - PinSize / 2,
        y: y - PinSize / 2,
        width: PinSize,
        height: PinSize
    };
}

/** FIRST for input boundary ports, LAST for output boundary ports, else none. */
function layerConstraint(node: EngineSchematicNode): 'FIRST' | 'LAST' | undefined {
    if (node.kind !== 'Port') {
        return undefined;
    }
    // An input port drives a signal out (has outputs); an output port consumes one.
    if (node.outputs.length > 0 && node.inputs.length === 0) {
        return 'FIRST';
    }
    if (node.inputs.length > 0 && node.outputs.length === 0) {
        return 'LAST';
    }
    return undefined;
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
    const nodeWidth = node.width ?? 1;
    const metrics = computeSymbolMetrics(source);
    const pins: EngineSchematicPin[] = [
        ...source.inputs.map((signal, index) => toLayoutPin(
            ports, inputPinId(source, signal), signal, pinLabel(source, 'input', index),
            pinDisplayLabel(source, 'input', index), 'input', nodeWidth)),
        ...source.outputs.map((signal, index) => toLayoutPin(
            ports, outputPinId(source, signal), signal, pinLabel(source, 'output', index),
            pinDisplayLabel(source, 'output', index), 'output', nodeWidth))
    ];
    return {
        ...source,
        x: node.x ?? 0,
        y: node.y ?? 0,
        width: nodeWidth,
        height: node.height ?? 1,
        pinLabelColumnWidth: metrics.pinLabelColumnWidth,
        headerHeight: metrics.headerHeight,
        pins
    };
}

function toLayoutPin(
    ports: Map<string, ElkPort>,
    id: string,
    signal: string,
    label: string,
    displayLabel: string,
    direction: 'input' | 'output',
    nodeWidth: number
): EngineSchematicPin {
    const port = ports.get(id);
    if (!port) {
        throw new Error(`ELK omitted schematic pin '${id}'.`);
    }
    return {
        id,
        signal,
        label,
        displayLabel,
        direction,
        // Snap the connection point flush to the node edge (WEST = left, EAST =
        // right) so the circle sits on the border instead of a few px inside.
        x: direction === 'input' ? 0 : nodeWidth,
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
