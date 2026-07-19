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
const RootId = 'bistable-schematic-root';
/** Vertical room reserved for a Container's two-line header. */
const ContainerHeaderHeight = 40;

export async function layoutSchematicWithElk(graph: EngineSchematicGraph): Promise<EngineSchematicLayout> {
    const sourceById = new Map(graph.nodes.map(node => [node.id, node]));
    const signalByEdgeId = new Map(graph.edges.map(edge => [edge.id, edge.signal]));
    const childrenByContainer = new Map<string | undefined, EngineSchematicNode[]>();
    for (const node of graph.nodes) {
        const siblings = childrenByContainer.get(node.containerId) ?? [];
        siblings.push(node);
        childrenByContainer.set(node.containerId, siblings);
    }
    const elkGraph: ElkNode = {
        id: RootId,
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
        children: (childrenByContainer.get(undefined) ?? []).map(node => toElkNode(node, childrenByContainer)),
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
    // Flatten the container hierarchy to absolute coordinates: ELK reports
    // child positions relative to their parent container.
    const nodes: EngineSchematicLayoutNode[] = [];
    const originById = new Map<string, { x: number; y: number }>([[RootId, { x: 0, y: 0 }]]);
    flattenInto(result.children ?? [], 0, 0, sourceById, nodes, originById);
    const edges = (result.edges ?? []).map(edge => toLayoutEdge(edge, signalByEdgeId, originById));
    return {
        width: Math.max(1, result.width ?? 1),
        height: Math.max(1, result.height ?? 1),
        nodes,
        edges
    };
}

function toElkNode(
    node: EngineSchematicNode,
    childrenByContainer: Map<string | undefined, EngineSchematicNode[]>
): ElkNode {
    if (node.kind === 'Container') {
        // A container is sized by ELK around its expanded children; the top
        // padding reserves the header band for the instance/module captions.
        return {
            id: node.id,
            layoutOptions: {
                'elk.padding': `[top=${ContainerHeaderHeight + 8},left=18,bottom=18,right=18]`
            },
            children: (childrenByContainer.get(node.id) ?? []).map(child => toElkNode(child, childrenByContainer))
        };
    }
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

/**
 * FIRST for input boundary ports, LAST for output boundary ports, else none.
 * Only the document's own boundary applies: a Port inside an expanded
 * Container must never be pinned to the whole graph's outer layers, and a
 * pass-through boundary port (both sides connected) carries no constraint.
 */
function layerConstraint(node: EngineSchematicNode): 'FIRST' | 'LAST' | undefined {
    if (node.kind !== 'Port' || node.containerId) {
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

/** Depth-first flatten: parents precede children so containers paint below. */
function flattenInto(
    elkNodes: ElkNode[],
    offsetX: number,
    offsetY: number,
    sourceById: Map<string, EngineSchematicNode>,
    out: EngineSchematicLayoutNode[],
    originById: Map<string, { x: number; y: number }>
): void {
    for (const elkNode of elkNodes) {
        const absoluteX = offsetX + (elkNode.x ?? 0);
        const absoluteY = offsetY + (elkNode.y ?? 0);
        originById.set(elkNode.id, { x: absoluteX, y: absoluteY });
        out.push(toLayoutNode(elkNode, absoluteX, absoluteY, sourceById));
        if (elkNode.children && elkNode.children.length > 0) {
            flattenInto(elkNode.children, absoluteX, absoluteY, sourceById, out, originById);
        }
    }
}

function toLayoutNode(
    node: ElkNode,
    absoluteX: number,
    absoluteY: number,
    sourceById: Map<string, EngineSchematicNode>
): EngineSchematicLayoutNode {
    const source = sourceById.get(node.id);
    if (!source) {
        throw new Error(`ELK returned unknown schematic node '${node.id}'.`);
    }
    const nodeWidth = node.width ?? 1;
    if (source.kind === 'Container') {
        return {
            ...source,
            x: absoluteX,
            y: absoluteY,
            width: nodeWidth,
            height: node.height ?? 1,
            pinLabelColumnWidth: 0,
            headerHeight: ContainerHeaderHeight,
            pins: []
        };
    }
    const ports = new Map((node.ports ?? []).map(port => [port.id, port]));
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
        x: absoluteX,
        y: absoluteY,
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
    signalByEdgeId: Map<string, string>,
    originById: Map<string, { x: number; y: number }>
): EngineSchematicLayoutEdge {
    // With INCLUDE_CHILDREN, elkjs reports each edge's coordinates relative to
    // a reference container (`edge.container`). Shift them to absolute space.
    const containerId = (edge as ElkExtendedEdge & { container?: string }).container;
    const origin = (containerId && originById.get(containerId)) || { x: 0, y: 0 };
    const points = (edge.sections ?? []).flatMap(section => [
        section.startPoint,
        ...(section.bendPoints ?? []),
        section.endPoint
    ]).map(point => ({ x: point.x + origin.x, y: point.y + origin.y }));
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
