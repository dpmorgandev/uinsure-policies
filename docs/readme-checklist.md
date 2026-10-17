# README checklist

Extracted in F0a (t17) by the main session, from every place a spec says the project README must state
something. Agents never read specs, so F6's builder works from this list. **F6 ticks each item and links
the README section that satisfies it.** An item marked *if cut* applies only when that node or step is cut.

How it was extracted: every `README` mention in `.claude/specs/*.spec.md`, then the ones that bind the
**project** README. Node and trail READMEs (`docs/trace/…`) are generated, not written, and are listed in
section C. Each source is cited as `spec · section`, because some obligations sit in an accepted Decision
or Recommendation rather than in the Contract text.

## A. From spec Contracts

| # | The README states… | Source | Done | README section |
|---|---|---|---|---|
| A1 | `main` is the integration branch: no `develop` branch and no integration agent. Feature and bug branches are cut from `origin/main`, rebased onto it before the PR, and merged by David with a **merge commit**, so every TDD commit and its SHA survives on `main`, and `git log --first-parent main` shows one line per feature. | orchestration · Contract (Branching) | ☐ | |
| A2 | SQLite can't prove SQL Server-specific behaviour (unique-violation codes, migrations). The pre-push idempotent migration script proves the migrations generate; the rest is a known gap. | testing · Contract C5 | ☐ | |
| A3 | The prod Approval check (environment `uinsurepolicies-prod`, approver: release manager, timeout 72 h) lives in the Azure DevOps UI, not the YAML, so the README is its only evidence. That's a platform limit, not a gap. | deployment · Contract | ☐ | |
| A4 | The one-time setup list, none of it done: (1) an ADO project and a pipeline from `infrastructure/pipelines/azure-pipelines.yml`; (2) three ARM service connections using workload identity federation, `sc-uinsurepolicies-dev\|test\|prod`, each authorised for this pipeline only; (3) each connection's identity given rights at subscription scope to its own subscription (Contributor, or a narrower custom role), because `main.bicep` creates the RG; (4) one Entra group per environment as the SQL admin (that environment's deploy identity plus a break-glass human), with its object ID and name in `<env>.bicepparam`; (5) the environments `uinsurepolicies-dev`, `-test` and `-prod`, which aren't auto-created from YAML, with the Approval check on prod. | deployment · Contract (One-time setup) | ☐ | |
| A5 | **Verbatim:** "**Never connected, never run.** This pipeline has never been connected to an Azure DevOps organisation or run. No service connections, environments or approvals exist. The Bicep is linted and built locally, and the YAML is schema-checked locally. Neither what-if nor a real pipeline compile was run, because both need Azure or Azure DevOps. The repo's real gate is its local git hooks (requirements §3, "No CI"). This pipeline shows how CI/CD would look at Uinsure." | deployment · Contract (README statement) | ☐ | |
| A6 | *If the schema check is cut:* the YAML is "unvalidated". *Otherwise* the claim is exactly: "the YAML is valid against the schema; template expansion, `${{ }}` expressions, task inputs, service connections and environments are not checked". | deployment · Contract (schema check, cut order) | ☐ | |
| A7 | **Status:** "The Bicep is linted and built locally and never deployed. what-if was not run, because it needs a signed-in subscription." | infrastructure · Contract, paragraph 1 | ☐ | |
| A8 | **Shape:** subscription scope, 6 resources per environment, 3 modules, 3 `.bicepparam` files, plus the sizing table. | infrastructure · Contract, paragraph 2 | ☐ | |
| A9 | **No secrets:** a system-assigned managed identity connects to Entra-only SQL, so there are no passwords. **Key Vault was considered and rejected**: there's nothing to store. It arrives with the first real secret, such as a payment API key. | infrastructure · Contract, paragraph 3 | ☐ | |
| A10 | **Next step, networking:** a private endpoint for SQL, VNet integration for the app, public network access off, and a self-hosted or Managed DevOps Pool agent in the VNet. (Today: a public endpoint plus the Azure-services rule.) | infrastructure · Contract, paragraph 4; Decision | ☐ | |
| A11 | **.NET 10 runtime:** "`DOTNETCORE\|10.0` is inferred from the documented pattern and a Nov 2025 Q&A. It wasn't confirmed with `az webapp list-runtimes`." | infrastructure · Contract, paragraph 5 | ☐ | |
| A12 | *If F5 is cut:* the README says so and keeps A9 (no secrets, Key Vault rejected) as the design intent. A3–A11 are then marked "cut". | infrastructure · Contract (cut order) | ☐ | |

## B. From accepted Decisions and Recommendations

| # | The README states… | Source | Done | README section |
|---|---|---|---|---|
| B1 | Security scope, OWASP API Top 10 2023. These are stated as out of scope: **API1** (no auth or tenancy; the reference is the only key, and references are client-chosen and guessable), **API2** (authentication is out of scope), **API5** (no roles; `POST /claims` would be an admin function in real life), **API7** (no outbound URL fetching), **API10** (no upstream APIs; pricing is client-supplied). | agents-skills · Research R6 (adopted by the owasp-api-security skill) | ☐ | |
| B2 | **Authentication is out of scope** by requirement. `UseAuthorization()` stays in the pipeline at its usual position, ready for it. | architecture · Recommendation (Auth); Contract C6 | ☐ | |
| B3 | **`Location` on renewals and claims** is `/policies/{reference}` (the policy), not a sub-resource. That's a deliberate deviation. | api-contract · Decision D4 (O5b) | ☐ | |
| B4 | **No API versioning.** A breaking change would mean `/v2` or an `api-version` parameter (`Asp.Versioning`). | api-contract · Decision D10 (O10a) | ☐ | |
| B5 | **Idempotency:** `POST /policies` is safe to retry, because a repeated reference gets 409 (SELL-1). **Claims are not idempotent**, and there's no `Idempotency-Key`. | api-contract · Recommendation 6 (accepted with the D-list) | ☐ | |
| B6 | **No £0 refunds:** a cancellation creates a Refund only for an amount above £0. A term with a claim (COULD-2) or an unpaid renewal term (RENEW-3b) gets no Refund record, but its calculation is still returned, with `ruleApplied` `COULD-2` or `UNPAID-TERM` and 0.00. | architecture · Open question 1 (resolved); api-contract · Contract | ☐ | |

## C. Not for the project README

These are generated or trail READMEs, so they aren't ticked here:
- **F0a trail README** (`docs/trace/build/F0a/README.md`, task t19): states "hooks not yet live: witnessed by
  git and trailers only", and notes every F0a probe result (traceability · Contract, F0a exception;
  guardrails · Contract, wiring self-test).
- **Node READMEs** (`docs/trace/<plan>/<node>/README.md`): generated by `render-trace.mjs`, with
  `TRAIL INCOMPLETE: <reasons>` first when the check fails (traceability · Contract).
