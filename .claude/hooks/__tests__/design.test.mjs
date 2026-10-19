// Fixtures for the UI design slice (guardrails Amendment A1: G1 extended, G15, G16, G17; efficiency
// EFF-1, EFF-2, EFF-12). Every check has a known-bad fixture it must fail on, and the two new hooks have a
// no-op fixture per build role (merge-safety rule 2). Run: node --test ".claude/hooks/__tests__/*.test.mjs"
import './clean-git-env.mjs';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, rmSync, readFileSync, readdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { execFileSync, spawnSync } from 'node:child_process';
import { checkDispatch } from '../guard-dispatch.mjs';
import { designProblems } from '../gate-design.mjs';
import { allowlist, isKnownRole } from '../lib/roles.mjs';
import { matchesAny } from '../lib/paths.mjs';
import { skillProblems } from '../lib/skills-lint.mjs';
import { renderPrompt, parseHeader } from '../../scripts/design/render-prompt.mjs';
import { lintDesign } from '../../scripts/design/lint.mjs';
import { coverageProblems } from '../../scripts/design/coverage.mjs';
import { directionsProblems, contrast, TOKENS } from '../../scripts/design/validate-directions.mjs';

const HOOKS = resolve(import.meta.dirname, '..');
const ROOT = resolve(HOOKS, '..', '..');
const BUILD_ROLES = ['tester', 'builder', 'reviewer', 'security', 'investigator', 'git'];

const SCREEN = {
  id: 'overview', components: ['status-tag'],
  regions: [{ name: 'headline answer', req: ['NAV-1'] }, { name: 'stamp', req: ['DATA-3'] }],
};

// A design repo in a temp dir: the catalogs the renderer reads, and one trail file it excerpts.
function designRepo() {
  const root = resolve(mkdtempSync(join(tmpdir(), 'design-fixture-')));
  execFileSync('git', ['init', '-q', '-b', 'feature/ui/DM'], { cwd: root });
  mkdirSync(join(root, 'docs', 'ui'), { recursive: true });
  mkdirSync(join(root, 'docs', 'trace'), { recursive: true });
  writeFileSync(join(root, 'docs', 'ui', 'styles.md'), '# Style options\n\n## Shared constraints (all three directions)\n\nWords and glyphs.\n\n## Style 0: Case file\n\nA document.\n\n## Style 1: Instrument\n\nA console.\n\n## Style 2: Chronology\n\nA time axis.\n');
  writeFileSync(join(root, 'docs', 'ui', 'screens.json'), JSON.stringify({ app: { screens: [SCREEN, { ...SCREEN, id: 'rule' }] } }));
  writeFileSync(join(root, 'docs', 'ui', 'components.md'), '| status-tag | Pass or fail | all | 10 | M1 |\n');
  writeFileSync(join(root, 'docs', 'trace', 'requirements-matrix.md'), '| rule | plan/node |\n| GET-1 | build/F2 |\n');
  return root;
}

function withDesignRepo(fn) {
  const root = designRepo();
  try {
    return fn(root);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}

// The mockups inputs (round 2): territories from docs/ui/directions.json, screens rule and wb-cancel.
const MOCKUPS = JSON.parse(readFileSync(join(ROOT, '.claude', 'skills', 'mockups', 'inputs.json'), 'utf8'));
const TERRITORY = 'idea-one';

// The fixture only needs the files the rule screen excerpts, and one territory; point every excerpt at
// lines its pattern matches.
function renderFixture(root, attempt = 1) {
  for (const e of MOCKUPS.screens.rule.excerpts) {
    mkdirSync(join(root, e.file, '..'), { recursive: true });
    writeFileSync(join(root, e.file), '| rule | plan/node |\n| GET-1 | build/F2 |\n  - id: F2\n# heading\n| — | Status |\n"event":"pause1"\n"event":"gate.red"\nRule IDs: GET-1\n| 1 | GET-1 | x |\n| 5 | 94a7473 | red | tests |\n');
  }
  writeFileSync(join(root, MOCKUPS.directionsFrom), JSON.stringify({ version: 1, territories: [{ name: TERRITORY, governingIdea: 'order is the proof', signatureMove: { explorer: 'a spine', workbench: 'three rungs' }, derivedStyle: { type: { sans: 'T3', mono: 'T3' } } }] }));
  mkdirSync(join(root, 'docs', 'ui', 'research'), { recursive: true });
  writeFileSync(join(root, 'docs', 'ui', 'research', 'mood-type-icons.md'), '| ID | Pairing |\n|---|---|\n| T3 | Source Sans 3 + Source Code Pro | OFL-1.1 |\n');
  return renderPrompt({ root, stage: 'mockups', direction: TERRITORY, screen: 'rule', attempt });
}

const dispatch = (root, subagent_type, prompt) => checkDispatch({ cwd: root, tool_input: { subagent_type, prompt } });

const GOOD_HTML = `<!doctype html>
<html data-theme="light"><head><style>
:root { --ink: #0b0c0c; --paper: #f7f5f0; }
[data-theme="dark"] { --ink: #f3f3f3; --paper: #161616; }
body { color: var(--ink); background: var(--paper); }
</style></head>
<body data-screen-id="overview"><main><section data-req="NAV-1"><h1>8 of 29 rules built</h1></section><footer data-req="DATA-3">ad7b6b0</footer></main></body></html>
`;

// ---------- G1 extended ----------

test('G1: ui-designer is a known role that may write only docs/ui/{mockups,kit,screens}', () => {
  assert.ok(isKnownRole('ui-designer'));
  const may = (p) => matchesAny(p, allowlist('ui-designer'));
  assert.ok(may('docs/ui/mockups/case-file/overview.html'));
  assert.ok(may('docs/ui/kit/tokens.css'));
  assert.ok(may('docs/ui/screens/rule.html'));
  for (const p of ['docs/ui/styles.md', 'docs/trace/ui/DM/loop.jsonl', 'src/web/App.tsx', '.claude/skills/mockups/template.md']) assert.ok(!may(p), p);
  for (const role of BUILD_ROLES) assert.ok(!matchesAny('docs/ui/mockups/x.html', allowlist(role)), `${role} must not gain design paths`);
});

// ---------- G15: dispatch guard ----------

test('G15: every build-loop dispatch passes untouched (no-op for the six roles and unknown input)', () => withDesignRepo((root) => {
  for (const role of BUILD_ROLES) assert.equal(dispatch(root, role, 'anything at all'), null, role);
  assert.equal(checkDispatch({}), null);
  for (const role of [...BUILD_ROLES, 'Explore']) {
    const r = spawnSync('node', [join(HOOKS, 'guard-dispatch.mjs')], { input: JSON.stringify({ cwd: root, tool_input: { subagent_type: role, prompt: 'x' } }), encoding: 'utf8' });
    assert.equal(r.status, 0, `${role}: ${r.stderr}`);
  }
  const garbage = spawnSync('node', [join(HOOKS, 'guard-dispatch.mjs')], { input: 'not json', encoding: 'utf8' });
  assert.equal(garbage.status, 0, 'unparseable input that names no design agent must not block the loop');
}));

test('G15: a rendered ui-designer prompt passes; one changed byte, a missing header or a deleted mandate is denied', () => withDesignRepo((root) => {
  const prompt = renderFixture(root);
  assert.equal(dispatch(root, 'ui-designer', prompt), null);
  assert.equal(dispatch(root, 'ui-designer', prompt.replace(/\n/g, '\r\n')), null, 'line endings are normalised');
  assert.match(dispatch(root, 'ui-designer', prompt.replace('Only observed data', 'Mostly observed data')), /^G15: .*differs/);
  assert.match(dispatch(root, 'ui-designer', prompt.split('\n').slice(1).join('\n')), /^G15: .*header is missing/);
  assert.match(dispatch(root, 'ui-designer', prompt.replace(/^6\. \*\*Status is a word.*\n/m, '')), /^G15: .*differs/);
  assert.match(dispatch(root, 'ui-designer', 'Write a nice overview page please.'), /^G15/);
  const hook = spawnSync('node', [join(HOOKS, 'guard-dispatch.mjs')], { input: JSON.stringify({ cwd: root, tool_input: { subagent_type: 'ui-designer', prompt: 'hand-written' } }), encoding: 'utf8' });
  assert.equal(hook.status, 2);
}));

test('G15: attempt 2 carries the trail feedback file verbatim; without it the render fails closed', () => withDesignRepo((root) => {
  assert.throws(() => renderFixture(root, 2), /missing source/);
  mkdirSync(join(root, 'docs', 'trace', 'ui', MOCKUPS.node, 'feedback'), { recursive: true });
  writeFileSync(join(root, 'docs', 'trace', 'ui', MOCKUPS.node, 'feedback', `${TERRITORY}-rule-2.txt`), 'region "stamp" is missing: no data-req carries DATA-3\n');
  const prompt = renderFixture(root, 2);
  assert.equal(parseHeader(prompt).attempt, 2);
  assert.match(prompt, /Attempt 1 failed these checks[\s\S]*no data-req carries DATA-3/);
  assert.equal(dispatch(root, 'ui-designer', prompt), null);
}));

test('round 2: a mockups prompt carries its territory verbatim from directions.json; an unknown one fails closed', () => withDesignRepo((root) => {
  const prompt = renderFixture(root);
  assert.match(prompt, /## Territory: idea-one[\s\S]*"governingIdea": "order is the proof"/);
  assert.match(prompt, /`docs\/ui\/mockups\/r2\/idea-one\/rule\.html`/);
  assert.match(prompt, /### Its type, from `docs\/ui\/research\/mood-type-icons\.md`, verbatim[\s\S]*\| T3 \| Source Sans 3 \+ Source Code Pro \|/, 'A5: the T-id resolves to its research row');
  assert.throws(() => renderPrompt({ root, stage: 'mockups', direction: 'case-file', screen: 'rule' }), /not a territory/);
  writeFileSync(join(root, 'docs', 'ui', 'research', 'mood-type-icons.md'), '| T1 | IBM Plex |\n');
  assert.throws(() => renderPrompt({ root, stage: 'mockups', direction: TERRITORY, screen: 'rule' }), /type T3 is not in/);
}));

// ---------- G16: receipt and artifact gate ----------

function receipt(extra = {}) {
  return JSON.stringify({ agent: 'ui-designer', stage: 'mockups', direction: 'case-file', screen: 'overview', status: 'written', files: ['docs/ui/mockups/case-file/overview.html'], notes: 'question: was it test-first', ...extra });
}

function withArtifact(html, fn) {
  return withDesignRepo((root) => {
    mkdirSync(join(root, 'docs', 'ui', 'mockups', 'case-file'), { recursive: true });
    writeFileSync(join(root, 'docs', 'ui', 'mockups', 'case-file', 'overview.html'), html);
    return fn(root);
  });
}

test('G16: a valid receipt with a clean artifact passes', () => withArtifact(GOOD_HTML, (root) => {
  assert.deepEqual(designProblems(root, receipt()), []);
}));

test('G16: prose around the JSON, HTML in the message, a fenced artifact, a missing file or empty files block', () => withArtifact(GOOD_HTML, (root) => {
  assert.match(designProblems(root, `Here is the page!\n${receipt()}`)[0], /exactly one JSON object/);
  assert.match(designProblems(root, GOOD_HTML)[0], /exactly one JSON object/);
  assert.ok(designProblems(root, receipt({ files: [] })).some((p) => /empty is a FAIL/.test(p)));
  assert.ok(designProblems(root, receipt({ files: ['docs/ui/mockups/case-file/missing.html'] })).some((p) => /missing or empty/.test(p)));
  assert.ok(designProblems(root, receipt({ files: ['docs/ui/styles.md'] })).some((p) => /outside the ui-designer's write scope/.test(p)));
  assert.ok(designProblems(root, receipt({ extra: 1 })).some((p) => /not a receipt field/.test(p)));
  assert.ok(designProblems(root, receipt({ notes: 'x'.repeat(601) })).some((p) => /notes/.test(p)));
  writeFileSync(join(root, 'docs', 'ui', 'mockups', 'case-file', 'overview.html'), "Sure, here's the mockup:\n```html\n" + GOOD_HTML + '```\n');
  const p = designProblems(root, receipt());
  assert.ok(p.some((x) => /<!doctype html>/.test(x)) && p.some((x) => /markdown fence/.test(x)), p.join(' | '));
}));

test('G16: the hook is a no-op for the six build roles, blocks a bad ui-designer stop, passes a good one', () => withArtifact(GOOD_HTML, (root) => {
  const run = (agent_type, message) => spawnSync('node', [join(HOOKS, 'gate-design.mjs')], { input: JSON.stringify({ cwd: root, agent_id: 'a1', agent_type, last_assistant_message: message }), encoding: 'utf8' });
  for (const role of BUILD_ROLES) assert.equal(run(role, 'prose, not JSON').status, 0, role);
  assert.equal(run('ui-designer', receipt()).status, 0);
  const bad = run('ui-designer', 'I wrote a lovely page.');
  assert.equal(bad.status, 2);
  assert.match(bad.stderr, /Restate your COMPLETE final message/);
}));

// ---------- EFF-12 / EFF-2: design lint ----------

test('G16 lint companion (EFF-12, EFF-2): a clean artifact passes; each mechanical fault fails', () => {
  assert.deepEqual(lintDesign(GOOD_HTML, SCREEN), []);
  const bad = (html, re) => assert.ok(lintDesign(html, SCREEN).some((p) => re.test(p)), `${re} not reported`);
  bad(GOOD_HTML.replace('<!doctype html>\n', ''), /<!doctype html>/);
  bad(GOOD_HTML.replace('</main>', '<script>alert(1)</script></main>'), /<script>/);
  bad(GOOD_HTML.replace('</head>', '<link rel="stylesheet" href="https://fonts.example/x.css"></head>'), /external <link href>/);
  bad(GOOD_HTML.replace('</main>', '<img src="//cdn.example/a.png" alt=""></main>'), /external src/);
  bad(GOOD_HTML.replace('body {', 'h1 { color: #ff0000; }\nbody {'), /colour outside the token blocks/);
  bad(GOOD_HTML.replace('[data-theme="dark"] { --ink: #f3f3f3; --paper: #161616; }\n', ''), /dark theme has no tokens/);
  bad(GOOD_HTML.replace(' data-req="DATA-3"', ''), /region "stamp" is missing/);
  bad(GOOD_HTML.replace(' data-screen-id="overview"', ''), /data-screen-id/);
  bad(GOOD_HTML.replace('</main>', '```\n</main>'), /markdown fence/);
  assert.ok(lintDesign(GOOD_HTML, { id: 'overview', regions: [] }).some((p) => /fails closed/.test(p)));
});

// ---------- requirement coverage (ui-requirements stage 1) ----------

const BRIEF = '# B\n## 2. Functional\n| ID | Priority | R | I |\n|---|---|---|---|\n| NAV-1 | MUST | a | b |\n'
  + '| WB-1 | MUST | c | d |\n| WB-8 | — | cut | e |\n| PERF-1 | SHOULD | f | g |\n## 3. Delivery\n| X-1 | MUST | not §2 | h |\n';
const catalog = (screens, notOnScreens) => ({ app: { screens, notOnScreens } });
const on = (id, ...req) => ({ id, regions: [{ name: 'r', req }] });
const PERF = { id: 'PERF-1', reason: 'load time, not a region', verifiedBy: 'web-testing perf budget' };

test('coverage: every §2 ID on a region or declared notOnScreens passes; a missing feature area fails', () => {
  assert.deepEqual(coverageProblems(BRIEF, catalog([on('overview', 'NAV-1'), on('sell', 'WB-1')], [PERF])), []);
  const missing = coverageProblems(BRIEF, catalog([on('overview', 'NAV-1')], [PERF]));
  assert.ok(missing.some((p) => /WB-1 \(MUST\) is on no screen region/.test(p)));
  assert.ok(coverageProblems(BRIEF, catalog([on('overview', 'NAV-1'), on('sell', 'WB-1')])).some((p) => /PERF-1/.test(p)));
});

test('coverage: unknown, cut or doubly-placed IDs, a reasonless exemption, and empty inputs fail', () => {
  const ok = [on('overview', 'NAV-1'), on('sell', 'WB-1')];
  const has = (c, re) => assert.ok(coverageProblems(BRIEF, c).some((p) => re.test(p)), `${re} not reported`);
  has(catalog([...ok, on('x', 'X-1')], [PERF]), /X-1 is tagged .* not a requirement/);
  has(catalog([...ok, on('x', 'WB-8')], [PERF]), /WB-8 is cut/);
  has(catalog([...ok, on('x', 'PERF-1')], [PERF]), /PERF-1 is in notOnScreens but also tagged/);
  has(catalog(ok, [{ id: 'PERF-1', reason: 'x' }]), /needs both a reason and verifiedBy/);
  has(catalog(ok, [PERF, { id: 'NOPE-1', reason: 'r', verifiedBy: 'v' }]), /NOPE-1, which is not a requirement/);
  has(catalog([], [PERF]), /no screens \(fails closed\)/);
  assert.ok(coverageProblems('# no section', catalog(ok)).some((p) => /fails closed/.test(p)));
});

// ---------- G17: skills lint ----------

const SKILL = (body, fm = 'name: x\ndescription: y\nported-from: elsewhere v1') => `---\n${fm}\n---\n# X\n\n${body}`;

test('G17: a clean ported skill passes; a foreign path, an undeclared missing path or no Reads / Writes fails', () => {
  const ok = SKILL('## Reads / Writes\n\n- **Reads:** `docs/ui/styles.md`\n- **Writes:** `docs/ui/mockups/**`\n\nWrite `docs/ui/mockups/a/b.html` from `docs/ui/styles.md`.\n');
  assert.deepEqual(skillProblems(ok, ROOT), []);
  assert.ok(skillProblems(SKILL('## Reads / Writes\n\n- **Reads:** `docs/ui/styles.md`\n\nRead projects/x/docs/analysis/shared/styles.md.\n'), ROOT).some((p) => /foreign reference/.test(p)));
  assert.ok(skillProblems(SKILL('## Reads / Writes\n\n- **Reads:** `docs/ui/styles.md`\n\nAs agentflow did.\n'), ROOT).some((p) => /foreign reference/.test(p)));
  assert.ok(skillProblems(SKILL('## Reads / Writes\n\n- **Reads:** `docs/ui/styles.md`\n\nThen open `docs/analysis/screens.json`.\n'), ROOT).some((p) => /isn't under a path/.test(p)));
  assert.match(skillProblems(SKILL('Just do it.\n'), ROOT)[0], /Reads \/ Writes/);
  assert.deepEqual(skillProblems(SKILL('anything, agentflow included', 'name: native\ndescription: y'), ROOT), [], 'native skills are out of scope');
});

test('G17: every ported skill in this repo passes the lint', () => {
  for (const s of readdirSync(join(ROOT, '.claude', 'skills'))) {
    let text;
    try { text = readFileSync(join(ROOT, '.claude', 'skills', s, 'SKILL.md'), 'utf8'); } catch { continue; }
    assert.deepEqual(skillProblems(text, ROOT), [], s);
  }
});

// ---------- G13 (vi): every hook and design script loads ----------

function loads(file) {
  const url = new URL(`file:///${file.replace(/\\/g, '/').replace(/^\//, '')}`).href;
  return spawnSync('node', ['--input-type=module', '-e', `import(${JSON.stringify(url)}).catch((e) => { console.error('LOADFAIL ' + e.message); process.exit(9); });`],
    { cwd: ROOT, input: '', encoding: 'utf8', timeout: 20000 });
}

test('G13 (vi): every hook, lib and design script module loads; settings.json parses', () => {
  const dirs = [join(HOOKS), join(HOOKS, 'lib'), join(ROOT, '.claude', 'scripts', 'design')];
  const files = dirs.flatMap((d) => readdirSync(d).filter((f) => f.endsWith('.mjs')).map((f) => join(d, f)));
  assert.ok(files.length >= 15, `only ${files.length} modules found`);
  for (const f of files) {
    const r = loads(f);
    assert.ok(r.status !== 9 && !/LOADFAIL/.test(r.stderr), `${f} failed to load: ${r.stderr}`);
  }
  JSON.parse(readFileSync(join(ROOT, '.claude', 'settings.json'), 'utf8'));
});

test('G13 (vi): the load check fails on a module with a syntax error (known-bad fixture)', () => {
  const dir = mkdtempSync(join(tmpdir(), 'load-'));
  try {
    writeFileSync(join(dir, 'broken.mjs'), 'export const x = ;\n');
    const r = loads(join(dir, 'broken.mjs'));
    assert.equal(r.status, 9);
    assert.match(r.stderr, /LOADFAIL/);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

// ---------- Amendment A3: the creative director and /directions ----------

const DIRECTIONS_READS = JSON.parse(readFileSync(join(ROOT, '.claude', 'skills', 'directions', 'inputs.json'), 'utf8')).reads;

// A design repo with the brief and every file the directions stage tells the agent to read.
function withDirectionsRepo(fn) {
  return withDesignRepo((root) => {
    writeFileSync(join(root, 'docs', 'ui-requirements.md'), '# Brief\n\n## 1. Context\n\nWhy a UI.\n\n## 2. Functional\n\nRows.\n');
    for (const f of DIRECTIONS_READS) if (!f.endsWith('ui-requirements.md')) {
      mkdirSync(join(root, f, '..'), { recursive: true });
      writeFileSync(join(root, f), '# x\n');
    }
    return fn(root);
  });
}

test('A3 G1: creative-director is a known role that may write only docs/ui/directions.json', () => {
  assert.ok(isKnownRole('creative-director'));
  const may = (p) => matchesAny(p, allowlist('creative-director'));
  assert.ok(may('docs/ui/directions.json'));
  for (const p of ['docs/ui/styles.md', 'docs/ui/mockups/a/overview.html', 'docs/ui/research/mood-type-icons.md', '.claude/skills/directions/template.md']) assert.ok(!may(p), p);
  for (const role of [...BUILD_ROLES, 'ui-designer']) assert.ok(!matchesAny('docs/ui/directions.json', allowlist(role)), `${role} must not write directions`);
});

test('A3 G15: a rendered creative-director prompt passes; a changed byte or a missing read source is denied', () => withDirectionsRepo((root) => {
  const prompt = renderPrompt({ root, stage: 'directions', direction: 'all', screen: 'all' });
  assert.match(prompt, /^<!-- design-prompt v1 stage=directions direction=all screen=all attempt=1 /);
  assert.match(prompt, /Why a UI\./, 'the brief section is carried verbatim');
  assert.equal(dispatch(root, 'creative-director', prompt), null);
  assert.match(dispatch(root, 'creative-director', prompt.replace('Exactly three territories', 'Some territories')), /^G15: the creative-director prompt differs/);
  assert.match(dispatch(root, 'creative-director', 'Invent three styles.'), /^G15: a creative-director prompt/);
  assert.throws(() => renderPrompt({ root, stage: 'directions', direction: 'case-file', screen: 'overview' }), /direction=all screen=all/);
  rmSync(join(root, 'docs', 'ui', 'research', 'api-inventory.md'));
  assert.throws(() => renderPrompt({ root, stage: 'directions', direction: 'all', screen: 'all' }), /missing source/);
}));

test('A3 G16: a creative-director receipt naming a valid directions.json passes; scope and agent mismatches block', () => withDesignRepo((root) => {
  writeFileSync(join(root, 'docs', 'ui', 'directions.json'), '{"version":1,"territories":[]}');
  const cd = (extra = {}) => JSON.stringify({ agent: 'creative-director', stage: 'directions', direction: 'all', screen: 'all', status: 'written', files: ['docs/ui/directions.json'], notes: 'three ideas', ...extra });
  assert.deepEqual(designProblems(root, cd(), 'creative-director'), []);
  assert.ok(designProblems(root, cd({ files: ['docs/ui/styles.md'] }), 'creative-director').some((p) => /outside the creative-director's write scope/.test(p)));
  assert.ok(designProblems(root, cd({ agent: 'ui-designer' }), 'creative-director').some((p) => /receipt.agent must be "creative-director"/.test(p)));
  writeFileSync(join(root, 'docs', 'ui', 'directions.json'), '{ not json');
  assert.ok(designProblems(root, cd(), 'creative-director').some((p) => /not valid JSON/.test(p)));
}));

const IDS = new Set(['B1', 'B3', 'B9', 'C1', 'P2', 'M6', 'D1', 'K1', 'T1', 'T3']);
const palette = () => Object.fromEntries(TOKENS.map((t) => [t, ['background', 'surface', 'border', 'accent'].includes(t)
  ? { light: '#ffffff', dark: '#111111', source: 'proposed' }
  : { light: '#1a1a1a', dark: '#f0f0f0', source: 'K1' }]));
const territory = (i) => ({
  name: `idea-${i}`, governingIdea: `idea ${i}`, singleMindedProposition: `belief ${i}`,
  excludes: ['a', 'b', 'c'], layoutArchetype: `archetype ${i}`, toneRegister: `tone ${i}`,
  signatureMove: { explorer: 'a rule chain from D1', workbench: 'a refund sum from A11' },
  categoryDeparture: 'no sidebar',
  derivedStyle: { palette: palette(), type: { sans: 'T3', mono: 'T1' }, dials: { design_variance: 3, motion_intensity: 1, visual_density: 5 } },
  citations: ['B1', 'B3', 'B9', 'C1', 'M6'],
});
const doc = (...ts) => ({ version: 1, territories: ts });

test('A3 validator: three complete, divergent, cited territories pass', () => {
  assert.deepEqual(directionsProblems(doc(territory(1), territory(2), territory(3)), IDS), []);
  assert.ok(contrast('#000000', '#ffffff') > 20.9 && contrast('#ffffff', '#ffffff') === 1);
});

test('A3 validator: wrong count, shared archetype or tone, bad citations, low contrast, bad type or empty input fail', () => {
  const has = (d, re) => assert.ok(directionsProblems(d, IDS).some((p) => re.test(p)), `${re} not reported`);
  has(doc(territory(1), territory(2)), /exactly 3/);
  has(doc(territory(1), territory(2), { ...territory(3), layoutArchetype: 'archetype 1' }), /layoutArchetype .* shared/);
  has(doc(territory(1), territory(2), { ...territory(3), toneRegister: 'Tone 2 ' }), /toneRegister .* shared/);
  has(doc(territory(1), territory(2), { ...territory(3), citations: ['B1', 'B3', 'B9', 'C1', 'X9'] }), /don't resolve.*X9/);
  has(doc(territory(1), territory(2), { ...territory(3), citations: ['B1', 'C1', 'P2', 'M6', 'D1'] }), /at least 2 workbench/);
  has(doc(territory(1), territory(2), { ...territory(3), citations: ['B1', 'B3', 'B9', 'M6', 'D1'] }), /explorer prior-art/);
  has(doc(territory(1), territory(2), { ...territory(3), excludes: ['a'] }), /excludes needs at least 3/);
  has(doc(territory(1), territory(2), { ...territory(3), signatureMove: { explorer: 'x' } }), /signatureMove.workbench/);
  const low = territory(3);
  low.derivedStyle.palette.warn = { light: '#e69f00', dark: '#e69f00', source: 'K3' };
  has(doc(territory(1), territory(2), low), /warn light is .* below 4.5:1/);
  low.derivedStyle.palette.warn = { light: '#a06800', dark: '#e69f00', source: 'K3', graphicsOnly: true };
  assert.ok(!directionsProblems(doc(territory(1), territory(2), low), IDS).some((p) => /warn/.test(p)), 'graphicsOnly passes at 3:1');
  has(doc(territory(1), territory(2), { ...territory(3), derivedStyle: { ...territory(3).derivedStyle, type: { sans: 'Inter', mono: 'T1' } } }), /type.sans must be a researched T-id/);
  has(doc(), /fails closed/);
  has(null, /fails closed/);
});
