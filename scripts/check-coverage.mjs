#!/usr/bin/env node
// Merges Cobertura reports (a line is covered if any report hit it) and fails below a threshold.
// Usage: node scripts/check-coverage.mjs <directory> [minimumPercent=80]
// Exit codes: 0 pass, 1 below threshold, 2 usage or unreadable reports.
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';

const [directory, minimumArgument = '80'] = process.argv.slice(2);
if (!directory) {
  console.error('Usage: node scripts/check-coverage.mjs <directory> [minimumPercent]');
  process.exit(2);
}
const minimum = Number(minimumArgument);
if (!Number.isFinite(minimum) || minimum < 0 || minimum > 100) {
  console.error(`Invalid minimum percentage: ${minimumArgument}`);
  process.exit(2);
}

function* coberturaFiles(dir) {
  for (const entry of readdirSync(dir)) {
    const path = join(dir, entry);
    if (statSync(path).isDirectory()) yield* coberturaFiles(path);
    else if (/cobertura.*\.xml$/i.test(entry)) yield path;
  }
}

const attribute = (tag, name) => new RegExp(`\\b${name}="([^"]*)"`).exec(tag)?.[1];

const files = [...coberturaFiles(directory)];
if (files.length === 0) {
  console.error(`No Cobertura reports found under ${directory}`);
  process.exit(2);
}

const hitsByLine = new Map();
for (const file of files) {
  const xml = readFileSync(file, 'utf8');
  if (!/<coverage\b/.test(xml)) {
    console.error(`${file} is not a Cobertura report`);
    process.exit(2);
  }
  for (const [, classTag, body] of xml.matchAll(/(<class\b[^>]*>)([\s\S]*?)<\/class>/g)) {
    const filename = attribute(classTag, 'filename');
    for (const [lineTag] of body.matchAll(/<line\b[^>]*>/g)) {
      const number = attribute(lineTag, 'number');
      const hits = attribute(lineTag, 'hits');
      if (filename === undefined || number === undefined || hits === undefined) {
        console.error(`Malformed <class>/<line> entry in ${file}`);
        process.exit(2);
      }
      const key = `${filename}:${number}`;
      hitsByLine.set(key, (hitsByLine.get(key) ?? 0) + Number(hits));
    }
  }
}

const total = hitsByLine.size;
if (total === 0) {
  console.error('The reports contain no coverable lines.');
  process.exit(2);
}
const covered = [...hitsByLine.values()].filter((hits) => hits > 0).length;
const percent = (covered / total) * 100;
console.log(`Line coverage: ${percent.toFixed(2)}% (${covered}/${total} lines, ${files.length} reports)`);
if (percent < minimum) {
  console.error(`Coverage ${percent.toFixed(2)}% is below the ${minimum}% minimum.`);
  process.exit(1);
}
