#!/usr/bin/env node
// Persistent newline-delimited JSON bridge for elkjs.
// Reads one JSON graph per line from stdin, writes one JSON response per line to stdout.
// Response format: {"ok":true,"graph":{...}} or {"ok":false,"error":"message"}
// Keeps the ELK instance alive across requests to avoid re-initialisation overhead.

const ELK = require('elkjs');
const readline = require('node:readline');

const elk = new ELK();

async function main() {
  const rl = readline.createInterface({ input: process.stdin, terminal: false, crlfDelay: Infinity });

  for await (const line of rl) {
    const trimmed = line.trim();
    if (!trimmed) continue;

    let graph;
    try {
      graph = JSON.parse(trimmed);
    } catch (err) {
      process.stdout.write(JSON.stringify({ ok: false, error: `invalid input JSON: ${err.message}` }) + '\n');
      continue;
    }

    try {
      const layouted = await elk.layout(graph);
      process.stdout.write(JSON.stringify({ ok: true, graph: layouted }) + '\n');
    } catch (err) {
      process.stdout.write(JSON.stringify({ ok: false, error: `layout failed: ${err.message}` }) + '\n');
    }
  }
}

main().catch(err => {
  process.stderr.write(`elk-router: ${err.stack || err.message}\n`);
  process.exit(1);
});
