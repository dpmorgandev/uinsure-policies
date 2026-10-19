# Design notes

Reference material for the [project README](../README.md). The rules and the interpretation agreed for each
are in [requirements.md](requirements.md). Deployment is in
[infrastructure/README.md](../infrastructure/README.md).

Contents: [Running locally](#running-locally) · [Endpoints](#endpoints) · [Errors](#errors) ·
[Tests](#tests) · [Branching and merging](#branching-and-merging) · [Design decisions](#design-decisions) ·
[Security scope](#security-scope) · [Known limits](#known-limits) ·
[Behaviours of the renewal and input handling](#behaviours-of-the-renewal-and-input-handling)

## Running locally

- Launch profiles: `http` (`http://localhost:5080`, used for development and Postman) and `https`
  (`https://localhost:7080;http://localhost:5080`). Both set `ASPNETCORE_ENVIRONMENT=Development`.
- Locally the app uses SQLite (`Database:Provider` = `Sqlite`, file `policies.development.db`). With SQLite it
  creates the schema at start-up with `EnsureCreated()`. It never calls `Database.Migrate()`.
- The Bicep sets the provider to `SqlServer` with an app setting. The one migration set is SQL Server only
  (`src/Uinsure.Policies.Persistence/Migrations`).
- In Development only: the OpenAPI 3.1 document at `/openapi/v1.json` and the Scalar UI at `/scalar`.
- Health: `/health/live` runs no checks. `/health/ready` runs the checks tagged `ready` (the database).
- Application Insights is wired only when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set.

## Endpoints

Policy routes are on `PoliciesController`.

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

A rule violation (the `404` for GET-1, a `409` rule conflict, a `422`) is RFC 9457 problem details with `type`
(`/problems/{rule-id}`, lower-case, `/` written as `-`), `title`, `status`, `detail`, `instance`, `ruleId` and
`traceId`. The other problems have no `ruleId`: the `400` shape error, the concurrency `409`
(`/problems/concurrency`), the `429` (`/problems/rate-limited`), the HTTPS `400` (`/problems/https-required`) and
the `500`. Rule problems, transport problems (`429`, HTTPS `400`, `413`) and the `400` shape error all carry
`instance` (the request path) and `traceId`, and the tests assert both. The `500` carries a `traceId`, which a test
also asserts.

| Status | When |
|---|---|
| `400` | Shape: `errors` map, no `ruleId`. A `400` short-circuits before the rules are evaluated |
| `404` | GET-1: unknown reference. A `404` short-circuits before the rules are evaluated |
| `409` | Rule conflicts (SELL-1, CANCEL-4, RENEW-6, COULD-2c), which short-circuit before the rules are evaluated. A database concurrency conflict is also a `409`, without a `ruleId` |
| `422` | The rules that can be broken: SELL-2, SELL-4, SELL-5, SELL-7, RENEW-1/2, RENEW-3b, RENEW-5, CANCEL-3, CANCEL-7, COULD-2a, COULD-2b, COULD-3. All broken rules are collected in `violations[]`; `ruleId` is the first of them |
| `429` | Rate limit, with `Retry-After` |
| `500` | A generic `detail` and a `traceId`. The full details go to the logs only, and never a stack trace outside Development |

Two details that surprise people:

- **Validation error keys.** For a body or model error, the API lower-cases the first letter of each dot-separated segment of the key (`ValidationProblems.cs`); for example, the key for a policyholder’s date of birth is `policyholders[0].dateOfBirth`. A query key that the action does not bind from the query string is a `400`, with `Unknown query parameter.` under that key in `errors`. A key named after a route parameter is one of these: `GET /policies/{reference}?reference=X` is a `400` keyed `reference`. No test sends a query key in upper case, so the key returned for one is not specified. The renewal endpoint with no body writes its key, `amount`, by hand in the controller.
- Outside Development the API rejects plain HTTP with a `400` (`/problems/https-required`) instead of
  redirecting. `/health/*` is exempt. There is no `UseHttpsRedirection()`.

## Tests

```sh
dotnet test --solution Uinsure.Policies.slnx
```

Result measured on this branch on 2026-10-19: **637 passed, 0 failed, 0 skipped.**

| Project | Tests | What it proves |
|---|---|---|
| `Uinsure.Policies.Domain.Tests` | 173 | Rule and boundary tests, with `DateOnly today` passed in |
| `Uinsure.Policies.Services.Tests` | 65 | Orchestration and `UkCalendar` (including the BST boundary) |
| `Uinsure.Policies.Api.Tests` | 332 | The acceptance suite over HTTP: `WebApplicationFactory`, SQLite in memory, `FakeTimeProvider` |
| `Uinsure.Policies.Architecture.Tests` | 67 | Layering (NetArchTest) and rule coverage |

- xUnit v3 on Microsoft.Testing.Platform, plain `Assert`.
- A rule test carries `[Trait("Rule", "<id>")]`. `RuleCoverageTests` fails if a rule ID in
  `docs/requirements.md` has no unit trait or no integration trait, unless it is listed in
  `tests/rule-coverage.pending.txt` (empty at the moment). Run one rule: `dotnet test --solution Uinsure.Policies.slnx -- --filter-trait "Rule=SELL-2"`.
- Postman: `tests/postman/Uinsure.Policies.postman_collection.json` (109 requests, in six folders from `F0b Walking skeleton` to `F4 Renew`)
  with the `local` environment (`baseUrl` = `http://localhost:5080`). Run it against a running API with the
  Postman CLI:

  ```sh
  postman collection run tests/postman/Uinsure.Policies.postman_collection.json \
    -e tests/postman/local.postman_environment.json
  ```

  No git hook runs Postman. The collection holds the rows that can run in real time
  (the convention in requirements section 3). Rows that need a fake clock are covered by the integration tests.
- Manual QA scripts are in `docs/qa/`.

### What SQLite cannot prove

The tests run on SQLite. SQLite cannot prove SQL Server-specific behaviour: unique-violation error codes and
the migrations themselves. The pre-push idempotent migration script
(`dotnet ef migrations script --idempotent`) proves the migrations generate. The rest is a known gap.

### Local git hooks

The repository has no CI for the code: the pipeline in `infrastructure/` was never connected (see
[infrastructure/README.md](../infrastructure/README.md)). The local gate is the hooks in `.githooks/`:

- `pre-commit`: refuses a commit on `main` once the `main-locked` marker exists, then a privacy scan of staged
  files, then `dotnet build -warnaserror`.
- `commit-msg`: on `feature/*` and `bug/*` branches, checks the `Agent`, `Plan` and `Node` trailers (and `Rule`, plus
  `TDD` unless the subject starts `docs(`, on tester and builder commits), and that `Plan` and `Node` match the
  branch.
- `pre-push`: refuses a push to `main` once it is locked, then a Release build, the full test suite, the
  idempotent migration script, and the harness self-test.

## Branching and merging

`main` is the integration branch. There is no `develop` branch and no integration agent. Feature and bug
branches are rebased onto `origin/main` before the pull request, and merged by the repository owner
in the GitHub UI with a **merge commit**. That is the practice followed, not something the repo enforces: the
merge method is a GitHub setting. With a merge commit the branch's commits (as rebased) stay on `main`, and
`git log --first-parent main` shows one `Merge pull request` line per merged pull request.

On feature and bug branches, tester and builder commits carry the trailers `Agent`, `Plan`, `Node` and `Rule`, and a
`TDD` trailer (`scaffold`, `red`, `stub`, `green` or `refactor`) unless the subject starts `docs(`. A rule's red and
green commits can be found through its `Rule` trailer.

## Design decisions

### Layers

`Api` → `Services` → `Domain`, and `Persistence` → `Services`. Domain has no packages and no references.
Services orchestrates: load, today, domain, save. GET-1 and SELL-1 are produced in `PolicyService`: GET-1
when the repository finds no policy, and SELL-1 (uniqueness) through the repository. There is one model per
layer (API DTOs, domain, persistence records). Interfaces exist at the layer seams only
(`IPolicyService`, `IPolicyRepository`, `IReferenceGenerator`). There is no generic repository and no unit of
work. Architecture tests enforce these references.

Considered and rejected: domain events, caching, CQRS or MediatR, Key Vault (there are no secrets), and
authentication.

### Authentication

Authentication is out of scope by requirement. No authentication is configured. `UseAuthorization()` is in
the pipeline, ready for it.

### `Location` on renewals and claims

`201` on `POST /policies/{reference}/renewals` and `POST /policies/{reference}/claims` returns
`Location: /policies/{reference}`: the policy, not a sub-resource. That is a deliberate deviation. A renewal
adds a term and a claim adds a claim, and the client reads both through the policy.

### No API versioning

There is no versioning. A breaking change would mean `/v2` or an `api-version` parameter (`Asp.Versioning`).

### Idempotency

`POST /policies` is safe to retry: a repeated reference gets `409` (SELL-1). **Claims are not idempotent**, and
there is no `Idempotency-Key`. `Policy.RecordClaim` has no duplicate check, and a second claim is added after the
first (tested).

### No refunds of zero

A cancellation creates a Refund only for an amount above £0. If the policy has a claim (COULD-2), none of its
paid terms gets a Refund record; an unpaid renewal term (RENEW-3b) gets none either. Their calculations are
still in the response, with `ruleApplied` `COULD-2` or `UNPAID-TERM` and `refundAmount` 0.00.

### Terms

A policy has terms. A renewal appends the next term under the same reference (RENEW-4). Policy-level
`startDate`, `endDate` and `amount` describe the current term. `terms[]` holds the history. The status is
derived from today and never stored.

## Security scope

The development loop includes a security review against the OWASP API Security Top 10 2023 (requirements
section 3). These items are out of scope, stated rather than skipped:

- **API1 (broken object-level authorisation):** there is no auth or tenancy. The reference is the only key,
  and references are client-chosen and guessable.
- **API2 (broken authentication):** authentication is out of scope.
- **API5 (broken function-level authorisation):** there are no roles. In real life `POST /claims` would be an
  admin function.
- **API7 (SSRF):** the API makes no outbound URL fetches.
- **API10 (unsafe consumption of APIs):** there are no upstream APIs. Pricing is supplied by the client.

In scope and built: resource limits (a 64 KiB body cap, a per-IP rate limit), strict input, no passwords in the
Bicep (the app signs in to SQL with a managed identity), log calls that name no personal field, and a generic 500
body. Rate limits are described under [Known limits](#known-limits).

**Exposure if deployed.** The Bicep sets no IP restriction on the web app (`appservice.bicep` has no
`ipSecurityRestrictions`), and its 16 resources include no Front Door, WAF or API gateway. With no
authentication and client-chosen, guessable references, anyone who reaches the host and knows or guesses a
reference could read that policy (names, dates of birth, address) and post claims, cancellations or renewals
to it. The API's own protections against volume are the per-client, per-instance rate limits and the request
body cap. Nothing has been deployed, so this is read from the Bicep and has not been observed on a running
service.

Two consequences are worth stating plainly:

- **A claim costs the policyholder their refund, permanently.** `POST /policies/{reference}/claims` sets the
  policy's claim flag, and from then on every cancellation refunds £0.00 on every paid term (COULD-2), including
  inside cooling-off. There is no endpoint to remove a claim, and a cancellation can't be undone. So an
  anonymous caller who guesses a reference can take a refund away from its holder. A `404` against a `200`
  tells a caller whether a guessed reference exists, at up to 100 requests per minute per client.
- **The Kudu (SCM) site is public too.** The Bicep turns off basic publishing credentials for SCM and FTP
  (`appservice.bicep`), but sets no `scmIpSecurityRestrictions`. The SCM site stays reachable from the
  internet for anyone holding an Entra token with rights on the web app.

## Known limits

- **Rate limits are per instance.** The limiter is the built-in in-memory fixed window, per client IP. Each
  instance counts on its own: the limiter holds its counts in memory and the code uses no shared store. Defaults
  outside Development: 100 requests per minute globally and 10 per minute on every `POST` (the `writes` policy).
  Development: 10,000 and 1,000. Both come from the `RateLimiting` configuration section.
- **Forwarded headers are configured, not tested on App Service.** The code honours `X-Forwarded-For` and
  `X-Forwarded-Proto` only from 10.0.0.0/8, 172.16.0.0/12 and 192.168.0.0/16, and tests cover that with a
  simulated proxy address. Whether App Service's front end connects from those ranges has not been tested. A
  request from any other address has its forwarded headers ignored. Outside Development, a plain-HTTP request
  that sends `X-Forwarded-Proto: https` from such an address gets the HTTPS `400`, and the rate limiter counts
  by the connecting address, not by `X-Forwarded-For` (both tested).
- **No cap on claims per policy.** Each claim POST appends a claim, and `GET` returns them all. There is no
  cap. The per-client, per-instance write rate limit applies to the claims route. No rule in the requirements asks
  for a cap.
- **Few application log lines.** There is no log line for a successful or a 4xx rule outcome. The log calls in
  `src/` are a client abort (Information), a bad request (Warning), a persistence conflict (Warning) and an
  unhandled exception (Error). Their templates carry the method, path, status or reason, and no call passes a
  name, date of birth, address, postcode or request body. The bad-request and unhandled-exception calls also
  pass the exception object, whose content this code does not control.
- **`400` bodies name internal types.** `AllowInputFormatterExceptionMessages` is left at its default, so a
  JSON error passes on the serialiser's message, for example `could not be converted to
  System.Nullable`1[Uinsure.Policies.Api.Contracts.PaymentMethod]`, with line and byte positions. That helps a
  client fix its request, and it also fingerprints the stack.
- **No caching or sniffing headers.** No middleware sets `Cache-Control: no-store` or
  `X-Content-Type-Options: nosniff`, so a shared cache could keep a `GET /policies/{reference}` response with
  names, dates of birth and the address.
- **`/health/ready` is not rate-limited** and checks the database on every call (`.DisableRateLimiting()` in
  `Program.cs`, `AddDbContextCheck`), so a flood of it opens a database connection per request.
- **SQLite versus SQL Server:** see [What SQLite cannot prove](#what-sqlite-cannot-prove).
- **`Policy.Renew` assumes an auto-renewing term has a payment.** On auto-renew it reads
  `CurrentTerm(today).Payment!.Method` when the request sends no method. A term of an auto-renewing policy gets
  a payment when it is created (SELL-6 on sale, RENEW-3a on renewal). The code does not check for a missing
  payment: it uses the null-forgiving `!` on `Payment`.

## Behaviours of the renewal and input handling

These are behaviours the written rules do not fully fix.

- **Renewal detail texts are the code's wording.** RENEW-1/2 says "A policy can be renewed from W to E."
  RENEW-6 says "The policy was cancelled on T, so it cannot be renewed." COULD-3 says "An auto-renewing policy
  cannot be renewed by Cheque. Use Card or DirectDebit." for both a sent and a defaulted Cheque. These differ
  from the example texts in the API contract written before the build (noted in `docs/qa/build-F4.md`). The
  requirement only asks that the COULD-3 `detail` asks for another method. The dates are also in the
  `windowStart`, `windowEnd` and `cancellationDate` extensions.
- **A multi-violation 422 has no top-level rule extensions.** With two or more violations the top level
  carries no `windowStart`, `windowEnd`, `amount` or `paymentMethod`. A `violations[]` entry carries only
  `ruleId` and `detail` (`ResultExtensions.cs`). The extensions are copied to the top level only for a single
  violation.
- **A `null` `paymentMethod` counts as none.** On a policy that does not auto-renew, `"paymentMethod": null`
  is not RENEW-3b, because the rule says a request that "sends" a method gets 422. Observed by hand
  (`docs/qa/build-F4.md`); no test pins this.
- **Enum names were accepted in lower case when tried by hand.** `"card"` was accepted as `Card` on renewal and
  `"household"` on sale (`docs/qa/build-F4.md`). No test sends a lower-case enum name, so this is observed, not
  pinned. Integers are refused (`JsonStringEnumConverter` with `allowIntegerValues: false`).
