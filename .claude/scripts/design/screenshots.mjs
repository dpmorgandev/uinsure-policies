// Screenshot review (efficiency EFF-8): renders every design artifact of a stage at 1440 and 390 wide
// in light and dark, and writes one review page for David. Run by the stage skill in the parent; no
// agent sees or describes these pixels.
//   node .claude/scripts/design/screenshots.mjs <stage-dir, e.g. docs/ui/mockups>
// Needs `npm install` once in .claude/scripts/design (Playwright, pinned); Chromium comes from the
// machine's Playwright cache.
import { existsSync, mkdirSync, readdirSync, statSync, writeFileSync } from 'node:fs';
import { join, relative, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

const WIDTHS = [1440, 390];
const THEMES = ['light', 'dark'];

export function artifacts(stageDir) {
  return readdirSync(stageDir).filter((d) => !d.startsWith('_') && statSync(join(stageDir, d)).isDirectory())
    .flatMap((d) => readdirSync(join(stageDir, d)).filter((f) => f.endsWith('.html')).map((f) => ({ direction: d, screen: f.slice(0, -5), file: join(stageDir, d, f) })));
}

export function reviewPage(stageDir, shots) {
  const rows = shots.map((s) => `<section><h2>${s.direction} · ${s.screen}</h2><div class="row">${
    s.images.map((i) => `<figure><img src="${i.src}" alt="${s.direction} ${s.screen} at ${i.width}px, ${i.theme} theme" loading="lazy"><figcaption>${i.width}px · ${i.theme}</figcaption></figure>`).join('')
  }</div></section>`).join('\n');
  return `<!doctype html>
<html lang="en"><head><meta charset="utf-8"><title>Design review: ${relative(process.cwd(), stageDir)}</title>
<style>body{font:14px system-ui,sans-serif;margin:16px;background:#fff;color:#111}h2{font-size:16px;margin:24px 0 8px}
.row{display:flex;gap:12px;align-items:flex-start;overflow-x:auto}figure{margin:0}img{border:1px solid #ccc;max-height:900px;display:block}
figcaption{font-size:12px;color:#444;margin-top:4px}</style></head>
<body><h1>Design review</h1><p>Rendered by .claude/scripts/design/screenshots.mjs. Judge these pixels; nothing here was described by an agent.</p>
${rows}
</body></html>
`;
}

async function main(stageDir) {
  const { chromium } = await import('playwright');
  const out = join(stageDir, '_shots');
  mkdirSync(out, { recursive: true });
  const browser = await chromium.launch();
  const shots = [];
  try {
    for (const a of artifacts(stageDir)) {
      const images = [];
      for (const width of WIDTHS) for (const theme of THEMES) {
        const page = await browser.newPage({ viewport: { width, height: 900 } });
        const errors = [];
        page.on('pageerror', (e) => errors.push(e.message));
        page.on('requestfailed', (r) => errors.push(`request failed: ${r.url()}`));
        await page.goto(pathToFileURL(resolve(a.file)).href);
        await page.evaluate((t) => document.documentElement.setAttribute('data-theme', t), theme);
        const name = `${a.direction}-${a.screen}-${width}-${theme}.png`;
        await page.screenshot({ path: join(out, name), fullPage: true });
        if (errors.length) throw new Error(`${a.file} at ${width}/${theme}: ${errors.join('; ')}`);
        images.push({ src: `_shots/${name}`, width, theme });
        await page.close();
      }
      shots.push({ ...a, images });
    }
  } finally {
    await browser.close();
  }
  if (!shots.length) throw new Error(`no artifacts under ${stageDir} (fails closed)`);
  writeFileSync(join(stageDir, 'review.html'), reviewPage(stageDir, shots));
  process.stdout.write(`${shots.length} artifacts × ${WIDTHS.length * THEMES.length} renders → ${join(stageDir, 'review.html')}\n`);
}

if (process.argv[1] && resolve(process.argv[1]) === resolve(import.meta.filename)) {
  const stageDir = resolve(process.argv[2] ?? '');
  if (!existsSync(stageDir)) { process.stderr.write(`missing stage dir ${stageDir}\n`); process.exit(1); }
  main(stageDir).catch((e) => { process.stderr.write(`${e.message}\n`); process.exit(1); });
}
