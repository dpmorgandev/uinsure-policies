// Shared trace helpers (traceability.spec Contract: layout, record schemas). Node built-ins only.
// Used by trace-activity.mjs, gate-subagent.mjs and .claude/scripts/render-trace.mjs.
import { appendFileSync, existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, isAbsolute, join, relative, resolve } from 'node:path';
import { execFileSync } from 'node:child_process';

export const LOOP_AGENTS_READ_LOGGED = ['reviewer', 'security', 'investigator'];

export function git(args, cwd) {
  return execFileSync('git', args, { cwd, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trim();
}

// Paths the orchestrator and the QA step write after review: trace, plan state, QA script, context snapshots.
// A verdict covers the code; commits that touch only these don't make it stale (G14).
export const BOOKKEEPING = ['docs/trace/', 'docs/plans/', 'docs/qa/', 'docs/context/'];

export const isBookkeeping = (path) => BOOKKEEPING.some((p) => path.startsWith(p));

// The verdict's diff_fingerprint: patch-id of base...head with the bookkeeping paths left out, so it
// survives a rebase that doesn't change the code and the bookkeeping commits after review.
export function codeFingerprint(root, base, head = 'HEAD') {
  const excludes = BOOKKEEPING.map((p) => `:(exclude)${p.replace(/\/$/, '')}`);
  const diff = execFileSync('git', ['diff', `${base}...${head}`, '--', '.', ...excludes], { cwd: root, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  if (diff === '') return '';
  return execFileSync('git', ['patch-id', '--stable'], { cwd: root, input: diff, encoding: 'utf8' }).trim().split(/\s+/)[0] ?? '';
}

export function repoRoot(cwd) {
  try {
    return resolve(git(['rev-parse', '--show-toplevel'], cwd));
  } catch {
    return null;
  }
}

export function currentBranch(root) {
  try {
    return git(['rev-parse', '--abbrev-ref', 'HEAD'], root);
  } catch {
    return null;
  }
}

// feature/<plan>/<node> or bug/<plan>/<node> → { plan, node }; anything else → null (no trail).
export function planNodeFromBranch(branch) {
  const match = /^(?:feature|bug)\/([a-z0-9-]+)\/([A-Za-z0-9]+)$/.exec(branch ?? '');
  return match ? { plan: match[1], node: match[2] } : null;
}

export function traceDir(root, plan, node) {
  return join(root, 'docs', 'trace', plan, node);
}

// Repo-relative with forward slashes, or "<outside-repo>". Never an absolute path.
export function relPath(root, p) {
  if (typeof p !== 'string' || p === '') return null;
  const abs = isAbsolute(p) ? resolve(p) : resolve(root, p);
  const rel = relative(root, abs);
  if (rel === '' || rel.startsWith('..') || isAbsolute(rel)) return '<outside-repo>';
  return rel.split('\\').join('/');
}

// First 200 chars, with the repo root rewritten to repo-relative form.
export function redactCommand(root, command) {
  if (typeof command !== 'string') return null;
  const forms = [root, root.split('\\').join('/'), root.replace(/^([A-Za-z]):/, (_, d) => `/${d.toLowerCase()}`).split('\\').join('/')];
  let out = command;
  for (const form of forms) out = out.split(form + '/').join('').split(form + '\\').join('').split(form).join('.');
  return out.slice(0, 200);
}

export function nowIso() {
  return new Date().toISOString().replace(/\.\d{3}Z$/, 'Z');
}

export function readJsonl(file) {
  if (!existsSync(file)) return [];
  return readFileSync(file, 'utf8')
    .split(/\r?\n/)
    .filter((line) => line.trim() !== '')
    .map((line) => {
      try {
        return JSON.parse(line);
      } catch {
        return { invalid: true, raw: line };
      }
    });
}

export function appendJsonl(file, record) {
  mkdirSync(dirname(file), { recursive: true });
  appendFileSync(file, JSON.stringify(record) + '\n');
}

// The node's attempt: 1, plus one per attempt++ event (only reviewer, security or gate FAILs record one).
export function currentAttempt(dir) {
  return 1 + readJsonl(join(dir, 'loop.jsonl')).filter((e) => e.event === 'attempt++').length;
}

// Round id for the next verdict or findings file: D<n> after an unanswered steer, else A<attempt>.
// Verdicts are always A<attempt>, so reviewer and security share one round per dispatch. Findings take
// D<n> when a steer is the latest event since the last attempt++ or verdict, otherwise the FAIL's round.
export function currentRound(dir, kind = 'findings') {
  if (kind === 'verdict') return `A${currentAttempt(dir)}`;
  const events = readJsonl(join(dir, 'loop.jsonl'));
  for (let i = events.length - 1; i >= 0; i--) {
    const e = events[i];
    const steer = /^steer\((D\d+),/.exec(e.event ?? '');
    if (steer) return steer[1];
    if (e.event === 'attempt++' || (e.event ?? '').startsWith('verdict(')) break;
  }
  // The investigator runs after attempt++, so the FAIL it diagnoses belongs to the attempt before.
  return `A${Math.max(currentAttempt(dir) - 1, 1)}`;
}

// Recorded verbatim; the caller (gate-subagent.mjs) has already validated the verdict.
// A verdict is a receipt, so it is never overwritten: a second review in the same round (a re-review after
// integrate, say) is written as <round>.2-<agent>.json, then .3, and so on.
export function writeVerdict(dir, { round, agent, agentId, plan, node, attempt, verdict }) {
  const at = (r) => join(dir, 'verdicts', `${r}-${agent}.json`);
  let n = 1;
  while (existsSync(at(n === 1 ? round : `${round}.${n}`))) n++;
  const file = at(n === 1 ? round : `${round}.${n}`);
  mkdirSync(dirname(file), { recursive: true });
  const record = { v: 1, ts: nowIso(), plan, node, attempt, agent, agent_id: agentId, verdict };
  writeFileSync(file, JSON.stringify(record, null, 2) + '\n');
  return file;
}

export function writeFindings(dir, { round, agentId, plan, node, attempt, source, findings, body }) {
  const file = join(dir, 'findings', `${round}.md`);
  mkdirSync(dirname(file), { recursive: true });
  const ids = (findings?.findings ?? []).map((f) => f.id).filter(Boolean);
  const prints = (findings?.findings ?? []).map((f) => f.fingerprint).filter(Boolean);
  const front = [
    '---',
    `v: 1`,
    `plan: ${plan}`,
    `node: ${node}`,
    `attempt: ${attempt}`,
    `round: ${round}`,
    `agent: investigator`,
    `agent_id: ${agentId}`,
    `source: ${source}`,
    `finding_ids: [${ids.join(', ')}]`,
    `fingerprints: [${prints.map((p) => JSON.stringify(p)).join(', ')}]`,
    '---',
  ].join('\n');
  writeFileSync(file, `${front}\n${body}\n`);
  return file;
}
