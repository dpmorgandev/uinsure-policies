// Path rules (guardrails.spec R5, G1, G4): resolve every path relative to its worktree root, then match.
// Harness protection names files, never `.claude/**`, so worktrees under .claude/worktrees/ stay writable.
import { isAbsolute, relative, resolve } from 'node:path';

// Repo-relative with forward slashes, with any `.claude/worktrees/<name>/` prefix stripped;
// null when the path is outside the repo (a subagent never legitimately writes there).
export function toRepoRel(root, p) {
  if (typeof p !== 'string' || p === '') return null;
  const abs = isAbsolute(p) ? resolve(p) : resolve(root, p);
  let rel = relative(root, abs).split('\\').join('/');
  if (rel.startsWith('..') || isAbsolute(rel)) return null;
  const wt = /^\.claude\/worktrees\/[^/]+\/(.*)$/.exec(rel);
  if (wt) rel = wt[1];
  return rel;
}

// Minimal glob: `**` any depth, `*` within one segment.
export function globToRegExp(glob) {
  let re = '';
  for (let i = 0; i < glob.length; i++) {
    const c = glob[i];
    if (c === '*' && glob[i + 1] === '*') {
      re += '.*';
      i++;
      if (glob[i + 1] === '/') i++;
    } else if (c === '*') re += '[^/]*';
    else re += c.replace(/[.+?^${}()|[\]\\]/g, '\\$&');
  }
  return new RegExp(`^${re}$`);
}

export function matchesAny(rel, globs) {
  return globs.some((g) => globToRegExp(g).test(rel));
}

export const HARNESS = [
  '.claude/agents/**', '.claude/skills/**', '.claude/hooks/**', '.claude/specs/**', '.claude/workflows/**',
  '.claude/settings*.json', '.githooks/**', '.git', '.git/**', 'CLAUDE.md',
];

export const TRACE = ['docs/trace/**'];

export const SPECS = ['.claude/specs/**'];

export function isHarness(rel) {
  return matchesAny(rel, HARNESS);
}

export function isSpecPath(text) {
  return /(^|[\s"'=/\\])\.claude[/\\]specs([/\\]|\b)/.test(text ?? '');
}
