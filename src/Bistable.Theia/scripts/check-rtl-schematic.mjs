import assert from 'node:assert/strict';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
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

assert.deepEqual(
    BistableSchematicDockOptions,
    { area: 'main', mode: 'tab-after' },
    'RTL schematic must open beside source files in the main document dock.'
);

const graph = {
    moduleName: 'top',
    nodes: [
        { id: 'a', kind: 'Port', label: 'a', inputs: [], outputs: ['a'] },
        { id: 'mux', kind: 'Mux', label: 'MUX', inputs: ['a'], outputs: ['y'] },
        { id: 'y', kind: 'Port', label: 'y', inputs: ['y'], outputs: [] }
    ],
    edges: [
        { id: 'e0', signal: 'a', sourceNodeId: 'a', targetNodeId: 'mux' },
        { id: 'e1', signal: 'y', sourceNodeId: 'mux', targetNodeId: 'y' }
    ]
};
const layout = await layoutSchematicWithElk(graph);
const byId = new Map(layout.nodes.map(node => [node.id, node]));
assert.ok(byId.get('a').x < byId.get('mux').x);
assert.ok(byId.get('mux').x < byId.get('y').x);
assert.equal(layout.edges.length, 2);
assert.ok(layout.edges.every(edge => edge.points.length >= 2));
assert.deepEqual(layout.edges.map(edge => edge.signal).sort(), ['a', 'y']);

const node = {
    id: 'symbol', label: 'MUX', inputs: ['a', 'b'], outputs: ['y'],
    x: 0, y: 0, width: 92, height: 82,
    pins: [
        { id: 'a', signal: 'a', direction: 'input', x: 0, y: 22 },
        { id: 'b', signal: 'b', direction: 'input', x: 0, y: 58 },
        { id: 'y', signal: 'y', direction: 'output', x: 92, y: 41 }
    ]
};
const cases = [
    ['Port', 'polygon'],
    ['Mux', '<path'],
    ['Gate', 'AND'],
    ['Inverter', '<circle'],
    ['FlipFlop', 'D → Q'],
    ['Instance', 'MODULE']
];
for (const [kind, expected] of cases) {
    const markup = renderToStaticMarkup(renderRtlSymbol({ ...node, kind }));
    assert.ok(markup.includes(expected), `${kind} must render its RTL symbol geometry.`);
}

process.stdout.write('Dockable ELK RTL schematic and symbols: passed\n');
