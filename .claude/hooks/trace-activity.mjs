// PostToolUse activity hook (traceability.spec Contract: activity.jsonl). Records who touched what,
// under the worktree's docs/trace/<plan>/<node>/. Runs after the tool, so it can't block: it always
// exits 0, and a gap it leaves is caught by render-trace.mjs check, which fails loudly.
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import {
  LOOP_AGENTS_READ_LOGGED, appendJsonl, currentAttempt, currentBranch, git, nowIso,
  planNodeFromBranch, readJsonl, redactCommand, relPath, repoRoot, traceDir,
} from './lib/trace.mjs';

const TOOLS = ['Edit', 'Write', 'Bash', 'PowerShell', 'Read', 'Grep'];

function readInput() {
  try {
    const input = JSON.parse(readFileSync(0, 'utf8'));
    return typeof input === 'object' && input !== null ? input : {};
  } catch {
    return {};
  }
}

// F0a spike rulings: agent_id marks a subagent; an empty agent_type is recorded as "unknown".
export function agentOf(input) {
  if (!input.agent_id) return 'main';
  return typeof input.agent_type === 'string' && input.agent_type !== '' ? input.agent_type : 'unknown';
}

function lastCommit(root) {
  const [sha, subject, tdd] = git(['log', '-1', '--format=%H%x1f%s%x1f%(trailers:key=TDD,valueonly,separator=%x2C)'], root).split('\x1f');
  return { sha, subject, tdd: tdd === '' ? null : tdd };
}

export function buildRecord(input) {
  const tool = input.tool_name;
  if (!TOOLS.includes(tool)) return null;
  const agent = agentOf(input);
  if ((tool === 'Read' || tool === 'Grep') && !LOOP_AGENTS_READ_LOGGED.includes(agent)) return null;

  const root = repoRoot(typeof input.cwd === 'string' ? input.cwd : process.cwd());
  if (!root) return null;
  const where = planNodeFromBranch(currentBranch(root));
  if (!where) return null; // outside a node branch there is no trail (F0a is git-only)

  const dir = traceDir(root, where.plan, where.node);
  const ti = input.tool_input ?? {};
  const record = {
    v: 1, ts: nowIso(), plan: where.plan, node: where.node, attempt: currentAttempt(dir),
    agent, agent_id: input.agent_id ?? null, tool, tool_use_id: input.tool_use_id ?? null,
    file: null, command: null,
  };
  if (tool === 'Edit' || tool === 'Write' || tool === 'Read') record.file = relPath(root, ti.file_path);
  if (tool === 'Grep') {
    record.file = relPath(root, ti.path);
    record.command = typeof ti.pattern === 'string' ? ti.pattern.slice(0, 200) : null;
  }
  if (tool === 'Bash' || tool === 'PowerShell') {
    record.command = redactCommand(root, ti.command);
    if (/\bgit\b[^|;&]*\bcommit\b/.test(ti.command ?? '')) {
      const commit = lastCommit(root);
      const seen = readJsonl(join(dir, 'activity.jsonl')).some((r) => r.commit?.sha === commit.sha);
      if (!seen) record.commit = commit;
    }
  }
  return { file: join(dir, 'activity.jsonl'), record };
}

function main() {
  try {
    const out = buildRecord(readInput());
    if (out) appendJsonl(out.file, out.record);
  } catch (err) {
    process.stderr.write(`trace-activity: not recorded (${err.message}); render-trace check will flag the gap\n`);
  }
  process.exit(0);
}

if (process.argv[1]?.replace(/\\/g, '/').endsWith('/trace-activity.mjs')) main();
