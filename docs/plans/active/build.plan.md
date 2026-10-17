---
plan_id: build
kind: build
title: Uinsure Policies API build
created: 2026-10-17
dag: build.dag.yaml
parent: null
---
# build: Uinsure Policies API build

## Why
Deliver the Uinsure Policies API: Sell, Retrieve, Cancel and Renew plus the could-haves. Ship it with its evidence: tests traced to rule ids, IaC and a pipeline, the README and a pitch deck.
Submission is due Monday 2026-10-19.
This plan runs once and closes when the system exists. Later changes go through /plan-feature and /plan-bug.

## Approach
- build.dag.yaml is the only state. The orchestrator (/feature) picks the next node and walks its tasks in order. Status changes are commits.
- F0a is the harness, built in session with David (owner: david, 20 session tasks, spikes first, the harness lock last). F0b, the walking skeleton, is the first full loop run.
- Each feature node is outside-in TDD per layer:
  - the tester's red acceptance suite names every node rule
  - a stub controller
  - tester red, then builder green, for domain and for services where it has logic
  - builder green for persistence, then api
  - refactor, then gates, then reviewer and security in parallel
  - QA, integrate, trace, then the PR
  Every red and green task names the rule ids it covers.
- Two pauses per loop node: Pause 1 when the acceptance suite is red and the stub is green; Pause 2 for HITL sign-off from walkthrough.md, the QA script and Postman. David merges.
- File order is the schedule. F5 depends only on F0b but sits after F4.
- Budgets: F5 is time-boxed at 3h, and every other node is unset.
- Cut order: F7, then F5. If F7 hasn't started by 12:00 on Monday 2026-10-19, it's cut; if F5 hasn't started by 14:00, it's cut. F0a to F4 and F6 are never cut. A cut is David's decision and needs a lesson.

## Rejected alternatives
- **One F0 node (C1):** the harness can't be built by the loop it builds, so harness and walking skeleton are split.
- **Renumbering to F0 harness, F1 skeleton (C3):** it churns every spec's feature numbers and moves Sell off F1.
- **Retrieve before Sell (D2):** a GET has nothing to return without a sale. F1 is Sell with a bare GET, so every Sell test round-trips.
- **A feature list or manifest beside the plan (A4):** two files holding status drift apart. The one DAG file is still the only state, and the .plan.md holds prose only.
- **Named task lists instead of explicit tasks:** the orchestrator would have to expand them at run time. Explicit tasks, each with its rule ids, make each step checkable.
- **The React frontend inside this plan:** it gets its own brief, mockups and plan later. src/web/ is only reserved.

## Prior work consulted
none (no docs/plans/ and no docs/lessons.md yet)

## Attempt log
(none)

## Completion record
(none)
