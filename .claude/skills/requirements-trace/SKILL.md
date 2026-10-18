---
name: requirements-trace
description: >-
  Rule ID ↔ test traceability: the trait convention, success and failure rows per rule, red-before-green
  per rule from the trailer log, no weakened assertion, no stub left, status codes per the contract.
user-invocable: false
---
# Requirements trace

Every rule in `docs/requirements.md` §2 must be proven by tests that trace back to its ID. Read the rule
row by ID: the ID, the rule, and the **agreed interpretation**, which is what the code must do.

## The trait

- Every test that proves a rule carries `[Trait("Rule", "<ID>")]`: a string literal, one ID per attribute,
  exactly as in `docs/requirements.md`.
- `RENEW-1/2` is traited as `RENEW-1` or `RENEW-2`. A test may carry several traits. Plumbing tests carry none.

## Coverage, per node rule

1. **A success row and a failure row** wherever the rule has both. Rules with no error path (SELL-3,
   SELL-6, CANCEL-1a/1b/1c, CANCEL-2, CANCEL-5, CANCEL-6, COULD-1, COULD-2, RENEW-3a, RENEW-4) need their
   success-path rows from the API contract's success-path table.
2. **A unit trait** (Domain.Tests or Services.Tests) **and an integration trait** (Api.Tests) for every ID.
3. **Boundaries:** each side of a numeric or date boundary is its own test. For example, SELL-2 at today,
   today + 60 and today + 61; SELL-5 on the 16th birthday and the day before; CANCEL-1b at start + 14 and
   start + 15.
4. **Status codes** match the contract: 400 shape, 404 GET-1, 409 SELL-1/CANCEL-4/RENEW-6/COULD-2c, and 422
   for every other numbered rule. A 422 lists every broken rule in `violations[]`.
5. **Stub-proof success rows.** A success test reads the resource back in full through the shared
   assertion, sells at least one value of its own (builder defaults never equal the contract example), and
   asserts derived values as literals. No rule-traited test passed on the stub except a 400 shape test
   (`gate.stub` receipt). A success row a canned stub could pass doesn't cover its rule.

## The rule-coverage check (Architecture.Tests, `RuleCoverageTests`)

It runs in every `dotnet test` and works from source text:
- **U:** the IDs in the first column of the rule tables in `docs/requirements.md`. `X-1/2` expands to `X-1`
  and `X-2`.
- **T:** every `[Trait("Rule", "…")]` literal in `tests/**/*.cs`, keyed by project.
- **P:** the IDs in `tests/rule-coverage.pending.txt`, which are nodes not yet built.

It fails when:
- an ID in U∖P has no unit trait or no integration trait;
- a trait isn't in U (a typo guard);
- an ID in P already has a trait (stale pending).

## Order and integrity (reviewer, from the review packet's trailer log)

- For every node rule, its `TDD: red` commit precedes its `TDD: green` commit. The stub commit comes
  before green.
- **No assertion is weakened, deleted or skipped after its red commit.** Compare each test file at its red
  commit with HEAD. A loosened expected value, a removed `Assert`, a new `Skip`, or a changed `[Trait]`
  is a finding.
- No `NotImplementedException` remains in `src/` at HEAD.
- Every commit carries the trailers `Agent`, `Plan`, `Node`, `Rule` and `TDD`. A `fix(…)` commit carries
  `Fixes-Finding`.
- A test written from a finding fails before the fix commit and passes after it.

A rule is **covered** only when all of the above hold. Report any gap against the rule ID.
