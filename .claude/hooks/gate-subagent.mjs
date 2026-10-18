// SubagentStart / SubagentStop gate (guardrails.spec G2 second leg, G3; orchestration.spec verdict and
// investigator findings schema v1). Usage from settings.json: gate-subagent.mjs start | stop.
// start records HEAD per agent_id. stop checks what git says changed against the role's allowlist and
// the harness lock, re-runs the tester stub rule over src/, validates reviewer/security verdicts and
// investigator findings, and files them in the node's trail. A block always asks for the complete final
// message to be restated. After 3 blocks the stop is let through with a loud message so the
// orchestrator escalates. An empty agent_type (Claude Code's internal helper) passes and is logged.
import { existsSync, mkdirSync, readFileSync, writeFileSync, appendFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { execFileSync } from 'node:child_process';
import { deny, identity, readInput } from './lib/io.mjs';
import { TRACE, isHarness, matchesAny } from './lib/paths.mjs';
import { allowlist, isKnownRole, nodeKind } from './lib/roles.mjs';
import { checkDiff } from './lib/stub-rule.mjs';
import { parseFinalMessage, validate, verdictSchema } from './lib/schema.mjs';
import {
  codeFingerprint, currentAttempt, currentBranch, currentRound, git, nowIso, planNodeFromBranch, repoRoot, traceDir, writeFindings, writeVerdict,
} from './lib/trace.mjs';
import { parseYaml } from '../skills/plan-dag/validate-dag.mjs';

const MAX_BLOCKS = 3;
const SECURITY_IN = ['API3:2023', 'API4:2023', 'API6:2023', 'API8:2023', 'API9:2023'];
const RESTATE = 'Restate your COMPLETE final message (the whole JSON object), with these problems fixed. Only your last message reaches the orchestrator.';

function stateDir(root) {
  const dir = join(resolve(root, git(['rev-parse', '--git-common-dir'], root)), 'claude-gate');
  mkdirSync(dir, { recursive: true });
  return dir;
}

function stateFile(root, agentId) {
  return join(stateDir(root), `${agentId.replace(/[^\w-]/g, '_')}.json`);
}

function loadState(root, agentId) {
  const f = stateFile(root, agentId);
  return existsSync(f) ? JSON.parse(readFileSync(f, 'utf8')) : null;
}

function saveState(root, agentId, state) {
  writeFileSync(stateFile(root, agentId), JSON.stringify(state));
}

function log(root, record) {
  if (root) appendFileSync(join(stateDir(root), 'log.jsonl'), JSON.stringify({ ts: nowIso(), ...record }) + '\n');
}

function changedSince(root, start) {
  return [...new Set([
    ...git(['diff', '--name-only', start], root).split('\n'),
    ...git(['ls-files', '--others', '--exclude-standard'], root).split('\n'),
  ].filter(Boolean))];
}

function hashOf(root, rel) {
  try {
    return existsSync(resolve(root, rel)) ? git(['hash-object', '--', rel], root) : 'deleted';
  } catch {
    return 'unreadable';
  }
}

// Files already dirty when the agent started, with their content hash, so G3 doesn't blame the agent.
export function dirtySnapshot(root, start) {
  return Object.fromEntries(changedSince(root, start).map((rel) => [rel, hashOf(root, rel)]));
}

// G3 (+ G2 second leg): every path git says changed since start must be in the role's scope.
// A path that now matches origin/main exactly arrived with a rebase (or was restored to main's version);
// the agent didn't author it. Without this the git agent's integrate trips G3/G4 after every harness merge.
function sameAsMain(root, path) {
  try {
    execFileSync('git', ['diff', '--quiet', 'origin/main', '--', path], { cwd: root, stdio: 'ignore' });
    return true;
  } catch {
    return false;
  }
}

export function scopeViolations(root, role, start, preexisting = {}) {
  const changed = new Set(changedSince(root, start)
    .filter((p) => !matchesAny(p, TRACE))
    .filter((p) => !(p in preexisting && preexisting[p] === hashOf(root, p)))
    .filter((p) => !sameAsMain(root, p)));
  const kind = nodeKind(root);
  const out = [];
  let testerSrc = false;
  for (const rel of changed) {
    if (isHarness(rel)) out.push(`G4: ${rel} (harness lock)`);
    else if (matchesAny(rel, allowlist(role, kind))) continue;
    else if (role === 'tester' && rel.startsWith('src/')) testerSrc = true;
    else out.push(`G3: ${rel} is outside ${role}'s scope`);
  }
  if (testerSrc) {
    const bad = checkDiff(git(['diff', '-U0', start, '--', 'src/'], root));
    const untracked = git(['ls-files', '--others', '--exclude-standard', '--', 'src/'], root).split('\n').filter(Boolean);
    for (const f of untracked) for (const line of readFileSync(resolve(root, f), 'utf8').split(/\r?\n/)) if (checkDiff(`+${line}`).length) bad.push(`${f}: ${line.trim()}`);
    if (bad.length) out.push(`G2: tester src/ changes must be throwing stubs only: ${bad.slice(0, 5).join(' | ')}`);
  }
  return out;
}

function nodeRules(root, where) {
  const file = join(root, 'docs', 'plans', 'active', `${where.plan}.dag.yaml`);
  if (!existsSync(file)) return [];
  return (parseYaml(readFileSync(file, 'utf8')).nodes ?? []).find((n) => n.id === where.node)?.rules ?? [];
}


// Verdict schema v1 plus the hook-side semantic checks.
export function verdictProblems(v, { role, rules, head, fp, attempt }) {
  const problems = validate(verdictSchema(), v);
  if (problems.length) return problems;
  if (v.agent !== role) problems.push(`agent must be "${role}"`);
  const blocking = v.findings.filter((f) => f.severity === 'P0' || f.severity === 'P1');
  if (v.verdict === 'PASS') {
    if (blocking.length) problems.push(`PASS with P0/P1 findings (${blocking.map((f) => f.id).join(', ')})`);
    if (v.rules_missing.length) problems.push('PASS with rules_missing not empty');
    const need = role === 'reviewer' ? rules : SECURITY_IN;
    const missing = need.filter((id) => !v.rules_covered.includes(id));
    if (missing.length) problems.push(`PASS but rules_covered misses ${missing.join(', ')}`);
  }
  if (v.verdict === 'FAIL' && v.findings.length === 0) problems.push('FAIL needs at least one finding');
  if (head && v.head_sha !== head) problems.push(`head_sha must be ${head}`);
  if (fp !== null && v.diff_fingerprint !== fp) problems.push(`diff_fingerprint must be ${fp} (code diff base...HEAD without the bookkeeping paths | git patch-id --stable)`);
  if (attempt && v.attempt !== attempt) problems.push(`attempt must be ${attempt}`);
  return problems;
}

const SOURCES = ['reviewer', 'security', 'gate', 'david', 'pr-comment'];
const LAYERS = ['domain', 'services', 'persistence', 'api', 'tests'];

export function findingsProblems(f, { attempt }) {
  const p = [];
  const str = (x) => typeof x === 'string' && x !== '';
  if (f.schema_version !== 1) p.push('schema_version must be 1');
  for (const k of ['plan_id', 'node_id', 'source_ref']) if (!str(f[k])) p.push(`${k} is required`);
  if (!Number.isInteger(f.attempt)) p.push('attempt must be an integer');
  else if (attempt && f.attempt !== attempt) p.push(`attempt must be ${attempt}`);
  if (!SOURCES.includes(f.source)) p.push(`source must be one of ${SOURCES.join('|')}`);
  if (!Array.isArray(f.previous_attempts)) p.push('previous_attempts must be an array');
  if (!Array.isArray(f.findings) || f.findings.length === 0) p.push('findings needs at least one entry');
  for (const [i, x] of (Array.isArray(f.findings) ? f.findings : []).entries()) {
    const at = `findings[${i}]`;
    if (!/^I\d+$/.test(x.id ?? '')) p.push(`${at}.id must be I<n>`);
    for (const k of ['rule_id', 'root_cause', 'fingerprint']) if (!str(x[k])) p.push(`${at}.${k} is required`);
    if (!LAYERS.includes(x.layer)) p.push(`${at}.layer must be one of ${LAYERS.join('|')}`);
    if (!Array.isArray(x.files) || !x.files.length) p.push(`${at}.files needs at least one path`);
    if (typeof x.testable !== 'boolean') p.push(`${at}.testable must be a boolean`);
    else if (x.testable && !str(x.suggested_test)) p.push(`${at}.suggested_test is required when testable`);
    else if (!x.testable && !str(x.untestable_reason)) p.push(`${at}.untestable_reason is required when not testable`);
  }
  return p;
}

function start(input) {
  const { role, agentId } = identity(input);
  const root = repoRoot(typeof input.cwd === 'string' ? input.cwd : process.cwd());
  if (!root || !agentId) return;
  if (role === '') return log(root, { event: 'start', agent_id: agentId, note: 'empty agent_type, logged only' });
  const head = git(['rev-parse', 'HEAD'], root);
  saveState(root, agentId, { role, start: head, dirty: dirtySnapshot(root, head), blocks: 0 });
}

function stop(input) {
  const { role, agentId } = identity(input);
  const root = repoRoot(typeof input.cwd === 'string' ? input.cwd : process.cwd());
  if (!agentId) return;
  if (role === '') return log(root, { event: 'stop', agent_id: agentId, note: 'empty agent_type, logged only' });
  if (!root) deny(`gate-subagent: cannot resolve the repository. ${RESTATE}`);
  const state = loadState(root, agentId) ?? { role, start: null, blocks: 0 };
  const problems = [];

  if (!state.start) problems.push('G3: no start commit was recorded for this agent, so its changes cannot be checked');
  else problems.push(...scopeViolations(root, role, state.start, state.dirty ?? {}));

  const where = planNodeFromBranch(currentBranch(root));
  const dir = where ? traceDir(root, where.plan, where.node) : null;
  const attempt = dir ? currentAttempt(dir) : 0;
  let parsed = null;
  if (['reviewer', 'security', 'investigator'].includes(role)) {
    try {
      parsed = parseFinalMessage(input.last_assistant_message);
    } catch (err) {
      problems.push(`your final message must be exactly one JSON object (${err.message})`);
    }
    if (parsed && role === 'investigator') problems.push(...findingsProblems(parsed, { attempt }));
    if (parsed && role !== 'investigator') {
      const head = git(['rev-parse', 'HEAD'], root);
      const fp = typeof parsed.base_sha === 'string' && /^[0-9a-f]{40}$/.test(parsed.base_sha) ? codeFingerprint(root, parsed.base_sha) : null;
      problems.push(...verdictProblems(parsed, { role, rules: where ? nodeRules(root, where) : [], head, fp, attempt }));
    }
  }

  if (problems.length) {
    state.blocks = (state.blocks ?? 0) + 1;
    saveState(root, agentId, state);
    log(root, { event: 'block', agent_id: agentId, role, blocks: state.blocks, problems });
    if (input.stop_hook_active && state.blocks > MAX_BLOCKS) {
      process.stdout.write(JSON.stringify({ systemMessage: `gate-subagent: ${role} still failing after ${MAX_BLOCKS} blocks; stop let through, ESCALATE. ${problems.join(' | ')}` }));
      return;
    }
    deny(`gate-subagent blocked the stop:\n- ${problems.join('\n- ')}\n${RESTATE}`);
  }

  if (parsed && dir) {
    const round = currentRound(dir, role === 'investigator' ? 'findings' : 'verdict');
    if (role === 'investigator') writeFindings(dir, { round, agentId, plan: where.plan, node: where.node, attempt, source: parsed.source, findings: parsed, body: input.last_assistant_message });
    else writeVerdict(dir, { round, agent: role, agentId, plan: where.plan, node: where.node, attempt, verdict: parsed });
  }
  log(root, { event: 'stop', agent_id: agentId, role, ok: true });
}

function main() {
  const mode = process.argv[2];
  try {
    const input = readInput();
    if (mode === 'start') start(input);
    else if (mode === 'stop') stop(input);
    else deny('gate-subagent: usage start|stop');
  } catch (err) {
    deny(`gate-subagent: internal error, blocked to fail closed (${err.message}). ${RESTATE}`);
  }
  process.exit(0);
}

if (process.argv[1]?.replace(/\\/g, '/').endsWith('/gate-subagent.mjs')) main();
