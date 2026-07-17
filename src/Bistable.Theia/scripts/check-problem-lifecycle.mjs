import assert from 'node:assert/strict';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
const { BistableProblemPublisher } = require(
    '../extensions/bistable-workbench/lib/browser/bistable-problem-publisher'
);

const calls = [];
const publisher = new BistableProblemPublisher({
    setMarkers: (uri, owner, markers) => calls.push({ uri: uri.toString(), owner, markers })
});
publisher.publish([{
    severity: 'Error',
    code: 'SYNTAX',
    message: 'unexpected token',
    filePath: '/workspace/top.sv',
    line: 7,
    column: 4
}]);
assert.equal(calls.length, 1);
assert.equal(calls[0].owner, 'bistable');
assert.equal(calls[0].markers[0].range.start.line, 6);
assert.equal(calls[0].markers[0].range.start.character, 3);

publisher.publish([]);
assert.equal(calls.length, 2);
assert.deepEqual(calls[1].markers, [], 'A successful reload must clear prior Bistable Problems.');
process.stdout.write('Problems error/recovery lifecycle: passed\n');
