// PreToolUse guard for Bash (guardrails.spec G4 Bash leg, G5, G7, G8, G9, G11, G14). Checks the whole
// command text, so wrapped forms (`sh -c '…'`, a full path to git or gh) are caught too; it over-matches
// rather than under-matches. Fails closed: any internal error denies.
import { existsSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { execFileSync } from 'node:child_process';
import { guarded, deny, identity, readInput } from './lib/io.mjs';
import { currentBranch, git, planNodeFromBranch, repoRoot, traceDir } from './lib/trace.mjs';

// Rewrite `/usr/bin/git`, `C:\…\git.exe`, `"…/gh"` to plain `git` / `gh`.
export function normalise(command) {
  return String(command ?? '')
    .replace(/(["'])[^"']*[\\/](git|gh)(?:\.exe)?\1/g, '$2')
    .replace(/(?:[A-Za-z]:)?[^\s"';&|]*[\\/](git|gh)(?:\.exe)?(?=[\s"']|$)/g, '$1');
}

function gitInvocations(text, sub) {
  return [...text.matchAll(new RegExp(`\\bgit\\b((?:\\s+-[cC]\\s+\\S+|\\s+-\\S+)*)\\s+${sub}\\b([^;&|\\n]*)`, 'g'))].map((m) => ({ pre: m[1], args: m[2] }));
}

function mainLocked(root) {
  try {
    execFileSync('git', ['cat-file', '-e', 'HEAD:.githooks/main-locked'], { cwd: root, stdio: 'ignore' });
    return true;
  } catch {
    return false;
  }
}

// G14: latest reviewer and security verdicts must both be PASS with head_sha = HEAD. Null = allow.
export function prGate(root) {
  const where = planNodeFromBranch(currentBranch(root));
  if (!where) return 'G14: gh pr create is only allowed from a node branch with verdicts';
  const dir = join(traceDir(root, where.plan, where.node), 'verdicts');
  if (!existsSync(dir)) return 'G14: no verdicts for this node; no PR without reviewer and security PASS for HEAD';
  const head = git(['rev-parse', 'HEAD'], root);
  const latest = {};
  for (const f of execFileSync('git', ['ls-files', '--others', '--cached', '--', dir], { cwd: root, encoding: 'utf8' }).split('\n').filter((l) => l.endsWith('.json'))) {
    let v;
    try {
      v = JSON.parse(readFileSync(resolve(root, f), 'utf8'));
    } catch {
      return `G14: unparseable verdict ${f}; denied`;
    }
    if (!latest[v.agent] || v.ts > latest[v.agent].ts) latest[v.agent] = v;
  }
  for (const agent of ['reviewer', 'security']) {
    const v = latest[agent];
    if (!v) return `G14: no ${agent} verdict for this node`;
    if (v.verdict?.verdict !== 'PASS') return `G14: the latest ${agent} verdict is ${v.verdict?.verdict ?? 'invalid'}`;
    if (v.verdict?.head_sha !== head) return `G14: the latest ${agent} verdict is for ${String(v.verdict?.head_sha).slice(0, 7)}, not HEAD ${head.slice(0, 7)}`;
  }
  return null;
}

function commitMessageText(root, args) {
  let text = args;
  const file = /(?:^|\s)(?:-F|--file)(?:\s+|=)("[^"]+"|'[^']+'|\S+)/.exec(args);
  if (file) {
    const p = resolve(root, file[1].replace(/^["']|["']$/g, ''));
    if (existsSync(p)) text += '\n' + readFileSync(p, 'utf8');
  }
  return text;
}

export function check({ command, subagent, role, root }) {
  const text = normalise(command);

  // G5: repo admin, merge and force-push are nobody's.
  if (/\bgh\s+repo\s+(edit|delete|create)\b/.test(text)) return 'G5: gh repo edit/delete/create is denied for everyone';
  if (/\bgh\s+api\b/.test(text)) return 'G5: raw gh api is denied for everyone';
  if (/\bgh\s+auth\b/.test(text)) return 'G5: gh auth is denied for everyone';
  if (/\bgh\s+pr\s+merge\b/.test(text)) return 'G5: gh pr merge is denied; David merges in the GitHub UI';

  for (const { pre, args } of gitInvocations(text, 'push')) {
    if (/(^|\s)(--force\S*|-[a-zA-Z]*f[a-zA-Z]*)(\s|$)/.test(args) || /(^|\s)\+\S/.test(args)) return 'G5: force-push is denied for everyone';
    if (/--no-verify/.test(args + pre)) return 'G11: --no-verify is denied for everyone';
    // G7: gated on the main-locked marker, like G8, so F0a's own pushes to main stay possible.
    if (root && mainLocked(root)) {
      const refs = args.split(/\s+/).filter((a) => a && !a.startsWith('-'));
      if (refs.some((r) => /^(?:[^:]*:)?(?:refs\/heads\/)?main$/.test(r)) || (refs.length <= 1 && currentBranch(root) === 'main')) {
        return 'G7: nothing is pushed to main';
      }
    }
  }

  // G11: hook bypasses and hook-path overrides, for everyone.
  if (/--no-verify\b/.test(text)) return 'G11: --no-verify is denied for everyone';
  if (gitInvocations(text, 'commit').some(({ args }) => /(^|\s)-[a-zA-Z]*n[a-zA-Z]*(\s|$)/.test(args.replace(/(-m|-F)\s+("[^"]*"|'[^']*'|\S+)/g, '')))) return 'G11: git commit -n (no-verify) is denied';
  if (/\bgit\s+(?:-\S+\s+)*-c\s+core\.hooksPath/i.test(text)) return 'G11: git -c core.hooksPath is denied';
  if (/\bgit\b[^;&|\n]*\bconfig\b[^;&|\n]*core\.hooksPath/i.test(text)) return 'G11: changing core.hooksPath is denied';

  // G4 (Bash leg): a subagent may not change git config at all.
  if (subagent && gitInvocations(text, 'config').length) return 'G4: git config is denied for agents';

  const commits = gitInvocations(text, 'commit');
  // G8: from F0b (marker in HEAD), no commits on main.
  if (commits.length && root && currentBranch(root) === 'main' && mainLocked(root)) return 'G8: main is locked; commit on a feature branch';

  if (subagent) {
    if (gitInvocations(text, 'add').some(({ args }) => /(^|\s)(-A|--all|\.)(\s|$)/.test(args))) return 'G9: git add -A / git add . is denied for agents; add explicit paths';
    // G9: trailers Agent = agent_type, Plan and Node = the branch's.
    if (commits.length) {
      const where = root && planNodeFromBranch(currentBranch(root));
      if (!where) return 'G9: agents commit only on a node branch';
      const msg = commitMessageText(root, commits.map((c) => c.args).join('\n') + '\n' + text);
      const trailer = (k) => (new RegExp(`(?:^|\\\\n|[\\s"'])${k}:\\s*([\\w/-]+)`).exec(msg) ?? [])[1];
      if (trailer('Agent') !== role) return `G9: the commit needs the trailer "Agent: ${role}" (found ${trailer('Agent') ?? 'none'})`;
      if (trailer('Plan') !== where.plan) return `G9: the commit needs the trailer "Plan: ${where.plan}"`;
      if (trailer('Node') !== where.node) return `G9: the commit needs the trailer "Node: ${where.node}"`;
    }
  }

  // G14: the PR gate, for everyone, before G6's ask prompt.
  if (/\bgh\s+pr\s+create\b/.test(text)) return root ? prGate(root) : 'G14: cannot resolve the repository; denied';
  return null;
}

function main() {
  const input = readInput();
  if (input.tool_name !== 'Bash') return;
  const { subagent, role } = identity(input);
  const root = repoRoot(typeof input.cwd === 'string' ? input.cwd : process.cwd());
  const reason = check({ command: input.tool_input?.command, subagent, role, root });
  if (reason) deny(reason);
}

if (process.argv[1]?.replace(/\\/g, '/').endsWith('/guard-bash.mjs')) guarded('guard-bash', main);
