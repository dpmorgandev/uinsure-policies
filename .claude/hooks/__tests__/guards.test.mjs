// Fixtures for the guard hooks (guardrails.spec manifest G1–G5, G7–G9, G11, G14; identity; spec reads;
// verdict and findings checks). Run: node --test ".claude/hooks/__tests__/*.test.mjs"
import './clean-git-env.mjs';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, rmSync, readFileSync, existsSync, readdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { execFileSync, spawnSync } from 'node:child_process';
import { checkWrite, bashWriteTargets } from '../guard-agent.mjs';
import { check, normalise } from '../guard-bash.mjs';
import { scopeViolations, dirtySnapshot, verdictProblems, findingsProblems } from '../gate-subagent.mjs';
import { checkEdit } from '../lib/stub-rule.mjs';
import { toRepoRel } from '../lib/paths.mjs';
import { allowlist } from '../lib/roles.mjs';

const HOOKS = resolve(import.meta.dirname, '..');
const DAG = 'plan_id: build\nstatus: approved\nnodes:\n  - id: F1\n    title: Sell\n    kind: feature\n    rules: [SELL-2]\n  - id: F5\n    title: IaC\n    kind: evidence\n    rules: []\n';

function sh(cwd, ...args) {
  return execFileSync('git', args, { cwd, encoding: 'utf8' }).trim();
}

function repo(branch = 'feature/build/F1') {
  const root = resolve(mkdtempSync(join(tmpdir(), 'guard-fixture-')));
  sh(root, 'init', '-q', '-b', 'main');
  sh(root, 'config', 'user.email', 'fixture@example.com');
  sh(root, 'config', 'user.name', 'Fixture');
  mkdirSync(join(root, 'docs', 'plans', 'active'), { recursive: true });
  writeFileSync(join(root, 'docs', 'plans', 'active', 'build.dag.yaml'), DAG);
  mkdirSync(join(root, 'src'), { recursive: true });
  writeFileSync(join(root, 'src', 'Existing.cs'), 'namespace X;\n');
  sh(root, 'add', '.');
  sh(root, 'commit', '-q', '-m', 'base');
  if (branch !== 'main') sh(root, 'checkout', '-q', '-b', branch);
  return root;
}

function withRepo(branch, fn) {
  const root = repo(branch);
  try {
    return fn(root);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}

function runHook(file, input, ...args) {
  return spawnSync('node', [join(HOOKS, file), ...args], { input: JSON.stringify(input), encoding: 'utf8' });
}

const w = (root, role, path, extra = {}) => checkWrite({ role, kind: 'feature', root, rel: toRepoRel(root, path), tool: 'Edit', ti: {}, ...extra });

// ---------- G1 / G4 ----------

test('G1: builder → tests/ deny, src/ allow, global.json allow', () => withRepo('feature/build/F1', (root) => {
  assert.match(w(root, 'builder', 'tests/X.cs'), /^G1/);
  assert.equal(w(root, 'builder', 'src/X.cs'), null);
  assert.equal(w(root, 'builder', 'global.json'), null);
}));

test('G1: tester → tests/T/T.csproj allow, Directory.Packages.props deny', () => withRepo('feature/build/F1', (root) => {
  assert.equal(w(root, 'tester', 'tests/T/T.csproj'), null);
  assert.match(w(root, 'tester', 'Directory.Packages.props'), /^G1/);
}));

test('G1: the builder may write the root BannedSymbols.txt; the tester may not', () => withRepo('feature/build/F1', (root) => {
  assert.equal(w(root, 'builder', 'BannedSymbols.txt'), null);
  assert.match(w(root, 'tester', 'BannedSymbols.txt'), /^G1/);
  assert.match(w(root, 'builder', 'docs/BannedSymbols.txt'), /^G1/);
}));

test('G1: a worktree path is resolved relative to the worktree root', () => withRepo('feature/build/F1', (root) => {
  assert.match(w(root, 'builder', join(root, '.claude', 'worktrees', 'build-F1', 'tests', 'X.cs')), /^G1/);
}));

test('G1: builder scope widens by node kind (evidence → infrastructure/**)', () => {
  assert.ok(allowlist('builder', 'evidence').includes('infrastructure/**'));
  assert.ok(!allowlist('builder', 'feature').includes('infrastructure/**'));
  assert.ok(allowlist('builder', 'docs').includes('README.md'));
});

test('G1: all agents are denied docs/trace/**', () => withRepo('feature/build/F1', (root) => {
  assert.match(w(root, 'tester', 'docs/trace/build/F1/loop.jsonl'), /docs\/trace/);
}));

test('G4: harness copies inside a worktree are locked; the worktree src/ is not', () => withRepo('feature/build/F1', (root) => {
  assert.match(w(root, 'builder', join(root, '.claude', 'worktrees', 'x', '.claude', 'hooks', 'a.mjs')), /^G4/);
  assert.equal(w(root, 'builder', join(root, '.claude', 'worktrees', 'x', 'src', 'A.cs')), null);
  assert.match(w(root, 'builder', 'CLAUDE.md'), /^G4/);
  assert.match(w(root, 'tester', '.claude/settings.json'), /^G4/);
}));

test('identity: reviewer Write, unknown type and empty agent_type deny; main session allows', () => withRepo('feature/build/F1', (root) => {
  const edit = (extra) => runHook('guard-agent.mjs', { tool_name: 'Write', cwd: root, tool_input: { file_path: join(root, 'src', 'A.cs'), content: 'x' }, ...extra });
  assert.equal(edit({ agent_id: 'a1', agent_type: 'reviewer' }).status, 2);
  assert.equal(edit({ agent_id: 'a1', agent_type: 'general-purpose' }).status, 2);
  assert.equal(edit({ agent_id: 'a1', agent_type: '' }).status, 2);
  assert.equal(edit({}).status, 0);
  assert.equal(edit({ agent_id: 'a1', agent_type: 'builder' }).status, 0);
}));

test('spec reads: an agent Read or Grep of .claude/specs/ is denied; other reads pass', () => withRepo('feature/build/F1', (root) => {
  const read = (tool, ti, agent_type = 'reviewer') => runHook('guard-agent.mjs', { tool_name: tool, cwd: root, agent_id: 'a1', agent_type, tool_input: ti }).status;
  assert.equal(read('Read', { file_path: join(root, '.claude', 'specs', 'x.spec.md') }), 2);
  assert.equal(read('Grep', { pattern: 'x', path: '.claude/specs' }), 2);
  assert.equal(read('Glob', { pattern: '.claude/specs/*.md' }), 2);
  assert.equal(read('Read', { file_path: join(root, 'src', 'A.cs') }, 'Explore'), 0);
}));

test('G1 Bash leg: write targets are parsed from redirects, tee, cp, sed -i', () => {
  assert.deepEqual(bashWriteTargets('echo x > tests/A.cs'), ['tests/A.cs']);
  assert.deepEqual(bashWriteTargets('cat a | tee out.txt'), ['out.txt']);
  assert.deepEqual(bashWriteTargets('cp a.cs tests/b.cs && ls'), ['tests/b.cs']);
  assert.deepEqual(bashWriteTargets("sed -i 's/a/b/' src/A.cs"), ['src/A.cs']);
  assert.deepEqual(bashWriteTargets('dotnet build 2>&1 > /dev/null'), []);
});

// ---------- G2 ----------

test('G2: signature + throw allow; return/if/removal deny; Write over an existing src file deny', () => withRepo('feature/build/F1', (root) => {
  assert.deepEqual(checkEdit('}', '    public Result<Cancellation> Cancel(DateOnly? on) => throw new NotImplementedException();\n}'), []);
  assert.ok(checkEdit('', '    return 0;').length);
  assert.ok(checkEdit('', '    if (x)').length);
  assert.ok(checkEdit('a;\nb;', 'a;').length);
  assert.match(checkWrite({ role: 'tester', kind: 'feature', root, rel: 'src/Existing.cs', tool: 'Write', ti: { content: 'namespace X;' } }), /^G2/);
  assert.equal(checkWrite({ role: 'tester', kind: 'feature', root, rel: 'src/New.cs', tool: 'Write', ti: { content: 'namespace X;\npublic sealed class New\n{\n}' } }), null);
}));

// ---------- G3 ----------

test('G3: a builder write to tests/ found by git blocks at stop; a clean diff passes; docs/trace is ignored', () => withRepo('feature/build/F1', (root) => {
  const start = sh(root, 'rev-parse', 'HEAD');
  assert.deepEqual(scopeViolations(root, 'builder', start), []);
  mkdirSync(join(root, 'docs', 'trace', 'build', 'F1'), { recursive: true });
  writeFileSync(join(root, 'docs', 'trace', 'build', 'F1', 'activity.jsonl'), '{}\n');
  assert.deepEqual(scopeViolations(root, 'builder', start), []);
  mkdirSync(join(root, 'tests'), { recursive: true });
  writeFileSync(join(root, 'tests', 'A.cs'), 'x');
  assert.ok(scopeViolations(root, 'builder', start).some((v) => v.includes('tests/A.cs')));
}));

test('G3: files already dirty at start are not blamed on the agent unless it changes them', () => withRepo('feature/build/F1', (root) => {
  const start = sh(root, 'rev-parse', 'HEAD');
  mkdirSync(join(root, '.claude', 'hooks'), { recursive: true });
  writeFileSync(join(root, '.claude', 'hooks', 'wip.mjs'), 'before');
  const snap = dirtySnapshot(root, start);
  assert.deepEqual(scopeViolations(root, 'builder', start, snap), []);
  writeFileSync(join(root, '.claude', 'hooks', 'wip.mjs'), 'after');
  assert.ok(scopeViolations(root, 'builder', start, snap).some((v) => v.startsWith('G4')));
}));

test('G3 + G2 second leg: a tester body in src/ blocks at stop; a throwing stub passes', () => withRepo('feature/build/F1', (root) => {
  const start = sh(root, 'rev-parse', 'HEAD');
  writeFileSync(join(root, 'src', 'Existing.cs'), 'namespace X;\npublic sealed class P\n{\n    public int Go() => throw new NotImplementedException();\n}\n');
  assert.deepEqual(scopeViolations(root, 'tester', start), []);
  writeFileSync(join(root, 'src', 'Existing.cs'), 'namespace X;\npublic sealed class P\n{\n    public int Go() { return 1; }\n}\n');
  assert.ok(scopeViolations(root, 'tester', start).some((v) => v.startsWith('G2')));
}));

test('G3: the stop gate blocks with a restate instruction, then lets through after 3 blocks', () => withRepo('feature/build/F1', (root) => {
  const base = { cwd: root, agent_id: 'agent-cap', agent_type: 'builder' };
  assert.equal(runHook('gate-subagent.mjs', base, 'start').status, 0);
  mkdirSync(join(root, 'tests'), { recursive: true });
  writeFileSync(join(root, 'tests', 'A.cs'), 'x');
  const first = runHook('gate-subagent.mjs', base, 'stop');
  assert.equal(first.status, 2);
  assert.match(first.stderr, /COMPLETE final message/);
  for (let i = 0; i < 2; i++) assert.equal(runHook('gate-subagent.mjs', { ...base, stop_hook_active: true }, 'stop').status, 2);
  const capped = runHook('gate-subagent.mjs', { ...base, stop_hook_active: true }, 'stop');
  assert.equal(capped.status, 0);
  assert.match(capped.stdout, /ESCALATE/);
}));

test('identity: an empty agent_type at stop passes and is logged', () => withRepo('feature/build/F1', (root) => {
  assert.equal(runHook('gate-subagent.mjs', { cwd: root, agent_id: 'helper', agent_type: '' }, 'stop').status, 0);
}));

// ---------- G5 / G7 / G8 / G11 (everyone) ----------

test('G5: each pattern, bare and wrapped, is denied for everyone', () => {
  for (const cmd of ['gh repo edit --visibility public', 'gh api repos/x', 'gh auth token', 'gh pr merge 3 --merge',
    'git push --force origin x', 'git push -f origin x', 'git push --force-with-lease', 'git push origin +x',
    "sh -c 'git push --force origin x'", '/usr/bin/git push -f origin x', '"C:\\Program Files\\GitHub CLI\\gh.exe" pr merge 3']) {
    assert.match(check({ command: cmd, subagent: false, role: '', root: null }) ?? '', /^G5/, cmd);
  }
  assert.equal(normalise('/usr/bin/git push'), 'git push');
});

test('G7: push to main is denied once the remote main is locked; the marker commit itself may be pushed once', () => withRepo('feature/build/F1', (root) => {
  const push = (cmd) => check({ command: cmd, subagent: false, role: '', root });
  assert.equal(push('git push origin HEAD:main'), null);
  mkdirSync(join(root, '.githooks'), { recursive: true });
  writeFileSync(join(root, '.githooks', 'main-locked'), '');
  sh(root, 'add', '.githooks/main-locked');
  sh(root, 'commit', '-q', '-m', 'lock');
  assert.equal(push('git push origin HEAD:main'), null, 'the marker commit is pushed once, while origin/main is unlocked');
  sh(root, 'update-ref', 'refs/remotes/origin/main', 'HEAD');
  assert.match(push('git push origin HEAD:main') ?? '', /^G7/);
  assert.match(push('git push origin :refs/heads/main') ?? '', /^G7/);
  assert.equal(push('git push -u origin feature/build/F1'), null);
}));

test('G8: commit on main with the marker deny; without allow; on a feature branch allow', () => withRepo('main', (root) => {
  const commit = () => check({ command: 'git commit -m x', subagent: false, role: '', root });
  assert.equal(commit(), null);
  mkdirSync(join(root, '.githooks'), { recursive: true });
  writeFileSync(join(root, '.githooks', 'main-locked'), '');
  sh(root, 'add', '.githooks/main-locked');
  sh(root, 'commit', '-q', '-m', 'lock');
  assert.match(commit() ?? '', /^G8/);
  sh(root, 'checkout', '-q', '-b', 'feature/build/F1');
  assert.equal(commit(), null);
}));

test('G11: --no-verify, commit -n and hook-path overrides are denied for everyone', () => {
  for (const cmd of ['git commit --no-verify -m x', 'git commit -n -m x', 'git push --no-verify', 'git -c core.hooksPath=/tmp commit -m x', 'git config core.hooksPath x']) {
    assert.match(check({ command: cmd, subagent: false, role: '', root: null }) ?? '', /^G11/, cmd);
  }
  assert.equal(check({ command: 'git commit -m "no new tests"', subagent: false, role: '', root: null }), null);
});

test('G11: reading core.hooksPath is allowed; every write form is still denied', () => {
  for (const cmd of ['git config --get core.hooksPath', 'git config core.hooksPath', 'git config --get-all core.hooksPath', 'git config get core.hooksPath', 'git -C repo config --get core.hooksPath']) {
    assert.equal(check({ command: cmd, subagent: false, role: '', root: null }), null, cmd);
  }
  for (const cmd of ['git config core.hooksPath .x', 'git config --unset core.hooksPath', 'git config --replace-all core.hooksPath x', 'git config set core.hooksPath x', 'git config --add core.hooksPath x', 'sh -c "git config core.hooksPath x"']) {
    assert.match(check({ command: cmd, subagent: false, role: '', root: null }) ?? '', /^G11/, cmd);
  }
});

// ---------- G4 Bash / G9 (agents) ----------

test('G4 Bash: an agent may not run git config', () => {
  assert.match(check({ command: 'git config user.name x', subagent: true, role: 'git', root: null }) ?? '', /^G4/);
});

test('G9: trailers must match agent_type and the branch; git add -A is denied', () => withRepo('feature/build/F1', (root) => {
  const c = (cmd, role = 'builder') => check({ command: cmd, subagent: true, role, root });
  assert.match(c('git commit -m "feat(SELL-2): green: x" -m "Agent: tester\nPlan: build\nNode: F1"') ?? '', /^G9/);
  assert.match(c('git commit -m "feat(SELL-2): green: x" -m "Agent: builder\nPlan: build"') ?? '', /Node/);
  assert.equal(c('git commit -m "feat(SELL-2): green: x" -m "Agent: builder\nPlan: build\nNode: F1\nRule: SELL-2\nTDD: green"'), null);
  assert.match(c('git add -A') ?? '', /^G9/);
  assert.match(c('git add .') ?? '', /^G9/);
  assert.equal(c('git add src/A.cs'), null);
}));

// ---------- G14 ----------

function verdictFile(root, round, agent, verdict, head, ts) {
  const dir = join(root, 'docs', 'trace', 'build', 'F1', 'verdicts');
  mkdirSync(dir, { recursive: true });
  writeFileSync(join(dir, `${round}-${agent}.json`), JSON.stringify({ v: 1, ts, agent, verdict: { verdict, head_sha: head } }));
}

test('G14: PR only when the latest reviewer and security verdicts are PASS for HEAD', () => withRepo('feature/build/F1', (root) => {
  const pr = () => check({ command: 'gh pr create --draft --body-file b.md', subagent: true, role: 'git', root });
  const head = sh(root, 'rev-parse', 'HEAD');
  assert.match(pr() ?? '', /^G14/);
  verdictFile(root, 'A1', 'reviewer', 'PASS', head, '2026-10-18T09:00:00Z');
  verdictFile(root, 'A1', 'security', 'FAIL', head, '2026-10-18T09:00:00Z');
  assert.match(pr() ?? '', /security verdict is FAIL/);
  verdictFile(root, 'A2', 'security', 'PASS', head, '2026-10-18T10:00:00Z');
  assert.equal(pr(), null);
  verdictFile(root, 'A2', 'reviewer', 'PASS', 'f'.repeat(40), '2026-10-18T10:00:00Z');
  assert.match(pr() ?? '', /not HEAD/);
}));

test('G14: a harness/<slug> branch opens a PR from the main session only, touching only the harness', () => withRepo('main', (root) => {
  sh(root, 'update-ref', 'refs/remotes/origin/main', 'HEAD');
  sh(root, 'checkout', '-q', '-b', 'harness/scaffold-step');
  const pr = (subagent = false) => check({ command: 'gh pr create --body-file b.md', subagent, role: subagent ? 'git' : '', root });
  assert.match(pr() ?? '', /no changes/);
  mkdirSync(join(root, '.claude', 'skills'), { recursive: true });
  writeFileSync(join(root, '.claude', 'skills', 'x.md'), 'x\n');
  writeFileSync(join(root, 'CLAUDE.md'), 'x\n');
  sh(root, 'add', '.claude/skills/x.md', 'CLAUDE.md');
  sh(root, 'commit', '-q', '-m', 'harness');
  assert.equal(pr(), null);
  assert.match(pr(true) ?? '', /main session/);
  writeFileSync(join(root, 'src', 'Existing.cs'), 'namespace Y;\n');
  sh(root, 'commit', '-q', '-am', 'src');
  assert.match(pr() ?? '', /only \.claude\/.*src\/Existing\.cs/);
  sh(root, 'checkout', '-q', '-b', 'harness/Bad_Name');
  assert.match(pr() ?? '', /node branch with verdicts, or a harness/);
}));

// ---------- verdict and findings checks ----------

const verdict = (over = {}) => ({
  schema_version: 1, agent: 'reviewer', plan_id: 'build', node_id: 'F1', attempt: 1,
  base_sha: 'a'.repeat(40), head_sha: 'b'.repeat(40), diff_fingerprint: 'fp', verdict: 'PASS',
  gate_refs: ['gate.integrate'], rules_covered: ['SELL-2'], rules_missing: [], findings: [], ...over,
});
const ctx = { role: 'reviewer', rules: ['SELL-2'], head: 'b'.repeat(40), fp: 'fp', attempt: 1 };

test('verdict: a valid PASS has no problems; PASS with a P1, FAIL with no findings, a stale head do', () => {
  assert.deepEqual(verdictProblems(verdict(), ctx), []);
  const p1 = { id: 'R1', rule_id: 'SELL-2', severity: 'P1', file: 'src/A.cs', what: 'x', why: 'y', route_to: 'builder' };
  assert.ok(verdictProblems(verdict({ findings: [p1] }), ctx).some((p) => p.includes('P0/P1')));
  assert.ok(verdictProblems(verdict({ verdict: 'FAIL' }), ctx).some((p) => p.includes('at least one finding')));
  assert.ok(verdictProblems(verdict({ head_sha: 'c'.repeat(40) }), ctx).some((p) => p.includes('head_sha')));
  assert.ok(verdictProblems(verdict({ rules_covered: [] }), ctx).some((p) => p.includes('SELL-2')));
  assert.ok(verdictProblems({ ...verdict(), extra: 1 }, ctx).some((p) => p.includes('unexpected field')));
});

// ---------- G13: declared = wired (reads the real repo; the Contract is the single source) ----------

const ROOT = resolve(import.meta.dirname, '..', '..', '..');
const read = (rel) => readFileSync(join(ROOT, rel), 'utf8').replace(/\r\n/g, '\n');
const settings = () => JSON.parse(read('.claude/settings.json'));
const guardrails = () => read('.claude/specs/guardrails.spec.md');

function bodyLines(rel) {
  const text = read(rel);
  const m = /^---\n[\s\S]*?\n---\n([\s\S]*)$/.exec(text);
  return (m ? m[1] : text).trimEnd().split('\n').length;
}

function frontmatter(rel) {
  return (/^---\n([\s\S]*?)\n---/.exec(read(rel)) ?? [])[1] ?? '';
}

function descriptionLines(fm) {
  const folded = /^description: >-?\n((?:  .*\n?)+)/m.exec(fm);
  if (folded) return folded[1].trim().split('\n').length;
  return /^description: .+$/m.test(fm) ? 1 : 0;
}

test('G13 (i): every hook script is wired in settings.json, and every wired command exists', () => {
  const commands = Object.values(settings().hooks).flat().flatMap((m) => m.hooks.map((h) => h.command));
  const wired = commands.map((c) => (/\.claude\/hooks\/([\w-]+\.mjs)/.exec(c) ?? [])[1]).filter(Boolean);
  for (const c of commands) {
    const script = (/\$CLAUDE_PROJECT_DIR\/([^"]+)"/.exec(c) ?? [])[1];
    assert.ok(script && existsSync(join(ROOT, script)), `wired command has no script: ${c}`);
  }
  for (const f of readdirSync(join(ROOT, '.claude', 'hooks')).filter((f) => f.endsWith('.mjs'))) {
    assert.ok(wired.includes(f), `${f} exists but isn't wired in settings.json`);
  }
  for (const f of ['guard-agent.mjs', 'guard-bash.mjs', 'gate-subagent.mjs', 'lib/roles.mjs', 'lib/paths.mjs', 'lib/stub-rule.mjs']) {
    assert.ok(existsSync(join(ROOT, '.claude', 'hooks', f)), `manifest file missing: ${f}`);
  }
});

test('G13 (ii): every manifest ID has at least one fixture', () => {
  const ids = [...guardrails().matchAll(/^\| (G\d+) \|/gm)].map((m) => m[1]);
  assert.ok(ids.length >= 14, 'manifest table not found');
  const titles = readdirSync(join(ROOT, '.claude', 'hooks', '__tests__')).filter((f) => f.endsWith('.test.mjs'))
    .flatMap((f) => [...read(`.claude/hooks/__tests__/${f}`).matchAll(/^test\('([^']+)'/gm)].map((m) => m[1]));
  for (const id of ids) assert.ok(titles.some((t) => new RegExp(`(^|[^0-9A-Za-z])${id}([^0-9]|$)`).test(t)), `${id} has no fixture`);
});

test('G13 (iii): core.hooksPath is .githooks, and every git hook is executable and LF', () => {
  assert.equal(sh(ROOT, 'config', '--get', 'core.hooksPath'), '.githooks');
  for (const hook of ['pre-commit', 'commit-msg', 'pre-push']) {
    assert.match(sh(ROOT, 'ls-files', '-s', `.githooks/${hook}`), /^100755 /, `${hook} isn't executable in the index`);
    assert.match(sh(ROOT, 'ls-files', '--eol', `.githooks/${hook}`), /^i\/lf\s+w\/lf/, `${hook} isn't LF`);
  }
});

test('G13 (iv): no plugins, marketplaces or MCP servers in project settings', () => {
  for (const rel of ['.claude/settings.json', '.claude/settings.local.json', '.mcp.json']) {
    if (!existsSync(join(ROOT, rel))) continue;
    const s = JSON.parse(read(rel));
    for (const key of ['enabledPlugins', 'extraKnownMarketplaces', 'mcpServers', 'enableAllProjectMcpServers']) {
      assert.ok(!(key in s), `${rel} sets ${key}`);
    }
  }
});

test('G13 (v) + G6: settings deny and ask match the Contract exactly', () => {
  const draft = /### settings\.json permissions block \(draft\)\n+```jsonc\n([\s\S]*?)\n```/.exec(guardrails());
  assert.ok(draft, 'permissions draft not found in guardrails');
  const contract = JSON.parse(draft[1].replace(/^\s*\/\/.*$/gm, '')).permissions;
  const actual = settings().permissions;
  assert.deepEqual([...actual.deny].sort(), [...contract.deny].sort());
  assert.deepEqual([...actual.ask].sort(), [...contract.ask].sort());
  for (const rule of ['Bash(git push *)', 'Bash(gh pr create *)', 'Bash(gh pr comment *)']) assert.ok(actual.ask.includes(rule), `G6: ${rule} missing`);
});

test('G12: reviewer, security and investigator have no Edit, Write or Bash', () => {
  for (const agent of ['reviewer', 'security', 'investigator']) {
    const tools = (/^tools: (.*)$/m.exec(frontmatter(`.claude/agents/${agent}.md`)) ?? [])[1] ?? '';
    assert.deepEqual(tools.split(',').map((t) => t.trim()), ['Read', 'Grep', 'Glob'], agent);
  }
});

test('G10: the privacy scan flags a local absolute path and passes clean text', () => {
  const dir = mkdtempSync(join(tmpdir(), 'privacy-'));
  try {
    writeFileSync(join(dir, 'leak.md'), `see ${['C:', 'Users', 'someone', 'notes.txt'].join('/')}\n`); // built at runtime so pre-commit's G10 doesn't flag this file
    writeFileSync(join(dir, 'clean.md'), 'nothing private here\n');
    const scan = (f) => spawnSync('bash', [join(ROOT, '.claude', 'hooks', 'privacy-scan.sh'), join(dir, f)], { cwd: ROOT, encoding: 'utf8' }).status;
    assert.equal(scan('leak.md'), 1);
    assert.equal(scan('clean.md'), 0);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('G13: line caps (agents 120, preloaded skills 200, architecture 150, main-session skills 250, descriptions 3, CLAUDE.md 80)', () => {
  for (const f of readdirSync(join(ROOT, '.claude', 'agents')).filter((f) => f.endsWith('.md'))) {
    const rel = `.claude/agents/${f}`;
    assert.ok(bodyLines(rel) <= 120, `${rel}: ${bodyLines(rel)} body lines`);
    assert.ok(descriptionLines(frontmatter(rel)) <= 3, `${rel}: description too long`);
  }
  for (const s of readdirSync(join(ROOT, '.claude', 'skills'))) {
    const rel = `.claude/skills/${s}/SKILL.md`;
    if (!existsSync(join(ROOT, rel))) continue;
    const preloaded = /^user-invocable: false$/m.test(frontmatter(rel));
    const cap = s === 'architecture' ? 150 : preloaded ? 200 : 250;
    assert.ok(bodyLines(rel) <= cap, `${rel}: ${bodyLines(rel)} body lines (cap ${cap})`);
    assert.ok(descriptionLines(frontmatter(rel)) <= 3, `${rel}: description too long`);
  }
  assert.ok(read('CLAUDE.md').trimEnd().split('\n').length <= 80, 'CLAUDE.md over 80 lines');
});

test('G13: the verdict-format schema is byte-identical to orchestration\'s', () => {
  const block = (text) => (/```json\n([\s\S]*?)\n```/.exec(text.slice(text.indexOf('### Verdict JSON schema v1'))) ?? [])[1];
  const skill = read('.claude/skills/verdict-format/SKILL.md');
  assert.equal((/```json\n([\s\S]*?)\n```/.exec(skill) ?? [])[1], block(read('.claude/specs/orchestration.spec.md')));
});

test('findings: testable=false needs untestable_reason; a complete entry passes', () => {
  const f = (x) => ({ schema_version: 1, plan_id: 'build', node_id: 'F1', attempt: 1, source: 'reviewer', source_ref: 'R1', previous_attempts: [],
    findings: [{ id: 'I1', rule_id: 'SELL-2', layer: 'domain', files: ['src/A.cs'], root_cause: 'A.cs:3 off by one', fingerprint: 'SELL-2|src/A.cs|k', ...x }] });
  assert.deepEqual(findingsProblems(f({ testable: true, suggested_test: 'Given…' }), { attempt: 1 }), []);
  assert.ok(findingsProblems(f({ testable: false }), { attempt: 1 }).some((p) => p.includes('untestable_reason')));
});
