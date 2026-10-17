// SessionStart hook (context.spec C7). Prints where the work stands and tells the
// session to orient first. Node built-ins only; never writes; always exits 0.
import { readFileSync, readdirSync } from 'node:fs';
import { join, relative } from 'node:path';
import { execFileSync } from 'node:child_process';

const MAX_CHARS = 9000;

function git(args, cwd) {
  return execFileSync('git', args, { cwd, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trim();
}

function tryOr(fn, fallback = 'unknown') {
  try {
    const value = fn();
    return value === '' ? fallback : value;
  } catch {
    return fallback;
  }
}

function readInput() {
  try {
    const raw = readFileSync(0, 'utf8');
    const input = JSON.parse(raw);
    return typeof input === 'object' && input !== null ? input : {};
  } catch {
    return {};
  }
}

function frontmatterValue(text, key) {
  const match = text.match(/^---\r?\n([\s\S]*?)\r?\n---/);
  if (!match) return null;
  const line = match[1].split(/\r?\n/).find((l) => l.startsWith(`${key}:`));
  if (!line) return null;
  const value = line.slice(key.length + 1).trim().replace(/^["']|["']$/g, '');
  return value === '' || value === 'null' ? null : value;
}

function main() {
  const input = readInput();
  const source = typeof input.source === 'string' ? input.source : 'unknown';
  const cwd = typeof input.cwd === 'string' ? input.cwd : process.cwd();

  const root = tryOr(() => git(['rev-parse', '--show-toplevel'], cwd), null);
  const branch = root ? tryOr(() => git(['rev-parse', '--abbrev-ref', 'HEAD'], root)) : 'unknown';
  const dirty = root
    ? tryOr(() => String(git(['status', '--porcelain'], root).split('\n').filter(Boolean).length), '0')
    : 'unknown';

  let idle = '';
  if (typeof input.seconds_since_last_response === 'number') {
    idle = ` idle=${(input.seconds_since_last_response / 3600).toFixed(1)}h`;
  }

  let snapshot = 'unknown';
  let nextAction = 'unknown';
  if (root) {
    const dir = join(root, 'docs', 'context');
    const files = tryOr(() => readdirSync(dir).filter((f) => f.endsWith('.md')).sort(), []);
    if (files.length === 0) {
      snapshot = 'none';
      nextAction = 'none';
    } else {
      const latest = files[files.length - 1];
      const text = tryOr(() => readFileSync(join(dir, latest), 'utf8'), '');
      // Age comes from the snapshot's own timestamp; file mtimes change on checkout.
      const written = Date.parse(frontmatterValue(text, 'timestamp') ?? '');
      const ageHours = Number.isNaN(written) ? 'unknown' : ((Date.now() - written) / 3_600_000).toFixed(1);
      snapshot = `docs/context/${latest} (${ageHours}h old)`;
      nextAction = frontmatterValue(text, 'next-action') ?? 'none';
    }
  }

  const worktrees = root
    ? tryOr(() => {
        const paths = git(['worktree', 'list', '--porcelain'], root)
          .split('\n')
          .filter((l) => l.startsWith('worktree '))
          .map((l) => l.slice('worktree '.length))
          .map((p) => relative(root, p).replaceAll('\\', '/'))
          .filter((p) => p !== '');
        return paths.length ? paths.join(', ') : 'none';
      })
    : 'unknown';

  const plans = root
    ? tryOr(() => {
        const names = readdirSync(join(root, 'docs', 'plans', 'active')).filter((f) => f.endsWith('.plan.md'));
        return names.length ? names.join(', ') : 'none';
      }, 'none')
    : 'unknown';

  const instruction =
    source === 'compact'
      ? 'Run /load-context-chain and confirm with David before resuming; then /save-context.'
      : 'Run /orient before any work.';

  let body = [
    `[context] source=${source} branch=${branch} dirty=${dirty}${idle}`,
    `[context] latest snapshot: ${snapshot}`,
    `[context] next-action: ${nextAction}`,
    `[context] worktrees: ${worktrees}`,
    `[context] active plans: ${plans}`,
  ].join('\n');
  const room = MAX_CHARS - instruction.length - 1;
  if (body.length > room) body = body.slice(0, room);
  return `${body}\n${instruction}`;
}

let text;
try {
  text = main();
} catch {
  text = 'Run /orient before any work.';
}
process.stdout.write(
  JSON.stringify({ hookSpecificOutput: { hookEventName: 'SessionStart', additionalContext: text } }),
);
process.exitCode = 0;
