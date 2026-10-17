#!/usr/bin/env node
// Validates a plan file against the plan template and the orchestrator read rules.
// Usage: node validate-dag.mjs <plan.md> [requirements.md]
// Exit 0 = valid, 1 = invalid (one "error:" line per problem), 2 = usage/setup error.
// Line-based on purpose: the DAG format is fixed and written by the plan-* skills, and the
// harness takes no npm dependencies. Anything it cannot parse is reported, never skipped.

import { readFileSync, existsSync } from "node:fs";
import { basename } from "node:path";

const [planPath, reqPath = "docs/requirements.md"] = process.argv.slice(2);
if (!planPath) { console.error("usage: validate-dag.mjs <plan.md> [requirements.md]"); process.exit(2); }
for (const p of [planPath, reqPath]) if (!existsSync(p)) { console.error(`missing file: ${p}`); process.exit(2); }

const text = readFileSync(planPath, "utf8").replace(/\r\n/g, "\n");
const reqText = readFileSync(reqPath, "utf8").replace(/\r\n/g, "\n");
const errors = [];
const err = (m) => errors.push(m);

const ENUM = {
  planKind: ["build", "feature", "bug", "investigation"],
  planStatus: ["draft", "approved", "active", "done", "archived"],
  nodeKind: ["harness", "feature", "bug", "evidence", "docs"],
  owner: ["loop", "david"],
  nodeStatus: ["pending", "in_progress", "paused_acceptance", "investigating", "hitl", "escalated", "done", "cut"],
  tasks: ["standard", "standard-docs", "session"],
};
const NODE_KEYS = ["id", "title", "kind", "owner", "depends_on", "rules", "acceptance", "status", "attempt",
  "cuttable", "budget", "branch", "worktree", "pr", "qa", "trace", "tasks"];

const stripComment = (v) => (/^["']/.test(v) ? v.replace(/(["'])\s+#.*$/, "$1") : v.replace(/\s+#.*$/, "")).trim();
const unquote = (v) => v.replace(/^"(.*)"$/, "$1").replace(/^'(.*)'$/, "$1");
const flowList = (v) => {
  const m = v.match(/^\[(.*)\]$/);
  if (!m) return null;
  return m[1].trim() === "" ? [] : m[1].split(",").map((s) => unquote(s.trim()));
};

// --- frontmatter
const fm = text.match(/^---\n([\s\S]*?)\n---\n/);
const front = {};
if (!fm) err("frontmatter: missing");
else for (const line of fm[1].split("\n")) {
  const m = line.match(/^(\w+):\s*(.*)$/);
  if (m) front[m[1]] = stripComment(m[2]);
}
const planId = front.plan_id ?? "";
if (!/^(build|feat-\d{3}|bug-\d{3}|inv-\d{3})$/.test(planId)) err(`frontmatter: plan_id '${planId}' is not build|feat-NNN|bug-NNN|inv-NNN`);
if (basename(planPath) !== `${planId}.plan.md`) err(`frontmatter: plan_id '${planId}' does not match file name ${basename(planPath)}`);
if (!ENUM.planKind.includes(front.kind)) err(`frontmatter: kind '${front.kind}' invalid`);
if (!ENUM.planStatus.includes(front.status)) err(`frontmatter: status '${front.status}' invalid`);
if (!front.title) err("frontmatter: title empty");
if (!/^\d{4}-\d{2}-\d{2}$/.test(front.created ?? "")) err("frontmatter: created is not YYYY-MM-DD");
if (front.status === "draft" && front.approved_by) err("frontmatter: approved_by is set on a draft plan (David only, at approval)");
if (front.status !== "draft" && (!front.approved_by || !front.approved_on)) err(`frontmatter: status '${front.status}' without approved_by/approved_on`);

// --- sections
const SECTIONS = front.kind === "investigation"
  ? ["Why", "Approach", "Rejected alternatives", "Prior work consulted"]
  : ["Why", "Approach", "Rejected alternatives", "Prior work consulted", "DAG", "Attempt log", "Completion record"];
const sectionBody = (name) => {
  const m = text.match(new RegExp(`^## ${name}[^\\n]*\\n([\\s\\S]*?)(?=^## |(?![\\s\\S]))`, "m"));
  return m ? m[1] : null;
};
for (const s of SECTIONS) if (sectionBody(s) === null) err(`section: '## ${s}' missing`);
const rejected = sectionBody("Rejected alternatives");
if (rejected !== null && !/^- \S/m.test(rejected)) err("section: '## Rejected alternatives' needs at least one '- ' bullet");
const prior = sectionBody("Prior work consulted");
if (prior !== null && prior.replace(/<!--[\s\S]*?-->/g, "").trim() === "") err("section: '## Prior work consulted' is empty (write hits or 'none')");

if (front.kind === "investigation") finish(0, 0);

// --- dag block
const blocks = [...text.matchAll(/^```yaml dag\n([\s\S]*?)^```$/gm)];
if (blocks.length !== 1) { err(`dag: expected exactly one \`\`\`yaml dag block, found ${blocks.length}`); finish(0, 0); }
const lines = blocks[0][1].split("\n");
const nodes = [];
let cur = null, listKey = null;
lines.forEach((raw, i) => {
  const n = i + 1;
  if (raw.trim() === "" || /^\s*#/.test(raw)) return;
  if (n === 1 || raw === "nodes:") { if (raw !== "nodes:") err(`dag line ${n}: expected 'nodes:'`); return; }
  let m;
  if ((m = raw.match(/^  - id:\s*(.*)$/))) { cur = { id: stripComment(m[1]), _line: n, acceptance: [] }; nodes.push(cur); listKey = null; return; }
  if (!cur) { err(`dag line ${n}: content before the first node`); return; }
  if ((m = raw.match(/^    (\w+):\s*(.*)$/))) {
    const key = m[1], val = stripComment(m[2]);
    if (key in cur && key !== "acceptance") err(`dag ${cur.id}: duplicate key '${key}'`);
    if (key === "acceptance" || (key === "tasks" && val === "")) { listKey = key; if (key === "tasks") cur.tasks = []; return; }
    listKey = null;
    cur[key] = val;
    return;
  }
  if ((m = raw.match(/^      - (.*)$/)) && listKey) {
    const item = stripComment(m[1]);
    if (listKey === "acceptance") {
      if (!/^".+"$/.test(item)) err(`dag ${cur.id}: acceptance item not a double-quoted string (line ${n})`);
      cur.acceptance.push(unquote(item));
    } else {
      const t = item.match(/^\{(.*)\}$/);
      if (!t || !/\bid:\s*t\d+/.test(t[1]) || !/\brole:/.test(t[1]) || !/\btitle:/.test(t[1]) || !/\bstatus:/.test(t[1]))
        err(`dag ${cur.id}: explicit task needs {id: tN, role, step, title, status} (line ${n})`);
      cur.tasks.push(item);
    }
    return;
  }
  err(`dag line ${n}: cannot parse '${raw.trim()}'`);
});

// --- requirement ids: first cell of a requirements table row
const reqIds = new Set([...reqText.matchAll(/^\| ([A-Z]+-[0-9][0-9A-Za-z/]*) \|/gm)].map((m) => m[1]));

const idRe = planId === "build" ? /^F\d+[a-z]?$/ : /^N\d+$/;
const byId = new Map();
for (const nd of nodes) {
  const where = `dag ${nd.id || `line ${nd._line}`}`;
  if (!idRe.test(nd.id)) err(`${where}: id does not match ${idRe}`);
  if (byId.has(nd.id)) err(`${where}: duplicate id`);
  byId.set(nd.id, nd);
  for (const k of NODE_KEYS) if (!(k in nd)) err(`${where}: missing key '${k}'`);
  if (nd.kind && !ENUM.nodeKind.includes(nd.kind)) err(`${where}: kind '${nd.kind}' invalid`);
  if (nd.owner && !ENUM.owner.includes(nd.owner)) err(`${where}: owner '${nd.owner}' invalid`);
  if (nd.status && !ENUM.nodeStatus.includes(nd.status)) err(`${where}: status '${nd.status}' invalid`);
  if (["cut", "escalated"].includes(nd.status) && nd.decided_by !== "david") err(`${where}: status '${nd.status}' needs decided_by: david`);
  if ("attempt" in nd && !/^[0-5]$/.test(nd.attempt)) err(`${where}: attempt '${nd.attempt}' is not 0-5`);
  if ("cuttable" in nd && !["true", "false"].includes(nd.cuttable)) err(`${where}: cuttable must be true|false`);
  if ("budget" in nd && !/^(null|\d+(\.\d+)?h|\d+m)$/.test(nd.budget)) err(`${where}: budget '${nd.budget}' is not null|<n>h|<n>m`);
  if (!Array.isArray(nd.tasks) && nd.tasks !== undefined && !ENUM.tasks.includes(nd.tasks)) err(`${where}: tasks '${nd.tasks}' is not a named list or an explicit list`);
  if (nd.owner === "david" && nd.tasks !== "session") err(`${where}: owner david requires tasks: session`);
  if (nd.owner === "loop" && nd.tasks === "session") err(`${where}: tasks: session requires owner: david`);
  if (nd.acceptance.length === 0) err(`${where}: needs at least one acceptance statement`);
  nd.deps = flowList(nd.depends_on ?? "");
  if (nd.deps === null) err(`${where}: depends_on is not a [list]`);
  nd.ruleIds = flowList(nd.rules ?? "");
  if (nd.ruleIds === null) err(`${where}: rules is not a [list]`);
  for (const r of nd.ruleIds ?? []) if (!reqIds.has(r)) err(`${where}: rule '${r}' not found in ${reqPath} (hard stop: add it there first)`);
  if (nd.qa && nd.qa !== `docs/qa/${planId}-${nd.id}.md`) err(`${where}: qa must be docs/qa/${planId}-${nd.id}.md`);
  if (nd.trace && nd.trace !== `docs/trace/${planId}/${nd.id}/`) err(`${where}: trace must be docs/trace/${planId}/${nd.id}/`);
  const prefix = front.kind === "bug" ? "bug" : "feature";
  if (nd.branch && nd.branch !== "null" && nd.branch !== `${prefix}/${planId}/${nd.id}`) err(`${where}: branch must be null or ${prefix}/${planId}/${nd.id}`);
  if (nd.worktree && nd.worktree !== "null" && nd.worktree !== `.claude/worktrees/${planId}-${nd.id}`) err(`${where}: worktree must be null or .claude/worktrees/${planId}-${nd.id}`);
  if (nd.pr && !/^(null|\d+)$/.test(nd.pr)) err(`${where}: pr must be null or a number`);
}

for (const nd of nodes) for (const d of nd.deps ?? []) {
  const dep = byId.get(d);
  if (!dep) { err(`dag ${nd.id}: depends_on '${d}' does not resolve`); continue; }
  if (nd.cuttable === "false" && dep.cuttable === "true") err(`dag ${nd.id}: non-cuttable node depends on cuttable '${d}'`);
}

// cycle check (DFS, white/grey/black)
const colour = new Map();
const visit = (id, path) => {
  colour.set(id, 1);
  for (const d of byId.get(id)?.deps ?? []) {
    if (!byId.has(d)) continue;
    if (colour.get(d) === 1) { err(`dag: cycle ${[...path, id, d].join(" → ")}`); continue; }
    if (!colour.get(d)) visit(d, [...path, id]);
  }
  colour.set(id, 2);
};
for (const id of byId.keys()) if (!colour.get(id)) visit(id, []);

finish(nodes.length, new Set(nodes.flatMap((n) => n.ruleIds ?? [])).size);

function finish(nodeCount, ruleCount) {
  if (errors.length) { for (const e of errors) console.log(`error: ${e}`); process.exit(1); }
  console.log(`dag: ok · ${nodeCount} nodes · ${ruleCount} rules · ${basename(planPath)}`);
  process.exit(0);
}
