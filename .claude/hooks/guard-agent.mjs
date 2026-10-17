// PreToolUse guard for subagent writes (guardrails.spec G1, G2, G4) and spec reads (agents-skills
// universal rule). Matcher: Edit|Write|NotebookEdit|Bash|Read|Grep|Glob. The main session is out of
// jurisdiction. Fails closed: any internal error denies.
import { existsSync } from 'node:fs';
import { resolve } from 'node:path';
import { guarded, deny, identity, readInput } from './lib/io.mjs';
import { TRACE, isHarness, isSpecPath, matchesAny, toRepoRel } from './lib/paths.mjs';
import { allowlist, isKnownRole, nodeKind } from './lib/roles.mjs';
import { checkEdit, isStubLine } from './lib/stub-rule.mjs';
import { repoRoot } from './lib/trace.mjs';

const READ_TOOLS = ['Read', 'Grep', 'Glob'];

// One write target against the role's rules. Returns a deny reason, or null to allow.
export function checkWrite({ role, kind, root, rel, tool, ti }) {
  if (rel === null) return 'G1: writes outside the repository are denied for agents';
  if (isHarness(rel)) return `G4: harness lock: ${rel} can't be written by an agent`;
  if (matchesAny(rel, TRACE)) return `G1: docs/trace/** is written by hooks only (${rel})`;
  if (matchesAny(rel, allowlist(role, kind))) return null;
  if (role === 'tester' && rel.startsWith('src/')) {
    if (tool === 'Edit') {
      const bad = checkEdit(ti.old_string, ti.new_string);
      return bad.length ? `G2: tester src/ changes must be throwing stubs only: ${bad.join(' | ')}` : null;
    }
    if (tool === 'Write') {
      if (existsSync(resolve(root, rel))) return `G2: tester may not overwrite an existing src/ file (${rel})`;
      const bad = String(ti.content ?? '').split(/\r?\n/).filter((l) => !isStubLine(l));
      return bad.length ? `G2: a new src/ file from the tester must be stub lines only: ${bad.slice(0, 3).join(' | ')}` : null;
    }
    return `G2: tester src/ changes must go through Edit or Write so the stub rule can check them (${rel})`;
  }
  return `G1: ${role} may not write ${rel}`;
}

const SEGMENT = /\s*(?:&&|\|\||;|\||\n)\s*/;

function tokens(segment) {
  return [...segment.matchAll(/"([^"]*)"|'([^']*)'|(\S+)/g)].map((m) => m[1] ?? m[2] ?? m[3]);
}

// Best-effort write targets of a Bash command (R4: a floor, not a boundary; G3 is the second leg).
export function bashWriteTargets(command) {
  const out = [];
  for (const m of (command ?? '').matchAll(/(?<![0-9&])>>?\s*("[^"]+"|'[^']+'|[^\s;&|<>]+)/g)) out.push(m[1].replace(/^["']|["']$/g, ''));
  for (const segment of (command ?? '').split(SEGMENT)) {
    const t = tokens(segment);
    const cmd = t[0]?.split(/[\\/]/).pop();
    const args = t.slice(1).filter((a) => !a.startsWith('-'));
    if (cmd === 'tee' || cmd === 'touch' || cmd === 'rm' || cmd === 'rmdir' || cmd === 'mkdir') out.push(...args);
    else if ((cmd === 'cp' || cmd === 'mv' || cmd === 'ln') && args.length) out.push(args.at(-1));
    else if (cmd === 'sed' && t.some((a) => /^-i/.test(a) || a === '--in-place')) out.push(...args.slice(1));
  }
  return out.filter((p) => p && !/^\/dev\/(null|stdout|stderr)$/.test(p) && !p.startsWith('&'));
}

function main() {
  const input = readInput();
  const { subagent, role } = identity(input);
  if (!subagent) return;
  const tool = input.tool_name;
  const ti = input.tool_input ?? {};

  if (READ_TOOLS.includes(tool)) {
    const text = [ti.file_path, ti.path, ti.pattern, ti.glob].filter(Boolean).join(' ');
    if (isSpecPath(text) || /(^|\/)\.claude\/specs/.test(String(ti.path ?? '').split('\\').join('/'))) deny('agents never read .claude/specs/ (agents-skills universal rule)');
    return;
  }
  if (!isKnownRole(role)) deny(`identity: subagent ${role === '' ? 'with an empty agent_type' : `"${role}"`} has no write role; denied (F0a spike rulings)`);

  const root = repoRoot(typeof input.cwd === 'string' ? input.cwd : process.cwd());
  if (!root) deny('G1: cannot resolve the repository root; denied');
  const kind = nodeKind(root);

  if (tool === 'Bash') {
    if (isSpecPath(ti.command)) deny('agents never read .claude/specs/ (agents-skills universal rule)');
    for (const target of bashWriteTargets(ti.command)) {
      const reason = checkWrite({ role, kind, root, rel: toRepoRel(root, target), tool: 'Bash', ti });
      if (reason) deny(`${reason} (via Bash)`);
    }
    return;
  }
  const reason = checkWrite({ role, kind, root, rel: toRepoRel(root, ti.file_path ?? ti.notebook_path), tool, ti });
  if (reason) deny(reason);
}

if (process.argv[1]?.replace(/\\/g, '/').endsWith('/guard-agent.mjs')) guarded('guard-agent', main);
