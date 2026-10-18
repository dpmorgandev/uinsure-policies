// Fixtures for the trace hook and render script (traceability.spec Contract). Run: node --test .claude/hooks/__tests__
import './clean-git-env.mjs';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { execFileSync, spawnSync } from 'node:child_process';
import { relPath, redactCommand, currentAttempt, currentRound, appendJsonl, writeVerdict, writeFindings } from '../lib/trace.mjs';
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

test('redactCommand leaves no local path: worktree, main checkout, home dir and user name, every spelling', () => {
  // built at runtime so pre-commit's G10 doesn't flag this file (same convention as the G10 fixture)
  const U = ['Us', 'ers'].join('');
  const opts = { home: `C:\\${U}\\jdoe`, user: 'jdoe' };
  const root = `C:\\${U}\\jdoe\\Work\\Repo\\.claude\\worktrees\\build-F1`;
  const cmds = [
    `cat C:/${U}/jdoe/Work/Repo/.claude/worktrees/build-F1/src/A.cs`,
    `cd /c/${U}/jdoe/Work/Repo && git log`,
    `ls C:\\${U}\\jdoe\\Work\\Repo\\docs`,
    `node C:/${U}/jdoe/AppData/Local/Temp/claude/C--${U}-jdoe-Work-Repo/abc/scratchpad/x.mjs`,
  ];
  const out = cmds.map((c) => redactCommand(root, c, opts));
  assert.equal(out[0], 'cat src/A.cs');
  assert.equal(out[1], 'cd <main> && git log');
  assert.equal(out[2], 'ls <main>\\docs');
  assert.equal(out[3], `node ~/AppData/Local/Temp/claude/C--${U}-<user>-Work-Repo/abc/scratchpad/x.mjs`);
  // and the committed privacy scan agrees: nothing it would block is left
  const dir = mkdtempSync(join(tmpdir(), 'redact-scan-'));
  try {
    writeFileSync(join(dir, 'out.txt'), out.join('\n') + '\n');
    const repo = resolve(import.meta.dirname, '..', '..', '..');
    const scan = spawnSync('bash', [join(repo, '.claude', 'hooks', 'privacy-scan.sh'), join(dir, 'out.txt')], { cwd: repo, encoding: 'utf8' });
    assert.equal(scan.status, 0, scan.stderr);
    writeFileSync(join(dir, 'raw.txt'), cmds.join('\n') + '\n');
    assert.notEqual(spawnSync('bash', [join(repo, '.claude', 'hooks', 'privacy-scan.sh'), join(dir, 'raw.txt')], { cwd: repo, encoding: 'utf8' }).status, 0);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
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
    // a verdict after a steer still belongs to the attempt, so reviewer and security share one round
    assert.equal(currentRound(dir, 'verdict'), 'A1');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('currentAttempt: 1 plus one per attempt++ event; the round follows it', () => {
  const dir = mkdtempSync(join(tmpdir(), 'trace-attempt-'));
  const events = (...names) => writeFileSync(join(dir, 'loop.jsonl'), names.map((event) => JSON.stringify({ event, attempt: 0 })).join('\n') + '\n');
  try {
    events('setup');
    assert.equal(currentAttempt(dir), 1);
    events('setup', 'verdict(reviewer,FAIL,x)', 'attempt++');
    assert.equal(currentAttempt(dir), 2);
    // the investigator's findings take the FAIL's round (A1); the re-review's verdicts are A2
    assert.equal(currentRound(dir), 'A1');
    assert.equal(currentRound(dir, 'verdict'), 'A2');
    events('setup', 'attempt++', 'attempt++');
    assert.equal(currentAttempt(dir), 3);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('writeVerdict never overwrites; writeFindings records the findings ids', () => {
  const dir = mkdtempSync(join(tmpdir(), 'trace-write-'));
  const base = { agent: 'reviewer', agentId: 'a', plan: 'build', node: 'F1', attempt: 1 };
  try {
    const first = writeVerdict(dir, { ...base, round: 'A1', verdict: { verdict: 'FAIL' } });
    const second = writeVerdict(dir, { ...base, round: 'A1', verdict: { verdict: 'PASS' } });
    assert.match(first, /A1-reviewer\.json$/);
    assert.match(second, /A1\.2-reviewer\.json$/);
    assert.equal(JSON.parse(readFileSync(first, 'utf8')).verdict.verdict, 'FAIL');
    const file = writeFindings(dir, { round: 'A1', agentId: 'i', plan: 'build', node: 'F1', attempt: 2, source: 'reviewer',
      findings: { source_ref: 'verdicts/A1-reviewer.json: R1, R2', findings: [{ id: 'I1' }, { id: 'I2' }] }, body: '{}' });
    assert.match(readFileSync(file, 'utf8'), /^finding_ids: \[I1, I2\]$/m);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('writeFindings: a revision in the same round adds its ids and keeps the earlier findings', () => {
  const dir = mkdtempSync(join(tmpdir(), 'trace-revise-'));
  const base = { round: 'D2', plan: 'build', node: 'F1', attempt: 1, source: 'david' };
  try {
    writeFindings(dir, { ...base, agentId: 'i1', findings: { findings: [{ id: 'I1', fingerprint: 'a|b|c' }, { id: 'I2' }] }, body: '{"findings":[{"id":"I1"},{"id":"I2"}]}' });
    const file = writeFindings(dir, { ...base, agentId: 'i2', findings: { findings: [{ id: 'I3', fingerprint: 'd|e|f' }] }, body: '{"findings":[{"id":"I3"}]}' });
    const text = readFileSync(file, 'utf8');
    assert.match(text, /^finding_ids: \[I1, I2, I3\]$/m);
    assert.match(text, /^fingerprints: \["a\|b\|c", "d\|e\|f"\]$/m);
    assert.match(text, /"id":"I1"[\s\S]*<!-- revision: agent i2, attempt 1 -->[\s\S]*"id":"I3"/);
    assert.equal(text.match(/^---$/gm).length, 2, 'one front matter, not two');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('completeness: a lost activity record or a bare Fixes-Finding passes only when reconciled with evidence', () => {
  const sha = 'b'.repeat(40);
  const t = {
    node: { owner: 'loop', kind: 'feature', rules: [] }, nodeId: 'F1', activity: [{ commit: { subject: 'other' } }], markers: {}, qa: {},
    commits: [{ sha, subject: 'plan(build/F1): t12 pending → done', agent: 'main', plan: 'build', node: 'F1', tdd: null, rules: [], fixes: ['I2'] }],
    loop: [{ event: 'gate.red', ok: true }, { event: 'gate.green', ok: true }, { event: 'gate.integrate', ok: true }],
    verdicts: [], findings: ['A1'], findingIds: ['A1/I2'],
  };
  let reasons = completeness(t).reasons;
  assert.ok(reasons.some((x) => x.includes('no activity record')), reasons.join('\n'));
  assert.ok(reasons.some((x) => x.includes('Fixes-Finding I2')), reasons.join('\n'));
  t.loop.push({ event: 'reconciled(activity:bbbbbbb)' }, { event: 'reconciled(fixes:bbbbbbb:I2)' });
  reasons = completeness(t).reasons;
  assert.ok(reasons.some((x) => x.includes('no activity record')), 'a reconciliation with no detail is not evidence');
  t.loop.splice(-2, 2, { event: 'reconciled(activity:bbbbbbb)', detail: 'command exited 2 after the commit' },
    { event: 'reconciled(fixes:bbbbbbb:I2)', detail: 'I2 is A1/I2: the commit follows findings/A1.md' });
  reasons = completeness(t).reasons;
  assert.ok(!reasons.some((x) => x.includes('no activity record') || x.includes('Fixes-Finding')), reasons.join('\n'));
  t.loop.push({ event: 'reconciled(fixes:ccccccc:I3)', detail: 'another commit' });
  t.commits[0].fixes.push('I3');
  assert.ok(completeness(t).reasons.some((x) => x.includes('Fixes-Finding I3')), 'a reconciliation names its own commit');
});

test('completeness: Fixes-Finding <round>/<id> resolves; a lost verdict passes only when reconciled', () => {
  const t = {
    node: { owner: 'loop', kind: 'feature', rules: [] }, nodeId: 'F1', activity: [], markers: {}, qa: {},
    commits: [{ sha: 'a'.repeat(40), subject: 'fix', agent: 'builder', plan: 'build', node: 'F1', tdd: 'green', rules: [], fixes: ['A1/I1', 'A1/I9'] }],
    loop: [{ event: 'gate.red', ok: true }, { event: 'gate.green', ok: true }, { event: 'gate.integrate', ok: true }],
    verdicts: [{ file: 'A1-security.json', agent: 'security', verdict: { verdict: 'PASS', findings: [] } }],
    findings: ['A1'], findingIds: ['A1/I1'],
  };
  let reasons = completeness(t).reasons;
  assert.ok(!reasons.some((x) => x.includes('A1/I1 ')), reasons.join('\n'));
  assert.ok(reasons.some((x) => x.includes('Fixes-Finding A1/I9')), reasons.join('\n'));
  assert.ok(reasons.some((x) => x.includes('round A1 lacks')), reasons.join('\n'));
  t.loop.push({ event: 'reconciled(A1-reviewer.json)' });
  reasons = completeness(t).reasons;
  assert.ok(!reasons.some((x) => x.includes('round A1 lacks')), reasons.join('\n'));
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
