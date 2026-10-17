---
name: verdict-format
description: >-
  The verdict JSON schema v1 for reviewer and security, with its PASS/FAIL semantics. Your final
  message is exactly one object that validates against it.
user-invocable: false
---
# Verdict format (schema v1)

Your final message is **exactly one JSON object**, with no prose around it, that validates against this
schema. A SubagentStop hook checks it. If it's rejected, restate the **complete** object with the
errors fixed, because only your last message reaches the orchestrator.

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "object", "additionalProperties": false,
  "required": ["schema_version","agent","plan_id","node_id","attempt","base_sha","head_sha","diff_fingerprint","verdict","gate_refs","rules_covered","rules_missing","findings"],
  "properties": {
    "schema_version": { "const": 1 },
    "agent":   { "enum": ["reviewer","security"] },
    "plan_id": { "pattern": "^(build|feat-\\d{3}|bug-\\d{3})$" },
    "node_id": { "pattern": "^(F\\d+[a-z]?|N\\d+)$" },
    "attempt": { "type": "integer", "minimum": 1, "maximum": 5 },
    "base_sha": { "pattern": "^[0-9a-f]{40}$" },
    "head_sha": { "pattern": "^[0-9a-f]{40}$" },
    "diff_fingerprint": { "type": "string", "description": "git diff base...head | git patch-id --stable" },
    "verdict": { "enum": ["PASS","FAIL"] },
    "gate_refs": { "type": "array", "minItems": 1, "items": { "type": "string" }, "description": "loop.jsonl gate events read" },
    "rules_covered": { "type": "array", "items": { "type": "string" }, "description": "rule ids (reviewer) or OWASP ids e.g. API3:2023 (security) checked and met" },
    "rules_missing": { "type": "array", "items": { "type": "string" } },
    "findings": { "type": "array", "items": {
      "type": "object", "additionalProperties": false,
      "required": ["id","rule_id","severity","file","what","why","route_to"],
      "properties": {
        "id": { "pattern": "^[RS]\\d+$" },
        "rule_id": { "type": "string", "description": "requirements.md id or OWASP API Top 10 2023 id" },
        "severity": { "enum": ["P0","P1","P2"] },
        "file": { "type": "string" }, "line": { "type": "integer" },
        "what": { "type": "string", "maxLength": 200 }, "why": { "type": "string" },
        "route_to": { "enum": ["tester","builder"] } } } },
    "notes": { "type": "array", "items": { "type": "string" } }
  }
}
```


## Semantics (checked by the hook)

- `PASS` requires no P0 or P1 findings, an empty `rules_missing`, and `rules_covered` ⊇ the node's rule ids
  (reviewer) or the stated OWASP checklist (security).
- `FAIL` requires at least one finding.
- `head_sha` and `diff_fingerprint` must equal the values in your prompt. The hook recomputes both.
- `gate_refs` names the `loop.jsonl` gate events you read. You run no commands, so they are your evidence.
- `attempt` is the value from your prompt.
- Finding ids: `R1`, `R2`, … for reviewer; `S1`, `S2`, … for security.
- Severity:
  - `P0`: the rule or acceptance is broken, or the issue is exploitable.
  - `P1`: a defect that must be fixed before merge.
  - `P2`: a minor issue that doesn't block.
- `route_to` is `tester` when a test is missing or wrong, and `builder` when the code is wrong. Every
  FAIL still goes to the investigator first.
- `what` is one line (≤ 200 characters). `why` cites the rule row, the OWASP item, or the contract line.
