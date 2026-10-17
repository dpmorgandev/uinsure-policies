// Fixtures for the self-test's isolation from a hook's git environment (pre-push exports GIT_DIR).
import './clean-git-env.mjs';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, readdirSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { execFileSync } from 'node:child_process';

const HERE = import.meta.dirname;
const CLEAN = resolve(HERE, 'clean-git-env.mjs');

test('git-env: a fixture git call under a hook GIT_DIR stays out of that repository', () => {
  const decoy = resolve(mkdtempSync(join(tmpdir(), 'git-env-decoy-')));
  execFileSync('git', ['init', '-q', '--bare', decoy]);
  const before = readFileSync(join(decoy, 'config'), 'utf8');
  const fixture = resolve(mkdtempSync(join(tmpdir(), 'git-env-fixture-')));
  const script = `await import(${JSON.stringify(new URL(`file:///${CLEAN.replace(/\\/g, '/')}`).href)});
    const { execFileSync } = await import('node:child_process');
    execFileSync('git', ['init', '-q', '-b', 'main'], { cwd: ${JSON.stringify(fixture)} });
    execFileSync('git', ['config', 'user.name', 'Fixture'], { cwd: ${JSON.stringify(fixture)} });`;
  execFileSync(process.execPath, ['--input-type=module', '-e', script], { env: { ...process.env, GIT_DIR: decoy, GIT_WORK_TREE: fixture } });
  assert.equal(readFileSync(join(decoy, 'config'), 'utf8'), before, 'the decoy repository config changed');
  assert.ok(existsSync(join(fixture, '.git')), 'the fixture repository was not created in its own directory');
});

test('git-env: every self-test file imports clean-git-env first', () => {
  for (const f of readdirSync(HERE).filter((n) => n.endsWith('.test.mjs'))) {
    const firstImport = readFileSync(join(HERE, f), 'utf8').split(/\r?\n/).find((l) => l.startsWith('import '));
    assert.equal(firstImport, "import './clean-git-env.mjs';", `${f} must import ./clean-git-env.mjs before anything else`);
  }
});
