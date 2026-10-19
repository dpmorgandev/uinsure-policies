// Skills lint (guardrails Amendment A1, G17; efficiency EFF-6): a skill ported from another harness must
// read this project and nothing else. Applies to every skill whose frontmatter has `ported-from:`. The
// Reads / Writes block is the reviewed contract; every repo path the body names must exist or fall under
// a path that block declares; no foreign reference may appear outside the frontmatter.
import { existsSync } from 'node:fs';
import { join } from 'node:path';

const DENY = [/agentflow/i, /\bprojects\//, /~\//, /\b[A-Za-z]:[\\/]/, /brief\.md\s*§/];
const PATH = /(?:^|[\s`'"(=])((?:docs|\.claude|src|tests|\.githooks)\/[^\s`'"<>*)\]|,;]*)/g;

export function splitSkill(text) {
  const m = /^---\n([\s\S]*?)\n---\n([\s\S]*)$/.exec(text.replace(/\r\n/g, '\n'));
  return m ? { frontmatter: m[1], body: m[2] } : { frontmatter: '', body: text };
}

export const isPorted = (text) => /^ported-from: \S/m.test(splitSkill(text).frontmatter);

// Paths named in the "## Reads / Writes" section: its bullet list only (the block ends at the first blank
// line after the list), so a path mentioned later in the body is never mistaken for a declared one.
export function declared(body) {
  const lines = body.split('\n');
  const start = lines.findIndex((l) => l.trim() === '## Reads / Writes');
  if (start < 0) return null;
  const block = [];
  for (const line of lines.slice(start + 1)) {
    if (line.trim() === '' && block.length) break;
    if (line.trim() === '') continue;
    if (!/^(-|\s)/.test(line)) break;
    block.push(line);
  }
  return [...block.join('\n').matchAll(PATH)].map((m) => m[1]);
}

export function skillProblems(text, root) {
  if (!isPorted(text)) return [];
  const { body } = splitSkill(text);
  const problems = [];
  const contract = declared(body);
  if (!contract) return ['a ported skill must open with a "## Reads / Writes" section'];
  if (!contract.length) problems.push('the Reads / Writes section names no path');
  for (const re of DENY) {
    const hit = re.exec(body);
    if (hit) problems.push(`foreign reference in the body: "${body.slice(Math.max(0, hit.index - 10), hit.index + 30).trim()}"`);
  }
  for (const [, path] of body.matchAll(PATH)) {
    if (path.startsWith('.claude/worktrees/')) continue;
    const clean = path.replace(/\/$/, '');
    if (existsSync(join(root, clean))) continue;
    if (contract.some((c) => clean.startsWith(c.replace(/\/$/, '')) || c.startsWith(clean))) continue;
    problems.push(`${path}: doesn't exist and isn't under a path the Reads / Writes section declares`);
  }
  return problems;
}
