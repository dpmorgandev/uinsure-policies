// Role table (guardrails.spec G1; agents-skills roster): the write allowlist per agent_type,
// widened for the builder by the node kind read from the branch → plan DAG.
import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { parseYaml } from '../../skills/plan-dag/validate-dag.mjs';
import { currentBranch, planNodeFromBranch } from './trace.mjs';

export const ROLES = ['tester', 'builder', 'reviewer', 'security', 'investigator', 'git', 'ui-designer'];

// agents-skills Amendment A1: design artifacts only.
const DESIGN = ['docs/ui/mockups/**', 'docs/ui/kit/**', 'docs/ui/screens/**'];

const BUILD_FILES = ['*.slnx', 'global.json', '.editorconfig', 'Directory.Build.props', 'Directory.Packages.props', 'BannedSymbols.txt', '.gitattributes'];
const BY_KIND = {
  evidence: ['infrastructure/**'],
  docs: ['README.md', 'docs/readme-checklist.md', 'docs/pitch-deck/**'],
};

export function nodeKind(root) {
  const where = planNodeFromBranch(currentBranch(root));
  if (!where) return null;
  const file = join(root, 'docs', 'plans', 'active', `${where.plan}.dag.yaml`);
  if (!existsSync(file)) return null;
  const node = (parseYaml(readFileSync(file, 'utf8')).nodes ?? []).find((n) => n.id === where.node);
  return node?.kind ?? null;
}

// Globs the role may write (before the harness lock and the trace ban, which apply to everyone).
// The tester's src/** is conditional on the stub rule (G2); callers check that separately.
export function allowlist(role, kind) {
  if (role === 'builder') return ['src/**', ...BUILD_FILES, ...(BY_KIND[kind] ?? [])];
  if (role === 'tester') return ['tests/**', 'docs/qa/**'];
  if (role === 'ui-designer') return DESIGN;
  return [];
}

export function isKnownRole(role) {
  return ROLES.includes(role);
}
