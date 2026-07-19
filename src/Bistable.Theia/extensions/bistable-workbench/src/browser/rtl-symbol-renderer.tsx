import * as React from '@theia/core/shared/react';
import { EngineSchematicLayoutNode } from '../common/bistable-engine-protocol';
import {
    displayNodeTitle,
    elideMiddle,
    showsPinLabels
} from '../common/schematic-visual-contract';

export function renderRtlSymbol(node: EngineSchematicLayoutNode): React.ReactElement {
    const inputClipId = clipId(node.id, 'input');
    const outputClipId = clipId(node.id, 'output');
    return <g
        key={node.id}
        className={`bistable-rtl-node bistable-rtl-node-${node.kind.toLowerCase()}`}
        transform={`translate(${node.x}, ${node.y})`}
    >
        <title>{`${node.label}${node.typeLabel ? ` : ${node.typeLabel}` : ''} · ${node.kind}`}</title>
        {showsPinLabels(node) && <defs>
            <clipPath id={inputClipId}>
                <rect x='10' y={node.headerHeight} width={node.pinLabelColumnWidth + 4} height={node.height - node.headerHeight} />
            </clipPath>
            <clipPath id={outputClipId}>
                <rect
                    x={node.width - node.pinLabelColumnWidth - 14}
                    y={node.headerHeight}
                    width={node.pinLabelColumnWidth + 4}
                    height={node.height - node.headerHeight}
                />
            </clipPath>
        </defs>}
        {renderBody(node)}
        {node.pins.map(pin => <g key={pin.id} className={`bistable-rtl-pin bistable-rtl-pin-${pin.direction}`}>
            {/* Short stub bridges the gap between the edge and inset symbol shapes. */}
            <line
                className='bistable-rtl-pin-stub'
                x1={pin.x}
                y1={pin.y}
                x2={pin.direction === 'input' ? pin.x + 9 : pin.x - 9}
                y2={pin.y}
            />
            <circle cx={pin.x} cy={pin.y} r='2.4' />
            {showsPinLabels(node) && <text
                x={pin.direction === 'input' ? pin.x + 14 : pin.x - 14}
                y={pin.y + 3}
                textAnchor={pin.direction === 'input' ? 'start' : 'end'}
                clipPath={`url(#${pin.direction === 'input' ? inputClipId : outputClipId})`}
            >{pin.displayLabel}<title>{`${pin.label} · ${pin.signal}`}</title></text>}
        </g>)}
    </g>;
}

function renderBody(node: EngineSchematicLayoutNode): React.ReactElement {
    switch (node.kind) {
        case 'Port': return renderPort(node);
        case 'Mux': return renderMux(node);
        case 'Gate': return renderGate(node);
        case 'Inverter': return renderTriangle(node, true);
        case 'Buffer': return renderTriangle(node, false);
        case 'Arithmetic': return renderArithmetic(node);
        case 'FlipFlop': return renderSequential(node, 'DFF');
        case 'Latch': return renderSequential(node, 'LATCH');
        case 'Instance': return renderInstance(node);
        case 'Container': return renderContainer(node);
        case 'Constant': return renderConstant(node);
        case 'Splitter':
        case 'Joiner':
        case 'StructFanOut': return renderWedge(node);
        case 'Memory':
        case 'MemoryRead': return renderMemory(node);
        default: return renderBlock(node);
    }
}

function renderPort(node: EngineSchematicLayoutNode): React.ReactElement {
    const { width: w, height: h } = node;
    // A pass-through boundary port of an expanded instance has both sides
    // connected, so its direction comes from the composer's typeLabel hint.
    const isInput = node.typeLabel ? node.typeLabel === 'input' : node.outputs.length > 0;
    const points = isInput
        ? `0,0 ${w - 14},0 ${w},${h / 2} ${w - 14},${h} 0,${h}`
        : `14,0 ${w},0 ${w},${h} 14,${h} 0,${h / 2}`;
    return <g>
        <polygon className='bistable-rtl-body' points={points} />
        <text className='bistable-rtl-label' x={w / 2} y={h / 2 + 4} textAnchor='middle'>
            {elideMiddle(node.label, 15)}
            <title>{node.label}</title>
        </text>
    </g>;
}

function renderMux(node: EngineSchematicLayoutNode): React.ReactElement {
    // Left wall flush at x=0 so input pins sit on the body; the trapezoid slant
    // is vertical-only. The "MUX" mark is a small top caption, not a big centred
    // operator, so it never covers the pin labels.
    const { width: w, height: h } = node;
    return <g>
        <path className='bistable-rtl-body' d={`M 0 0 L ${w} 12 L ${w} ${h - 12} L 0 ${h} Z`} />
        <text className='bistable-rtl-mux-mark' x={w / 2} y='15' textAnchor='middle'>MUX</text>
    </g>;
}

function renderGate(node: EngineSchematicLayoutNode): React.ReactElement {
    const { width: w, height: h } = node;
    const kind = node.label.toLowerCase();
    if (kind.includes('or') || kind.includes('xor')) {
        return <g>
            {kind.includes('xor') && <path className='bistable-rtl-detail' d={`M 2 0 Q ${w * 0.28} ${h / 2} 2 ${h}`} />}
            <path
                className='bistable-rtl-body'
                d={`M 8 0 Q ${w * 0.46} ${h * 0.06} ${w} ${h / 2} Q ${w * 0.46} ${h * 0.94} 8 ${h} Q ${w * 0.3} ${h / 2} 8 0 Z`}
            />
            <text className='bistable-rtl-operator' x={w * 0.52} y={h / 2 + 4} textAnchor='middle'>
                {kind.includes('xor') ? '≥1' : 'OR'}
            </text>
        </g>;
    }
    return <g>
        <path
            className='bistable-rtl-body'
            d={`M 0 0 H ${w * 0.5} C ${w * 0.84} 0 ${w} ${h * 0.2} ${w} ${h / 2} C ${w} ${h * 0.8} ${w * 0.84} ${h} ${w * 0.5} ${h} H 0 Z`}
        />
        <text className='bistable-rtl-operator' x={w * 0.5} y={h / 2 + 4} textAnchor='middle'>AND</text>
    </g>;
}

function renderTriangle(node: EngineSchematicLayoutNode, inverted: boolean): React.ReactElement {
    const { width: w, height: h } = node;
    return <g>
        <path className='bistable-rtl-body' d={`M 8 4 L ${w - 13} ${h / 2} L 8 ${h - 4} Z`} />
        {inverted && <circle className='bistable-rtl-body' cx={w - 7} cy={h / 2} r='6' />}
    </g>;
}

function renderArithmetic(node: EngineSchematicLayoutNode): React.ReactElement {
    return <g>
        <rect className='bistable-rtl-body' width={node.width} height={node.height} rx='8' />
        <text className='bistable-rtl-operator' x={node.width / 2} y={node.height / 2 + 9} textAnchor='middle'>
            {arithmeticOperator(node.label)}
        </text>
        <text className='bistable-rtl-caption' x={node.width / 2} y='14' textAnchor='middle'>{node.label}</text>
    </g>;
}

function renderSequential(node: EngineSchematicLayoutNode, caption: string): React.ReactElement {
    const { width: w, height: h } = node;
    return <g>
        <rect className='bistable-rtl-body' width={w} height={h} rx='2' />
        <path className='bistable-rtl-detail' d={`M 0 ${h * 0.72 - 6} L 8 ${h * 0.72} L 0 ${h * 0.72 + 6}`} />
        <text className='bistable-rtl-caption' x={w / 2} y='16' textAnchor='middle'>{caption}</text>
        <text className='bistable-rtl-operator' x={w / 2} y={h / 2 + 9} textAnchor='middle'>D → Q</text>
    </g>;
}

function renderInstance(node: EngineSchematicLayoutNode): React.ReactElement {
    const separatorY = node.headerHeight - 4;
    return <g>
        <rect className='bistable-rtl-body bistable-rtl-instance-body' width={node.width} height={node.height} rx='3' />
        <text className='bistable-rtl-caption bistable-rtl-instance-name' x={node.width / 2} y='18' textAnchor='middle'>
            {displayNodeTitle(node.label)}<title>{node.label}</title>
        </text>
        {node.typeLabel && <text className='bistable-rtl-instance-type' x={node.width / 2} y='33' textAnchor='middle'>
            {displayNodeTitle(node.typeLabel)}<title>{node.typeLabel}</title>
        </text>}
        <line className='bistable-rtl-detail' x1='0' y1={separatorY} x2={node.width} y2={separatorY} />
    </g>;
}

function renderContainer(node: EngineSchematicLayoutNode): React.ReactElement {
    // An expanded instance: a transparent dashed region so the internal nets
    // stay visible, with the same two-line instance/type header as a symbol.
    const separatorY = node.headerHeight - 4;
    return <g>
        <rect className='bistable-rtl-container-body' width={node.width} height={node.height} rx='6' />
        <text className='bistable-rtl-caption bistable-rtl-instance-name' x={node.width / 2} y='18' textAnchor='middle'>
            {displayNodeTitle(node.label)}<title>{node.label}</title>
        </text>
        {node.typeLabel && <text className='bistable-rtl-instance-type' x={node.width / 2} y='33' textAnchor='middle'>
            {displayNodeTitle(node.typeLabel)}<title>{node.typeLabel}</title>
        </text>}
        <line className='bistable-rtl-detail' x1='0' y1={separatorY} x2={node.width} y2={separatorY} />
    </g>;
}

function renderConstant(node: EngineSchematicLayoutNode): React.ReactElement {
    // A constant is a literal value box rather than a ground symbol — the value
    // is the whole point, so show it plainly.
    const { width: w, height: h } = node;
    const boxHeight = Math.min(h, 26);
    const y = (h - boxHeight) / 2;
    return <g>
        <rect
            className='bistable-rtl-body bistable-rtl-constant-body'
            x='2'
            y={y}
            width={w - 4}
            height={boxHeight}
            rx='4'
        />
        <text
            className='bistable-rtl-constant-value'
            x={w / 2}
            y={h / 2 + 4}
            textAnchor='middle'
        >{shorten(node.label, 12)}<title>{node.label}</title></text>
    </g>;
}

function renderWedge(node: EngineSchematicLayoutNode): React.ReactElement {
    const { width: w, height: h } = node;
    return <g>
        <path className='bistable-rtl-body' d={`M 8 0 L ${w - 8} ${h * 0.2} L ${w - 8} ${h * 0.8} L 8 ${h} Z`} />
        <text className='bistable-rtl-caption' x={w / 2} y={h / 2 + 4} textAnchor='middle'>{node.kind}</text>
    </g>;
}

function renderMemory(node: EngineSchematicLayoutNode): React.ReactElement {
    return <g>
        <rect className='bistable-rtl-body' x='5' y='0' width={node.width - 5} height={node.height} rx='2' />
        <line className='bistable-rtl-detail' x1='0' y1='7' x2='0' y2={node.height - 7} />
        <line className='bistable-rtl-detail' x1='0' y1='7' x2='5' y2='7' />
        <line className='bistable-rtl-detail' x1='0' y1={node.height - 7} x2='5' y2={node.height - 7} />
        <text className='bistable-rtl-caption' x={node.width / 2} y='16' textAnchor='middle'>MEMORY</text>
        <text className='bistable-rtl-label' x={node.width / 2} y={node.height / 2 + 7} textAnchor='middle'>{shorten(node.label, 15)}</text>
    </g>;
}

function renderBlock(node: EngineSchematicLayoutNode): React.ReactElement {
    return <g>
        <rect className='bistable-rtl-body' width={node.width} height={node.height} rx='5' />
        <text className='bistable-rtl-caption' x={node.width / 2} y='16' textAnchor='middle'>{node.kind}</text>
        <text className='bistable-rtl-label' x={node.width / 2} y={node.height / 2 + 8} textAnchor='middle'>{shorten(node.label, 15)}</text>
    </g>;
}

function arithmeticOperator(kind: string): string {
    const operators: Record<string, string> = {
        Add: '+', Sub: '−', Mul: '×', Div: '÷', Mod: '%',
        ShiftLeft: '≪', ShiftRight: '≫', ShiftRightArithmetic: '≫A',
        Equal: '=', NotEqual: '≠', LessThan: '<', GreaterThan: '>',
        LessOrEqual: '≤', GreaterOrEqual: '≥'
    };
    return operators[kind] ?? kind;
}

function shorten(value: string, length: number): string {
    return value.length > length ? `${value.slice(0, length - 1)}…` : value;
}

function clipId(nodeId: string, direction: 'input' | 'output'): string {
    let hash = 2166136261;
    for (let index = 0; index < nodeId.length; index++) {
        hash ^= nodeId.charCodeAt(index);
        hash = Math.imul(hash, 16777619);
    }
    return `bistable-pin-clip-${direction}-${(hash >>> 0).toString(36)}`;
}
