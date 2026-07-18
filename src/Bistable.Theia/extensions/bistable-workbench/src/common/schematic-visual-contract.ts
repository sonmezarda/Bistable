import { EngineSchematicNode } from './bistable-engine-protocol';

/**
 * Vivado-inspired schematic density contract. Text is rendered with a fixed
 * 10 px monospace face, so a conservative character-cell estimate is stable
 * in the Node layout process and does not require browser text measurement.
 */
const PinCharacterWidth = 6.4;
const TitleCharacterWidth = 7.1;
const PinLabelLimit = 13;
const TitleLabelLimit = 25;
const MaximumPinColumnWidth = 84;
const PinTextInset = 14;
const RowHeight = 20;

export interface SchematicSymbolMetrics {
    width: number;
    height: number;
    headerHeight: number;
    bottomInset: number;
    pinLabelColumnWidth: number;
    centerWidth: number;
}

export function computeSymbolMetrics(node: EngineSchematicNode): SchematicSymbolMetrics {
    if (node.kind === 'Port' || node.kind === 'Constant') {
        const title = elideMiddle(node.label, node.kind === 'Port' ? 15 : 12);
        const minimum = minimumWidth(node.kind);
        return {
            width: Math.ceil(Math.max(minimum, measureTitleText(title) + 24)),
            height: node.kind === 'Port' ? 32 : 30,
            headerHeight: 0,
            bottomInset: 0,
            pinLabelColumnWidth: 0,
            centerWidth: minimum
        };
    }
    const labelsVisible = showsPinLabels(node);
    const displayLabels = labelsVisible
        ? [...pinLabels(node, 'input'), ...pinLabels(node, 'output')]
            .map(label => elideMiddle(label, PinLabelLimit))
        : [];
    const pinLabelColumnWidth = displayLabels.length === 0
        ? 0
        : Math.min(MaximumPinColumnWidth, Math.max(8, ...displayLabels.map(measurePinText)));
    const centerWidth = symbolCenterWidth(node.kind);
    const pinAwareWidth = labelsVisible
        ? 2 * (PinTextInset + pinLabelColumnWidth) + centerWidth
        : minimumWidth(node.kind);
    const title = node.kind === 'Instance'
        ? elideMiddle(node.label, TitleLabelLimit)
        : elideMiddle(node.label, 18);
    const typeTitle = node.typeLabel ? elideMiddle(node.typeLabel, TitleLabelLimit) : '';
    const titleWidth = Math.max(measureTitleText(title), measureTitleText(typeTitle)) + 24;
    const headerHeight = symbolHeaderHeight(node.kind);
    const bottomInset = 8;
    const rows = Math.max(node.inputs.length, node.outputs.length, 1);
    const pinRegionHeight = Math.max(minimumPinRegionHeight(node.kind), (rows + 1) * RowHeight);

    return {
        width: Math.ceil(Math.max(minimumWidth(node.kind), pinAwareWidth, titleWidth)),
        height: Math.ceil(headerHeight + pinRegionHeight + bottomInset),
        headerHeight,
        bottomInset,
        pinLabelColumnWidth,
        centerWidth
    };
}

export function pinLabel(node: EngineSchematicNode, direction: 'input' | 'output', index: number): string {
    const configured = direction === 'input' ? node.inputLabels?.[index] : node.outputLabels?.[index];
    return configured?.trim() || fallbackPinLabel(node.kind, direction, index);
}

export function pinDisplayLabel(node: EngineSchematicNode, direction: 'input' | 'output', index: number): string {
    return elideMiddle(pinLabel(node, direction, index), PinLabelLimit);
}

export function pinPositionY(
    metrics: SchematicSymbolMetrics,
    index: number,
    count: number
): number {
    const span = metrics.height - metrics.headerHeight - metrics.bottomInset;
    return metrics.headerHeight + ((index + 1) * span) / (count + 1);
}

export function showsPinLabels(node: Pick<EngineSchematicNode, 'kind'>): boolean {
    return node.kind !== 'Port' && node.kind !== 'Constant' && node.kind !== 'Net';
}

export function displayNodeTitle(value: string): string {
    return elideMiddle(value, TitleLabelLimit);
}

/** Keep both the identifying prefix and suffix, as Vivado's long-text elision does. */
export function elideMiddle(value: string, limit: number): string {
    if (value.length <= limit || limit < 2) {
        return value;
    }
    const content = limit - 1;
    const prefix = Math.ceil(content / 2);
    const suffix = Math.floor(content / 2);
    return `${value.slice(0, prefix)}…${value.slice(value.length - suffix)}`;
}

function pinLabels(node: EngineSchematicNode, direction: 'input' | 'output'): string[] {
    const signals = direction === 'input' ? node.inputs : node.outputs;
    return signals.map((_, index) => pinLabel(node, direction, index));
}

function fallbackPinLabel(kind: string, direction: 'input' | 'output', index: number): string {
    if (direction === 'output') {
        if (kind === 'FlipFlop' || kind === 'Latch') {
            return index === 0 ? 'Q' : `Q${index}`;
        }
        return index === 0 ? 'Y' : `O${index}`;
    }
    if (kind === 'FlipFlop') {
        return ['D', 'CLK', 'ARST'][index] ?? `I${index}`;
    }
    if (kind === 'Latch') {
        return ['D', 'G'][index] ?? `I${index}`;
    }
    if (kind === 'Arithmetic') {
        return ['A', 'B'][index] ?? `I${index}`;
    }
    return index < 4 ? String.fromCharCode('A'.charCodeAt(0) + index) : `I${index}`;
}

function measurePinText(value: string): number {
    return value.length * PinCharacterWidth;
}

function measureTitleText(value: string): number {
    return value.length * TitleCharacterWidth;
}

function symbolCenterWidth(kind: string): number {
    switch (kind) {
        case 'Mux': return 44;
        case 'Gate': return 54;
        case 'Inverter':
        case 'Buffer': return 48;
        case 'FlipFlop':
        case 'Latch': return 58;
        case 'Arithmetic': return 52;
        case 'Instance': return 28;
        case 'Memory':
        case 'MemoryRead': return 44;
        default: return 40;
    }
}

function symbolHeaderHeight(kind: string): number {
    switch (kind) {
        case 'Instance': return 48;
        case 'Mux': return 24;
        case 'Arithmetic':
        case 'FlipFlop':
        case 'Latch':
        case 'Memory':
        case 'MemoryRead': return 22;
        case 'Splitter':
        case 'Joiner':
        case 'StructFanOut': return 18;
        default: return 8;
    }
}

function minimumWidth(kind: string): number {
    switch (kind) {
        case 'Port': return 92;
        case 'Constant': return 76;
        case 'Mux': return 100;
        case 'Gate': return 104;
        case 'Inverter':
        case 'Buffer': return 96;
        case 'FlipFlop':
        case 'Latch': return 120;
        case 'Instance': return 176;
        case 'Memory':
        case 'MemoryRead': return 140;
        default: return 120;
    }
}

function minimumPinRegionHeight(kind: string): number {
    switch (kind) {
        case 'Port': return 24;
        case 'Constant': return 22;
        case 'Mux': return 52;
        case 'FlipFlop':
        case 'Latch': return 58;
        case 'Memory':
        case 'MemoryRead': return 54;
        default: return 48;
    }
}
