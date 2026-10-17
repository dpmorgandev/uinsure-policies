// Fixtures for the trace hook and render script (traceability.spec Contract). Run: node --test .claude/hooks/__tests__
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { execFileSync, spawnSync } from 'node:child_process';
import { relPath, redactCommand, currentRound, appendJsonl } from '../lib/trace.mjs';
import { agentOf, buildRecord } from '../trace-activity.mjs';
import { validateEvent, gather, completeness } from '../../scripts/render-trace.mjs';

const SCRIPT = resolve(import.meta.dirname, '../../scripts/render-trace.mjs');

const DAG = `plan_id: build
kind: build
title: Fixture
status: approved
nodes:
  - id: F0a
    title: Harness
    kind: harness
    owner: david
    rules: []
    status: in_progress
  - id: F1
    title: Sell
    kind: feature
    owner: loop
    rules: [SELL-2]
    status: in_progress
`;
const REQ = '| ID | Rule | Interpretation |\n|---|---|---|\n| SELL-2 | Sixty days | window |\n| SELL-4 | Holders | 1 to 3 |\n';

function sh(cwd, ...args) {
  return execFileSync('git', args, { cwd, encoding: 'utf8' }).trim();
}

function fixtureRepo() {
  const root = resolve(mkdtempSync(join(tmpdir(), 'trace-fixture-')));
  sh(root, 'init', '-q', '-b', 'main');
  sh(root, 'config', 'user.email', 'fixture@example.com');
  sh(root, 'config', 'user.name', 'Fixture');
  mkdirSync(join(root, 'docs', 'plans', 'active'), { recursive: true });
  writeFileSync(join(root, 'docs', 'plans', 'active', 'build.dag.yaml'), DAG);
  writeFileSync(join(root, 'docs', 'requirements.md'), REQ);
  sh(root, 'add', '.');
  sh(root, 'commit', '-q', '-m', 'base');
  sh(root, 'update-ref', 'refs/remotes/origin/main', 'HEAD');
  sh(root, 'checkout', '-q', '-b', 'feature/build/F1');
  return root;
}

function commit(root, file, content, message) {
  mkdirSync(join(root, file, '..'), { recursive: true });
  writeFileSync(join(root, file), content);
  sh(root, 'add', file);
  sh(root, 'commit', '-q', '-m', message);
}

const hook = (root, extra) => ({ cwd: root, tool_use_id: 'toolu_1', ...extra });

test('relPath is repo-relative with forward slashes, or <outside-repo>', () => {
  const root = resolve(tmpdir(), 'repo');
  assert.equal(relPath(root, join(root, 'src', 'A.cs')), 'src/A.cs');
  assert.equal(relPath(root, 'tests/B.cs'), 'tests/B.cs');
  assert.equal(relPath(root, resolve(tmpdir(), 'elsewhere', 'x')), '<outside-repo>');
});

test('redactCommand strips the repo root and caps at 200 chars', () => {
  const root = resolve(tmpdir(), 'repo');
  assert.equal(redactCommand(root, `cat ${join(root, 'src', 'A.cs')}`).includes(root), false);
  assert.equal(redactCommand(root, 'x'.repeat(300)).length, 200);
});

test('agentOf: no agent_id is main; an empty agent_type is unknown (F0a spike rulings)', () => {
  assert.equal(agentOf({}), 'main');
  assert.equal(agentOf({ agent_id: 'a1', agent_type: '' }), 'unknown');
  assert.equal(agentOf({ agent_id: 'a1', agent_type: 'builder' }), 'builder');
});

test('activity: a builder Edit on a node branch is recorded with a relative path', () => {
  const root = fixtureRepo();
  try {
    const out = buildRecord(hook(root, { tool_name: 'Edit', agent_id: 'a1', agent_type: 'builder', tool_input: { file_path: join(root, 'src', 'A.cs') } }));
    assert.equal(out.record.file, 'src/A.cs');
    assert.equal(out.record.agent, 'builder');
    assert.equal(out.record.plan, 'build');
    assert.equal(out.record.node, 'F1');
    assert.match(out.file.split('\\').join('/'), /docs\/trace\/build\/F1\/activity\.jsonl$/);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('activity: Read is logged for reviewer only, never for builder', () => {
  const root = fixtureRepo();
  try {
    const read = (agent_type) => buildRecord(hook(root, { tool_name: 'Read', agent_id: 'a1', agent_type, tool_input: { file_path: 'src/A.cs' } }));
    assert.equal(read('builder'), null);
    assert.equal(read('reviewer').record.file, 'src/A.cs');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('activity: nothing is recorded on main (F0a is git-only)', () => {
  const root = fixtureRepo();
  try {
    sh(root, 'checkout', '-q', 'main');
    assert.equal(buildRecord(hook(root, { tool_name: 'Edit', tool_input: { file_path: 'x' } })), null);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('activity: a git commit records the new commit once', () => {
  const root = fixtureRepo();
  try {
    commit(root, 'tests/T.cs', '[Trait("Rule", "SELL-2")]', 'test(SELL-2): red: rejects day 61\n\nTDD: red');
    const first = buildRecord(hook(root, { tool_name: 'Bash', agent_id: 'a1', agent_type: 'tester', tool_input: { command: 'git commit -m x' } }));
    assert.equal(first.record.commit.subject, 'test(SELL-2): red: rejects day 61');
    assert.equal(first.record.commit.tdd, 'red');
    appendJsonl(first.file, first.record);
    const again = buildRecord(hook(root, { tool_name: 'Bash', agent_id: 'a1', agent_type: 'tester', tool_input: { command: 'git commit --amend' } }));
    assert.equal(again.record.commit, undefined);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('event validation: unknown events and gates without receipts are rejected', () => {
  assert.deepEqual(validateEvent({ event: 'gate.red', cmd: 'c', exit: 1, expect: 'fail', ok: true, head: 'h' }), []);
  assert.ok(validateEvent({ event: 'made-up' }).length > 0);
  assert.ok(validateEvent({ event: 'gate.red', cmd: 'c' }).length > 0);
  assert.deepEqual(validateEvent({ event: 'steer(D1,pause2)' }), []);
});

test('currentRound: a steer makes the next round D<n>, otherwise A<attempt>', () => {
  const dir = mkdtempSync(join(tmpdir(), 'trace-round-'));
  try {
    writeFileSync(join(dir, 'loop.jsonl'), JSON.stringify({ event: 'setup', attempt: 1 }) + '\n');
    assert.equal(currentRound(dir), 'A1');
    writeFileSync(join(dir, 'loop.jsonl'), [{ event: 'setup', attempt: 1 }, { event: 'steer(D1,pause1)', attempt: 1 }].map((e) => JSON.stringify(e)).join('\n') + '\n');
    assert.equal(currentRound(dir), 'D1');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('completeness: F0a (owner david) is OK git-only', () => {
  const root = fixtureRepo();
  try {
    assert.equal(completeness(gather(root, 'build', 'F0a')).label, 'OK (git-only, F0a)');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('completeness fails loudly: a commit with no activity record, no gates, no QA step', () => {
  const root = fixtureRepo();
  try {
    commit(root, 'tests/T.cs', '[Trait("Rule", "SELL-2")]', 'test(SELL-2): red: rejects day 61\n\nAgent: tester\nPlan: build\nNode: F1\nRule: SELL-2\nTDD: red');
    const check = completeness(gather(root, 'build', 'F1'));
    assert.equal(check.ok, false);
    assert.match(check.label, /^TRAIL INCOMPLETE: /);
    assert.ok(check.reasons.some((r) => r.includes('no activity record')));
    assert.ok(check.reasons.some((r) => r.includes('gate.integrate is missing')));
    assert.ok(check.reasons.some((r) => r.includes('SELL-2 has no QA step')));
    assert.ok(check.reasons.some((r) => r.includes('no red commit before its green commit')));
    const cli = spawnSync('node', [SCRIPT, 'check', 'build', 'F1'], { cwd: root, encoding: 'utf8' });
    assert.equal(cli.status, 1);
    assert.match(cli.stdout, /TRAIL INCOMPLETE/);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('completeness passes for a fully witnessed node, and render writes README, walkthrough and matrix', () => {
  const root = fixtureRepo();
  try {
    const trailers = (tdd, agent) => `\n\nAgent: ${agent}\nPlan: build\nNode: F1\nRule: SELL-2\nTDD: ${tdd}`;
    commit(root, 'tests/T.cs', '[Trait("Rule", "SELL-2")]', 'test(SELL-2): red: rejects day 61' + trailers('red', 'tester'));
    commit(root, 'src/Uinsure.Policies.Domain/P.cs', 'class P {}', 'feat(SELL-2): green: domain enforces the window' + trailers('green', 'builder'));
    commit(root, 'docs/qa/build-F1.md', '| 1 | SELL-2 | req | send | 422 | | |\n', 'docs(F1): QA script' + trailers('red', 'tester').replace('TDD: red', 'TDD: red'));
    const dir = join(root, 'docs', 'trace', 'build', 'F1');
    mkdirSync(join(dir, 'verdicts'), { recursive: true });
    const subjects = sh(root, 'log', '--reverse', '--format=%s%x1f%(trailers:key=Agent,valueonly)', 'origin/main..HEAD').split('\n').map((l) => l.split('\x1f'));
    writeFileSync(join(dir, 'activity.jsonl'), subjects.map(([subject, agent]) => JSON.stringify({ v: 1, agent, tool: 'Bash', file: null, commit: { subject } })).join('\n') + '\n');
    const gate = (event) => JSON.stringify({ v: 1, event, cmd: 'c', exit: 0, expect: 'pass', ok: true, head: 'abc', attempt: 1 });
    writeFileSync(join(dir, 'loop.jsonl'), ['setup', 'gate.red', 'gate.green', 'gate.integrate'].map((e) => (e === 'setup' ? JSON.stringify({ event: e, attempt: 1 }) : gate(e))).join('\n') + '\n');
    for (const agent of ['reviewer', 'security']) writeFileSync(join(dir, 'verdicts', `A1-${agent}.json`), JSON.stringify({ agent, verdict: { verdict: 'PASS', rules_covered: ['SELL-2'], findings: [] } }));
    const check = completeness(gather(root, 'build', 'F1'));
    assert.deepEqual(check.reasons, []);
    const cli = spawnSync('node', [SCRIPT, 'render', 'build', 'F1'], { cwd: root, encoding: 'utf8' });
    assert.equal(cli.status, 0, cli.stderr);
    const readme = readFileSync(join(dir, 'README.md'), 'utf8');
    assert.match(readme, /## Completeness\nOK/);
    assert.match(readFileSync(join(dir, 'walkthrough.md'), 'utf8'), /\| 2 \| [0-9a-f]{7} \| green \| domain \| SELL-2 \| builder \| domain enforces the window \|/);
    const matrix = readFileSync(join(root, 'docs', 'trace', 'requirements-matrix.md'), 'utf8');
    assert.match(matrix, /\| SELL-2 \| build\/F1 \| 1\/1 \|/);
    assert.match(matrix, /\| SELL-4 \| unmapped \| not built yet \|/);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
