# Uinsure Policies API

A C# REST API for home insurance policies: sell, retrieve, cancel (with a refund quote), record claims and
renew. It is a take-home for a Senior Software Engineer interview. It was built outside-in with TDD, one
feature at a time.

Every rule, its ID and the interpretation we agreed for it are in [docs/requirements.md](docs/requirements.md).
This README does not repeat them. It says how to run the project, what it does not do, and where the
decisions and limits are.

Contents: [Run it](#run-it) · [Endpoints](#endpoints) · [Errors](#errors) · [Tests](#tests) ·
[Branching and merging](#branching-and-merging) · [Design decisions](#design-decisions) ·
[Security scope](#security-scope) · [Known limits](#known-limits) ·
[Behaviours to review](#behaviours-to-review) · [Infrastructure and deployment](#infrastructure-and-deployment)

## Run it

Needs the .NET 10 SDK (`global.json` pins 10.0.100 and rolls forward within the feature band).

```sh
dotnet build Uinsure.Policies.slnx -warnaserror
dotnet run --project src/Uinsure.Policies.Api --launch-profile http     # http://localhost:5080
```

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

Current result: **637 passed, 0 failed, 0 skipped.**

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

There is no CI for the code (see [Infrastructure and deployment](#infrastructure-and-deployment)). The gate
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

## Infrastructure and deployment

> **Never connected, never run.** This pipeline has never been connected to an Azure DevOps organisation or run.
> No service connections, environments or approvals exist. The Bicep is linted and built locally, and the YAML
> is schema-checked locally. Neither what-if nor a real pipeline compile was run, because both need Azure or
> Azure DevOps. The repo's real gate is its local git hooks (requirements §3, "No CI"). This pipeline shows
> how CI/CD would look at Uinsure.

Everything is under `infrastructure/`. It was built (F5) and was not cut. `az` is never run and nothing is
deployed.

### Status

The Bicep is linted and built locally and never deployed. what-if was not run, because it needs a
signed-in subscription.

### Shape

- Hand-written Bicep at subscription scope: `main.bicep` creates the resource group `rg-policies-<env>` and
  calls four local modules (`monitoring`, `network`, `sql`, `appservice`).
- Per environment that is the resource group plus 16 resources: a Log Analytics workspace and Application
  Insights; a VNet and two NSGs; an Azure SQL server and database, a private endpoint and private DNS (zone,
  VNet link, zone group); a user-assigned identity, an App Service plan (Linux), the web app and two
  publishing-credential policies.
- Three `.bicepparam` files: `dev`, `test` and `prod`. Region `uksouth`.

| | App Service plan | SQL database |
|---|---|---|
| dev | B1 | Basic |
| test | B1 | Basic |
| prod | P1v3 | General Purpose serverless, `GP_S_Gen5_1` |

### No secrets

The app signs in to Entra-only Azure SQL with a **user-assigned** managed identity (the connection string
uses `Authentication=Active Directory Managed Identity`). The SQL server has no administrator login or
password, so there are no passwords. **Key Vault was considered and rejected**: there is nothing to store.
It arrives with the first real secret, such as a payment API key. Basic-auth publishing (`scm`, `ftp`) is
disabled on the app. The Application Insights connection string is an ingestion endpoint, not a credential.

### Networking

SQL has no public endpoint and no "Allow Azure services" rule. It is reached through a private endpoint in
`snet-private-endpoints`, and the app uses VNet integration (`snet-app-integration`). An NSG on the endpoint
subnet allows port 1433 only from the app subnet and the deployment agents' range, and denies the rest.
Deployment agents must therefore be self-hosted, inside a network that reaches the VNet (see
[Deployment prerequisites](#deployment-prerequisites-outside-the-repo)).

Still to do: restrict outbound traffic from the integration subnet (an allow-list), which would be extra
hardening and is not built.

### Runtime

`linuxFxVersion` is `DOTNETCORE|10.0`. That value is inferred from the documented pattern and a Nov 2025 Q&A.
It wasn't confirmed with `az webapp list-runtimes`.

### The pipeline

`infrastructure/pipelines/azure-pipelines.yml` runs on pushes to `main` only (`pr: none`):

1. **Build** (hosted agent): `dotnet build -warnaserror`, `dotnet test`, the idempotent migration script
   (`dotnet-ef` 10.0.13), then a published app zip and the `infrastructure/` folder as one `drop` artifact.
2. Then, for `dev`, `test` and `prod` in that order, from one `stage-template.yml`: a **`WhatIf_<env>`** stage
   (hosted agent, no environment, so it runs unapproved), then **`Deploy_<env>`** (self-hosted pool
   `policies-<env>-vnet`, the only stage on the environment `policies-<env>`). Deploy runs the infrastructure
   deployment, applies the migration script with `sqlcmd`, creates the app's database user (by SID, with
   `db_datareader` and `db_datawriter` only), deploys the zip, and smoke-tests `/health/ready` with `curl`.

### Schema check

The claim is exactly this: the YAML is valid against the schema; template expansion, `${{ }}` expressions,
task inputs, service connections and environments are not checked.

`python -I infrastructure/pipelines/check-pipeline.py` (needs PyYAML and jsonschema) validates both YAML files
against `pipeline.schema.json` and asserts a set of structural rules the schema cannot express: the stage
order, a what-if stage with no environment, the approval environment, the migration step, the smoke test, and
the NSG rules.

### Prod approval

The Approval check on the environment `policies-prod` (approver: release manager, timeout 72 h) lives in the
Azure DevOps UI, not the YAML. This README is its only evidence. That is a platform limit, not a gap. Because
checks are evaluated when a stage starts, the what-if has its own stage, so the approver sees it before
approving. The check must sit on the environment only. If it were put on the ARM service connection or on the
`policies-<env>` variable group, the what-if stage would pause first.

### One-time setup (none of it done)

1. An Azure DevOps project, and a pipeline from `infrastructure/pipelines/azure-pipelines.yml`.
2. Three ARM service connections using workload identity federation, `sc-policies-dev`, `sc-policies-test`
   and `sc-policies-prod`, each authorised for this pipeline only. Three more, `sc-policies-<env>-sql`, are for
   the SQL steps (see below).
3. Each ARM connection's identity gets rights at subscription scope on its own subscription (Contributor, or a
   narrower custom role), because `main.bicep` creates the resource group.
4. One Entra group per environment as the SQL admin (that environment's deploy identity plus a break-glass
   human), named `sg-policies-sql-admins-<env>`. Its object ID goes in the variable group `policies-<env>` as
   `sqlAdminGroupObjectId`. The `.bicepparam` files hold an all-zero placeholder.
5. The environments `policies-dev`, `policies-test` and `policies-prod`, which aren't auto-created from YAML,
   with the Approval check on prod.

The names above are the ones in the repo. The original brief used `sc-uinsurepolicies-*` and
`uinsurepolicies-*`; the YAML says `sc-policies-*` and `policies-*`.

### Deployment prerequisites outside the repo

None of this exists and none of it can be checked from the repo.

- **Three self-hosted agent pools**, `policies-dev-vnet`, `policies-test-vnet`, `policies-prod-vnet`. Their
  agents need `az`, `jq`, go-sqlcmd (`sqlcmd`) and `curl`. **The agent hosts are privileged**: they can reach
  the private SQL endpoint and run with the service connections. Only merged code reaches them (`main` only,
  `pr: none`).
- **Network peering and DNS.** The agents' network must be peered to (or be) `vnet-policies-<env>` and resolve
  `privatelink.database.windows.net`. The private DNS zone is linked only to the environment's own VNet, so
  the agents' network needs its own zone link or DNS forwarding. The peering can only be made after the first
  infrastructure deployment creates the VNet.
- **A narrow `agentAddressPrefix`.** Each `.bicepparam` holds the placeholder `10.30.0.0/24`. Replace it with
  the real agent subnet before the first deployment. Never `0.0.0.0/0` or a `/8`. There is no CIDR validation,
  so the narrowness rests on review. The placeholder fails closed: it blocks the agents, it does not open SQL.
- **The SQL admin group** (item 4 above) and **ARM and SQL service connections**. The SQL connections
  (`sc-policies-<env>-sql`) are separate from the ARM ones. Each one's principal is a database-scoped user with
  `db_owner` on the `policies` database and is not in the server admin group.
- **A one-off database bootstrap per environment.** After the first infrastructure deployment, a member of the
  admin group, connected to the `policies` database, runs
  `CREATE USER [<SQL service connection principal name>] FROM EXTERNAL PROVIDER;` and
  `ALTER ROLE db_owner ADD MEMBER [<SQL service connection principal name>];`.
  **The first run fails at the migrations step until this is done.**

### Accepted risks and notes

- **The unapproved what-if stage shares the deploy ARM connection.** `WhatIf_<env>` uses the same
  subscription-scope ARM connection as the deployment. A separate lower-privilege what-if connection was
  considered and rejected. It only runs `az deployment sub what-if`, which creates nothing. The variable group
  it reads must stay secret-free, because it is read before approval.
- **The ARM connection could rewrite the SQL admin.** It holds subscription-scope rights, so a leaked ARM token
  could change the SQL server's Entra admin through ARM. A subscription-scope deployment needs those rights.
  The pipeline's SQL steps use the separate SQL connection, which cannot administer the server.
- **`httpsOnly` and forwarded headers.** The web app sets `httpsOnly: true`, so App Service redirects plain
  HTTP at its front end and the API's own `400 /problems/https-required` path is never reached there. The API
  trusts `X-Forwarded-For` and `X-Forwarded-Proto` only from private ranges (10.0.0.0/8, 172.16.0.0/12,
  192.168.0.0/16) and never uses `ASPNETCORE_FORWARDEDHEADERS_ENABLED`. App Service Linux may set that
  variable itself, and the API's design says not to rely on it. This interaction has not been tested on App
  Service.
- **SQL connection policy is `Default`.** `sql.bicep` does not set it. Private-endpoint traffic is then
  expected to use port 1433 only, which is all the NSG opens. If the policy were set to Redirect, ports
  11000-11999 would also need to be opened. This is an expectation, not something verified.
- **One VNet range for all environments.** Every `vnet-policies-<env>` is `10.20.0.0/16`. One agent network
  cannot be peered to more than one environment, which is why the pipeline uses one pool per environment.

### Known limits of the evidence

The offline checker proves structure, not runtime. A green check means the files are shaped as described. It
does not mean the pipeline would run. Three review findings on the checker were carried and not fixed:

- **F5 A2.3 R1:** the NSG assertions do not prove "only". A second Allow rule, or an extra source in the
  existing allow, would still pass. The Bicep itself is correct: one allow rule, then deny-all. It must be
  read by eye.
- **F5 A3 R1:** the "AzureCLI@2 inputs" check covers the deploying stage only. The what-if stage's AzureCLI@2
  step is not checked for `scriptType`, `scriptLocation` or `inlineScript`.
- **F5 A3 R2:** the checker has a few redundant assertions, which the author would have to explain.
  Each is harmless.

The investigations that shaped F2 and F5 are in `docs/plans/active/inv-001.plan.md` and
`docs/plans/active/inv-002.plan.md`. The second moved the what-if into its own stage, because an approval check
on the environment ran before the what-if in the first layout.
