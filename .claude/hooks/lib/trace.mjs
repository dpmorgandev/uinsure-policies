// Shared trace helpers (traceability.spec Contract: layout, record schemas). Node built-ins only.
// Used by trace-activity.mjs, gate-subagent.mjs and .claude/scripts/render-trace.mjs.
import { appendFileSync, existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, isAbsolute, join, relative, resolve } from 'node:path';
import { execFileSync } from 'node:child_process';

export const LOOP_AGENTS_READ_LOGGED = ['reviewer', 'security', 'investigator'];

export function git(args, cwd) {
  return execFileSync('git', args, { cwd, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trim();
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

// attempt = the last loop.jsonl event's attempt (0 before setup).
export function currentAttempt(dir) {
  const events = readJsonl(join(dir, 'loop.jsonl'));
  for (let i = events.length - 1; i >= 0; i--) {
    if (Number.isInteger(events[i].attempt)) return events[i].attempt;
  }
  return 0;
}

// Round id for the next verdict or findings file: D<n> after an unanswered steer, else A<attempt>.
export function currentRound(dir) {
  const events = readJsonl(join(dir, 'loop.jsonl'));
  for (let i = events.length - 1; i >= 0; i--) {
    const e = events[i];
    const steer = /^steer\((D\d+),/.exec(e.event ?? '');
    if (steer) return steer[1];
    if (e.event === 'attempt++' || (e.event ?? '').startsWith('verdict(')) break;
  }
  return `A${Math.max(currentAttempt(dir), 1)}`;
}

// Recorded verbatim; the caller (gate-subagent.mjs) has already validated the verdict.
export function writeVerdict(dir, { round, agent, agentId, plan, node, attempt, verdict }) {
  const file = join(dir, 'verdicts', `${round}-${agent}.json`);
  mkdirSync(dirname(file), { recursive: true });
  const record = { v: 1, ts: nowIso(), plan, node, attempt, agent, agent_id: agentId, verdict };
  writeFileSync(file, JSON.stringify(record, null, 2) + '\n');
  return file;
}

export function writeFindings(dir, { round, agentId, plan, node, attempt, source, findings, body }) {
  const file = join(dir, 'findings', `${round}.md`);
  mkdirSync(dirname(file), { recursive: true });
  const ids = String(findings?.source_ref ?? '').split(/[,\s]+/).filter(Boolean);
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
