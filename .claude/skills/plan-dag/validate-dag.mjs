#!/usr/bin/env node
// Validates a plan's DAG file (<plan-id>.dag.yaml) and its sibling <plan-id>.plan.md, or an investigation's
// inv-NNN.plan.md on its own (it has no DAG).
// Usage: node validate-dag.mjs <path/to/plan-id.dag.yaml | path/to/inv-NNN.plan.md> [requirements.md]
// Exit 0 = valid, 1 = invalid (one "error:" line per problem), 2 = usage/setup error.
// The YAML reader below accepts a strict subset (block maps and lists, flow lists and maps,
// plain/quoted scalars, comments) and reports anything else, because the harness takes no
// npm dependencies. The orchestrator (/feature) imports parseYaml and validate from here.

import { readFileSync, existsSync } from "node:fs";
import { basename, dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

// ---------- YAML subset ----------
export function parseYaml(src) {
  const lines = [];
  src.replace(/\r\n/g, "\n").split("\n").forEach((raw, i) => {
    if (/^\s*(#.*)?$/.test(raw)) return;
    if (/\t/.test(raw.match(/^\s*/)[0])) throw new Error(`line ${i + 1}: tab indentation`);
    lines.push({ n: i + 1, indent: raw.match(/^ */)[0].length, text: stripComment(raw.trim()) });
  });
  let pos = 0;
  const block = (indent) => {
    const first = lines[pos];
    if (!first || first.indent < indent) return null;
    return first.text.startsWith("- ") || first.text === "-" ? seq(first.indent) : map(first.indent);
  };
  const map = (indent) => {
    const out = {};
    while (pos < lines.length && lines[pos].indent === indent && !lines[pos].text.startsWith("- ")) {
      const { n, text } = lines[pos];
      const m = text.match(/^([A-Za-z_][\w-]*):(?:\s+(.*))?$/);
      if (!m) throw new Error(`line ${n}: expected 'key: value', got '${text}'`);
      if (m[1] in out) throw new Error(`line ${n}: duplicate key '${m[1]}'`);
      pos++;
      if (m[2] !== undefined && m[2] !== "") out[m[1]] = scalarOrFlow(m[2], n);
      else {
        const next = lines[pos];
        if (next && (next.indent > indent || (next.indent === indent && next.text.startsWith("- ")))) out[m[1]] = block(next.indent);
        else out[m[1]] = null;
      }
    }
    if (pos < lines.length && lines[pos].indent > indent) throw new Error(`line ${lines[pos].n}: unexpected indentation`);
    return out;
  };
  const seq = (indent) => {
    const out = [];
    while (pos < lines.length && lines[pos].indent === indent && lines[pos].text.startsWith("-")) {
      const { n, text } = lines[pos];
      const rest = text.replace(/^-\s*/, "");
      if (/^[A-Za-z_][\w-]*:(\s|$)/.test(rest)) {
        // "- key: value" opens a map whose further keys sit at indent + 2
        lines[pos] = { n, indent: indent + 2, text: rest };
        out.push(map(indent + 2));
      } else {
        pos++;
        out.push(scalarOrFlow(rest, n));
      }
    }
    return out;
  };
  const result = block(0);
  if (pos < lines.length) throw new Error(`line ${lines[pos].n}: cannot parse '${lines[pos].text}'`);
  return result;
}

function stripComment(t) {
  let q = null;
  for (let i = 0; i < t.length; i++) {
    const c = t[i];
    if (q) { if (c === "\\" && q === '"') i++; else if (c === q) q = null; continue; }
    if (c === '"' || c === "'") q = c;
    else if (c === "#" && (i === 0 || t[i - 1] === " ")) return t.slice(0, i).trimEnd();
  }
  return t;
}

function scalarOrFlow(v, n) {
  if (/^[|>]/.test(v) || /^[&*!]/.test(v)) throw new Error(`line ${n}: block scalars, anchors and tags are not supported`);
  if (v.startsWith("[") || v.startsWith("{")) {
    const p = { s: v, i: 0, n };
    const val = flow(p);
    if (p.s.slice(p.i).trim() !== "") throw new Error(`line ${n}: trailing text after flow value`);
    return val;
  }
  return scalar(v, n);
}

function flow(p) {
  ws(p);
  const c = p.s[p.i];
  if (c === "[" || c === "{") {
    const close = c === "[" ? "]" : "}";
    const out = c === "[" ? [] : {};
    p.i++; ws(p);
    if (p.s[p.i] === close) { p.i++; return out; }
    for (;;) {
      if (Array.isArray(out)) out.push(flow(p));
      else {
        const key = token(p, ":").trim();
        if (!/^[A-Za-z_][\w-]*$/.test(key)) throw new Error(`line ${p.n}: bad flow-map key '${key}'`);
        if (p.s[p.i] !== ":") throw new Error(`line ${p.n}: expected ':' after '${key}'`);
        p.i++;
        if (key in out) throw new Error(`line ${p.n}: duplicate key '${key}'`);
        out[key] = flow(p);
      }
      ws(p);
      if (p.s[p.i] === ",") { p.i++; continue; }
      if (p.s[p.i] === close) { p.i++; return out; }
      throw new Error(`line ${p.n}: expected ',' or '${close}' in flow collection`);
    }
  }
  if (c === '"' || c === "'") {
    const start = p.i++;
    while (p.i < p.s.length && p.s[p.i] !== c) p.i += p.s[p.i] === "\\" && c === '"' ? 2 : 1;
    if (p.i >= p.s.length) throw new Error(`line ${p.n}: unterminated string`);
    p.i++;
    return scalar(p.s.slice(start, p.i), p.n);
  }
  return scalar(token(p, ",]}").trim(), p.n);
}
const ws = (p) => { while (p.s[p.i] === " ") p.i++; };
const token = (p, stops) => { const s = p.i; while (p.i < p.s.length && !stops.includes(p.s[p.i])) p.i++; return p.s.slice(s, p.i); };

function scalar(v, n) {
  if (/^".*"$/.test(v)) {
    try { return JSON.parse(v); } catch { throw new Error(`line ${n}: bad double-quoted string ${v}`); }
  }
  if (/^'.*'$/.test(v)) return v.slice(1, -1).replace(/''/g, "'");
  if (v === "" || v === "null" || v === "~") return null;
  if (v === "true") return true;
  if (v === "false") return false;
  if (/^-?\d+$/.test(v)) return Number(v);
  if (/^[\[\]{},"']/.test(v)) throw new Error(`line ${n}: cannot parse '${v}'`);
  return v;
}

// ---------- plan rules ----------
const ENUM = {
  planKind: ["build", "feature", "bug"],
  planStatus: ["draft", "approved", "active", "done", "archived"],
  nodeKind: ["harness", "feature", "bug", "evidence", "docs"],
  owner: ["loop", "david"],
  nodeStatus: ["pending", "in_progress", "paused_acceptance", "investigating", "hitl", "escalated", "done", "cut"],
  role: ["tester", "builder", "review", "git", "orchestrator", "david"],
  step: ["session", "scaffold", "red", "stub", "pause1", "green", "refactor", "gates", "review", "qa", "integrate", "trace", "pr", "pause2", "merge", "write"],
  layer: ["acceptance", "domain", "services", "persistence", "api", null],
  taskStatus: ["pending", "in_progress", "done", "skipped"],
};
const PLAN_KEYS = ["plan_id", "kind", "title", "status", "created", "approved_by", "approved_on", "requirements_rev", "parent", "nodes"];
const NODE_KEYS = ["id", "title", "kind", "owner", "depends_on", "rules", "cuttable", "budget", "status", "attempt",
  "branch", "worktree", "pr", "qa", "trace", "acceptance", "tasks"];
const TASK_KEYS = ["id", "role", "step", "layer", "rules", "title", "status"];
// loop nodes must carry these steps, in this relative order
const LOOP_SPINE = {
  feature: ["red", "stub", "pause1", "green", "refactor", "gates", "review", "qa", "integrate", "trace", "pr", "pause2", "merge"],
  docs: ["write", "gates", "review", "qa", "integrate", "trace", "pr", "pause2", "merge"],
};

export function requirementIds(reqText) {
  return new Set([...reqText.matchAll(/^\| ([A-Z]+-[0-9][0-9A-Za-z/]*) \|/gm)].map((m) => m[1]));
}

export function validate(dag, { fileBase, reqIds, planMd }) {
  const errors = [];
  const err = (m) => errors.push(m);
  if (!dag || typeof dag !== "object" || Array.isArray(dag)) return ["dag: top level is not a map"];

  for (const k of PLAN_KEYS) if (!(k in dag)) err(`plan: missing key '${k}'`);
  for (const k of Object.keys(dag)) if (!PLAN_KEYS.includes(k)) err(`plan: unknown key '${k}'`);
  const planId = dag.plan_id;
  if (!/^(build|feat-\d{3}|bug-\d{3})$/.test(planId ?? "")) err(`plan: plan_id '${planId}' is not build|feat-NNN|bug-NNN`);
  if (fileBase && fileBase !== `${planId}.dag.yaml`) err(`plan: plan_id '${planId}' does not match file name ${fileBase}`);
  if (!ENUM.planKind.includes(dag.kind)) err(`plan: kind '${dag.kind}' invalid`);
  if (!ENUM.planStatus.includes(dag.status)) err(`plan: status '${dag.status}' invalid`);
  if (typeof dag.title !== "string" || !dag.title) err("plan: title empty");
  if (!/^\d{4}-\d{2}-\d{2}$/.test(String(dag.created ?? ""))) err("plan: created is not YYYY-MM-DD");
  if (dag.status === "draft" && (dag.approved_by || dag.approved_on)) err("plan: approved_by/approved_on set on a draft (David only, at approval)");
  if (dag.status && dag.status !== "draft" && (!dag.approved_by || !dag.approved_on)) err(`plan: status '${dag.status}' without approved_by/approved_on`);

  if (planMd !== undefined) checkPlanMd(planMd, planId, err);

  const nodes = Array.isArray(dag.nodes) ? dag.nodes : (err("plan: nodes is not a list"), []);
  const idRe = planId === "build" ? /^F\d+[a-z]?$/ : /^N\d+$/;
  const byId = new Map();
  const allRules = new Set();
  for (const [i, nd] of nodes.entries()) {
    if (!nd || typeof nd !== "object" || Array.isArray(nd)) { err(`node #${i + 1}: not a map`); continue; }
    const where = `node ${nd.id ?? `#${i + 1}`}`;
    for (const k of NODE_KEYS) if (!(k in nd)) err(`${where}: missing key '${k}'`);
    for (const k of Object.keys(nd)) if (![...NODE_KEYS, "decided_by"].includes(k)) err(`${where}: unknown key '${k}'`);
    if (!idRe.test(nd.id ?? "")) err(`${where}: id does not match ${idRe}`);
    if (byId.has(nd.id)) err(`${where}: duplicate id`);
    byId.set(nd.id, nd);
    if (!ENUM.nodeKind.includes(nd.kind)) err(`${where}: kind '${nd.kind}' invalid`);
    if (!ENUM.owner.includes(nd.owner)) err(`${where}: owner '${nd.owner}' invalid`);
    if (!ENUM.nodeStatus.includes(nd.status)) err(`${where}: status '${nd.status}' invalid`);
    if (["cut", "escalated"].includes(nd.status) && nd.decided_by !== "david") err(`${where}: status '${nd.status}' needs decided_by: david`);
    if (!Number.isInteger(nd.attempt) || nd.attempt < 0 || nd.attempt > 5) err(`${where}: attempt '${nd.attempt}' is not 0-5`);
    if (typeof nd.cuttable !== "boolean") err(`${where}: cuttable must be true|false`);
    if (nd.budget !== null && !/^(\d+(\.\d+)?h|\d+m)$/.test(String(nd.budget))) err(`${where}: budget '${nd.budget}' is not null|<n>h|<n>m`);
    if (!Array.isArray(nd.depends_on)) err(`${where}: depends_on is not a list`);
    if (!Array.isArray(nd.rules)) err(`${where}: rules is not a list`);
    const nodeRules = Array.isArray(nd.rules) ? nd.rules : [];
    for (const r of nodeRules) {
      if (!reqIds.has(r)) err(`${where}: rule '${r}' not found in requirements (hard stop: add it there first)`);
      if (allRules.has(r)) err(`${where}: rule '${r}' is already mapped to another node`);
      allRules.add(r);
    }
    if (!Array.isArray(nd.acceptance) || nd.acceptance.length === 0 || nd.acceptance.some((a) => typeof a !== "string" || !a))
      err(`${where}: acceptance needs at least one non-empty string`);
    if (nd.qa !== `docs/qa/${planId}-${nd.id}.md`) err(`${where}: qa must be docs/qa/${planId}-${nd.id}.md`);
    if (nd.trace !== `docs/trace/${planId}/${nd.id}/`) err(`${where}: trace must be docs/trace/${planId}/${nd.id}/`);
    const prefix = dag.kind === "bug" ? "bug" : "feature";
    if (nd.branch !== null && nd.branch !== `${prefix}/${planId}/${nd.id}`) err(`${where}: branch must be null or ${prefix}/${planId}/${nd.id}`);
    if (nd.worktree !== null && nd.worktree !== `.claude/worktrees/${planId}-${nd.id}`) err(`${where}: worktree must be null or .claude/worktrees/${planId}-${nd.id}`);
    if (nd.pr !== null && !Number.isInteger(nd.pr)) err(`${where}: pr must be null or a number`);
    checkTasks(nd, where, nodeRules, err);
  }

  for (const nd of nodes) for (const d of Array.isArray(nd?.depends_on) ? nd.depends_on : []) {
    const dep = byId.get(d);
    if (!dep) { err(`node ${nd.id}: depends_on '${d}' does not resolve`); continue; }
    if (nd.cuttable === false && dep.cuttable === true) err(`node ${nd.id}: non-cuttable node depends on cuttable '${d}'`);
  }
  const colour = new Map();
  const visit = (id, path) => {
    colour.set(id, 1);
    for (const d of byId.get(id)?.depends_on ?? []) {
      if (!byId.has(d)) continue;
      if (colour.get(d) === 1) { err(`dag: cycle ${[...path, id, d].join(" → ")}`); continue; }
      if (!colour.get(d)) visit(d, [...path, id]);
    }
    colour.set(id, 2);
  };
  for (const id of byId.keys()) if (!colour.get(id)) visit(id, []);

  return errors;
}

function checkTasks(nd, where, nodeRules, err) {
  if (!Array.isArray(nd.tasks) || nd.tasks.length === 0) { err(`${where}: tasks must be a non-empty explicit list`); return; }
  const covered = { red: new Set(), green: new Set() };
  nd.tasks.forEach((t, i) => {
    const tw = `${where} task #${i + 1}`;
    if (!t || typeof t !== "object" || Array.isArray(t)) { err(`${tw}: not a map`); return; }
    for (const k of TASK_KEYS) if (!(k in t)) err(`${tw}: missing key '${k}'`);
    for (const k of Object.keys(t)) if (!TASK_KEYS.includes(k)) err(`${tw}: unknown key '${k}'`);
    if (t.id !== `t${i + 1}`) err(`${tw}: id must be t${i + 1} (sequential, in run order)`);
    if (!ENUM.role.includes(t.role)) err(`${tw}: role '${t.role}' invalid`);
    if (!ENUM.step.includes(t.step)) err(`${tw}: step '${t.step}' invalid`);
    if (!ENUM.layer.includes(t.layer)) err(`${tw}: layer '${t.layer}' invalid`);
    if (!ENUM.taskStatus.includes(t.status)) err(`${tw}: status '${t.status}' invalid`);
    if (typeof t.title !== "string" || !t.title) err(`${tw}: title empty`);
    if (!Array.isArray(t.rules)) { err(`${tw}: rules is not a list`); return; }
    for (const r of t.rules) if (!nodeRules.includes(r)) err(`${tw}: rule '${r}' is not one of the node's rules`);
    if (t.step === "red") t.rules.forEach((r) => covered.red.add(r));
    if (t.step === "green") t.rules.forEach((r) => covered.green.add(r));
    if (nd.owner === "david" && (t.role !== "david" || t.step !== "session")) err(`${tw}: an owner david node has only role david, step session tasks`);
    if (nd.owner === "loop" && t.step === "session") err(`${tw}: step session belongs to owner david nodes`);
    if (["pause1", "pause2", "merge"].includes(t.step) && t.role !== "david") err(`${tw}: ${t.step} is David's`);
    if (["red", "qa"].includes(t.step) && t.role !== "tester") err(`${tw}: ${t.step} is the tester's`);
    if (["scaffold", "stub", "green", "refactor", "write"].includes(t.step) && t.role !== "builder") err(`${tw}: ${t.step} is the builder's`);
    if (t.step === "scaffold" && (i !== 0 || !["feature", "bug"].includes(nd.kind) || t.layer !== null || t.rules.length))
      err(`${tw}: scaffold is only a feature or bug node's first task, with layer null and no rules`);
    if (["integrate", "pr"].includes(t.step) && t.role !== "git") err(`${tw}: ${t.step} is the git agent's`);
    if (["gates", "trace"].includes(t.step) && t.role !== "orchestrator") err(`${tw}: ${t.step} is the orchestrator's`);
    if (t.step === "review" && t.role !== "review") err(`${tw}: review is role review (reviewer ∥ security)`);
  });
  if (nd.owner !== "loop") return;
  const spine = LOOP_SPINE[["docs", "evidence"].includes(nd.kind) ? "docs" : "feature"];
  const steps = nd.tasks.map((t) => t?.step);
  let at = -1;
  for (const s of spine) {
    const i = steps.indexOf(s, at + 1);
    if (i === -1) { err(`${where}: loop node is missing step '${s}' (or it is out of order)`); return; }
    at = i;
  }
  const first = nd.tasks[nd.tasks[0]?.step === "scaffold" ? 1 : 0];
  if (spine[0] === "red" && (first.step !== "red" || first.layer !== "acceptance"))
    err(`${where}: the first task (after an optional scaffold) must be the tester's red acceptance suite`);
  if (spine[0] === "red") {
    const t1 = new Set(first.rules ?? []);
    for (const r of nodeRules) {
      if (!t1.has(r)) err(`${where}: rule '${r}' is missing from t1 (the full acceptance suite)`);
      if (!covered.green.has(r)) err(`${where}: rule '${r}' has no green task`);
    }
  }
}

function checkPlanMd(md, planId, err) {
  const text = md.replace(/\r\n/g, "\n");
  const fm = text.match(/^---\n([\s\S]*?)\n---\n/);
  if (!fm) { err("plan.md: frontmatter missing"); return; }
  const front = Object.fromEntries(fm[1].split("\n").map((l) => l.match(/^(\w+):\s*(.*)$/)).filter(Boolean).map((m) => [m[1], m[2].trim()]));
  if (front.plan_id !== planId) err(`plan.md: plan_id '${front.plan_id}' does not match the DAG's '${planId}'`);
  if (front.dag !== `${planId}.dag.yaml`) err(`plan.md: frontmatter 'dag:' must be ${planId}.dag.yaml`);
  for (const k of ["status", "approved_by", "approved_on", "nodes"]) if (k in front) err(`plan.md: '${k}' belongs in the DAG file only`);
  if (/^```yaml dag/m.test(text)) err("plan.md: holds a yaml dag block; the DAG file is the only state");
  const body = (name) => {
    const m = text.match(new RegExp(`^## ${name}[^\\n]*\\n([\\s\\S]*?)(?=^## |(?![\\s\\S]))`, "m"));
    return m ? m[1].replace(/<!--[\s\S]*?-->/g, "") : null;
  };
  for (const s of ["Why", "Approach", "Rejected alternatives", "Prior work consulted", "Attempt log", "Completion record"])
    if (body(s) === null) err(`plan.md: section '## ${s}' missing`);
  const rej = body("Rejected alternatives");
  if (rej !== null && !/^- \S/m.test(rej)) err("plan.md: '## Rejected alternatives' needs at least one '- ' bullet");
  const prior = body("Prior work consulted");
  if (prior !== null && prior.trim() === "") err("plan.md: '## Prior work consulted' is empty (write hits or 'none')");
}

// An inv-NNN plan has no DAG file: its .plan.md holds the question, the findings and one `yaml recommendations`
// block (planning.spec, The four plan-* skills). Kinds feature|bug|amend-node; David alone moves a status off proposed.
const REC_KEYS = ["kind", "target", "rule", "reason", "status"];
export function validateInvestigation(md, { fileBase, reqIds }) {
  const errors = [];
  const err = (m) => errors.push(m);
  const text = md.replace(/\r\n/g, "\n");
  const fm = text.match(/^---\n([\s\S]*?)\n---\n/);
  if (!fm) return ["plan.md: frontmatter missing"];
  const front = Object.fromEntries(fm[1].split("\n").map((l) => l.match(/^(\w+):\s*(.*?)\s*(#.*)?$/)).filter(Boolean).map((m) => [m[1], m[2]]));
  const planId = front.plan_id;
  if (!/^inv-\d{3}$/.test(planId ?? "")) err(`plan.md: plan_id '${planId}' is not inv-NNN`);
  if (fileBase && fileBase !== `${planId}.plan.md`) err(`plan.md: plan_id '${planId}' does not match file name ${fileBase}`);
  if (front.kind !== "investigation") err(`plan.md: kind '${front.kind}' must be investigation`);
  if (front.dag !== "null") err("plan.md: frontmatter 'dag:' must be null (an investigation has no DAG)");
  if (!/^\d{4}-\d{2}-\d{2}$/.test(front.created ?? "")) err("plan.md: created is not YYYY-MM-DD");
  const parent = front.parent ?? "";
  if (parent !== "null" && !/^(build|feat-\d{3}|bug-\d{3})\/(F\d+[a-z]?|N\d+)$/.test(parent)) err(`plan.md: parent '${parent}' is not null or <plan>/<node>`);
  for (const k of ["status", "approved_by", "approved_on", "nodes"]) if (k in front) err(`plan.md: '${k}' has no place in an investigation`);
  if (/^```yaml dag/m.test(text)) err("plan.md: an investigation never holds a DAG");

  const body = (name) => {
    const m = text.match(new RegExp(`^## ${name}[^\\n]*\\n([\\s\\S]*?)(?=^## |(?![\\s\\S]))`, "m"));
    return m ? m[1].replace(/<!--[\s\S]*?-->/g, "") : null;
  };
  for (const s of ["Question", "Hypothesis", "Evidence", "Findings", "Recommendations", "Rejected alternatives", "Prior work consulted", "Attempt log", "Completion record"])
    if (body(s) === null) err(`plan.md: section '## ${s}' missing`);
  const question = body("Question");
  if (question !== null && !/\?\s*$/.test(question.trim())) err("plan.md: '## Question' must be one falsifiable question ending in '?'");
  const rej = body("Rejected alternatives");
  if (rej !== null && !/^- \S/m.test(rej)) err("plan.md: '## Rejected alternatives' needs at least one '- ' bullet");
  const prior = body("Prior work consulted");
  if (prior !== null && prior.trim() === "") err("plan.md: '## Prior work consulted' is empty (write hits or 'none')");

  const blocks = [...text.matchAll(/^```yaml recommendations\n([\s\S]*?)^```$/gm)];
  if (blocks.length !== 1) { err(`plan.md: needs exactly one \`\`\`yaml recommendations block (found ${blocks.length})`); return errors; }
  let recs;
  try { recs = parseYaml(`recommendations:\n${blocks[0][1].replace(/^(?=\S)/gm, "  ")}`).recommendations; }
  catch (e) { err(`plan.md: recommendations: ${e.message}`); return errors; }
  if (!Array.isArray(recs) || recs.length === 0) { err("plan.md: recommendations needs at least one entry"); return errors; }
  recs.forEach((r, i) => {
    const at = `recommendation #${i + 1}`;
    if (!r || typeof r !== "object" || Array.isArray(r)) { err(`${at}: not a map`); return; }
    for (const k of ["kind", "target", "reason", "status"]) if (!(k in r)) err(`${at}: missing key '${k}'`);
    for (const k of Object.keys(r)) if (!REC_KEYS.includes(k)) err(`${at}: unknown key '${k}'`);
    if (!["feature", "bug", "amend-node"].includes(r.kind)) err(`${at}: kind '${r.kind}' is not feature|bug|amend-node`);
    if (!["proposed", "accepted", "rejected"].includes(r.status)) err(`${at}: status '${r.status}' is not proposed|accepted|rejected`);
    if (typeof r.reason !== "string" || !r.reason) err(`${at}: reason empty`);
    if (r.kind === "amend-node" && (parent === "null" || r.target !== parent)) err(`${at}: amend-node must target the parent node '${parent}'`);
    if (r.kind !== "amend-node" && r.target !== null) err(`${at}: a ${r.kind} recommendation has target null (its plan does not exist yet)`);
    if (r.rule !== undefined && r.rule !== null && reqIds && !reqIds.has(r.rule)) err(`${at}: rule '${r.rule}' not found in requirements`);
  });
  return errors;
}

// ---------- CLI ----------
const samePath = (a, b) => (process.platform === "win32" ? resolve(a).toLowerCase() === resolve(b).toLowerCase() : resolve(a) === resolve(b));
if (process.argv[1] && samePath(fileURLToPath(import.meta.url), process.argv[1])) {
  const [dagPath, reqPath = "docs/requirements.md"] = process.argv.slice(2);
  if (dagPath && /(^|[\\/])inv-\d{3}\.plan\.md$/.test(dagPath)) {
    for (const p of [dagPath, reqPath]) if (!existsSync(p)) { console.error(`missing file: ${p}`); process.exit(2); }
    const errors = validateInvestigation(readFileSync(dagPath, "utf8"), { fileBase: basename(dagPath), reqIds: requirementIds(readFileSync(reqPath, "utf8")) });
    if (errors.length) { for (const e of errors) console.log(`error: ${e}`); process.exit(1); }
    console.log(`investigation: ok · ${basename(dagPath)}`);
    process.exit(0);
  }
  if (!dagPath || !dagPath.endsWith(".dag.yaml")) { console.error("usage: validate-dag.mjs <plan-id>.dag.yaml | inv-NNN.plan.md [requirements.md]"); process.exit(2); }
  for (const p of [dagPath, reqPath]) if (!existsSync(p)) { console.error(`missing file: ${p}`); process.exit(2); }
  const mdPath = join(dirname(dagPath), basename(dagPath).replace(/\.dag\.yaml$/, ".plan.md"));
  let dag;
  try { dag = parseYaml(readFileSync(dagPath, "utf8")); }
  catch (e) { console.log(`error: yaml: ${e.message}`); process.exit(1); }
  const errors = validate(dag, {
    fileBase: basename(dagPath),
    reqIds: requirementIds(readFileSync(reqPath, "utf8")),
    planMd: existsSync(mdPath) ? readFileSync(mdPath, "utf8") : (console.log(`error: plan.md: ${mdPath} missing`), process.exit(1)),
  });
  if (errors.length) { for (const e of errors) console.log(`error: ${e}`); process.exit(1); }
  const tasks = dag.nodes.reduce((s, n) => s + n.tasks.length, 0);
  const rules = new Set(dag.nodes.flatMap((n) => n.rules)).size;
  console.log(`dag: ok · ${dag.nodes.length} nodes · ${tasks} tasks · ${rules} rules · ${basename(dagPath)}`);
}
