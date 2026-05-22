#!/usr/bin/env node
// Reads an ELK graph from stdin, runs layout, renders the result as SVG to stdout.
// Used to visually evaluate ELK output without round-tripping through the C# app.

const ELK = require('elkjs');

function escape(str) {
  return String(str).replace(/[<>&"]/g, c => ({ '<': '&lt;', '>': '&gt;', '&': '&amp;', '"': '&quot;' }[c]));
}

function renderNode(node, parentX, parentY, lines) {
  const ax = parentX + (node.x || 0);
  const ay = parentY + (node.y || 0);
  lines.push(`<g>`);
  lines.push(`  <rect x="${ax}" y="${ay}" width="${node.width}" height="${node.height}" fill="#1f2734" stroke="#5b8fb0" stroke-width="1.2" rx="6"/>`);
  lines.push(`  <text x="${ax + 8}" y="${ay + 14}" fill="#dce3ec" font-family="monospace" font-size="11">${escape(node.id)}</text>`);
  for (const port of node.ports || []) {
    const px = ax + (port.x || 0);
    const py = ay + (port.y || 0);
    lines.push(`  <circle cx="${px}" cy="${py}" r="2.5" fill="#7fb3d5"/>`);
    if (port.labels) {
      for (const label of port.labels) {
        lines.push(`  <text x="${px + 4}" y="${py + 3}" fill="#7fb3d5" font-family="monospace" font-size="9">${escape(label.text || '')}</text>`);
      }
    }
  }
  for (const child of node.children || []) {
    renderNode(child, ax, ay, lines);
  }
  lines.push(`</g>`);
}

function renderEdge(edge, color, width, lines) {
  for (const section of edge.sections || []) {
    const pts = [section.startPoint, ...(section.bendPoints || []), section.endPoint];
    const path = pts.map((p, i) => `${i === 0 ? 'M' : 'L'} ${p.x} ${p.y}`).join(' ');
    lines.push(`<path d="${path}" stroke="${color}" stroke-width="${width}" fill="none" stroke-linecap="square" stroke-linejoin="miter"/>`);
  }
  if (edge.junctionPoints) {
    for (const j of edge.junctionPoints) {
      lines.push(`<circle cx="${j.x}" cy="${j.y}" r="3" fill="${color}"/>`);
    }
  }
}

function renderSvg(graph) {
  const lines = [];
  const w = graph.width || 1200;
  const h = graph.height || 800;
  lines.push(`<?xml version="1.0" encoding="UTF-8"?>`);
  lines.push(`<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}" style="background:#0e1623">`);

  for (const child of graph.children || []) {
    renderNode(child, 0, 0, lines);
  }

  const palette = ['#7fb3d5', '#f6c177', '#a3be8c', '#bf80c4', '#ec9b9b', '#85d5b3'];
  let i = 0;
  for (const edge of graph.edges || []) {
    const color = palette[i % palette.length]; i++;
    const isBus = edge.bus === true;
    renderEdge(edge, color, isBus ? 2.6 : 1.4, lines);
  }

  lines.push(`</svg>`);
  return lines.join('\n');
}

async function main() {
  let input = '';
  process.stdin.setEncoding('utf8');
  for await (const chunk of process.stdin) input += chunk;
  const graph = JSON.parse(input);
  const elk = new ELK();
  const layouted = await elk.layout(graph);
  process.stdout.write(renderSvg(layouted));
}

main().catch(err => { process.stderr.write(err.stack + '\n'); process.exit(1); });
