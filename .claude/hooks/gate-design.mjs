// SubagentStop gate for design agents (guardrails Amendment A1, G16; efficiency EFF-13): the final
// message is a receipt, never artifact content, and every file it lists is clean. A separate hook from
// gate-subagent.mjs, which still runs its G3 scope check on design agents. Every other role passes at
// once (merge-safety rule 2). For a design agent (ui-designer; creative-director, Amendment A3), any error
// blocks (fails closed), with the same restate-then-escalate cap as gate-subagent.
import { existsSync, mkdirSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { deny, identity } from './lib/io.mjs';
import { matchesAny, toRepoRel } from './lib/paths.mjs';
import { allowlist } from './lib/roles.mjs';
import { parseFinalMessage } from './lib/schema.mjs';
import { git, repoRoot } from './lib/trace.mjs';

export const DESIGN_AGENT = 'ui-designer';
export const DESIGN_AGENTS = ['ui-designer', 'creative-director', 'kit-builder'];
const MAX_BLOCKS = 3;
const RESTATE = 'Restate your COMPLETE final message: the receipt JSON object only, with these problems fixed.';
const KEYS = ['agent', 'stage', 'direction', 'screen', 'status', 'files', 'notes'];

export function receiptProblems(receipt, role = DESIGN_AGENT) {
  const p = [];
  for (const k of KEYS) if (!(k in receipt)) p.push(`receipt.${k} is required`);
  for (const k of Object.keys(receipt)) if (!KEYS.includes(k)) p.push(`receipt.${k} is not a receipt field`);
  if (receipt.agent !== role) p.push(`receipt.agent must be "${role}"`);
  if (!['written', 'blocked'].includes(receipt.status)) p.push('receipt.status must be "written" or "blocked"');
  if (!Array.isArray(receipt.files) || receipt.files.some((f) => typeof f !== 'string')) p.push('receipt.files must be an array of paths');
  else if (receipt.status === 'written' && receipt.files.length === 0) p.push('receipt.files is empty but status is "written" (empty is a FAIL)');
  if (typeof receipt.notes !== 'string' || receipt.notes.length > 600) p.push('receipt.notes must be a string of at most 600 characters');
  return p;
}

export function artifactProblems(root, path, role = DESIGN_AGENT) {
  const rel = toRepoRel(root, path);
  if (rel === null) return [`${path}: outside the repository`];
  if (!matchesAny(rel, allowlist(role))) return [`${rel}: outside the ${role}'s write scope`];
  const abs = resolve(root, rel);
  if (!existsSync(abs) || statSync(abs).size === 0) return [`${rel}: missing or empty`];
  const text = readFileSync(abs, 'utf8');
  const p = [];
  if (rel.endsWith('.html')) {
    if (!/^\s*<!doctype html>/i.test(text)) p.push(`${rel}: must start with <!doctype html> (no preamble)`);
    if (text.includes('```')) p.push(`${rel}: holds a markdown fence (prose leaked into the artifact)`);
  } else if (rel.endsWith('.md')) {
    if (!/^# \S/.test(text)) p.push(`${rel}: must start with its "# " heading (no preamble)`);
    if (/^\s*```/.test(text)) p.push(`${rel}: is wrapped in a markdown fence`);
  } else if (rel.endsWith('.json')) {
    try { JSON.parse(text); } catch (err) { p.push(`${rel}: is not valid JSON (${err.message})`); }
  } else if (/\.(tsx?|css)$/.test(rel)) {
    // guardrails A6: kit code holds no markdown fence and no prose preamble before the code.
    if (text.includes('```')) p.push(`${rel}: holds a markdown fence (prose leaked into the code)`);
    if (/^\s*(Here|Sure|Below|This file|I've|I have)\b/i.test(text)) p.push(`${rel}: starts with prose, not code`);
  }
  return p;
}

export function designProblems(root, message, role = DESIGN_AGENT) {
  let receipt;
  try {
    receipt = parseFinalMessage(message);
  } catch (err) {
    return [`your final message must be exactly one JSON object, the receipt (${err.message})`];
  }
  const p = receiptProblems(receipt, role);
  if (Array.isArray(receipt.files)) for (const f of receipt.files) if (typeof f === 'string') p.push(...artifactProblems(root, f, role));
  return p;
}

function blocks(root, agentId, bump) {
  const dir = join(resolve(root, git(['rev-parse', '--git-common-dir'], root)), 'claude-gate');
  mkdirSync(dir, { recursive: true });
  const file = join(dir, `design-${agentId.replace(/[^\w-]/g, '_')}.json`);
  const n = (existsSync(file) ? JSON.parse(readFileSync(file, 'utf8')).blocks : 0) + (bump ? 1 : 0);
  writeFileSync(file, JSON.stringify({ blocks: n }));
  return n;
}

function main() {
  let raw = '';
  try {
    raw = readFileSync(0, 'utf8');
    const input = JSON.parse(raw);
    const { role, agentId } = identity(input);
    if (!DESIGN_AGENTS.includes(role) || !agentId) process.exit(0);
    const root = repoRoot(typeof input.cwd === 'string' ? input.cwd : process.cwd());
    if (!root) deny(`G16: cannot resolve the repository. ${RESTATE}`);
    const problems = designProblems(root, input.last_assistant_message, role);
    if (!problems.length) process.exit(0);
    const n = blocks(root, agentId, true);
    if (input.stop_hook_active && n > MAX_BLOCKS) {
      process.stdout.write(JSON.stringify({ systemMessage: `gate-design: ${role} still failing after ${MAX_BLOCKS} blocks; stop let through, ESCALATE. ${problems.join(' | ')}` }));
      process.exit(0);
    }
    deny(`gate-design blocked the stop:\n- ${problems.join('\n- ')}\n${RESTATE}`);
  } catch (err) {
    if (DESIGN_AGENTS.some((a) => raw.includes(a))) deny(`G16: internal error, blocked to fail closed (${err.message}). ${RESTATE}`);
  }
  process.exit(0);
}

if (process.argv[1]?.replace(/\\/g, '/').endsWith('/gate-design.mjs')) main();
