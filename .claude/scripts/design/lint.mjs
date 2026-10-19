// Design lint (efficiency EFF-12, EFF-2): mechanical checks on one static design artifact, run by the
// stage skill in the parent after each dispatch. Parity counts omissions: it iterates the regions the
// screen catalog expects, so a missing region fails.
//   node .claude/scripts/design/lint.mjs <file.html> <screen-id> [--root <repo>]   exit 1 on any problem
import { existsSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';

const TOKEN_BLOCK = /(?::root|\[data-theme=["']?(?:light|dark)["']?\])\s*\{[^}]*\}/g;
const COLOUR = /#[0-9a-fA-F]{3,8}\b|\brgba?\(|\bhsla?\(/;

export function lintDesign(html, entry) {
  const problems = [];
  const text = String(html ?? '');
  if (!/^\s*<!doctype html>/i.test(text)) problems.push('the file must start with <!doctype html>');
  if (/```/.test(text)) problems.push('a markdown fence is in the file (prose or commentary leaked into the artifact)');
  if (/<script\b/i.test(text)) problems.push('a <script> element is present; design artifacts are static');
  for (const [what, re] of [
    ['an external src', /\ssrc\s*=\s*["']?\s*(?:https?:)?\/\//i],
    ['an external <link href>', /<link\b[^>]*\bhref\s*=\s*["']?\s*(?:https?:)?\/\//i],
    ['an external url()', /url\(\s*["']?\s*(?:https?:)?\/\//i],
    ['an @import', /@import\b/i],
  ]) if (re.test(text)) problems.push(`${what} is present; nothing may load from outside the file`);
  if (!/<html\b[^>]*\bdata-theme\s*=\s*["']?(light|dark)/i.test(text)) problems.push('<html> must carry data-theme="light" or "dark"');
  if (!/\[data-theme=["']?dark["']?\]\s*\{/.test(text)) problems.push('no [data-theme="dark"] token block: the dark theme has no tokens');
  const withoutTokens = text.replace(TOKEN_BLOCK, '');
  const stray = COLOUR.exec(withoutTokens);
  if (stray) problems.push(`a colour outside the token blocks: "${withoutTokens.slice(stray.index, stray.index + 24)}"`);
  if (entry) {
    if (!new RegExp(`<body\\b[^>]*\\bdata-screen-id\\s*=\\s*["']${entry.id}["']`, 'i').test(text)) problems.push(`<body> must carry data-screen-id="${entry.id}"`);
    const tagged = new Set([...text.matchAll(/\bdata-req\s*=\s*["']([^"']*)["']/gi)].flatMap((m) => m[1].split(/\s+/).filter(Boolean)));
    for (const region of entry.regions ?? []) {
      const missing = region.req.filter((id) => !tagged.has(id));
      if (missing.length) problems.push(`region "${region.name}" is missing: no data-req carries ${missing.join(', ')}`);
    }
    if (!entry.regions?.length) problems.push(`screen ${entry.id} has no regions in the catalog, so nothing can be checked (fails closed)`);
  }
  return problems;
}

export function screenEntry(root, id) {
  const file = join(root, 'docs', 'ui', 'screens.json');
  if (!existsSync(file)) throw new Error(`missing ${file}`);
  const entry = JSON.parse(readFileSync(file, 'utf8')).app.screens.find((s) => s.id === id);
  if (!entry) throw new Error(`screen ${id} is not in docs/ui/screens.json`);
  return entry;
}

if (process.argv[1] && resolve(process.argv[1]) === resolve(import.meta.filename)) {
  const [file, id] = process.argv.slice(2);
  const i = process.argv.indexOf('--root');
  const root = resolve(i > 0 ? process.argv[i + 1] : process.cwd());
  const problems = existsSync(file) ? lintDesign(readFileSync(file, 'utf8'), screenEntry(root, id)) : [`missing file ${file}`];
  for (const p of problems) process.stdout.write(`${file}: ${p}\n`);
  process.exit(problems.length ? 1 : 0);
}
