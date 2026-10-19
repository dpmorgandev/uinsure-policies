// The kit's coverage audit (design-system C3; efficiency EFF-2, EFF-11; guardrails A6). Serves the built
// kitchen sink (src/web/dist), reads every data-comp tag in a real browser, and fails unless every
// component in docs/ui/components.md appears in every state its row lists, every icon the screens need
// is catalogued, and the type specimen is present. Optionally screenshots the sink for David's G3.
// Counts omissions: it iterates the catalog, not the page. Empty input fails closed.
//   npm --prefix src/web run build && node .claude/scripts/design/audit-kit.mjs [--root <repo>] [--shots]
import { createServer } from 'node:http';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
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
    if (shots) {
      const out = join(root, 'docs', 'ui', 'kit', '_shots');
      mkdirSync(out, { recursive: true });
      for (const width of [1440, 390]) {
        await page.setViewportSize({ width, height: 900 });
        await page.screenshot({ path: join(out, `kitchen-sink-${width}.png`), fullPage: true });
      }
      writeFileSync(join(root, 'docs', 'ui', 'kit', 'review.html'), `<!doctype html>
<html lang="en"><head><meta charset="utf-8"><title>Kit review</title>
<style>body{font:14px system-ui,sans-serif;margin:16px;background:#fff;color:#111}img{border:1px solid #ccc;display:block;max-width:100%}</style></head>
<body><h1>Kit review: the kitchen sink</h1><p>Rendered by .claude/scripts/design/audit-kit.mjs. Judge these pixels; nothing here was described by an agent.</p>
<h2>1440 wide</h2><img src="_shots/kitchen-sink-1440.png" alt="kitchen sink at 1440px">
<h2>390 wide</h2><img src="_shots/kitchen-sink-390.png" alt="kitchen sink at 390px">
</body></html>
`);
    }
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
