// PreToolUse guard for design dispatches (guardrails Amendment A1, G15; efficiency EFF-5). Matcher:
// Agent|Task. A design agent's prompt (ui-designer; creative-director, Amendment A3) must be byte-identical
// (after line-ending normalisation) to the rendering of its stage template for the inputs its header
// names, so no prompt is hand-written or summarised. Every other dispatch is out of jurisdiction and passes
// at once, even on bad input, so the build loop's dispatches can't be blocked by this hook (merge-safety
// rule 2). For a design agent, any error denies (fails closed).
import { readFileSync } from 'node:fs';
import { deny } from './lib/io.mjs';
import { repoRoot } from './lib/trace.mjs';
import { parseHeader, renderPrompt, samePrompt } from '../scripts/design/render-prompt.mjs';

export const DESIGN_AGENT = 'ui-designer';
export const DESIGN_AGENTS = ['ui-designer', 'creative-director', 'kit-builder'];

// Returns a deny reason, or null to allow.
export function checkDispatch(input) {
  const ti = input?.tool_input ?? {};
  if (!DESIGN_AGENTS.includes(ti.subagent_type)) return null;
  const who = ti.subagent_type;
  const header = parseHeader(ti.prompt);
  if (!header) return `G15: a ${who} prompt must be rendered by .claude/scripts/design/render-prompt.mjs; its design-prompt header is missing`;
  const root = repoRoot(typeof input.cwd === 'string' ? input.cwd : process.cwd());
  if (!root) return 'G15: cannot resolve the repository to re-render the prompt';
  const expected = renderPrompt({ root, stage: header.stage, direction: header.direction, screen: header.screen, attempt: header.attempt });
  if (!samePrompt(ti.prompt, expected)) {
    return `G15: the ${who} prompt differs from its rendering (stage=${header.stage} direction=${header.direction} screen=${header.screen} attempt=${header.attempt}); dispatch the rendered file unchanged`;
  }
  return null;
}

function main() {
  let raw = '';
  try {
    raw = readFileSync(0, 'utf8');
    const reason = checkDispatch(JSON.parse(raw));
    if (reason) deny(reason);
  } catch (err) {
    if (DESIGN_AGENTS.some((a) => raw.includes(a))) deny(`G15: internal error, denied to fail closed (${err.message})`);
  }
  process.exit(0);
}

if (process.argv[1]?.replace(/\\/g, '/').endsWith('/guard-dispatch.mjs')) main();
