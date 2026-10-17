// Fixtures for the DAG validator's scaffold step (planning.spec amendment, F0b t1). Run: node --test .claude/hooks/__tests__
import './clean-git-env.mjs';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { parseYaml, validate, requirementIds } from '../../skills/plan-dag/validate-dag.mjs';

const DAG_PATH = resolve(import.meta.dirname, '../../../docs/plans/active/build.dag.yaml');
const reqIds = requirementIds(readFileSync(resolve(import.meta.dirname, '../../../docs/requirements.md'), 'utf8'));
const scaffold = { id: 't1', role: 'builder', step: 'scaffold', layer: null, rules: [], title: 'slnx and csproj', status: 'pending' };

function withF0bTasks(edit) {
  const dag = parseYaml(readFileSync(DAG_PATH, 'utf8'));
  const f0b = dag.nodes.find((n) => n.id === 'F0b');
  f0b.tasks = edit(f0b.tasks).map((t, i) => ({ ...t, id: `t${i + 1}` }));
  return validate(dag, { reqIds });
}

test('validate-dag: a leading builder scaffold before red is accepted', () => {
  assert.deepEqual(withF0bTasks((tasks) => [scaffold, ...tasks]), []);
});

test('validate-dag: a scaffold after red is refused', () => {
  const errors = withF0bTasks((tasks) => [tasks[0], scaffold, ...tasks.slice(1)]);
  assert.ok(errors.some((e) => e.includes('scaffold is only a feature or bug node')), errors.join('\n'));
});

test('validate-dag: a scaffold that is not the builder\'s is refused', () => {
  const errors = withF0bTasks((tasks) => [{ ...scaffold, role: 'tester' }, ...tasks]);
  assert.ok(errors.some((e) => e.includes('scaffold is the builder\'s')), errors.join('\n'));
});

test('validate-dag: red stays mandatory after a scaffold', () => {
  const errors = withF0bTasks((tasks) => [scaffold, ...tasks.slice(1)]);
  assert.ok(errors.some((e) => e.includes("missing step 'red'")), errors.join('\n'));
});
