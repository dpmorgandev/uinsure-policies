// Fixtures for the investigation-plan check (planning.spec, The four plan-* skills). Run: node --test .claude/hooks/__tests__
import './clean-git-env.mjs';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { validateInvestigation, requirementIds } from '../../skills/plan-dag/validate-dag.mjs';

const reqIds = requirementIds(readFileSync(resolve(import.meta.dirname, '../../../docs/requirements.md'), 'utf8'));

const plan = ({ front = {}, question = 'Why does the reviewer FAIL F2 at attempt 2 on hasClaims?', recs, drop } = {}) => {
  const fm = { plan_id: 'inv-001', kind: 'investigation', title: 'F2 attempt 3', created: '2026-10-18', dag: 'null', parent: 'build/F2', ...front };
  const sections = {
    Question: question,
    Hypothesis: 'The mapping cans hasClaims.',
    Evidence: '- verdicts/A2-reviewer.json R1',
    Findings: '- I1 PolicyMappings.cs:37',
    Recommendations: ['```yaml recommendations', ...(recs ?? ['- {kind: amend-node, target: build/F2, reason: "map the stored hasClaims", status: proposed}']), '```'].join('\n'),
    Why: 'Attempt 3 on build/F2.',
    Approach: 'Read-only diagnosis.',
    'Rejected alternatives': '- Cut F2: GET-1 is not cuttable.',
    'Prior work consulted': 'none',
    'Attempt log': '',
    'Completion record': '',
  };
  for (const s of drop ?? []) delete sections[s];
  const head = ['---', ...Object.entries(fm).map(([k, v]) => `${k}: ${v}`), '---'].join('\n');
  return `${head}\n# inv-001: F2 attempt 3\n${Object.entries(sections).map(([k, v]) => `## ${k}\n${v}\n`).join('')}`;
};
const check = (md, fileBase = 'inv-001.plan.md') => validateInvestigation(md, { fileBase, reqIds });
const has = (errors, text) => assert.ok(errors.some((e) => e.includes(text)), errors.join('\n') || '(no errors)');

test('validate-investigation: a complete escalation plan is accepted', () => {
  assert.deepEqual(check(plan()), []);
});

test('validate-investigation: feature and bug recommendations with null targets are accepted', () => {
  assert.deepEqual(check(plan({ recs: [
    '- {kind: feature, target: null, reason: "a list endpoint", status: rejected}',
    '- {kind: bug, target: null, rule: GET-1, reason: "GET omits a field since F1", status: accepted}',
  ] })), []);
});

test('validate-investigation: the plan id must match the file name', () => {
  has(check(plan(), 'inv-002.plan.md'), 'does not match file name');
});

test('validate-investigation: a DAG reference is refused', () => {
  has(check(plan({ front: { dag: 'inv-001.dag.yaml' } })), "'dag:' must be null");
});

test('validate-investigation: a status key is refused', () => {
  has(check(plan({ front: { status: 'active' } })), "'status' has no place");
});

test('validate-investigation: the question must be a question', () => {
  has(check(plan({ question: 'Fix the hasClaims bug.' })), 'falsifiable question');
});

test('validate-investigation: a missing section is refused', () => {
  has(check(plan({ drop: ['Hypothesis'] })), "section '## Hypothesis' missing");
});

test('validate-investigation: a rejected alternative is required', () => {
  const md = plan().replace('- Cut F2: GET-1 is not cuttable.', 'none');
  has(check(md), 'Rejected alternatives');
});

test('validate-investigation: exactly one recommendations block', () => {
  has(check(plan().replace(/```yaml recommendations[\s\S]*?```\n/, '')), 'exactly one');
});

test('validate-investigation: an unknown kind is refused', () => {
  has(check(plan({ recs: ['- {kind: refactor, target: null, reason: "x", status: proposed}'] })), "kind 'refactor'");
});

test('validate-investigation: an amend-node must target the parent node', () => {
  has(check(plan({ recs: ['- {kind: amend-node, target: build/F3, reason: "x", status: proposed}'] })), 'must target the parent');
});

test('validate-investigation: a standalone investigation cannot amend a node', () => {
  has(check(plan({ front: { parent: 'null' } })), 'must target the parent');
});

test('validate-investigation: an unknown status is refused', () => {
  has(check(plan({ recs: ['- {kind: amend-node, target: build/F2, reason: "x", status: done}'] })), "status 'done'");
});

test('validate-investigation: a rule must exist in requirements', () => {
  has(check(plan({ recs: ['- {kind: bug, target: null, rule: NOPE-9, reason: "x", status: proposed}'] })), "rule 'NOPE-9'");
});
