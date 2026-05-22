#!/usr/bin/env node
// Reads an ELK graph spec from stdin (JSON), runs the Eclipse Layout Kernel via elkjs,
// writes the layouted graph back to stdout (JSON). Single-shot, exits when done.

const ELK = require('elkjs');

async function main() {
  let input = '';
  process.stdin.setEncoding('utf8');
  for await (const chunk of process.stdin) {
    input += chunk;
  }

  let graph;
  try {
    graph = JSON.parse(input);
  } catch (err) {
    process.stderr.write(`elk-router: invalid input JSON: ${err.message}\n`);
    process.exit(2);
  }

  const elk = new ELK();
  try {
    const layouted = await elk.layout(graph);
    process.stdout.write(JSON.stringify(layouted));
  } catch (err) {
    process.stderr.write(`elk-router: layout failed: ${err.message}\n`);
    process.exit(3);
  }
}

main().catch(err => {
  process.stderr.write(`elk-router: ${err.stack || err.message}\n`);
  process.exit(1);
});
