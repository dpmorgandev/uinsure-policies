#!/usr/bin/env node
// F0a spike probe (t3/t4): logs what a hook sees, never blocks. Removed once the spikes are recorded.
// Usage (from settings.json): node probe-log.mjs <label>   label = which settings file registered it
import { appendFileSync, mkdirSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

let raw = "";
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  try {
    const input = raw ? JSON.parse(raw) : {};
    const ti = input.tool_input ?? {};
    const dir = join(tmpdir(), "uinsure-probe");
    mkdirSync(dir, { recursive: true });
    appendFileSync(join(dir, "hook-log.jsonl"), JSON.stringify({
      ts: new Date().toISOString(),
      label: process.argv[2] ?? null,
      event: input.hook_event_name ?? null,
      agent_type: input.agent_type ?? null,
      agent_id: input.agent_id ?? null,
      session_id: input.session_id ?? null,
      cwd: input.cwd ?? null,
      process_cwd: process.cwd(),
      project_dir: process.env.CLAUDE_PROJECT_DIR ?? null,
      tool_name: input.tool_name ?? null,
      target: (ti.file_path ?? ti.path ?? ti.command ?? "").toString().slice(0, 160),
      keys: Object.keys(input),
    }) + "\n");
  } catch { /* a probe never breaks the session */ }
  process.exit(0);
});
