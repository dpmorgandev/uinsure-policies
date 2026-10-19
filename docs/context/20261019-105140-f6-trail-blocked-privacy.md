---
timestamp: 2026-10-19T10:51:40Z
previous-context: 20261019-083752-f5-done-merged.md
checkpoint: false
status: blocked
plan: build
node: F6
phase: loop
step: null
attempt: 4
branch: feature/build/F6
worktree: .claude/worktrees/build-F6
head: 7a293dd
tests: 637/637
next-action: David decides the privacy-scan false positive blocking the F6 trail commit; then t6, PR body, his paragraph
open-decisions: 2
---

## Summary
F6 (README) passed review at attempt 4 (A4 PASS for both agents at 46b175e). The README is 92 lines (how to run,
assumptions, known limits) with reference docs docs/design.md and infrastructure/README.md; every factual sentence is
traced in docs/trace/build/F6/claims.json (431 claims, 1006 anchors verified). QA 57/57 and the first-run path pass;
integrated onto 181db37 (637/637, fingerprint unchanged). The t6 trail commit is blocked by the pre-commit privacy scan.

## Completed since last snapshot
- Harness PR #35: docs nodes may write docs/design.md and infrastructure/README.md (99e2f36)
- F6 acceptance amended to the assessor's short-README ask (b91958c); inv-003 opened, decided, applied (214938a, 78992dc)
- README and reference docs rewritten with a claim trace (46b175e); review A4 PASS; QA 57/57 (c657a94); integrate (32ea892)

## Current state
feature/build/F6 at 7a293dd, tests 637/637. The trail files are staged (18 under docs/trace/build/F6 plus
docs/trace/requirements-matrix.md). Blocker: the committed denylist pattern for key/secret/token/password followed by
":" and 16+ path-like characters matches prose (the word key, a colon, then a repo path or test file name) in
docs/trace/build/F6/findings/A3.md:14 and docs/trace/build/F6/verdicts/A3-reviewer.json:40. No secret is present.
No --no-verify.

## Next action
David chooses how the false positive is cleared (see Open decisions). Then: the chore(trace) F6 trail commit, t6
done, the PR body, David's paragraph, PR, Pause 2 and sign-off, merge.

## Carry-overs
- README wording (A4 P2s, carried): infrastructure/README.md "aren't auto-created from YAML" unproven (R1); "not checked" vs the checker's partial input checks (R2); design.md COULD-2 stated per term though policy-wide (R3); README "every term with refundAmount 0.00" reads too broadly (R4) — F6 review A4; David to decide before the PR
- Bug plan (inv-003 rec 4, accepted): pin error-key casing (?REFERENCE observed as reference, ?COLOUR as cOLOUR) and lower-case enum names with tests — inv-003
- An intermittent test failure seen once (1 of 637, name not captured) and not reproduced in 10+ later runs — F6 t1 gates
- Lessons required at archive: F2 (inv-001), F5 (inv-002), F6 (attempt 4, inv-003) — planning.spec Lessons
- F7 stays pending, not cut (David: decide what it needs to be later)
- Harness items: the privacy pattern above; tester cannot write infrastructure/** or delete the dev db; Fixes-Finding id forms for reviewer and QA findings; git grep of specs not guarded; the activity hook re-dirties a worktree on cleanup; pr-body refactor/Attempts/Rules lines; gate.postman grammar; dotnet-ef tool manifest; and the earlier queue in 20261019-083752-f5-done-merged.md
- Schedule: none (F7 pending by David's choice)

## Open decisions
- Privacy false positive: reword the two lines of agent records, or a harness PR narrowing the key:value denylist pattern so repo paths don't match — guardrails privacy scan
- Fix the A4 R3 and R4 wording (assessor-facing) before the PR, or carry them — rule 3

## Decisions made
- docs/plans/active/inv-003.plan.md#recommendations (accept 1-4)
- F6 acceptance: short README + separate docs (David, 2026-10-19), docs/plans/active/build.dag.yaml F6
- F7 pending, not cut (David, 2026-10-19)

## Pointers
- docs/plans/active/build.dag.yaml (F6), docs/plans/active/inv-003.plan.md
- docs/trace/build/F6/ (verdicts A2-A4, findings A1-A3, claims.json), docs/qa/build-F6.md
- PR #35 (harness)
