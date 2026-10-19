// Renders a design agent's prompt from a stage's checked-in template and inputs (guardrails G15,
// efficiency EFF-5, EFF-10). Every section is a verbatim slice of a source file, so nothing can be
// summarised away. The same function runs in the dispatch guard, which denies any ui-designer prompt
// that isn't byte-identical to its rendering.
//   node .claude/scripts/design/render-prompt.mjs <stage> <direction> <screen> [--attempt <n>] [--root <repo>] [--out <file>]
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';

const HARNESS = resolve(import.meta.dirname, '..', '..', '..');
const HEADER = /^<!-- design-prompt v1 stage=([a-z-]+) direction=([a-z-]+) screen=([a-z-]+) attempt=([1-9]\d*) sha256=([0-9a-f]{64}) -->\n/;

const lf = (text) => text.replace(/\r\n/g, '\n');
const readText = (file) => {
  if (!existsSync(file)) throw new Error(`missing source: ${file}`);
  return lf(readFileSync(file, 'utf8'));
};

// The block from a heading line to the next heading of the same or higher level.
export function section(text, heading) {
  const lines = text.split('\n');
  const start = lines.findIndex((l) => l.trim() === heading.trim());
  if (start < 0) throw new Error(`heading not found: ${heading}`);
  const level = /^#+/.exec(heading)[0].length;
  const end = lines.findIndex((l, i) => i > start && /^#+ /.test(l) && /^#+/.exec(l)[0].length <= level);
  return lines.slice(start, end < 0 ? undefined : end).join('\n').trimEnd();
}

// One excerpt: a whole file, or the lines matching `match`, or the first `head` lines.
export function excerpt(root, spec) {
  const text = readText(join(root, spec.file));
  let lines = text.trimEnd().split('\n');
  if (spec.match) lines = lines.filter((l) => new RegExp(spec.match).test(l));
  if (spec.head) lines = lines.slice(0, spec.head);
  if (!lines.length) throw new Error(`excerpt is empty: ${spec.file} ${spec.match ?? ''}`);
  const selector = spec.match ? ` (lines matching \`${spec.match}\`)` : spec.head ? ` (first ${spec.head} lines)` : '';
  return `### \`${spec.file}\`${selector}\n\n\`\`\`text\n${lines.join('\n')}\n\`\`\``;
}

// Attempt n > 1 carries why attempt n-1 failed, verbatim from the trail file the stage skill wrote
// (efficiency EFF-4): docs/trace/ui/<node>/feedback/<direction>-<screen>-<n>.txt.
export function feedbackFile(root, node, direction, screen, attempt) {
  return join(root, 'docs', 'trace', 'ui', node, 'feedback', `${direction}-${screen}-${attempt}.txt`);
}

// The directions stage (agents-skills Amendment A3): one dispatch for the whole set, so its header names
// direction=all screen=all. The prompt carries the brief's sections verbatim and names the research files
// the creative director must read itself; each must exist, or the render fails (closed).
function directionsFill(root, inputs, { direction, screen, attempt }) {
  if (direction !== 'all' || screen !== 'all') throw new Error('the directions stage renders one prompt: direction=all screen=all');
  const brief = readText(join(root, 'docs', 'ui-requirements.md'));
  for (const f of inputs.reads) if (!existsSync(join(root, f))) throw new Error(`missing source: ${f}`);
  return {
    output: inputs.output,
    brief: inputs.brief.map((h) => section(brief, h)).join('\n\n'),
    reads: inputs.reads.map((f) => `- \`${f}\``).join('\n'),
    feedback: attempt === 1
      ? 'None: this is the first attempt.'
      : `Attempt ${attempt - 1} failed these checks; fix every one:\n\n\`\`\`text\n${readText(feedbackFile(root, inputs.node, direction, screen, attempt)).trimEnd()}\n\`\`\``,
  };
}

// A territory names its type by research ID (T1..T5); the designer needs the families. The rows for its
// IDs, verbatim from the research's typography table (guardrails A5). A missing ID fails the render.
function typeRows(root, territory) {
  const file = 'docs/ui/research/mood-type-icons.md';
  const lines = readText(join(root, file)).split('\n');
  const ids = [...new Set([territory.derivedStyle?.type?.sans, territory.derivedStyle?.type?.mono].filter(Boolean))];
  const rows = ids.map((id) => {
    const row = lines.find((l) => l.startsWith(`| ${id} |`));
    if (!row) throw new Error(`type ${id} is not in ${file}`);
    return row;
  });
  return `### Its type, from \`${file}\`, verbatim\n\n\`\`\`text\n${rows.join('\n')}\n\`\`\``;
}

// A direction's block. Round 2 (agents-skills A3): a territory, verbatim from the creative director's
// docs/ui/directions.json, named by its kebab-case `name`. Round 1: a `## Style` section of styles.md.
function directionStyle(root, inputs, styles, direction) {
  if (inputs.directionsFrom) {
    const doc = JSON.parse(readText(join(root, inputs.directionsFrom)));
    const t = (doc.territories ?? []).find((x) => x.name === direction);
    if (!t) throw new Error(`unknown direction: ${direction} is not a territory in ${inputs.directionsFrom}`);
    return `## Territory: ${direction}\n\nFrom \`${inputs.directionsFrom}\`, verbatim.\n\n\`\`\`json\n${JSON.stringify(t, null, 2)}\n\`\`\`\n\n${typeRows(root, t)}`;
  }
  const heading = inputs.directions?.[direction];
  if (!heading) throw new Error(`unknown direction: ${direction}`);
  return section(styles, heading);
}

export function renderPrompt({ root, stage, direction, screen, attempt = 1 }) {
  if (!Number.isInteger(attempt) || attempt < 1) throw new Error(`bad attempt: ${attempt}`);
  const dir = join(HARNESS, '.claude', 'skills', stage);
  const inputs = JSON.parse(readText(join(dir, 'inputs.json')));
  const template = readText(join(dir, 'template.md'));
  if (inputs.kind === 'directions') {
    return finish(template, { stage, direction, screen, attempt: String(attempt), ...directionsFill(root, inputs, { direction, screen, attempt }) }, { stage, direction, screen, attempt });
  }
  const spec = inputs.screens[screen];
  if (!spec) throw new Error(`screen ${screen} is not part of stage ${stage}`);
  const styles = readText(join(root, 'docs', 'ui', 'styles.md'));
  const style = directionStyle(root, inputs, styles, direction);
  const entry = JSON.parse(readText(join(root, 'docs', 'ui', 'screens.json'))).app.screens.find((s) => s.id === screen);
  if (!entry) throw new Error(`screen ${screen} is not in docs/ui/screens.json`);
  const components = readText(join(root, 'docs', 'ui', 'components.md')).split('\n')
    .filter((l) => entry.components.some((c) => l.startsWith(`| ${c} |`)));

  const fill = {
    stage, direction, screen,
    output: `docs/ui/${inputs.outputDir}/${direction}/${screen}.html`,
    shared_constraints: section(styles, '## Shared constraints (all three directions)'),
    style,
    screen_entry: JSON.stringify(entry, null, 2),
    components: ['| ID | Purpose | States | Uses | From |', '|---|---|---|---|---|', ...components].join('\n'),
    data: spec.excerpts.map((e) => excerpt(root, e)).join('\n\n'),
    attempt: String(attempt),
    feedback: attempt === 1
      ? 'None: this is the first attempt.'
      : `Attempt ${attempt - 1} failed these checks; fix every one:\n\n\`\`\`text\n${readText(feedbackFile(root, inputs.node, direction, screen, attempt)).trimEnd()}\n\`\`\``,
  };
  return finish(template, fill, { stage, direction, screen, attempt });
}

function finish(template, fill, { stage, direction, screen, attempt }) {
  const body = template.replace(/\{\{(\w+)\}\}/g, (_, k) => {
    if (!(k in fill)) throw new Error(`template placeholder has no value: ${k}`);
    return fill[k];
  }).trimEnd() + '\n';
  const sha = createHash('sha256').update(body).digest('hex');
  return `<!-- design-prompt v1 stage=${stage} direction=${direction} screen=${screen} attempt=${attempt} sha256=${sha} -->\n${body}`;
}

// The header's inputs, or null when the prompt has no valid header.
export function parseHeader(prompt) {
  const m = HEADER.exec(lf(String(prompt ?? '')));
  return m ? { stage: m[1], direction: m[2], screen: m[3], attempt: Number(m[4]), sha: m[5] } : null;
}

// Prompts compare after line-ending and trailing-whitespace normalisation only.
export const samePrompt = (a, b) => lf(a).trimEnd() === lf(b).trimEnd();

if (process.argv[1] && resolve(process.argv[1]) === resolve(import.meta.filename)) {
  const [stage, direction, screen] = process.argv.slice(2);
  const opt = (name) => { const i = process.argv.indexOf(name); return i > 0 ? process.argv[i + 1] : null; };
  const attempt = Number(opt('--attempt') ?? 1);
  const prompt = renderPrompt({ root: resolve(opt('--root') ?? process.cwd()), stage, direction, screen, attempt });
  const out = opt('--out');
  if (out) {
    mkdirSync(dirname(out), { recursive: true });
    writeFileSync(out, prompt);
  } else process.stdout.write(prompt);
}
