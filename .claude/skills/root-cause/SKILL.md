---
name: root-cause
description: >-
  Root-cause diagnosis of a FAIL or a David steer: work from the evidence, one cause per finding with
  file:line, symptom vs cause, fix direction without code, testable or not, and the findings JSON.
user-invocable: false
---
# Root cause

Your output decides what the next attempt does differently. A wrong or vague cause wastes an attempt. A
repeated cause escalates the node.

## Work only from evidence

- Use the source you were given (the verdict findings, the gate receipt or David's comment), the review
  packet (diff, trailer log, gate receipts), the code at HEAD, and the rule rows in `docs/requirements.md`.
- Producer reports and commit messages are claims, not evidence.

## Method, per problem

1. **Restate the symptom** in one line: what was observed, and where.
2. **Locate the layer**: `domain`, `services`, `persistence`, `api` or `tests`. A wrong expected value in
   a test is a `tests` cause, not a code cause.
3. **Find the cause**, one per finding. Follow the behaviour from the symptom inward until you reach the
   line whose logic is wrong, and cite it as file:line. "The test fails" is a symptom. "`Policy.cs:88`
   compares `on < LatestTerm.End`, but CANCEL-7 allows `on == LatestTerm.End`" is a cause.
4. **Check it against the rule:** quote the agreed interpretation the code violates.
5. **Fix direction** in words. Say what must change, not how to code it: "include the end date in the
   CANCEL-7 comparison", not a patch.
6. **Testable?** If a test can reproduce it, give Given/When/Then plus the target test class. If it
   can't, state why concretely. "Hard to test" isn't a reason.
7. **Fingerprint:** `<rule_id>|<file>|<short cause key>`, stable across attempts. If the same cause as a
   previous attempt recurs, reuse its fingerprint exactly; the retry ladder escalates on it.
8. **Previous attempts:** read their one-line history. If the last fix addressed a symptom, say so, and
   aim this finding at the cause.

When two findings share one cause, say so in both `root_cause` fields. Never merge them silently.

## Output: the investigator findings schema v1 (your whole final message)

```json
{"schema_version":1,"plan_id":"<plan>","node_id":"<node>","attempt":1,
 "source":"reviewer|security|gate|david|pr-comment",
 "source_ref":"<verdict finding ids, gate event, or PR comment id>",
 "findings":[{"id":"I1","rule_id":"<RULE or OWASP id>","layer":"domain|services|persistence|api|tests",
   "files":["<repo-relative path>"],"root_cause":"<cause, citing file:line>","fingerprint":"<rule|file|key>",
   "testable":true,"suggested_test":"Given … When … Then … (TestClass)","untestable_reason":null}],
 "previous_attempts":["<one line each>"]}
```

- Every item in `source_ref` is covered by at least one finding.
- `testable: false` requires `untestable_reason`, and then `suggested_test` is null.
- Paths are repo-relative with forward slashes.
- If the stop hook rejects your message, restate the **complete** JSON with the errors fixed.
