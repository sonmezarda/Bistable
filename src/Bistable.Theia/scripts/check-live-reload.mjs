import assert from 'node:assert/strict';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
const { LatestReloadCoordinator } = require(
    '../extensions/bistable-workbench/lib/browser/latest-reload-coordinator'
);

let reloadCount = 0;
let releaseFirstReload;
const firstReloadGate = new Promise(resolve => {
    releaseFirstReload = resolve;
});
const coordinator = new LatestReloadCoordinator(async () => {
    reloadCount++;
    if (reloadCount === 1) {
        await firstReloadGate;
    }
}, 5);

const firstReload = coordinator.requestNow();
coordinator.schedule();
coordinator.schedule();
await new Promise(resolve => setTimeout(resolve, 15));
assert.equal(reloadCount, 1, 'A save burst must not overlap an active elaboration.');

releaseFirstReload();
await firstReload;
await new Promise(resolve => setTimeout(resolve, 0));
assert.equal(reloadCount, 2, 'Saves during elaboration must produce one newest follow-up pass.');

coordinator.schedule();
coordinator.dispose();
await new Promise(resolve => setTimeout(resolve, 10));
assert.equal(reloadCount, 2, 'Disposal must cancel a pending debounce timer.');

process.stdout.write('Latest-save-wins reload coordinator: passed\n');
