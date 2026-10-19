// PreToolUse guard for Bash (guardrails.spec G4 Bash leg, G5, G7, G8, G9, G11, G14). Checks the whole
// command text, so wrapped forms (`sh -c '…'`, a full path to git or gh) are caught too; it over-matches
// rather than under-matches. Fails closed: any internal error denies.
import { existsSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { execFileSync } from 'node:child_process';
import { guarded, deny, identity, readInput } from './lib/io.mjs';
import { BOOKKEEPING, codeFingerprint, currentBranch, git, isBookkeeping, planNodeFromBranch, repoRoot, traceDir } from './lib/trace.mjs';

// Rewrite `/usr/bin/git`, `C:\…\git.exe`, `"…/gh"` to plain `git` / `gh`.
export function normalise(command) {
  return String(command ?? '')
    .replace(/(["'])[^"']*[\\/](git|gh)(?:\.exe)?\1/g, '$2')
    .replace(/(?:[A-Za-z]:)?[^\s"';&|]*[\\/](git|gh)(?:\.exe)?(?=[\s"']|$)/g, '$1');
}

// Blank heredoc bodies and quoted strings (keeping the quotes), so a message's own `;`, newlines or words
// like `-warnaserror` can't be read as flags. Used where flags matter; content checks keep the full text.
export function withoutQuoted(text) {
  return text
    .replace(/<<-?\s*(['"]?)(\w+)\1[^\n]*\n[\s\S]*?\n\s*\2(?=\s|$)/g, '<<HEREDOC')
    .replace(/"(?:[^"\\]|\\[\s\S])*"/g, '""')
    .replace(/'[^']*'/g, "''");
}

function gitInvocations(text, sub) {
  return [...text.matchAll(new RegExp(`\\bgit\\b((?:\\s+-[cC]\\s+\\S+|\\s+-\\S+)*)\\s+${sub}\\b([^;&|\\n]*)`, 'g'))].map((m) => ({ pre: m[1], args: m[2] }));
}

// A config call that only reads core.hooksPath: --get / --get-all / --get-regexp, the `get` subcommand,
// or the bare key with no value and no write flag.
function isConfigRead(args) {
  const words = args.trim().split(/\s+/);
  if (words.some((w) => /^--(unset|unset-all|replace-all|add|edit|rename-section|remove-section)$/.test(w) || /^(set|unset|edit|rename-section|remove-section)$/.test(w))) return false;
  if (words.some((w) => /^--get(-all|-regexp)?$/.test(w)) || words[0] === 'get') return true;
  const key = words.findIndex((w) => /^core\.hooksPath$/i.test(w));
  return key !== -1 && key === words.length - 1;
}

function hasMarker(root, ref) {
  try {
    execFileSync('git', ['cat-file', '-e', `${ref}:.githooks/main-locked`], { cwd: root, stdio: 'ignore' });
    return true;
  } catch {
    return false;
  }
}

// G7: pushes are blocked once the remote's main carries the marker, so the marker commit itself can be
// pushed once. G8 (commits on main) reads the marker from HEAD.
function remoteMainLocked(root) {
  return hasMarker(root, 'refs/remotes/origin/main');
}

// G14 harness route: a harness/<slug> branch from the main session whose diff touches only the harness.
const HARNESS_PATH = /^(\.claude\/|\.githooks\/|CLAUDE\.md$)/;
function harnessPrGate(root, subagent) {
  if (subagent) return 'G14: a harness PR is the main session\'s, not an agent\'s';
  let files;
  try {
    files = git(['diff', '--name-only', 'origin/main...HEAD'], root).split('\n').filter(Boolean);
  } catch {
    return 'G14: cannot diff the harness branch against origin/main; denied';
  }
  if (!files.length) return 'G14: the harness branch has no changes against origin/main';
  const outside = files.filter((f) => !HARNESS_PATH.test(f) || f.startsWith('.claude/worktrees/'));
  return outside.length ? `G14: a harness PR may touch only .claude/, .githooks/ and CLAUDE.md (found ${outside.slice(0, 3).join(', ')})` : null;
}

// G14 design route (guardrails Amendment A2): a ui design stage (feature/ui/D<X>) has no reviewer or security;
// David judges it. Its PR needs his signoff(<sha>) in the stage's trail, with only the trail changed since,
// and a diff into origin/ui that touches only docs/ui/ and the stage's own trail.
function designPrGate(root, subagent, node) {
  if (subagent) return 'G14: a design-stage PR is the main session\'s, not an agent\'s';
  const trail = `docs/trace/ui/${node}/`;
  let files;
  try {
    files = git(['diff', '--name-only', 'origin/ui...HEAD'], root).split('\n').filter(Boolean);
  } catch {
    return 'G14: cannot diff the design branch against origin/ui; denied';
  }
  if (!files.length) return 'G14: the design branch has no changes against origin/ui';
  const outside = files.filter((f) => !f.startsWith('docs/ui/') && !f.startsWith(trail));
  if (outside.length) return `G14: a design-stage PR may touch only docs/ui/ and ${trail} (found ${outside.slice(0, 3).join(', ')})`;
  const loop = join(root, trail, 'loop.jsonl');
  const signoffs = existsSync(loop)
    ? readFileSync(loop, 'utf8').split('\n').map((l) => /"event":"signoff\(([0-9a-f]{40})\)"/.exec(l)?.[1]).filter(Boolean)
    : [];
  if (!signoffs.length) return `G14: no signoff(<sha>) by David in ${trail}loop.jsonl; no design PR before his sign-off`;
  const signed = signoffs.at(-1);
  try {
    execFileSync('git', ['merge-base', '--is-ancestor', signed, 'HEAD'], { cwd: root, stdio: 'ignore' });
    const since = git(['diff', '--name-only', signed, 'HEAD'], root).split('\n').filter(Boolean);
    if (since.some((f) => !f.startsWith(trail))) return `G14: HEAD changed more than ${trail} since the sign-off at ${signed.slice(0, 7)}`;
  } catch {
    return `G14: the signed-off commit ${signed.slice(0, 7)} is not an ancestor of HEAD`;
  }
  return null;
}

// G14: the latest reviewer and security verdicts must both be PASS and still cover HEAD (coversHead). Null = allow.
export function prGate(root, subagent = false) {
  const branch = currentBranch(root);
  if (/^harness\/[a-z0-9][a-z0-9-]*$/.test(branch)) return harnessPrGate(root, subagent);
  const design = /^feature\/ui\/(D[A-Z][A-Z0-9]*)$/.exec(branch);
  if (design) return designPrGate(root, subagent, design[1]);
  const where = planNodeFromBranch(branch);
  if (!where) return 'G14: gh pr create is only allowed from a node branch with verdicts, or a harness/<slug> branch';
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
    if (!coversHead(root, v.verdict, head)) {
      return `G14: the latest ${agent} verdict is for ${String(v.verdict?.head_sha).slice(0, 7)}, and HEAD ${head.slice(0, 7)} changed the code since (only ${BOOKKEEPING.join(', ')} may change after review)`;
    }
  }
  return null;
}

// A PASS still covers HEAD when it is HEAD; when every commit since it touches only bookkeeping paths; or,
// after a rebase, when the code diff against the merge base has the same patch-id as the reviewed one.
function coversHead(root, verdict, head) {
  const reviewed = verdict?.head_sha;
  if (typeof reviewed !== 'string' || !/^[0-9a-f]{40}$/.test(reviewed)) return false;
  if (reviewed === head) return true;
  try {
    execFileSync('git', ['merge-base', '--is-ancestor', reviewed, head], { cwd: root, stdio: 'ignore' });
    const changed = git(['diff', '--name-only', reviewed, head], root).split('\n').filter(Boolean);
    if (changed.every(isBookkeeping)) return true;
  } catch {
    // not an ancestor (a rebase), or the reviewed commit is gone: fall through to the fingerprint
  }
  try {
    const base = git(['merge-base', 'origin/main', head], root);
    return typeof verdict.diff_fingerprint === 'string' && verdict.diff_fingerprint !== '' && verdict.diff_fingerprint === codeFingerprint(root, base, head);
  } catch {
    return false;
  }
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
    if (root && remoteMainLocked(root)) {
      const refs = args.split(/\s+/).filter((a) => a && !a.startsWith('-'));
      if (refs.some((r) => /^(?:[^:]*:)?(?:refs\/heads\/)?main$/.test(r)) || (refs.length <= 1 && currentBranch(root) === 'main')) {
        return 'G7: nothing is pushed to main';
      }
    }
  }

  // G11: hook bypasses and hook-path overrides, for everyone.
  if (/--no-verify\b/.test(text)) return 'G11: --no-verify is denied for everyone';
  if (gitInvocations(withoutQuoted(text), 'commit').some(({ args }) => /(^|\s)-[a-zA-Z]*n[a-zA-Z]*(\s|$)/.test(args.replace(/(-m|-F)\s+\S+/g, '')))) return 'G11: git commit -n (no-verify) is denied';
  if (/\bgit\s+(?:-\S+\s+)*-c\s+core\.hooksPath/i.test(text)) return 'G11: git -c core.hooksPath is denied';
  if (gitInvocations(text, 'config').some(({ args }) => /core\.hooksPath/i.test(args) && !isConfigRead(args))) return 'G11: changing core.hooksPath is denied';

  // G4 (Bash leg): a subagent may not change git config at all.
  if (subagent && gitInvocations(text, 'config').length) return 'G4: git config is denied for agents';

  const commits = gitInvocations(text, 'commit');
  // G8: from F0b (marker in HEAD), no commits on main.
  if (commits.length && root && currentBranch(root) === 'main' && hasMarker(root, 'HEAD')) return 'G8: main is locked; commit on a feature branch';

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
      if (['tester', 'builder'].includes(role)) {
        if (!trailer('Rule')) return 'G9: the commit needs a Rule trailer (an id, or "none" on a node without rules)';
        // The QA script (subject docs(...)) has no TDD step; every other tester or builder commit has one.
        const qaScript = /(?:-m\s+["']|<<-?\s*['"]?\w+['"]?[^\n]*\n)docs\(/.test(text);
        if (!qaScript && !['scaffold', 'red', 'stub', 'green', 'refactor'].includes(trailer('TDD'))) return 'G9: the commit needs TDD: scaffold|red|stub|green|refactor';
      }
    }
  }

  // G14: the PR gate, for everyone, before G6's ask prompt.
  if (/\bgh\s+pr\s+create\b/.test(text)) return root ? prGate(root, subagent) : 'G14: cannot resolve the repository; denied';
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
