#!/usr/bin/env node
// Syncs the curated ECC subset listed in .claude/ecc-manifest.json into .claude/.
//
// Usage:
//   node scripts/ecc-sync.mjs            # sync from the manifest ref (default: main)
//   node scripts/ecc-sync.mjs --ref v2.3.0
//   node scripts/ecc-sync.mjs --check    # only report whether upstream has a newer commit
//
// Review the result with `git diff .claude` before committing.

import { execFileSync } from 'node:child_process';
import { cpSync, existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

const root = resolve(import.meta.dirname, '..');
const claudeDir = join(root, '.claude');
const manifestPath = join(claudeDir, 'ecc-manifest.json');
const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'));

const args = process.argv.slice(2);
const refArg = args.indexOf('--ref');
const ref = refArg >= 0 ? args[refArg + 1] : manifest.ref;
const checkOnly = args.includes('--check');

const git = (...a) => execFileSync('git', a, { encoding: 'utf8' }).trim();

if (checkOnly) {
  const remote = git('ls-remote', manifest.repository, ref).split(/\s+/)[0]?.slice(0, 7);
  const current = manifest.installed.commit;
  console.log(remote === current
    ? `ECC is up to date (${current}).`
    : `ECC update available: installed ${current ?? 'unknown'} -> upstream ${remote} (${ref}).`);
  process.exit(0);
}

const tmp = mkdtempSync(join(tmpdir(), 'ecc-'));
try {
  console.log(`Cloning ${manifest.repository} (${ref})...`);
  git('clone', '--depth', '1', '--branch', ref, '--quiet', manifest.repository, tmp);
  const commit = git('-C', tmp, 'rev-parse', '--short', 'HEAD');
  const version = readFileSync(join(tmp, 'VERSION'), 'utf8').trim();

  const missing = [];
  const copy = (from, to) => {
    if (!existsSync(from)) return missing.push(from.replace(tmp, 'ECC'));
    rmSync(to, { recursive: true, force: true });
    cpSync(from, to, { recursive: true });
  };

  // Agents: replace only the ones listed in the manifest.
  mkdirSync(join(claudeDir, 'agents'), { recursive: true });
  for (const a of manifest.agents) copy(join(tmp, 'agents', `${a}.md`), join(claudeDir, 'agents', `${a}.md`));

  // Skills: replace only the listed ones (OpenSpec skills are left untouched).
  mkdirSync(join(claudeDir, 'skills'), { recursive: true });
  for (const s of manifest.skills) copy(join(tmp, 'skills', s), join(claudeDir, 'skills', s));

  // Rules: rules/ecc is fully managed by this script.
  const rulesDir = join(claudeDir, 'rules', 'ecc');
  rmSync(rulesDir, { recursive: true, force: true });
  for (const lang of manifest.rules.languages) {
    const src = join(tmp, 'rules', lang);
    if (!existsSync(src)) { missing.push(`ECC/rules/${lang}`); continue; }
    mkdirSync(join(rulesDir, lang), { recursive: true });
    for (const f of readdirSync(src)) {
      if (f.endsWith('.md') && !manifest.rules.exclude.includes(f)) cpSync(join(src, f), join(rulesDir, lang, f));
    }
  }

  cpSync(join(tmp, 'LICENSE'), join(claudeDir, 'ECC-LICENSE'));

  manifest.installed = { version, commit };
  writeFileSync(manifestPath, JSON.stringify(manifest, null, 2) + '\n');

  console.log(`Synced ECC v${version} (${commit}).`);
  if (missing.length) {
    console.warn('\nNot found upstream (renamed or removed?) — review the manifest:');
    for (const m of missing) console.warn(`  - ${m}`);
    process.exitCode = 1;
  }
  console.log('\nReview with: git diff --stat .claude && git diff .claude');
} finally {
  rmSync(tmp, { recursive: true, force: true });
}
