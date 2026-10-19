# Design notes

Reference material for the [project README](../README.md). The rules and the interpretation agreed for each
are in [requirements.md](requirements.md). This file does not repeat them. Deployment is in
[infrastructure/README.md](../infrastructure/README.md).

Contents: [Running locally](#running-locally) · [Endpoints](#endpoints) · [Errors](#errors) ·
[Tests](#tests) · [Branching and merging](#branching-and-merging) · [Design decisions](#design-decisions) ·
[Security scope](#security-scope) · [Known limits](#known-limits) ·
[Behaviours to review](#behaviours-to-review)

## Running locally

- Launch profiles: `http` (`http://localhost:5080`, used for development and Postman) and `https`
  (`https://localhost:7080;http://localhost:5080`). Both set `ASPNETCORE_ENVIRONMENT=Development`.
- Locally the app uses SQLite (`Database:Provider` = `Sqlite`, file `policies.development.db` in the project
  folder). It creates the schema at start-up with `EnsureCreated()`. It never calls `Database.Migrate()`.
- In the deployed environments the provider is `SqlServer`, set by an app setting. The one migration set is
  SQL Server only (`src/Uinsure.Policies.Persistence/Migrations`).
- In Development only: the OpenAPI 3.1 document at `/openapi/v1.json` and the Scalar UI at `/scalar`.
- Health: `/health/live` runs no checks. `/health/ready` runs the checks tagged `ready` (the database).
- Application Insights is wired only when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set.

## Endpoints

All routes are on `PoliciesController`.

| Route | What | Success |
|---|---|---|
| `POST /policies` | Sell a policy (SELL-1 to SELL-7) | `201` + `Location: /policies/{reference}` |
| `GET /policies/{reference}` | Retrieve it (GET-1) | `200` |
| `POST /policies/{reference}/cancellation` | Cancel. The body is optional (`cancellationDate`) | `201`, no `Location` |
| `GET /policies/{reference}/cancellation-quote?cancellationDate=yyyy-MM-dd` | Quote a refund without cancelling (COULD-1) | `200` |
| `POST /policies/{reference}/claims` | Record a claim (`claimDate`, optional `description`) | `201` + `Location: /policies/{reference}` |
| `POST /policies/{reference}/renewals` | Renew (`amount`, optional `paymentMethod`) | `201` + `Location: /policies/{reference}` |

Policy types are `Household` and `BuyToLet`. Payment methods are `Card`, `DirectDebit` and `Cheque`. The
client chooses the reference. It is trimmed and upper-cased, so `abc-1` and `ABC-1` are the same policy.

Input is strict. A malformed body, a wrong type, a missing field, an unknown JSON field, an integer where an
enum is expected and an unknown query parameter are all a `400`. A request body is capped at 65,536 bytes
(`413` over that).

"Today" is today in Europe/London, taken from the injected `TimeProvider` in Services and passed to the domain
as a `DateOnly`. The code never uses `DateTime.Now`, `UtcNow` or `Today`; an analyser bans them
(`BannedSymbols.txt`). Money is rounded only in `Money.Round` (2 dp, away from zero).

## Errors

Every problem is RFC 9457 problem details with `type` (`/problems/{rule-id}`, lower-case), `title`,
`status`, `detail`, `instance`, `ruleId` and `traceId`.

| Status | When |
|---|---|
| `400` | Shape: `errors` map, no `ruleId`. Checked first |
| `404` | GET-1: unknown reference |
| `409` | State conflicts (SELL-1, CANCEL-4, RENEW-6, COULD-2c) and a database concurrency conflict. Returned alone |
| `422` | Every numbered rule. All broken rules are collected in `violations[]`; `ruleId` is the first of them |
| `429` | Rate limit, with `Retry-After` |
| `500` | A generic `detail` and a `traceId`. The exception goes to the logs only, never a stack trace in the body |

Two details that surprise people:

- **Validation error keys are camelCase.** An unknown query key `?REFERENCE=x` is a `400` whose `errors` key is
  `reference`, not `REFERENCE`. That is the JSON naming policy applied to the key. It is cosmetic.
- Outside Development the API rejects plain HTTP with a `400` (`/problems/https-required`) instead of
  redirecting. `/health/*` is exempt. There is no `UseHttpsRedirection()`.

## Tests

```sh
dotnet test --solution Uinsure.Policies.slnx
```

Result at F6: **637 passed, 0 failed, 0 skipped.**

| Project | Tests | What it proves |
|---|---|---|
| `Uinsure.Policies.Domain.Tests` | 173 | Every rule and boundary, with `DateOnly today` passed in |
| `Uinsure.Policies.Services.Tests` | 65 | Orchestration and `UkCalendar` (including the BST boundary) |
| `Uinsure.Policies.Api.Tests` | 332 | The acceptance suite over HTTP: `WebApplicationFactory`, SQLite in memory, `FakeTimeProvider` |
| `Uinsure.Policies.Architecture.Tests` | 67 | Layering (NetArchTest) and rule coverage |

- xUnit v3 on Microsoft.Testing.Platform, plain `Assert`.
- Each rule test carries `[Trait("Rule", "<id>")]`. A check fails if any rule ID in `docs/requirements.md`
  has no test. Run one rule: `dotnet test --solution Uinsure.Policies.slnx -- --filter-trait "Rule=SELL-2"`.
- Postman: `tests/postman/Uinsure.Policies.postman_collection.json` (109 requests in folders `F0b` to `F4`)
  with the `local` environment (`baseUrl` = `http://localhost:5080`). Run it against a running API with the
  Postman CLI:

  ```sh
  postman collection run tests/postman/Uinsure.Policies.postman_collection.json \
    -e tests/postman/local.postman_environment.json
  ```

  Postman runs by hand only; no git hook runs it. It holds only rows that can run in real time. Rows that need
  a fake clock are integration tests only.
- Manual QA scripts, one per node, are in `docs/qa/`.

### What SQLite cannot prove

The tests run on SQLite. SQLite cannot prove SQL Server-specific behaviour: unique-violation error codes and
the migrations themselves. The pre-push idempotent migration script
(`dotnet ef migrations script --idempotent`) proves the migrations generate. The rest is a known gap.

### Local git hooks

There is no CI for the code (see [infrastructure/README.md](../infrastructure/README.md)). The gate
is the hooks in `.githooks/`:

- `pre-commit`: a privacy scan of staged files, then `dotnet build -warnaserror`.
- `pre-push`: a Release build, the full test suite, the idempotent migration script, and the harness self-test.

## Branching and merging

`main` is the integration branch. There is no `develop` branch and no integration agent. Feature and bug
branches are cut from `origin/main`, rebased onto it before the pull request, and merged by the author with a
**merge commit**. Every TDD commit and its SHA therefore survives on `main`, and
`git log --first-parent main` shows one line per feature.

Commits carry the trailers `Agent`, `Plan`, `Node`, `Rule` and `TDD` (`scaffold`, `red`, `stub`, `green` or
`refactor`), so each rule can be traced from its red test to its green commit.

## Design decisions

### Layers

`Api` → `Services` → `Domain`, and `Persistence` → `Services`. Domain has no packages and no references.
Services orchestrates only: load, today, domain, save. The rules live in the domain. There is one model per
layer (API DTOs, domain, persistence records). Interfaces exist at the layer seams only
(`IPolicyService`, `IPolicyRepository`, `IReferenceGenerator`). There is no generic repository and no unit of
work. Architecture tests enforce these references.

Considered and rejected: domain events, caching, CQRS or MediatR, Key Vault (there are no secrets), and
authentication.

### Authentication

Authentication is out of scope by requirement. `UseAuthorization()` stays in the pipeline at its usual
position, ready for it.

### `Location` on renewals and claims

`201` on `POST /policies/{reference}/renewals` and `POST /policies/{reference}/claims` returns
`Location: /policies/{reference}`: the policy, not a sub-resource. That is a deliberate deviation. A renewal
adds a term and a claim adds a claim, and the client reads both through the policy.

### No API versioning

There is no versioning. A breaking change would mean `/v2` or an `api-version` parameter (`Asp.Versioning`).

### Idempotency

`POST /policies` is safe to retry: a repeated reference gets `409` (SELL-1). **Claims are not idempotent**, and
there is no `Idempotency-Key`. Posting the same claim twice records two claims.

### No refunds of zero

A cancellation creates a Refund only for an amount above £0. A term with a claim (COULD-2) or an unpaid
renewal term (RENEW-3b) gets no Refund record. Its calculation is still in the response, with `ruleApplied`
`COULD-2` or `UNPAID-TERM` and `refundAmount` 0.00.

### Terms

A policy has terms. A renewal appends the next term under the same reference (RENEW-4). Policy-level
`startDate`, `endDate` and `amount` describe the current term. `terms[]` holds the history. The status is
derived from today and never stored.

## Security scope

Checked against the OWASP API Security Top 10 2023. Reviewed on every node. These are out of scope, stated
rather than skipped:

- **API1 (broken object-level authorisation):** there is no auth or tenancy. The reference is the only key,
  and references are client-chosen and guessable.
- **API2 (broken authentication):** authentication is out of scope.
- **API5 (broken function-level authorisation):** there are no roles. In real life `POST /claims` would be an
  admin function.
- **API7 (SSRF):** the API makes no outbound URL fetches.
- **API10 (unsafe consumption of APIs):** there are no upstream APIs. Pricing is supplied by the client.

In scope and built: resource limits (a 64 KiB body cap, a per-IP rate limit), strict input, no secrets, no
personal data in logs, and a generic 500 body. Rate limits are described under [Known limits](#known-limits).

## Known limits

- **Rate limits are per instance.** The limiter is the built-in in-memory fixed window, per client IP. Each
  App Service instance counts on its own, so with N instances the real limit is up to N times higher.
  Defaults outside Development: 100 requests per minute globally and 10 per minute on every `POST` (the
  `writes` policy). Development: 10,000 and 1,000. Both come from the `RateLimiting` configuration section.
  Behind App Service the client IP is read from `X-Forwarded-For` only when the proxy is in 10.0.0.0/8,
  172.16.0.0/12 or 192.168.0.0/16.
- **No cap on claims per policy.** Each claim POST appends a claim, and `GET` returns them all. Growth is
  bounded only by the write rate limit. No rule asks for a cap, and it was left out on purpose.
- **Few application log lines.** There is no log line for a successful or a 4xx rule outcome. The app logs only
  a client abort (Information), a bad request, a persistence conflict (Warning) and an unhandled exception
  (Error). Logs carry the method and path, never names, dates of birth, addresses, postcodes or bodies.
- **SQLite versus SQL Server:** see [What SQLite cannot prove](#what-sqlite-cannot-prove).
- **`Policy.Renew` relies on every auto-renewing term being paid.** On auto-renew it reads
  `CurrentTerm(today).Payment!.Method`. That is safe because every term of an auto-renewing policy is created
  with a payment. If stored data ever broke that invariant, it would throw a `NullReferenceException` (a 500).
  No test breaks the invariant. (Noted in the F4 security review.)

## Behaviours to review

These came out of the F4 (renew) review. Each is a decision the author should confirm.

- **E6 detail texts differ from the contract's examples.** RENEW-1/2 says "A policy can be renewed from W to E."
  RENEW-6 says "The policy was cancelled on T, so it cannot be renewed." COULD-3 says "An auto-renewing policy
  cannot be renewed by Cheque. Use Card or DirectDebit." for both a sent and a defaulted Cheque. The
  requirement only asks that the `detail` asks for another method. The dates are in the `windowStart`,
  `windowEnd` and `cancellationDate` extensions.
- **A multi-violation 422 has no top-level rule extensions.** With two or more violations the top level
  carries no `windowStart`, `windowEnd`, `amount` or `paymentMethod`. A `violations[]` entry carries only
  `ruleId` and `detail`. The contract defines the extensions only for a single violation.
- **A `null` `paymentMethod` counts as none.** On a policy that does not auto-renew, `"paymentMethod": null`
  is not RENEW-3b, because the rule says a request that "sends" a method gets 422. No test pins this.
- **Enum names are case-insensitive.** `"card"` is accepted as `Card`, on sale and renewal alike. Integers are
  refused.
