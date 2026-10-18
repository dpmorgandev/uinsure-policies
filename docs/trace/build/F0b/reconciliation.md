# build/F0b: reconciliation and rulings

This file is written by the orchestrator, not rendered from the trail. It records what the trail can't say
by itself: one lost receipt, the renames, and David's rulings and waivers in this node. Each item points at
its evidence in `loop.jsonl`.

## Lost receipt: the attempt-1 reviewer FAIL

- **What was lost.** `verdicts/A1-reviewer.json` held the reviewer's FAIL for attempt 1 (head `6a615c8`,
  written 2026-10-18T00:12Z). It had one P0, R1: the app fails at start-up because there is no
  appsettings, so `Database:Provider` is null. It also had P2s R2–R9.
- **How it was lost.** Under the old round naming, the attempt-2 PASS at `455afeb` was written to the same
  file at 00:29Z and overwrote it. Harness PRs #3 and #4 fixed the naming, and verdicts are never
  overwritten now.
- **What survives.**
  - the `verdict(reviewer,FAIL,…)` and `attempt++` events in `loop.jsonl`
  - `findings/A1.md`, the investigator's diagnosis of R1–R9
  - the orchestrator's receipt that `dotnet run` failed with `OptionsValidationException` at `6a615c8`
  - the `reconciled(A1-reviewer.json)` event, which states the loss

  No verdict file was re-created by hand.

## Renames (`reconciled(…)` events)

| Now | Was | Review |
|---|---|---|
| `A1-security.json` | `D1-security.json` | attempt 1 security PASS at `6a615c8`, misfiled as D1 |
| `A2-reviewer.json`, `A2-security.json` | `A1-*` | attempt 2 PASS at `455afeb` |
| `A2.2-reviewer.json`, `A2.2-security.json` | `A2-*` | attempt 2 re-review after integrate, PASS at `238a61e` |

## David's rulings and waivers in F0b

| Ruling | Effect |
|---|---|
| Scaffold step before red (PR #1) | t1 became the builder scaffold; the plan was amended to 16 tasks |
| Exit code 8 ignored in test projects | a filter matching no test exits 0 |
| `gate.scaffold` Api leg waived (CS5001) | the rule now allows it (PR #2) |
| Pkcs 10.0.13 transitive pin | clears NU1902/NU1903 under `-warnaserror` |
| D1: launch profile on :5080 | untestable; proved by the real process |
| `DatabaseOptions` as a string plus `Validate` (R7) | accepted as built |
| `dotnet-ef` tool manifest | deferred to F5 |
| `gate.diff-scope` waived for the two carried `docs/context` snapshots | the list now allows `docs/context/**` (PR #3) |
| I4: six early tester and builder commits lack `Rule:`, and the QA commit `5c50836` carries a stray `TDD: red` | history stays; the hooks enforce this now (PR #3) |
| I5: TrxReport out of scope for F0b | revisit in F5; the other verify-in-F0 receipts are in `gate.verify-in-f0` |
| R5: SHAs rewritten by rebases onto PRs #1–#4 | git wins; the events keep the SHAs as they were when recorded |

## Harness defects this node found and fixed

PR #1, PR #2, PR #3 and PR #4. Their bodies list each defect and the fixture that pins it.
