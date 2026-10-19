// Requirement coverage (ui-requirements.md stage 1; EFF-1, EFF-2): every requirement ID in the brief's §2
// tables must sit on a tagged region of some screen in the catalog, and every region's IDs must exist in
// the brief. Counts omissions: it iterates the brief's IDs, so a whole missing feature area fails. A cut
// row (priority "—") is exempt. A requirement that no region can show (the compiler's own rules, a
// quality across every screen) is exempt only when the catalog's `app.notOnScreens` names it with a
// reason and how it is verified instead. An empty brief or catalog fails closed.
//   node .claude/scripts/design/coverage.mjs [--root <repo>]   exit 1 on any problem
import { existsSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';

const ROW = /^\|\s*([A-Z][A-Z0-9]*-\d+[a-z]?)\s*\|\s*([^|]*?)\s*\|/;

// IDs from the requirement tables of §2 only (from "## 2." up to the next "## " heading).
export function briefIds(markdown) {
  const lines = String(markdown ?? '').replace(/\r\n/g, '\n').split('\n');
  const start = lines.findIndex((l) => /^## 2\./.test(l));
  if (start < 0) return null;
  const ids = new Map();
  for (const line of lines.slice(start + 1)) {
    if (/^## /.test(line)) break;
    const m = ROW.exec(line);
    if (m && m[1] !== 'ID') ids.set(m[1], m[2]);
  }
  return ids;
}

export function coverageProblems(briefMarkdown, catalog) {
  const ids = briefIds(briefMarkdown);
  if (!ids) return ['the brief has no "## 2." section, so nothing can be checked (fails closed)'];
  if (!ids.size) return ['the brief\'s §2 has no requirement rows (fails closed)'];
  const screens = catalog?.app?.screens ?? [];
  if (!screens.length) return ['the screen catalog has no screens (fails closed)'];
  const where = new Map();
  for (const s of screens) {
    for (const r of s.regions ?? []) for (const id of r.req ?? []) where.set(id, [...(where.get(id) ?? []), s.id]);
  }
  const problems = [];
  const exempt = new Map();
  for (const e of catalog?.app?.notOnScreens ?? []) {
    if (!ids.has(e?.id)) problems.push(`notOnScreens names ${e?.id}, which is not a requirement in the brief`);
    else if (!e.reason?.trim() || !e.verifiedBy?.trim()) problems.push(`notOnScreens ${e.id} needs both a reason and verifiedBy`);
    else if (where.has(e.id)) problems.push(`${e.id} is in notOnScreens but also tagged on ${where.get(e.id).join(', ')}; pick one`);
    else exempt.set(e.id, e);
  }
  for (const [id, priority] of ids) {
    if (priority === '—' || exempt.has(id)) continue;
    if (!where.has(id)) problems.push(`${id} (${priority}) is on no screen region`);
  }
  for (const [id, on] of where) {
    if (!ids.has(id)) problems.push(`${id} is tagged on ${on.join(', ')} but is not a requirement in the brief`);
    else if (ids.get(id) === '—') problems.push(`${id} is cut in the brief but still tagged on ${on.join(', ')}`);
  }
  return problems;
}

if (process.argv[1] && resolve(process.argv[1]) === resolve(import.meta.filename)) {
  const i = process.argv.indexOf('--root');
  const root = resolve(i > 0 ? process.argv[i + 1] : process.cwd());
  const brief = join(root, 'docs', 'ui-requirements.md');
  const catalog = join(root, 'docs', 'ui', 'screens.json');
  const problems = !existsSync(brief) ? [`missing ${brief}`]
    : !existsSync(catalog) ? [`missing ${catalog}`]
    : coverageProblems(readFileSync(brief, 'utf8'), JSON.parse(readFileSync(catalog, 'utf8')));
  for (const p of problems) process.stdout.write(`coverage: ${p}\n`);
  process.exit(problems.length ? 1 : 0);
}
