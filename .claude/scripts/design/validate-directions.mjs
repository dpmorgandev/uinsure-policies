// Validates docs/ui/directions.json (ui-research.spec.md, stage 2 contract; efficiency EFF-1, EFF-2), run by
// the /directions stage in the parent, never by the agent. Exactly three territories, each complete,
// pairwise distinct in archetype and tone, every citation resolving to a research entry, citing both
// halves, a palette that passes contrast in both themes, type from the researched pairings. Empty input
// fails closed.
//   node .claude/scripts/design/validate-directions.mjs [--root <repo>]   exit 1 on any problem
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';

export const COUNT = 3;
const TEXT_TOKENS = ['textPrimary', 'textSecondary', 'primary', 'error', 'success', 'warn'];
export const TOKENS = ['background', 'surface', 'border', 'accent', ...TEXT_TOKENS];
const STRINGS = ['name', 'governingIdea', 'singleMindedProposition', 'layoutArchetype', 'toneRegister', 'categoryDeparture'];
const DIALS = ['design_variance', 'motion_intensity', 'visual_density'];
const HEX = /^#[0-9a-fA-F]{6}$/;
const CITE = /\b[PCMTIKDBEA]\d{1,2}\b/g;

// Every entry ID that appears in the research files (P, C, M, T, I, K, D, B, E, A families).
export function researchIds(root) {
  const dir = join(root, 'docs', 'ui', 'research');
  if (!existsSync(dir)) return new Set();
  return new Set(readdirSync(dir).filter((f) => f.endsWith('.md'))
    .flatMap((f) => readFileSync(join(dir, f), 'utf8').match(CITE) ?? []));
}

function luminance(hex) {
  const [r, g, b] = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255)
    .map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

export function contrast(a, b) {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

const blank = (x) => typeof x !== 'string' || !x.trim();
const norm = (x) => String(x ?? '').trim().toLowerCase();

function paletteProblems(at, palette) {
  const p = [];
  if (!palette || typeof palette !== 'object') return [`${at}.derivedStyle.palette is required`];
  for (const t of TOKENS) {
    const v = palette[t];
    if (!v || !HEX.test(v.light ?? '') || !HEX.test(v.dark ?? '')) { p.push(`${at}.palette.${t} needs light and dark as #rrggbb`); continue; }
    if (blank(v.source)) p.push(`${at}.palette.${t}.source is required (a research ID, or "proposed")`);
  }
  if (p.length) return p;
  for (const t of TEXT_TOKENS) {
    for (const theme of ['light', 'dark']) {
      const ratio = contrast(palette[t][theme], palette.background[theme]);
      const floor = palette[t].graphicsOnly ? 3 : 4.5;
      if (ratio < floor) p.push(`${at}.palette.${t} ${theme} is ${ratio.toFixed(2)}:1 on background, below ${floor}:1${palette[t].graphicsOnly ? ' (graphics)' : ' (mark graphicsOnly to use it for glyphs and marks only, at 3:1)'}`);
    }
  }
  return p;
}

export function directionsProblems(doc, ids) {
  if (!doc || typeof doc !== 'object') return ['directions.json is not an object (fails closed)'];
  const p = [];
  if (doc.version !== 1) p.push('version must be 1');
  const ts = doc.territories;
  if (!Array.isArray(ts) || ts.length === 0) return [...p, 'territories is empty (fails closed)'];
  if (ts.length !== COUNT) p.push(`there must be exactly ${COUNT} territories, found ${ts.length}`);
  for (const [i, t] of ts.entries()) {
    const at = `territories[${i}]${t?.name ? ` (${t.name})` : ''}`;
    for (const k of STRINGS) if (blank(t?.[k])) p.push(`${at}.${k} is required`);
    if (!blank(t?.name) && !/^[a-z0-9]+(-[a-z0-9]+)*$/.test(t.name)) p.push(`${at}.name must be kebab-case`);
    if (!Array.isArray(t?.excludes) || t.excludes.filter((x) => !blank(x)).length < 3) p.push(`${at}.excludes needs at least 3 refusals`);
    for (const half of ['explorer', 'workbench']) if (blank(t?.signatureMove?.[half])) p.push(`${at}.signatureMove.${half} is required`);
    const ds = t?.derivedStyle;
    p.push(...paletteProblems(at, ds?.palette));
    for (const k of ['sans', 'mono']) if (!/^T[1-9]$/.test(ds?.type?.[k] ?? '') || !ids.has(ds.type[k])) p.push(`${at}.derivedStyle.type.${k} must be a researched T-id`);
    for (const k of DIALS) if (!Number.isInteger(ds?.dials?.[k]) || ds.dials[k] < 1 || ds.dials[k] > 10) p.push(`${at}.derivedStyle.dials.${k} must be an integer 1-10`);
    const cites = Array.isArray(t?.citations) ? t.citations : [];
    const unresolved = cites.filter((c) => !ids.has(c));
    if (unresolved.length) p.push(`${at}.citations don't resolve to a research entry: ${unresolved.join(', ')}`);
    if (new Set(cites).size < 5) p.push(`${at}.citations needs at least 5 distinct research IDs`);
    if (cites.filter((c) => /^B\d/.test(c)).length < 2) p.push(`${at}.citations needs at least 2 workbench entries (B)`);
    if (!cites.some((c) => /^[PC]\d/.test(c))) p.push(`${at}.citations needs at least 1 explorer prior-art entry (P or C)`);
  }
  for (const k of ['name', 'layoutArchetype', 'toneRegister']) {
    const seen = new Map();
    for (const t of ts) {
      const v = norm(t?.[k]);
      if (!v) continue;
      if (seen.has(v)) p.push(`${k} "${t[k]}" is shared by two territories; they must diverge`);
      seen.set(v, true);
    }
  }
  return p;
}

if (process.argv[1] && resolve(process.argv[1]) === resolve(import.meta.filename)) {
  const i = process.argv.indexOf('--root');
  const root = resolve(i > 0 ? process.argv[i + 1] : process.cwd());
  const file = join(root, 'docs', 'ui', 'directions.json');
  let problems;
  if (!existsSync(file)) problems = [`missing ${file}`];
  else {
    try {
      problems = directionsProblems(JSON.parse(readFileSync(file, 'utf8')), researchIds(root));
    } catch (err) {
      problems = [`directions.json is not valid JSON (${err.message})`];
    }
  }
  for (const p of problems) process.stdout.write(`directions: ${p}\n`);
  process.exit(problems.length ? 1 : 0);
}
