# Uinsure Policies API

A C# REST API for home insurance policies: sell a policy, retrieve it, cancel it (with a refund and a refund
quote), record claims and renew it. It is a .NET 10 ASP.NET Core controller API with EF Core (SQLite locally,
SQL Server when deployed), problem-details errors (RFC 9457) and OpenAPI with Scalar. It was built outside-in
with TDD, one feature at a time. Every rule, its ID and the interpretation taken for it are in
[docs/requirements.md](docs/requirements.md).

## The API

| Route | What | Success |
|---|---|---|
| `POST /policies` | Sell a policy (SELL-1 to SELL-7) | `201` + `Location: /policies/{reference}` |
| `GET /policies/{reference}` | Retrieve it, with its terms, payments, refunds, claims and cancellation (GET-1) | `200` |
| `POST /policies/{reference}/cancellation` | Cancel, with a refund. The body is optional (`cancellationDate`) | `201` |
| `GET /policies/{reference}/cancellation-quote?cancellationDate=yyyy-MM-dd` | Quote the refund without cancelling (COULD-1) | `200` |
| `POST /policies/{reference}/claims` | Record a claim (`claimDate`, optional `description`) | `201` |
| `POST /policies/{reference}/renewals` | Renew for another year (`amount`, optional `paymentMethod`) | `201` |

Policy types are `Household` and `BuyToLet`, and payment methods are `Card`, `DirectDebit` and `Cheque`.

**Sell a policy.** These examples were captured on 2026-10-19. To run one yourself, set `startDate` to a date
from today to today + 60 days (SELL-2). An earlier or later date returns a `422`.

```http
POST /policies
Content-Type: application/json

{
  "reference": "HOME-8550",
  "type": "Household",
  "startDate": "2026-11-01",
  "amount": 415.80,
  "autoRenew": true,
  "paymentMethod": "Card",
  "policyholders": [{ "firstName": "Katherine", "lastName": "Johnson", "dateOfBirth": "1981-08-26" }],
  "property": { "addressLine1": "48 Quay St", "addressLine2": null, "addressLine3": null, "postcode": "M3 4LZ" }
}
```

It returns `201 Created` with `Location: /policies/HOME-8550`. `GET /policies/HOME-8550` then returns the policy.
The end date is calculated, the status is derived from today, and the payment is recorded:

```json
{
  "reference": "HOME-8550", "type": "Household", "status": "Pending", "currentTermNumber": 1,
  "startDate": "2026-11-01", "endDate": "2027-10-31", "amount": 415.8, "hasClaims": false, "autoRenew": true,
  "policyholders": [{ "firstName": "Katherine", "lastName": "Johnson", "dateOfBirth": "1981-08-26" }],
  "property": { "addressLine1": "48 Quay St", "addressLine2": null, "addressLine3": null, "postcode": "M3 4LZ" },
  "terms": [{ "termNumber": 1, "startDate": "2026-11-01", "endDate": "2027-10-31", "amount": 415.8,
              "paymentReference": "PAY-0426a077e2b54726b6020e0afad57cdd" }],
  "payments": [{ "reference": "PAY-0426a077e2b54726b6020e0afad57cdd", "type": "Card", "amount": 415.8 }],
  "refunds": [], "claims": [], "cancellation": null
}
```

**Errors** are RFC 9457 problem details:

- `400`: the body has the wrong shape, with an `errors` map.
- `404`: the reference is unknown.
- `409`: a state conflict (a duplicate reference, cancelling twice, renewing a cancelled policy).
- `422`: a broken business rule. Every broken rule is listed, not just the first. A sale backdated to 2020,
  with no policyholders and an amount of 0, returns:

```json
{
  "type": "/problems/sell-2", "title": "Start date outside the sale window", "status": 422,
  "detail": "3 rules failed.", "instance": "/policies",
  "traceId": "00-d8b267db53999aa53ea3bde9309ccf23-81cba93dc934447f-00", "ruleId": "SELL-2",
  "violations": [
    { "ruleId": "SELL-2", "detail": "The start date must be between 2026-10-19 and 2026-12-18." },
    { "ruleId": "SELL-4", "detail": "A policy needs 1 to 3 policyholders." },
    { "ruleId": "SELL-7", "detail": "The policy amount must be greater than 0." }
  ]
}
```

Each body has a `traceId`. The full contract (every status, the `429` rate limit, the `413` body cap, how
validation keys are written) is in [docs/design.md](docs/design.md#endpoints). In Development, the OpenAPI
document and the Scalar UI describe every route (see below).

## How to run it

**Prerequisites:** the .NET 10 SDK (`global.json` pins 10.0.100 with `rollForward` set to `latestFeature`).
The Postman CLI or the Postman app is only needed for the Postman collection. Visual Studio is optional.

**On Windows, clone to a short path** such as `C:\src\uinsure-policies`. The SQLite native library is copied
deep under each test project's `bin` folder. From a clone path of about 130 characters, its full path passes
Windows' 260-character limit, and two architecture tests fail with "The filename or extension is too long".

**Run the API** (SQLite, schema created at start-up, nothing to configure):

```sh
dotnet build Uinsure.Policies.slnx -warnaserror
dotnet run --project src/Uinsure.Policies.Api --launch-profile http
```

It listens on `http://localhost:5080`. In Development only, the OpenAPI document is at
`http://localhost:5080/openapi/v1.json` and the Scalar UI is at `http://localhost:5080/scalar`.
Health checks are at `/health/live` and `/health/ready`.

**Run the tests:**

```sh
dotnet test --solution Uinsure.Policies.slnx
```

Measured with that command on this branch on 2026-10-19: 637 tests (173 domain, 65 services, 332 API acceptance
and 67 architecture), 0 failed, 0 skipped.

**Run the Postman collection** (109 requests; start the API first):

```sh
postman collection run tests/postman/Uinsure.Policies.postman_collection.json \
  -e tests/postman/local.postman_environment.json
```

### In Visual Studio

1. Open `Uinsure.Policies.slnx`. Besides `src` and `tests`, Solution Explorer shows solution folders for the
   docs, the infrastructure, the Postman files and the root build files. For every folder on disk, use
   **Switch Views → Folder View**.
2. **Run the API:** set **Uinsure.Policies.Api** as the startup project, pick the **http** launch profile and
   press **Ctrl+F5**. A browser opens on `/health/live` and shows `Healthy`. F5 also works, but a breakpoint
   pauses every request behind it.
3. **Run the tests:** **Test → Test Explorer**, then **Run All Tests** (Ctrl+R, A). The tests group by project.
   Right-click a project, class or test to run just that. Tests for a rule carry a `Rule` trait with its ID,
   so **Group By → Traits** lists them per rule.

Only one process can listen on port 5080. Stop the API before running `dotnet run` from a terminal, or the second
one fails with "address already in use".

### In the Postman app

1. Install Postman Desktop and sign in. The browser version can't reach `localhost` without Postman's Desktop
   Agent.
2. **Import** both files in `tests/postman/`: the collection and `local.postman_environment.json`.
3. Pick the **local** environment in the top-right dropdown. It holds one variable,
   `baseUrl = http://localhost:5080`.
4. Start the API, then:
   - **Everything:** on the collection, **⋯ → Run collection**, then **Run**. Expect 246 assertions to pass.
   - **One feature:** **⋯ → Run folder** on a folder (`F1 Sell`, `F3 Cancel`, …) or on its `happy path` or
     `rule violations` sub-folder.
   - **One request:** open it, **Send**, then read the **Test Results** tab.

Every request stands alone: a pre-request script makes a fresh reference and today's dates, and sells any policy
it needs. So order doesn't matter, and nothing needs resetting between runs. The **F4 Renew → happy path** folder
is empty on purpose: a successful renewal needs a term near its end, and Postman runs on the real clock, so those
cases are covered by the acceptance tests, which inject the date.

## Assumptions

Where the brief was unclear I took a sensible view. The full list, one row per rule, is in
[docs/requirements.md](docs/requirements.md). The ones worth knowing:

- **"Today" is Europe/London**, read from an injected clock, never from `DateTime.Now`. See
  [section 2](docs/requirements.md#2-functional-requirements).
- **The client chooses the policy reference.** It is trimmed and upper-cased (`abc-1` = `ABC-1`), and a
  duplicate is a `409` (SELL-1, [Sell](docs/requirements.md#sell-must-1)).
- **Sold up to 60 days ahead, no backdating** (SELL-2). Start date is today to today + 60, inclusive.
- **A policy is one year, calculated:** the end date is start + 1 year - 1 day. A 29 February start ends on
  28 February the next year (SELL-3).
- **16 or older on the start date** passes SELL-5.
- **The amount is supplied by the client** and must be above zero (SELL-7, RENEW-5). Pricing is out of scope.
- **Cooling-off runs to start + 14 days, inclusive** (CANCEL-1b). After that the refund is pro-rata over the
  actual days in the term (365 or 366), and the cancellation day counts as used (CANCEL-1c, [Cancel](docs/requirements.md#cancel-should-1)).
- **Money is rounded to 2 dp, away from zero**, in one place (CANCEL-6).
- **Cancelling:** the date is optional and defaults to today, past dates are refused (CANCEL-3), and it must be
  on or before the end of cover (CANCEL-7). Cancelling twice is a `409` (CANCEL-4).
- **A renewal adds a term** to the same policy, starting the day after the current end (RENEW-4,
  [Renew](docs/requirements.md#renew-should-2)). The window runs from 30 days before the latest term's end date to the end date, both inclusive (31 calendar days). For a term ending 16 October 2027, that is 16 September to 16 October 2027.
- **Renewing with auto-renew off creates an unpaid term**, and sending a payment method with it is a `422`
  (RENEW-3b). A cancelled policy cannot be renewed (`409`, RENEW-6).
- **A claim means a £0 refund** (COULD-2, [Could-haves](docs/requirements.md#could-haves-all-in-scope)). Claims
  are recorded with `POST /policies/{reference}/claims`.
- **No £0 Refund record is created.** The cancellation response still lists every term, including those with `refundAmount` 0.00.
- **Status is derived** from today (Pending, Active, Expired, Cancelled) and never stored.
- **Errors:** `400` for shape, `404` unknown reference, `409` state conflicts, and `422` for the rules that can be
  broken (SELL-2, SELL-4, SELL-5, SELL-7, RENEW-1/2, RENEW-3b, RENEW-5, CANCEL-3, CANCEL-7, COULD-2a, COULD-2b,
  COULD-3). A `422` lists every broken rule together. Unknown JSON fields and query parameters are a `400`.
- **No authentication** (out of scope). `Location` on renewals and claims points at the policy.

## Known limits

- No authentication, so the reference is the only key. Anyone who knows or guesses a reference can read the
  policy, and can post a claim, which turns every later refund on it into £0 and can't be undone. See the
  [security scope](docs/design.md#security-scope).
- The Bicep sets no WAF, Front Door or IP restriction in front of the API, or in front of its Kudu (SCM) site.
- `400` bodies pass on the JSON serialiser's messages, which name internal .NET types. Responses carry no
  `Cache-Control: no-store` or `X-Content-Type-Options` headers.
- The pipeline and the Azure resources have security gaps found in a review before publishing: see
  [Known security gaps](infrastructure/README.md#known-security-gaps).
- Rate limits are counted per instance. Claims are not idempotent and have no cap per policy.
- The tests run on SQLite. No test runs against a SQL Server database; the pre-push hook generates the
  idempotent migration script.
- The Bicep and Azure DevOps pipeline were never deployed or run. See
  [infrastructure/README.md](infrastructure/README.md).

The full lists are in [docs/design.md](docs/design.md#known-limits) and
[docs/design.md](docs/design.md#behaviours-of-the-renewal-and-input-handling).

## How it was built

AI agents in Claude Code built this repo, inside a harness designed for it, with a human deciding at fixed
points. In outline:

- **Decide first.** Each part of the system has a spec with options, a recorded decision and a binding contract
  (`.claude/specs/`). Every rule in the brief has an ID and an interpretation ([docs/requirements.md](docs/requirements.md)).
- **Plan as a DAG.** Features are nodes with explicit tasks ([build.dag.yaml](docs/plans/active/build.dag.yaml)).
  Status changes only by commit.
- **One loop per feature** (`/feature`): failing acceptance tests first, then green layer by layer, then an
  independent code review and security review. A failure is diagnosed before it is fixed, and the third failure
  stops for a written investigation.
- **Six agents with fixed roles.** The agents that review can't write, and the agents that write don't judge
  their own work. The orchestrating session runs every check itself.
- **Hooks, not trust.** Hooks block what the rules forbid: writing outside a role, pushing to `main`,
  force-pushes, skipping the git hooks, leaking private data.
- **A human at two pauses per feature.** One with the tests red, one at sign-off with a QA script. Every push, PR
  and merge was approved by hand.
- **A trail for everything.** Gate results, verdicts, findings and every agent's tool calls, per feature
  ([docs/trace/](docs/trace/)), a rule-by-rule matrix, and session snapshots so work resumed across days
  ([docs/context/](docs/context/)).

[docs/harness.md](docs/harness.md) has the full account: what each piece is, why it exists, the slash commands,
how each feature went, and what the harness missed.

## More detail

- [docs/design.md](docs/design.md): endpoints, errors, tests, design decisions, security scope, known limits and
  behaviours of the renewal and input handling.
- [infrastructure/README.md](infrastructure/README.md): the Bicep and pipeline, networking, secrets, one-time
  setup and what the evidence does and does not prove.
- [docs/requirements.md](docs/requirements.md): every rule with its agreed interpretation.
- [docs/harness.md](docs/harness.md): the AI build harness that produced this repo, how a feature moves
  through it, and where its audit trail lives. You don't need it to run or test the API.
