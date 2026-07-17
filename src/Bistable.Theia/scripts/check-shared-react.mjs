import assert from 'node:assert/strict';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
const applicationReact = require('react');
const theiaReact = require('@theia/core/shared/react');

assert.strictEqual(
    applicationReact,
    theiaReact,
    'Bistable widgets and Theia must resolve the same React runtime instance.'
);

process.stdout.write(`Shared React runtime: ${applicationReact.version}\n`);
