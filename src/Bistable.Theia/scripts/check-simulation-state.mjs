import assert from 'node:assert/strict';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
const React = require('@theia/core/shared/react');
const { renderToStaticMarkup } = require('react-dom/server');
const {
    emptySimulationState,
    applySnapshot,
    applyFrame,
    applyReadResult,
    pinClasses,
    liveValue,
    probePath,
    nodeBodySelectionTarget,
    logicBitValue,
    nextBinaryToggleValue,
    pokeAction
} = require('../extensions/bistable-workbench/lib/browser/simulation-state');
const {
    formatPokeValue,
    parsePokeDraft,
    parseWorkerBitPattern,
    togglePokeBit,
    visiblePokeBits
} = require('../extensions/bistable-workbench/lib/browser/poke-value-editor');
const { PokeValuePopover } = require(
    '../extensions/bistable-workbench/lib/browser/poke-value-popover'
);

// probePath prefixes the top module — hierarchical identity is preserved.
assert.equal(probePath('top', 'y'), 'top.y');

// A snapshot seeds live values and bumps the generation so a stale worker's late
// frame can be detected and dropped by callers.
const snapshot = {
    topModule: 'counter',
    ports: [{ name: 'enable', direction: 'Input', width: 1, isSigned: false }],
    probes: [
        { path: 'counter.count', width: 8, isSigned: false, isRegistered: true, isMemory: false }
    ],
    initialFrame: { time: 0, signals: [{ signal: 'count', value: '0' }] }
};
const seeded = applySnapshot(emptySimulationState(), snapshot);
assert.equal(seeded.generation, 1);
assert.equal(seeded.status, 'ready');
assert.equal(liveValue('count', 'counter.count', seeded), '0');
assert.ok(seeded.probes.has('counter.count'));

// A stepping frame updates values in place without changing the generation.
const stepped = applyFrame(seeded, { time: 1, signals: [{ signal: 'count', value: '1' }] });
assert.equal(stepped.generation, 1);
assert.equal(liveValue('count', 'counter.count', stepped), '1');

// A batched read merges successful outcomes; a per-path error keeps the old value.
const read = applyReadResult(stepped, {
    results: [
        { path: 'counter.enable', value: '1', width: 1, isSigned: false },
        { path: 'counter.count', value: undefined, width: 8, isSigned: false, error: 'boom' }
    ]
});
assert.equal(read.values.get('counter.enable'), '1');
assert.equal(read.values.get('counter.count'), '1', 'errored path must not overwrite the prior value');

// pinClasses maps selected / driven / live state to CSS classes — DOM-free.
const state = {
    ...read,
    selected: { signal: 'enable', path: 'counter.enable', nodeKind: 'Port' },
    driven: new Set(['enable'])
};
const classes = pinClasses('enable', 'counter.enable', state);
assert.ok(classes.includes('bistable-pin-selected'));
assert.ok(classes.includes('bistable-pin-driven'));
assert.ok(classes.includes('bistable-pin-live'));

const plain = pinClasses('terminal', 'counter.terminal', state);
assert.ok(!plain.includes('bistable-pin-selected'));
assert.ok(!plain.includes('bistable-pin-driven'));
assert.ok(!plain.includes('bistable-pin-live'));

// The visible literal box, not merely its small output pin, selects the exact
// driven net. Its hit rectangle must remain aligned with renderConstant().
const constantTarget = nodeBodySelectionTarget({
    id: 'const-1', kind: 'Constant', label: "4'hA", inputs: [], outputs: ['literal_bus'],
    x: 10, y: 20, width: 52, height: 40, pinLabelColumnWidth: 0, headerHeight: 0, pins: []
}, 'counter');
assert.deepEqual(constantTarget, {
    selected: { signal: 'literal_bus', path: 'counter.literal_bus', nodeKind: 'Constant' },
    x: 2,
    y: 7,
    width: 48,
    height: 26
});

const portTarget = nodeBodySelectionTarget({
    id: 'port-1', kind: 'Port', label: 'enable', inputs: [], outputs: ['enable'],
    x: 0, y: 0, width: 64, height: 28, pinLabelColumnWidth: 0, headerHeight: 0, pins: []
}, 'counter');
assert.equal(portTarget.selected.path, 'counter.enable');
assert.deepEqual(
    { x: portTarget.x, y: portTarget.y, width: portTarget.width, height: portTarget.height },
    { x: 0, y: 0, width: 64, height: 28 }
);

assert.equal(nodeBodySelectionTarget({
    id: 'mux-1', kind: 'Mux', label: 'MUX', inputs: ['a', 'b'], outputs: ['y'],
    x: 0, y: 0, width: 100, height: 80, pinLabelColumnWidth: 10, headerHeight: 20, pins: []
}, 'counter'), undefined, 'A multi-signal body must not ambiguously select one of its nets.');

// Poke mutates only an exact top-level input Port. A bus opens the editor;
// outputs and internal pins sharing the same name remain selection-only.
const enableSelection = { signal: 'enable', path: 'counter.enable', nodeKind: 'Port' };
assert.equal(pokeAction(enableSelection,
    { name: 'enable', direction: 'Input', width: 1, isSigned: false }), 'toggle');
assert.equal(pokeAction(enableSelection,
    { name: 'enable', direction: 'Input', width: 8, isSigned: false }), 'edit');
assert.equal(pokeAction(enableSelection,
    { name: 'enable', direction: 'Output', width: 1, isSigned: false }), 'select');
assert.equal(pokeAction(
    { ...enableSelection, nodeKind: 'Gate' },
    { name: 'enable', direction: 'Input', width: 1, isSigned: false }
), 'select');

assert.equal(logicBitValue("1'b0"), '0');
assert.equal(logicBitValue("1'h1"), '1');
assert.equal(nextBinaryToggleValue('0x0'), '1');
assert.equal(nextBinaryToggleValue('1'), '0');
assert.equal(nextBinaryToggleValue("1'bx"), undefined,
    'Scalar Poke must refuse to guess when the current value is X/Z.');

// Multi-bit editor: lossless radix conversion, signed two's complement, and
// exact individual-bit toggles all use BigInt rather than lossy JS numbers.
const deadBeef = parseWorkerBitPattern('0xDEAD_BEEF', 32);
assert.equal(deadBeef, 0xDEADBEEFn);
assert.equal(formatPokeValue(deadBeef, 'hex', 32), 'DEADBEEF');
assert.equal(formatPokeValue(deadBeef, 'binary', 32), '11011110101011011011111011101111');
assert.equal(parsePokeDraft('ff', 'hex', 8).value, 255n);
assert.equal(parsePokeDraft('-1', 'signed', 8).value, 255n);
assert.equal(formatPokeValue(255n, 'signed', 8), '-1');
assert.ok(parsePokeDraft('100', 'hex', 8).error?.includes('does not fit'));
assert.equal(togglePokeBit(0b1000n, 1, 4), 0b1010n);
assert.deepEqual(visiblePokeBits(4), [3, 2, 1, 0]);
assert.deepEqual(visiblePokeBits(128), Array.from({ length: 64 }, (_, index) => 63 - index),
    'Very wide buses must not create an unbounded number of DOM buttons.');

const popoverMarkup = renderToStaticMarkup(React.createElement(PokeValuePopover, {
    editor: {
        id: 1,
        selected: { signal: 'data', path: 'counter.data', nodeKind: 'Port' },
        port: { name: 'data', direction: 'Input', width: 8, isSigned: false },
        x: 10,
        y: 20,
        radix: 'hex',
        draft: 'A5'
    },
    currentValue: '0xA5',
    busy: false,
    onClose() {},
    onRadixChange() {},
    onDraftChange() {},
    onToggleBit() {},
    onApply() {},
    onKeyDown() {}
}));
assert.ok(popoverMarkup.includes('role="dialog"'));
assert.ok(['BIN', 'HEX', 'UDEC', 'SDEC'].every(label => popoverMarkup.includes(`>${label}<`)));
assert.ok(popoverMarkup.includes('Individual bits'));
assert.ok(popoverMarkup.includes('>Apply<') && popoverMarkup.includes('>OK<'));
assert.equal((popoverMarkup.match(/class="bistable-poke-bit /g) ?? []).length, 8,
    'An 8-bit input editor must expose eight independently clickable bit buttons.');

process.stdout.write('Simulation state DTO / CSS mapping: passed\n');
