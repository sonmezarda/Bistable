import assert from 'node:assert/strict';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
const React = require('@theia/core/shared/react');
const { renderToStaticMarkup } = require('react-dom/server');
const { BistableSchematicDockOptions } = require(
    '../extensions/bistable-workbench/lib/browser/bistable-commands'
);
const { renderRtlSymbol } = require(
    '../extensions/bistable-workbench/lib/browser/rtl-symbol-renderer'
);
const { layoutSchematicWithElk } = require(
    '../extensions/bistable-workbench/lib/node/bistable-schematic-layout-service'
);
const { elideMiddle } = require(
    '../extensions/bistable-workbench/lib/common/schematic-visual-contract'
);
const renderSvg = element => renderToStaticMarkup(React.createElement('svg', null, element));

assert.deepEqual(
    BistableSchematicDockOptions,
    { area: 'main', mode: 'tab-after' },
    'RTL schematic must open beside source files in the main document dock.'
);

const graph = {
    moduleName: 'top',
    nodes: [
        { id: 'sel', kind: 'Port', label: 'sel', inputs: [], outputs: ['__schematic_expr_select_42'] },
        { id: 'data', kind: 'Port', label: 'alu_result', inputs: [], outputs: ['alu_result'] },
        {
            id: 'mux', kind: 'Mux', label: 'MUX',
            inputs: ['__schematic_expr_select_42', 'alu_result'], outputs: ['branch_taken'],
            inputLabels: ['S', 'I0'], outputLabels: ['Y']
        },
        { id: 'y', kind: 'Port', label: 'branch_taken', inputs: ['branch_taken'], outputs: [] },
        {
            id: 'instance', kind: 'Instance', label: 'u_very_long_decoder_instance',
            typeLabel: 'riscv_single_cycle_instruction_decoder',
            inputs: ['instruction'], outputs: ['control_word'],
            inputLabels: ['very_long_instruction_port_name'],
            outputLabels: ['very_long_control_word_port_name']
        }
    ],
    edges: [
        { id: 'e0', signal: '__schematic_expr_select_42', sourceNodeId: 'sel', targetNodeId: 'mux' },
        { id: 'e1', signal: 'alu_result', sourceNodeId: 'data', targetNodeId: 'mux' },
        { id: 'e2', signal: 'branch_taken', sourceNodeId: 'mux', targetNodeId: 'y' }
    ]
};
const layout = await layoutSchematicWithElk(graph);
const byId = new Map(layout.nodes.map(node => [node.id, node]));
assert.ok(byId.get('sel').x < byId.get('mux').x);
assert.ok(byId.get('mux').x < byId.get('y').x);
assert.equal(layout.edges.length, 3);
assert.ok(layout.edges.every(edge => edge.points.length >= 2));
assert.deepEqual(layout.edges.map(edge => edge.signal).sort(),
    ['__schematic_expr_select_42', 'alu_result', 'branch_taken']);

const mux = byId.get('mux');
assert.deepEqual(mux.pins.map(pin => pin.label), ['S', 'I0', 'Y']);
assert.deepEqual(mux.pins.map(pin => pin.signal),
    ['__schematic_expr_select_42', 'alu_result', 'branch_taken']);
const leftLabelEnd = 14 + mux.pinLabelColumnWidth;
const rightLabelStart = mux.width - 14 - mux.pinLabelColumnWidth;
assert.ok(rightLabelStart - leftLabelEnd >= 28,
    'Input and output label columns must have a protected center gutter.');
assert.equal(
    mux.pins.find(pin => pin.direction === 'output').y,
    mux.headerHeight + (mux.height - mux.headerHeight - 8) / 2,
    'A lone output must remain vertically centered in the pin body against multiple inputs.');

const instance = byId.get('instance');
assert.ok(instance.width >= 176 && instance.width <= 240,
    'Long module/port names must use a bounded content-aware width.');
assert.ok(instance.pins.every(pin => pin.displayLabel.length <= 13));
assert.ok(instance.pins.every(pin => pin.displayLabel.includes('…')));
assert.equal(elideMiddle('very_long_generated_signal_name', 13).length, 13);

const node = {
    id: 'symbol', label: 'MUX', inputs: ['a', 'b'], outputs: ['y'],
    inputLabels: ['A', 'B'], outputLabels: ['Y'],
    x: 0, y: 0, width: 104, height: 82, pinLabelColumnWidth: 8, headerHeight: 24,
    pins: [
        { id: 'a', signal: 'a', label: 'A', displayLabel: 'A', direction: 'input', x: 0, y: 22 },
        { id: 'b', signal: 'b', label: 'B', displayLabel: 'B', direction: 'input', x: 0, y: 58 },
        { id: 'y', signal: 'y', label: 'Y', displayLabel: 'Y', direction: 'output', x: 104, y: 41 }
    ]
};
const cases = [
    ['Port', 'polygon'],
    ['Mux', '<path'],
    ['Gate', 'AND'],
    ['Inverter', '<circle'],
    ['FlipFlop', 'D → Q'],
    ['Instance', 'bistable-rtl-instance-body']
];
for (const [kind, expected] of cases) {
    const markup = renderSvg(renderRtlSymbol({ ...node, kind }));
    assert.ok(markup.includes(expected), `${kind} must render its RTL symbol geometry.`);
}

const muxMarkup = renderSvg(renderRtlSymbol(mux));
assert.ok(muxMarkup.includes('>S<') && muxMarkup.includes('>I0<') && muxMarkup.includes('>Y<'));
assert.ok(!muxMarkup.includes('>__schematic_expr_select_42<'),
    'Generated net identities must never become visible pin labels.');
assert.ok(muxMarkup.includes('__schematic_expr_select_42'),
    'The exact signal identity must remain available in the SVG tooltip.');
assert.ok(muxMarkup.includes('clip-path'), 'Pin columns need an SVG clipping safety net.');

const instanceMarkup = renderSvg(renderRtlSymbol(instance));
assert.ok(instanceMarkup.includes('u_very_long_…der_instance'));
assert.ok(instanceMarkup.includes('riscv_single…tion_decoder'));

process.stdout.write('Dockable Vivado-style ELK RTL schematic and symbols: passed\n');
