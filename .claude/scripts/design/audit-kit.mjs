// The kit's coverage audit (design-system C3; efficiency EFF-2, EFF-11; guardrails A6). Serves the built
// kitchen sink (src/web/dist), reads every data-comp tag in a real browser, and fails unless every
// component in docs/ui/components.md appears in every state its row lists, every icon the screens need
// is catalogued, and the type specimen is present. Optionally screenshots the sink for David's G3: one
// shot per state per width (a full-page shot of the sink is too tall for Chromium to paint, and comes
// out mostly blank), plus each width's measured horizontal overflow.
// Counts omissions: it iterates the catalog, not the page. Empty input fails closed.
//   npm --prefix src/web run build && node .claude/scripts/design/audit-kit.mjs [--root <repo>] [--shots]
import { createServer } from 'node:http';
import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { extname, join, normalize, resolve } from 'node:path';
import { catalog } from './render-prompt.mjs';

// "a, b (x, y), c" → ["a", "b (x, y)", "c"]: commas inside brackets don't split.
export function splitStates(text) {
  const out = [];
  let depth = 0;
  let cur = '';
  for (const ch of String(text ?? '')) {
    if (ch === '(') depth++;
    if (ch === ')') depth = Math.max(0, depth - 1);
    if (ch === ',' && depth === 0) { out.push(cur.trim()); cur = ''; } else cur += ch;
  }
  if (cur.trim()) out.push(cur.trim());
  return out.filter(Boolean);
}

// id → { tag, states } from the catalog rows (the States column is third from the end).
export function roster(cat) {
  return [...cat].map(([id, c]) => {
    const cols = c.row.split('|').slice(1, -1).map((s) => s.trim());
    return { id, tag: c.tag, states: splitStates(cols[cols.length - 3]) };
  });
}

const norm = (s) => s.trim().toLowerCase().replace(/\s+/g, ' ');

export function kitProblems(expected, found) {
  if (!expected.length) return ['the catalog has no components (fails closed)'];
  if (!found.tags.length) return ['the sink has no data-comp tags (fails closed)'];
  const seen = new Set(found.tags.map(norm));
  const p = [];
  for (const { id, tag, states } of expected) {
    const missing = states.filter((s) => !seen.has(norm(`${id} · ${tag} · ${s}`)));
    if (missing.length === states.length) p.push(`${id} (${tag}): not in the sink`);
    else if (missing.length) p.push(`${id} (${tag}): missing states ${missing.map((s) => `"${s}"`).join(', ')}`);
  }
  const known = new Set(expected.flatMap(({ id, tag, states }) => states.map((s) => norm(`${id} · ${tag} · ${s}`))));
  for (const t of new Set(found.tags)) if (!known.has(norm(t))) p.push(`data-comp "${t}" matches no catalog row and state`);
  for (const i of found.iconsExpected) {
    if (found.iconsMissing.includes(i)) p.push(`icon ${i}: lucide-react has no such icon (catalogued as missing)`);
    else if (!found.icons.includes(i)) p.push(`icon ${i}: not in the icon catalogue`);
  }
  if (!found.specimens.includes('type')) p.push('no type specimen (data-specimen="type")');
  if (!found.specimens.includes('icons')) p.push('no icon catalogue (data-specimen="icons")');
  return p;
}

export const WIDTHS = [1440, 390];
// Chromium's paint limit: a shot taller than this can come back blank, so the review page says so.
export const MAX_SHOT_HEIGHT = 16384;

// "422 multi (no field path: listed, not placed)" → "422-multi-no-field-path-listed-not-placed"
export const slug = (s) => norm(s).replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '') || 'x';

const esc = (s) => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

// shots: [{ group, label, files: { [width]: { src, height } } }] in sink order, grouped by component or specimen.
// measures: [{ width, pageWidth, over: [{ id, right }] }]. Facts only: no judgement of the pixels.
export function reviewPage(shots, measures) {
  const facts = measures.map(({ width, pageWidth, over }) => `<li><b>${width} wide:</b> the page is ${pageWidth} wide${
    pageWidth > width ? ` (${pageWidth - width}px of horizontal overflow)` : ' (no horizontal overflow)'}${
    over.length ? `; content past the viewport in ${over.map((o) => `<a href="#${esc(o.id)}">${esc(o.id)}</a> (to ${o.right})`).join(', ')}` : ''}</li>`).join('\n');
  const groups = [...new Set(shots.map((s) => s.group))];
  const body = groups.map((g) => `<section id="${esc(g)}"><h2>${esc(g)}</h2>\n${shots.filter((s) => s.group === g).map((s) => `<h3>${esc(s.label)}</h3>\n<div class="pair">${
    WIDTHS.filter((w) => s.files[w]).map((w) => `<figure><figcaption>${w} wide${s.files[w].height > MAX_SHOT_HEIGHT ? `: ${s.files[w].height}px tall, past Chromium's paint limit, may be blank` : ''}</figcaption><img loading="lazy" src="${esc(s.files[w].src)}" alt="${esc(`${g}, ${s.label}, ${w} wide`)}"></figure>`).join('')}</div>`).join('\n')}</section>`).join('\n');
  return `<!doctype html>
<html lang="en"><head><meta charset="utf-8"><title>Kit review</title>
<style>body{font:14px system-ui,sans-serif;margin:16px;background:#fff;color:#111}h2{margin-top:48px;border-top:1px solid #ccc;padding-top:16px}h3{margin:24px 0 8px}.pair{display:flex;gap:16px;align-items:flex-start;flex-wrap:wrap}figure{margin:0;max-width:100%}figcaption{color:#555;margin-bottom:4px}img{border:1px solid #ccc;display:block;max-width:100%}nav a{margin-right:8px}</style></head>
<body><h1>Kit review: the kitchen sink</h1><p>Rendered by .claude/scripts/design/audit-kit.mjs, one shot per state at each width. Judge these pixels; nothing here was described by an agent.</p>
<h2>Measured</h2><ul>
${facts}
</ul>
<nav>${groups.map((g) => `<a href="#${esc(g)}">${esc(g)}</a>`).join(' ')}</nav>
${body}
</body></html>
`;
}

const TYPES = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.woff2': 'font/woff2', '.woff': 'font/woff', '.svg': 'image/svg+xml' };

function serve(dir) {
  const server = createServer((req, res) => {
    const path = normalize(join(dir, decodeURIComponent(new URL(req.url, 'http://x').pathname)));
    if (!path.startsWith(dir) || !existsSync(path)) { res.writeHead(404); res.end(); return; }
    res.writeHead(200, { 'content-type': TYPES[extname(path)] ?? 'application/octet-stream' });
    res.end(readFileSync(path));
  });
  return new Promise((ok) => server.listen(0, '127.0.0.1', () => ok(server)));
}

// Marks one element per shot (data-shot="n"): each state's two theme panes together (their nearest
// common ancestor), and each specimen. Returns the shots in page order, and what overflows the viewport.
function markShots() {
  const common = (a, b) => { for (let n = a; n; n = n.parentElement) if (n.contains(b)) return n; return document.body; };
  const byTag = new Map();
  for (const el of document.querySelectorAll('[data-comp]')) {
    const t = el.getAttribute('data-comp');
    byTag.set(t, byTag.has(t) ? common(byTag.get(t), el) : el);
  }
  const marks = [...byTag].map(([tag, el]) => {
    const [id, , state] = tag.split(' · ');
    return { el, group: id, label: state ?? tag };
  });
  for (const el of document.querySelectorAll('[data-specimen]')) marks.push({ el, group: el.id || `specimen-${el.getAttribute('data-specimen')}`, label: el.getAttribute('data-specimen') });
  marks.sort((a, b) => (a.el.compareDocumentPosition(b.el) & Node.DOCUMENT_POSITION_FOLLOWING ? -1 : 1));
  return marks.map((m, n) => { m.el.setAttribute('data-shot', String(n)); return { n, group: m.group, label: m.label }; });
}

function measure(width) {
  const over = new Map();
  for (const el of document.querySelectorAll('body *')) {
    const r = el.getBoundingClientRect();
    if (!r.width || r.right <= width + 1) continue;
    const id = el.closest('[data-comp]')?.getAttribute('data-comp')?.split(' · ')[0] ?? el.closest('section[id]')?.id.replace(/^comp-/, '') ?? 'page';
    over.set(id, Math.max(over.get(id) ?? 0, Math.round(r.right)));
  }
  return { width, pageWidth: document.documentElement.scrollWidth, over: [...over].map(([id, right]) => ({ id, right })) };
}

async function shoot(page, kitDir) {
  const out = join(kitDir, '_shots');
  rmSync(out, { recursive: true, force: true });
  const shots = [];
  const measures = [];
  for (const width of WIDTHS) {
    mkdirSync(join(out, String(width)), { recursive: true });
    await page.setViewportSize({ width, height: 900 });
    const marks = await page.evaluate(markShots);
    measures.push(await page.evaluate(measure, width));
    for (const m of marks) {
      const el = await page.$(`[data-shot="${m.n}"]`);
      const height = Math.round((await el.boundingBox()).height);
      const name = `${slug(m.group)}--${slug(m.label)}.png`;
      await el.screenshot({ path: join(out, String(width), name) });
      let s = shots.find((x) => x.group === m.group && x.label === m.label);
      if (!s) shots.push(s = { group: m.group, label: m.label, files: {} });
      s.files[width] = { src: `_shots/${width}/${name}`, height };
    }
  }
  writeFileSync(join(kitDir, 'review.html'), reviewPage(shots, measures));
}

async function main(root, shots) {
  const dist = resolve(root, 'src', 'web', 'dist');
  if (!existsSync(join(dist, 'kitchen-sink.html'))) throw new Error('no src/web/dist/kitchen-sink.html: run `npm --prefix src/web run build` first');
  const app = JSON.parse(readFileSync(join(root, 'docs', 'ui', 'screens.json'), 'utf8')).app;
  const iconsExpected = [...new Set([...(app.iconsVerified ?? []), ...(app.iconsToVerify ?? []), ...app.screens.flatMap((s) => s.icons ?? [])])].sort();
  const { chromium } = await import('playwright');
  const server = await serve(dist);
  const url = `http://127.0.0.1:${server.address().port}/kitchen-sink.html`;
  const browser = await chromium.launch();
  try {
    const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
    const errors = [];
    page.on('pageerror', (e) => errors.push(e.message));
    await page.goto(url, { waitUntil: 'networkidle' });
    if (errors.length) throw new Error(`the sink threw: ${errors.join('; ')}`);
    const found = await page.evaluate(() => ({
      tags: [...document.querySelectorAll('[data-comp]')].map((e) => e.getAttribute('data-comp')),
      icons: [...document.querySelectorAll('[data-icon]')].map((e) => e.getAttribute('data-icon')),
      iconsMissing: [...document.querySelectorAll('[data-icon-missing]')].map((e) => e.getAttribute('data-icon-missing')),
      specimens: [...document.querySelectorAll('[data-specimen]')].map((e) => e.getAttribute('data-specimen')),
    }));
    const problems = kitProblems(roster(catalog(root)), { ...found, iconsExpected });
    if (shots) await shoot(page, join(root, 'docs', 'ui', 'kit'));
    return problems;
  } finally {
    await browser.close();
    server.close();
  }
}

if (process.argv[1] && resolve(process.argv[1]) === resolve(import.meta.filename)) {
  const i = process.argv.indexOf('--root');
  main(resolve(i > 0 ? process.argv[i + 1] : process.cwd()), process.argv.includes('--shots'))
    .then((problems) => {
      for (const p of problems) process.stdout.write(`kit: ${p}\n`);
      process.stdout.write(problems.length ? `kit audit: ${problems.length} problem(s)\n` : 'kit audit: clean\n');
      process.exit(problems.length ? 1 : 0);
    })
    .catch((e) => { process.stderr.write(`kit audit: ${e.message}\n`); process.exit(1); });
}
