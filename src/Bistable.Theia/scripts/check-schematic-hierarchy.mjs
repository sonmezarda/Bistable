import assert from 'node:assert/strict';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
const {
    SchematicDocumentFactoryId,
    breadcrumbSegments,
    childInstancePath,
    collapseInstance,
    containerRelativePath,
    expandInstance,
    expansionKey,
    instanceRelativePath,
    parentInstancePath,
    schematicDocumentOptions,
    schematicWidgetId
} = require('../extensions/bistable-workbench/lib/browser/schematic-hierarchy');
const {
    liveValue,
    mergeVisiblePaths,
    nodeBodySelectionTarget,
    pinClasses,
    pokeAction,
    probePath,
    topLevelDrivePort
} = require('../extensions/bistable-workbench/lib/browser/simulation-state');
const { layoutSchematicWithElk } = require(
    '../extensions/bistable-workbench/lib/node/bistable-schematic-layout-service'
);

// ── Document identity: hierarchical instance path, never module type ─────

assert.equal(schematicWidgetId(), SchematicDocumentFactoryId,
    'The root top-module document keeps the historical widget id.');
assert.equal(schematicWidgetId('top.u_alu'), `${SchematicDocumentFactoryId}:top.u_alu`);
assert.notEqual(schematicWidgetId('top.u_alu'), schematicWidgetId('top.u_core.u_alu'),
    'Two instances of the same module type must map to distinct documents.');

// Navigating to a one-segment path (the top module itself) re-activates the
// root document instead of opening a duplicate keyed by module name.
assert.deepEqual(schematicDocumentOptions('top'), {});
assert.deepEqual(schematicDocumentOptions('top.u_alu'), { instancePath: 'top.u_alu' });

// ── Breadcrumb + parent navigation ───────────────────────────────────────

assert.deepEqual(breadcrumbSegments('top.u_core.u_alu'), [
    { label: 'top', instancePath: 'top' },
    { label: 'u_core', instancePath: 'top.u_core' },
    { label: 'u_alu', instancePath: 'top.u_core.u_alu' }
]);
assert.deepEqual(breadcrumbSegments(''), []);
assert.equal(parentInstancePath('top.u_core.u_alu'), 'top.u_core');
assert.equal(parentInstancePath('top'), undefined, 'The root has no parent.');
assert.equal(childInstancePath('top.u_core', 'u_alu'), 'top.u_core.u_alu');

// ── Child probe identity: document path prefixes the module-local signal ─

assert.equal(probePath('top.u_alu', 'result'), 'top.u_alu.result');

// ── Poke safety (mandatory regression) ───────────────────────────────────
// A child module's boundary port is NOT a top-level input. Even when its
// module-local name matches a real top-level input port, a hierarchical
// document must never resolve a drive port — simulation.setInput stays
// unreachable outside the root document.

const topPorts = [
    { name: 'enable', direction: 'Input', width: 1, isSigned: false },
    { name: 'prog_wdata', direction: 'Input', width: 32, isSigned: false }
];
const childBoundarySelection = { signal: 'enable', path: 'top.u_alu.enable', nodeKind: 'Port' };

assert.equal(topLevelDrivePort(topPorts, childBoundarySelection, false), undefined,
    'A hierarchical document must not resolve a same-named top-level port.');
assert.equal(pokeAction(childBoundarySelection,
    topLevelDrivePort(topPorts, childBoundarySelection, false)), 'select',
    'Poke on a child boundary port must stay selection-only.');

const rootSelection = { signal: 'enable', path: 'top.enable', nodeKind: 'Port' };
assert.equal(topLevelDrivePort(topPorts, rootSelection, true), topPorts[0],
    'The root document still resolves exact top-level inputs.');
assert.equal(pokeAction(rootSelection, topLevelDrivePort(topPorts, rootSelection, true)), 'toggle');
assert.equal(topLevelDrivePort(undefined, rootSelection, true), undefined);

// ── Child documents never borrow top-level values or driven state ────────

const state = {
    generation: 1,
    topModule: 'top',
    probes: new Map(),
    values: new Map([
        ['enable', '1'],                 // bare top-level name from a frame
        ['top.enable', '1'],
        ['top.u_alu.result', '0x2A']
    ]),
    driven: new Set(['enable']),
    status: 'ready'
};

assert.equal(liveValue('enable', 'top.enable', state), '1');
assert.equal(liveValue('enable', 'top.u_alu.enable', state, false), undefined,
    'A child net must not borrow the value of a same-named top-level signal.');
assert.equal(liveValue('result', 'top.u_alu.result', state, false), '0x2A',
    'Exact hierarchical paths still resolve in child documents.');

const childClasses = pinClasses('enable', 'top.u_alu.enable', state, false);
assert.ok(!childClasses.includes('bistable-pin-driven'),
    'The driven set holds top-level names; a same-named child pin must not light up.');
assert.ok(!childClasses.includes('bistable-pin-live'),
    'Liveness in a child document requires the exact hierarchical path.');
const rootClasses = pinClasses('enable', 'top.enable', state);
assert.ok(rootClasses.includes('bistable-pin-driven') && rootClasses.includes('bistable-pin-live'));

// ── One batched read across every open document ──────────────────────────

const union = mergeVisiblePaths(new Map([
    ['bistable.schematic.document', ['top.enable', 'top.result']],
    ['bistable.schematic.document:top.u_alu', ['top.u_alu.result', 'top.enable']]
]));
assert.deepEqual([...union].sort(), ['top.enable', 'top.result', 'top.u_alu.result'],
    'Parent and child visible sets must merge into one deduplicated batch.');

// ── Selective inline expansion: relative paths and toggle semantics ──────

assert.equal(containerRelativePath('container:u_core'), 'u_core');
assert.equal(containerRelativePath('u_core/container:u_alu'), 'u_core.u_alu');
assert.equal(instanceRelativePath(undefined, 'u_alu'), 'u_alu');
assert.equal(instanceRelativePath('container:u_core', 'u_alu'), 'u_core.u_alu');

let expanded = expandInstance(new Set(), 'u_core');
expanded = expandInstance(expanded, 'u_core.u_alu');
expanded = expandInstance(expanded, 'u_dmem');
assert.deepEqual([...expanded].sort(), ['u_core', 'u_core.u_alu', 'u_dmem']);
assert.equal(expansionKey(expanded), 'u_core|u_core.u_alu|u_dmem');
const collapsed = collapseInstance(expanded, 'u_core');
assert.deepEqual([...collapsed], ['u_dmem'],
    'Collapsing an instance must prune every expansion nested inside it.');
assert.deepEqual([...collapseInstance(expanded, 'u_core.u_alu')].sort(), ['u_core', 'u_dmem']);

// A pass-through boundary port selects its inner namespaced net — never the
// parent net it connects to, and never a top-level drive port.
const passThroughOutput = nodeBodySelectionTarget({
    id: 'u_alu/port-y', kind: 'Port', label: 'y', typeLabel: 'output',
    inputs: ['u_alu.y'], outputs: ['result'], containerId: 'container:u_alu',
    x: 0, y: 0, width: 64, height: 28, pinLabelColumnWidth: 0, headerHeight: 0, pins: []
}, 'top');
assert.equal(passThroughOutput.selected.path, 'top.u_alu.y');
const passThroughInput = nodeBodySelectionTarget({
    id: 'u_alu/port-a', kind: 'Port', label: 'a', typeLabel: 'input',
    inputs: ['enable'], outputs: ['u_alu.a'], containerId: 'container:u_alu',
    x: 0, y: 0, width: 64, height: 28, pinLabelColumnWidth: 0, headerHeight: 0, pins: []
}, 'top');
assert.equal(passThroughInput.selected.path, 'top.u_alu.a');
assert.equal(topLevelDrivePort(topPorts, passThroughInput.selected, true), undefined,
    'A namespaced boundary net must never resolve a top-level drive port, even on the root document.');

// ── Nested container layout: absolute coordinates, edges routed across ───

const containerGraph = {
    moduleName: 'top',
    nodes: [
        { id: 'x', kind: 'Port', label: 'x', inputs: [], outputs: ['x'] },
        { id: 'container:u_alu', kind: 'Container', label: 'u_alu', typeLabel: 'alu', inputs: [], outputs: [] },
        {
            id: 'u_alu/port-a', kind: 'Port', label: 'a', typeLabel: 'input',
            inputs: ['x'], outputs: ['u_alu.a'], containerId: 'container:u_alu'
        },
        {
            id: 'u_alu/inv', kind: 'Inverter', label: 'NOT',
            inputs: ['u_alu.a'], outputs: ['u_alu.y'], containerId: 'container:u_alu'
        },
        {
            id: 'u_alu/port-y', kind: 'Port', label: 'y', typeLabel: 'output',
            inputs: ['u_alu.y'], outputs: ['z'], containerId: 'container:u_alu'
        },
        { id: 'z', kind: 'Port', label: 'z', inputs: ['z'], outputs: [] }
    ],
    edges: [
        { id: 'e0', signal: 'x', sourceNodeId: 'x', targetNodeId: 'u_alu/port-a' },
        { id: 'e1', signal: 'u_alu.a', sourceNodeId: 'u_alu/port-a', targetNodeId: 'u_alu/inv' },
        { id: 'e2', signal: 'u_alu.y', sourceNodeId: 'u_alu/inv', targetNodeId: 'u_alu/port-y' },
        { id: 'e3', signal: 'z', sourceNodeId: 'u_alu/port-y', targetNodeId: 'z' }
    ]
};
const containerLayout = await layoutSchematicWithElk(containerGraph);
const byId = new Map(containerLayout.nodes.map(node => [node.id, node]));
const container = byId.get('container:u_alu');
assert.ok(container.width > 0 && container.height > 0, 'ELK must size the container around its children.');
for (const childId of ['u_alu/port-a', 'u_alu/inv', 'u_alu/port-y']) {
    const child = byId.get(childId);
    assert.ok(
        child.x >= container.x && child.x + child.width <= container.x + container.width
        && child.y >= container.y && child.y + child.height <= container.y + container.height,
        `Flattened child '${childId}' must sit inside its container in absolute coordinates.`);
}
assert.ok(byId.get('x').x + byId.get('x').width <= container.x + 1,
    'Root nodes must stay outside the container.');
assert.equal(containerLayout.edges.length, 4);
assert.ok(containerLayout.edges.every(edge => edge.points.length >= 2));
// The internal edge's endpoints must land inside the container after the
// container-relative → absolute shift.
const internal = containerLayout.edges.find(edge => edge.id === 'e1');
for (const point of internal.points) {
    assert.ok(point.x >= container.x - 1 && point.x <= container.x + container.width + 1,
        'Container-relative edge coordinates must be shifted to absolute space.');
}
// Container list order paints parents before children (background first).
assert.ok(
    containerLayout.nodes.findIndex(node => node.id === 'container:u_alu')
    < containerLayout.nodes.findIndex(node => node.id === 'u_alu/inv'));

process.stdout.write('Hierarchical schematic navigation and poke safety: passed\n');
