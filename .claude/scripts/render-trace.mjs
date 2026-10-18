// Render script (traceability.spec Contract: render script, completeness check, templates).
// Usage: node .claude/scripts/render-trace.mjs event '<json>'
//        node .claude/scripts/render-trace.mjs render|pr-body|check <plan> <node> [--archive]
// Exit 0 = complete, 1 = TRAIL INCOMPLETE (render still writes, with the reasons first), 2 = usage/setup error.
import { existsSync, readdirSync, readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { parseYaml, requirementIds } from '../skills/plan-dag/validate-dag.mjs';
import {
  appendJsonl, currentAttempt, currentBranch, git, nowIso, planNodeFromBranch, readJsonl, repoRoot, traceDir,
} from '../hooks/lib/trace.mjs';

const BASE = 'origin/main';
const TDD = ['scaffold', 'red', 'stub', 'green', 'refactor'];
const EVENT = /^(setup|state\(\w+\)|gate\.[a-z0-9.-]+|pause1|verdict\((reviewer|security),(PASS|FAIL),[^)]+\)|findings\([^)]+\)|attempt\+\+|steer\(D\d+,(pause1|pause2|pr-comment)\)|investigation\.opened\(inv-\d{3}\)|escalated|trace\.rendered\((ok|incomplete)\)|reconciled\([^)]+\)|pr\(\d+\)|pr\.comment(\.fixed)?(\([^)]*\))?|signoff\([0-9a-f]{7,40}\)|merged\([0-9a-f]{7,40}\)|archived)$/;
const GATE_KEYS = ['cmd', 'exit', 'expect', 'ok', 'head'];

function fail(message) {
  process.stderr.write(`render-trace: ${message}\n`);
  process.exit(2);
}

// ---------- event ----------

export function validateEvent(e) {
  const errors = [];
  if (typeof e.event !== 'string' || !EVENT.test(e.event)) errors.push(`unknown event ${JSON.stringify(e.event)}`);
  if (e.event?.startsWith('gate.')) for (const k of GATE_KEYS) if (!(k in e)) errors.push(`gate event missing ${k}`);
  if (e.source && !['orchestrator', 'reconciled'].includes(e.source)) errors.push(`bad source ${e.source}`);
  return errors;
}

function cmdEvent(root, json) {
  let input;
  try {
    input = JSON.parse(json);
  } catch {
    fail('event: argument is not JSON');
  }
  const where = planNodeFromBranch(currentBranch(root)) ?? { plan: input.plan, node: input.node };
  if (!where.plan || !where.node) fail('event: not on a node branch and no plan/node given');
  const dir = traceDir(root, where.plan, where.node);
  const record = { v: 1, ts: nowIso(), plan: where.plan, node: where.node, attempt: currentAttempt(dir), source: 'orchestrator', ...input };
  const errors = validateEvent(record);
  if (errors.length) fail(`event rejected: ${errors.join('; ')}`);
  appendJsonl(join(dir, 'loop.jsonl'), record);
  process.stdout.write(`event: ${record.event} → docs/trace/${where.plan}/${where.node}/loop.jsonl\n`);
}

// ---------- inputs ----------

function loadNode(root, plan, nodeId) {
  const file = join(root, 'docs', 'plans', 'active', `${plan}.dag.yaml`);
  if (!existsSync(file)) fail(`no plan file docs/plans/active/${plan}.dag.yaml`);
  const dag = parseYaml(readFileSync(file, 'utf8'));
  const node = (dag.nodes ?? []).find((n) => n.id === nodeId);
  if (!node) fail(`node ${nodeId} not in ${plan}`);
  return { dag, node };
}

function commits(root) {
  const fmt = ['%H', '%s', 'Agent', 'Plan', 'Node', 'TDD', 'Rule', 'Fixes-Finding']
    .map((k) => (k.startsWith('%') ? k : `%(trailers:key=${k},valueonly,separator=%x2C)`)).join('%x1f');
  let out = '';
  try {
    out = git(['log', '--reverse', `--format=${fmt}%x1e`, `${BASE}..HEAD`], root);
  } catch {
    return [];
  }
  return out.split('\x1e').map((s) => s.trim()).filter(Boolean).map((rec) => {
    const [sha, subject, agent, plan, node, tdd, rule, fixes] = rec.split('\x1f');
    const list = (v) => (v ?? '').split(',').map((x) => x.trim()).filter(Boolean);
    const files = git(['show', '--name-only', '--format=', sha], root).split('\n').filter(Boolean);
    return { sha, subject, agent, plan, node, tdd: tdd || null, rules: list(rule), fixes: list(fixes), files };
  });
}

function walk(dir, out = []) {
  if (!existsSync(dir)) return out;
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const p = join(dir, entry.name);
    if (entry.isDirectory() && !['bin', 'obj'].includes(entry.name)) walk(p, out);
    else if (entry.name.endsWith('.cs')) out.push(p);
  }
  return out;
}

function testMarkers(root) {
  const counts = {};
  for (const file of walk(join(root, 'tests'))) {
    for (const m of readFileSync(file, 'utf8').matchAll(/\[Trait\("Rule",\s*"([^"]+)"\)\]/g)) counts[m[1]] = (counts[m[1]] ?? 0) + 1;
  }
  return counts;
}

function qaSteps(root, plan, node) {
  const file = join(root, 'docs', 'qa', `${plan}-${node}.md`);
  const steps = {};
  if (!existsSync(file)) return steps;
  for (const m of readFileSync(file, 'utf8').matchAll(/^\|\s*(\d+)\s*\|\s*([A-Z]+-[0-9a-z/]+)\s*\|/gm)) steps[m[2]] ??= m[1];
  return steps;
}

function readDir(dir, ext) {
  return existsSync(dir) ? readdirSync(dir).filter((f) => f.endsWith(ext)) : [];
}

export function gather(root, plan, nodeId) {
  const { node } = loadNode(root, plan, nodeId);
  const dir = traceDir(root, plan, nodeId);
  const verdicts = readDir(join(dir, 'verdicts'), '.json').map((f) => ({ file: f, ...JSON.parse(readFileSync(join(dir, 'verdicts', f), 'utf8')) }));
  return {
    root, plan, nodeId, node, dir,
    commits: commits(root),
    activity: readJsonl(join(dir, 'activity.jsonl')),
    loop: readJsonl(join(dir, 'loop.jsonl')),
    verdicts,
    findings: readDir(join(dir, 'findings'), '.md').map((f) => f.replace(/\.md$/, '')),
    // <round>/<id> for every finding in findings/<round>.md (ids read from the investigator's JSON body)
    findingIds: readDir(join(dir, 'findings'), '.md').flatMap((f) => {
      const round = f.replace(/\.md$/, '');
      return [...readFileSync(join(dir, 'findings', f), 'utf8').matchAll(/"id"\s*:\s*"(\w+)"/g)].map((m) => `${round}/${m[1]}`);
    }),
    markers: testMarkers(root),
    qa: qaSteps(root, plan, nodeId),
    reqIds: requirementIds(readFileSync(join(root, 'docs', 'requirements.md'), 'utf8')),
  };
}

// ---------- completeness ----------

function lastGate(loop, name) {
  return [...loop].reverse().find((e) => e.event === name);
}

export function completeness(t, { archive = false } = {}) {
  if (t.node.owner === 'david') return { ok: true, label: `OK (git-only, ${t.nodeId})`, reasons: [] };
  const r = [];
  const rules = t.node.rules ?? [];
  const recorded = t.activity.filter((a) => a.commit);
  for (const c of t.commits) {
    if (!recorded.some((a) => a.agent === c.agent && a.commit.subject === c.subject)) r.push(`commit ${c.sha.slice(0, 7)} "${c.subject}" has no activity record for Agent ${c.agent || '?'}`);
    if (!c.agent || !c.plan || !c.node) r.push(`commit ${c.sha.slice(0, 7)} is missing Agent/Plan/Node`);
    if (c.tdd && !TDD.includes(c.tdd)) r.push(`commit ${c.sha.slice(0, 7)} has TDD ${c.tdd}`);
  }
  if (t.commits.length && !t.activity.length) r.push('activity.jsonl is empty while commits exist');
  if (t.commits.length && !t.loop.length) r.push('loop.jsonl is empty while commits exist');
  const rounds = new Set(t.verdicts.map((v) => v.file.split('-')[0]));
  for (const round of rounds) {
    const pair = ['reviewer', 'security'].map((a) => t.verdicts.find((v) => v.file === `${round}-${a}.json`));
    // A lost verdict file passes only when a reconciled(<file>) event says so, pointing at the evidence.
    const reconciled = (a) => t.loop.some((e) => e.event === `reconciled(${round}-${a}.json)`);
    if (['reviewer', 'security'].some((a, i) => !pair[i] && !reconciled(a))) r.push(`review round ${round} lacks a reviewer + security verdict pair`);
    for (const v of pair.filter(Boolean)) {
      if (v.verdict?.verdict === 'FAIL' && !t.findings.includes(round)) r.push(`FAIL verdict ${v.file} has no findings/${round}.md`);
      if (v.agent === 'reviewer' && v.verdict?.verdict === 'PASS') {
        const missing = rules.filter((id) => !(v.verdict.rules_covered ?? []).includes(id));
        if (missing.length) r.push(`PASS verdict ${v.file} doesn't cover ${missing.join(', ')}`);
      }
    }
  }
  for (const e of t.loop) {
    const steer = /^steer\((D\d+),/.exec(e.event ?? '');
    if (steer && !t.findings.includes(steer[1])) r.push(`steer ${steer[1]} has no findings/${steer[1]}.md`);
  }
  const knownIds = new Set([...t.findings, ...(t.findingIds ?? []), ...t.verdicts.flatMap((v) => (v.verdict?.findings ?? []).map((f) => f.id))]);
  for (const c of t.commits) for (const id of c.fixes) if (!knownIds.has(id)) r.push(`Fixes-Finding ${id} in ${c.sha.slice(0, 7)} resolves to nothing`);
  for (const id of rules) {
    if (!t.markers[id]) r.push(`rule ${id} has no test marker`);
    const red = t.commits.findIndex((c) => c.tdd === 'red' && c.rules.includes(id));
    const green = t.commits.findIndex((c) => c.tdd === 'green' && c.rules.includes(id));
    if (red < 0 || green < 0 || red > green) r.push(`rule ${id} has no red commit before its green commit`);
    if (!t.qa[id]) r.push(`rule ${id} has no QA step`);
  }
  const required = t.node.kind === 'feature' ? ['gate.red', 'gate.green', 'gate.integrate'] : ['gate.integrate'];
  for (const name of required) {
    const g = lastGate(t.loop, name);
    if (!g) r.push(`${name} is missing`);
    else if (g.ok !== true) r.push(`${name} is not ok`);
  }
  for (const a of t.activity) if (typeof a.file === 'string' && /^([A-Za-z]:|\/|\\)/.test(a.file)) r.push(`activity record has an absolute path (privacy breach)`);
  if (archive) {
    const pr = [...t.loop].reverse().find((e) => /^pr\(/.test(e.event ?? ''));
    const sign = [...t.loop].reverse().find((e) => /^signoff\(/.test(e.event ?? ''));
    if (!sign || (pr?.head && !pr.head.startsWith(sign.event.slice(8, -1)))) r.push('no signoff whose sha is the PR head');
    if (!t.loop.some((e) => /^merged\(/.test(e.event ?? ''))) r.push('no merged event');
  }
  const reasons = [...new Set(r)];
  return { ok: reasons.length === 0, label: reasons.length ? `TRAIL INCOMPLETE: ${reasons.join('; ')}` : 'OK', reasons };
}

// ---------- rendering ----------

function layerOf(files) {
  const layers = new Set(files.map((f) => {
    if (f.startsWith('tests/')) return 'tests';
    const m = /^src\/Uinsure\.Policies\.(Domain|Services|Persistence|Api)\//.exec(f);
    if (m) return m[1].toLowerCase();
    if (/^(\.claude\/|\.githooks\/|CLAUDE\.md$)/.test(f)) return 'harness';
    if (f.startsWith('docs/')) return 'docs';
    return 'other';
  }));
  return layers.size === 1 ? [...layers][0] : 'mixed';
}

function whatItDid(subject) {
  return subject.replace(/^\w+\([^)]*\):\s*/, '').replace(/^(red|green|stub|refactor):\s*/, '');
}

function header(inputs, ts) {
  return `Generated by .claude/scripts/render-trace.mjs from ${inputs} at ${ts}. Do not edit.`;
}

function short(sha) {
  return sha ? sha.slice(0, 7) : '';
}

function ruleRows(t) {
  const integrate = lastGate(t.loop, 'gate.integrate');
  return (t.node.rules ?? []).map((id) => {
    const n = t.markers[id] ?? 0;
    const pass = integrate?.ok ? n : '?';
    const find = (tdd) => short(t.commits.find((c) => c.tdd === tdd && c.rules.includes(id))?.sha);
    return { id, tests: `${pass}/${n}`, red: find('red'), green: find('green'), refactor: find('refactor'), qa: t.qa[id] ? `docs/qa/${t.plan}-${t.nodeId}.md#${t.qa[id]}` : '' };
  });
}

function walkthrough(t, ts) {
  const lines = [`# ${t.plan}/${t.nodeId} walkthrough: ${t.commits.length} commits`, header('git log', ts) + ' The "what it did" column is the commit subject (agent-written).', '', '| # | sha | TDD | layer | rule | agent | what it did |', '|---|---|---|---|---|---|---|'];
  t.commits.forEach((c, i) => {
    const tdd = c.tdd ?? (c.subject.startsWith('plan(') ? 'plan' : c.subject.startsWith('chore(trace)') ? 'trace' : '');
    lines.push(`| ${i + 1} | ${short(c.sha)} | ${tdd} | ${layerOf(c.files)} | ${c.rules.join(', ')} | ${c.agent} | ${whatItDid(c.subject)} |`);
  });
  return lines.join('\n') + '\n';
}

function loopLine(t) {
  const out = [];
  for (const e of t.loop) {
    const v = /^verdict\((\w+),(PASS|FAIL),/.exec(e.event ?? '');
    if (v) out.push(`attempt ${e.attempt}: ${v[1]} ${v[2]}`);
    const s = /^steer\((D\d+),(\S+)\)/.exec(e.event ?? '');
    if (s) out.push(`steer ${s[1]} (${s[2]})`);
  }
  return out.join(' · ') || 'no review yet';
}

function readme(t, check, ts) {
  const pr = [...t.loop].reverse().find((e) => /^pr\(/.test(e.event ?? ''));
  const merged = [...t.loop].reverse().find((e) => /^merged\(/.test(e.event ?? ''));
  const integrate = lastGate(t.loop, 'gate.integrate');
  const sign = [...t.loop].reverse().find((e) => /^signoff\(/.test(e.event ?? ''));
  const lines = [
    `# ${t.plan}/${t.nodeId}: ${t.node.title} · status: ${t.node.status} · PR ${pr ? '#' + pr.event.slice(3, -1) : 'pending'} · merge ${merged ? merged.event.slice(7, -1).slice(0, 7) : 'pending'}`,
    header('loop/activity JSONL, verdicts, findings, git and test source', ts),
    `## Completeness`, check.label, '',
  ];
  if (t.node.owner === 'david') {
    lines.push('Hooks not yet live: witnessed by git and trailers only.', '', '## Re-verify', `git log --format='%h %s' --grep='(${t.plan}/${t.nodeId})'`);
    return lines.join('\n') + '\n';
  }
  lines.push('## Rules', '| rule | tests (pass/total) | red | green | refactor | QA step |', '|---|---|---|---|---|---|');
  for (const row of ruleRows(t)) lines.push(`| ${row.id} | ${row.tests} | ${row.red} | ${row.green} | ${row.refactor} | ${row.qa} |`);
  lines.push('', '## Walkthrough', `${t.commits.length} commits → walkthrough.md`, '', '## Loop', loopLine(t), '', '## Who touched what', '| commit | Agent trailer | files | hook-log agent | match |', '|---|---|---|---|---|');
  for (const c of t.commits) {
    const rec = t.activity.find((a) => a.commit?.subject === c.subject);
    lines.push(`| ${short(c.sha)} | ${c.agent} | ${c.files.length} | ${rec?.agent ?? '—'} | ${rec?.agent === c.agent ? '✓' : '✗'} |`);
  }
  const mark = (name) => (lastGate(t.loop, name)?.ok ? '✓' : '✗');
  lines.push('', '## Gates', `red ${mark('gate.red')} · pause 1 ${t.loop.some((e) => e.event === 'pause1') ? '✓' : '✗'} · green ${mark('gate.green')} · integrate ${mark('gate.integrate')}${integrate ? ` (full suite ${integrate.passed ?? '?'}/${(integrate.passed ?? 0) + (integrate.failed ?? 0)} at ${short(integrate.head)})` : ''} · sign-off ${sign ? 'David ' + sign.ts.slice(0, 10) + ' ' + sign.event.slice(8, 15) : 'pending'}`);
  lines.push('', '## Re-verify', `git log --format='%h %(trailers:key=Agent,valueonly,separator=%x2C) %(trailers:key=TDD,valueonly,separator=%x2C) %s' ${BASE}..${short(t.commits.at(-1)?.sha)}`);
  return lines.join('\n') + '\n';
}

function matrix(t, ts) {
  const { dag } = loadNode(t.root, t.plan, t.nodeId);
  const file = join(t.root, 'docs', 'trace', 'requirements-matrix.md');
  const previous = {};
  if (existsSync(file)) for (const m of readFileSync(file, 'utf8').matchAll(/^\| ([A-Z]+-[0-9a-z/]+) \|.*$/gm)) previous[m[1]] = m[0];
  const mine = Object.fromEntries(ruleRows(t).map((r) => [r.id, r]));
  const pr = [...t.loop].reverse().find((e) => /^pr\(/.test(e.event ?? ''));
  const sign = [...t.loop].reverse().find((e) => /^signoff\(/.test(e.event ?? ''));
  const lines = ['# Requirements matrix', header('every node trail and docs/requirements.md', ts), '', '| rule | plan/node | tests (pass/total) | red | green | refactor | QA step | sign-off | PR |', '|---|---|---|---|---|---|---|---|---|'];
  for (const id of t.reqIds) {
    const owner = (dag.nodes ?? []).find((n) => (n.rules ?? []).includes(id));
    if (mine[id]) {
      const r = mine[id];
      lines.push(`| ${id} | ${t.plan}/${t.nodeId} | ${r.tests} | ${r.red} | ${r.green} | ${r.refactor} | ${r.qa} | ${sign ? 'David ' + sign.ts.slice(0, 10) : 'pending'} | ${pr ? '#' + pr.event.slice(3, -1) : ''} |`);
    } else if (previous[id]) lines.push(previous[id]);
    else if (owner?.status === 'cut') lines.push(`| ${id} | ${t.plan}/${owner.id} | cut (${owner.id}) | | | | | | |`);
    else lines.push(`| ${id} | ${owner ? `${t.plan}/${owner.id}` : 'unmapped'} | not built yet | | | | | | |`);
  }
  return lines.join('\n') + '\n';
}

function prBody(t, check) {
  const ids = (t.node.rules ?? []).join(', ');
  const integrate = lastGate(t.loop, 'gate.integrate');
  const attempts = currentAttempt(t.dir);
  const lines = [
    `## ${t.plan}/${t.nodeId}: ${t.node.title}`,
    `Rules: ${ids} · Tests: ${integrate ? `${integrate.passed ?? '?'} passed, ${integrate.failed ?? '?'} failed on ${short(integrate.head)} after rebase onto ${BASE}` : 'integrate not run'}`,
    `Walkthrough: docs/trace/${t.plan}/${t.nodeId}/walkthrough.md · Trail: docs/trace/${t.plan}/${t.nodeId}/README.md`,
    '', '| rule | tests | red | green | refactor |', '|---|---|---|---|---|',
    ...ruleRows(t).map((r) => `| ${r.id} | ${r.tests} | ${r.red} | ${r.green} | ${r.refactor} |`),
    '', `Attempts: ${attempts} · ${loopLine(t)} · Verdicts and the rebase receipt are posted below as PR comments by the git agent.`,
    'How to run: dotnet run --project src/Uinsure.Policies.Api --urls http://localhost:5080 · Postman env baseUrl=http://localhost:5080',
    `QA: docs/qa/${t.plan}-${t.nodeId}.md · sign-off: pending`,
    `Completeness: ${check.label}`,
    '## From David',
    "<!-- one paragraph, written by David before `gh pr create`; the PR isn't raised while it's empty -->",
  ];
  return lines.join('\n') + '\n';
}

// ---------- main ----------

function main() {
  const [cmd, a, b, flag] = process.argv.slice(2);
  const root = repoRoot(process.cwd());
  if (!root) fail('not inside a git repository');
  if (cmd === 'event') return cmdEvent(root, a);
  if (!['render', 'pr-body', 'check'].includes(cmd) || !a || !b) fail('usage: event <json> | render|pr-body|check <plan> <node> [--archive]');
  const t = gather(root, a, b);
  const check = completeness(t, { archive: flag === '--archive' });
  if (cmd === 'check') {
    process.stdout.write(check.label + '\n');
  } else if (cmd === 'pr-body') {
    process.stdout.write(prBody(t, check));
  } else if (t.node.owner === 'david') {
    // An owner: david node (F0a) has a written README with its probe results; rendering would overwrite it.
    process.stdout.write(`not rendered: ${a}/${b} is David's node, and its README is written, not generated · ${check.label}\n`);
  } else {
    const ts = nowIso();
    mkdirSync(t.dir, { recursive: true });
    writeFileSync(join(t.dir, 'README.md'), readme(t, check, ts));
    writeFileSync(join(t.dir, 'walkthrough.md'), (check.ok ? '' : check.label + '\n\n') + walkthrough(t, ts));
    writeFileSync(join(root, 'docs', 'trace', 'requirements-matrix.md'), matrix(t, ts));
    process.stdout.write(`rendered docs/trace/${a}/${b}/ · ${check.label}\n`);
  }
  process.exit(check.ok ? 0 : 1);
}

if (process.argv[1]?.replace(/\\/g, '/').endsWith('/render-trace.mjs')) main();
