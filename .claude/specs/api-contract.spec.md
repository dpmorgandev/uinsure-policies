# api-contract.spec.md

Status: RESEARCH COMPLETE — decision pending
Researched: 2026-10-17 · Input: `docs/requirements.md` (rule IDs are quoted from there, not restated)

## Dependencies

**Upstream: `architecture.spec.md`, which is not decided yet.** This spec fixes the **wire contract** only. It assumes:

- **A1.** ASP.NET Core **controllers** with `[ApiController]`. Requirements §3 says "Web API with controllers".
- **A2.** `AddProblemDetails()`, `UseExceptionHandler()` and `UseStatusCodePages()` are registered, so that **every** 4xx/5xx (including routing 404s and unhandled 500s) comes out as `application/problem+json` via `IProblemDetailsService`.
- **A3.** Rule violations reach the HTTP edge as **one typed thing** carrying `RuleId`, `Status`, `Title` and `Detail`. It could be a `Result`/error value mapped in the controller, or a `RuleViolationException` mapped by an `IExceptionHandler`; architecture picks which. This contract only requires that the mapping from `RuleId` to status, type and title lives **in one place**.
- **A4.** Request/response DTOs live in the API layer, separate from the domain entities, and map explicitly. No entity is ever serialized.
- **A5.** `TimeProvider` is injected. "Today" means `DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime)` in **UK local time** (architecture to confirm the time zone).

**Downstream:** `testing.spec` (the integration-test matrix = the table in §Contract), `planning.spec` (features F1–F4 map to endpoint groups), the Postman collection, and the QA scripts (one step per row).

## Research

**R1. RFC 9457 obsoletes RFC 7807 (July 2023).** Abstract: "This document obsoletes RFC 7807." The members are `type`, `title`, `status`, `detail` and `instance`, with media type `application/problem+json`.
- `type` defaults to `about:blank`, and "is allowed to be a non-resolvable URI". Resolvable URIs are encouraged, but clients SHOULD NOT auto-dereference them.
- Extension members are allowed, and clients MUST ignore the ones they don't recognise.
- When there are several problems, "it is RECOMMENDED that the most relevant or urgent problem be represented". `errors` is only an *example* extension.
- It creates an IANA "HTTP Problem Types" registry.
- **Consequence:** requirements.md says "RFC 7807". The correct current citation is **RFC 9457**. The wire format is unchanged and backward-compatible. *Source: https://www.rfc-editor.org/rfc/rfc9457.html*

**R2. ASP.NET Core 10 ProblemDetails** (learn.microsoft.com, "Handle errors in ASP.NET Core APIs", .NET 10 view, updated 2026-03):
- `AddProblemDetails()` registers `IProblemDetailsService`. The exception handler, status-code-pages and developer-exception-page middleware then emit problem details "for all HTTP client and server error responses that don't have body content yet".
- `ProblemDetailsOptions.CustomizeProblemDetails` is the single hook for adding extensions such as `traceId` and `ruleId`.
- MVC builds every `ProblemDetails` and `ValidationProblemDetails` through `ProblemDetailsFactory`, including `ControllerBase.Problem()` and `ValidationProblem()`.
- `[ApiController]` turns model-validation failures into **automatic 400 `ValidationProblemDetails`**, which can be overridden with `InvalidModelStateResponseFactory`. `ApiBehaviorOptions.ClientErrorMapping` sets the default `type` link per status.
- `DefaultProblemDetailsWriter` serves `application/json`, `application/problem+json` and wildcard types. An `Accept` header for XML or HTML falls back.
- *Source: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling-api?view=aspnetcore-10.0*

**R3. Microsoft (Azure) REST API Guidelines** (*Source: https://github.com/microsoft/api-guidelines/blob/vNext/azure/Guidelines.md*):
- **Actions:** POST to `.../{id}:{action}`, returning 200 for a synchronous action. Don't use an action where standard CRUD fits.
- **JSON:** camelCase for all JSON field names. RFC 3339 dates.
- **Enums:** "extensible enumerations" modelled as strings.
- **Creates:** a POST create "MUST return … 201-Created".
- **Versioning:** a required `api-version` query parameter, with no version segment in the path.
- **Errors:** an Azure-specific `{ "error": { code, message } }` envelope plus an `x-ms-error-code` header. That is not RFC 9457, so we don't adopt it, but the same idea applies: a **stable, documented error code is part of the contract**, which maps to our `ruleId`.
- **422:** not addressed.

**R4. Zalando RESTful API Guidelines** (*Source: https://opensource.zalando.com/restful-api-guidelines/*):
- **Naming:** #134 plural collection names. #129 kebab-case path segments. #118 **snake_case** JSON. We deliberately diverge from #118 and follow the camelCase default of Microsoft and ASP.NET.
- **Formats:** #169 RFC 3339 `date`. #240 UPPER_SNAKE_CASE enum strings. #173 Money is `{amount: decimal number, currency: ISO 4217}`.
- **Actions:** #138/#141 say "avoid actions, URLs contain only nouns", and model actions as resources (e.g. `PUT /article-locks/{id}`).
- **Errors:** #176 MUST support problem JSON. It prefers *relative*, non-resolved `type` URIs such as `/problems/out-of-stock`.
- **Status codes:** #220 MUST use the most specific status code.
- **Versioning:** #113 SHOULD avoid versioning. #115 MUST NOT version in the URL.
- **Idempotency:** #229/#231 make POST idempotent through a secondary (client-supplied) key. #230 MAY add `Idempotency-Key`.
- **On 400 vs 422, Zalando says "do not use" 422.** Use 400 even when "input payload fails business logic / semantic validation", because separating the two brings "no clear benefit".
- **On 201:** return 201 + `Location` for a create. Use 409 for conflicts with the current state.

**R5. The 400 vs 422 debate.**
- RFC 9110 §15.5.21 standardised **422 Unprocessable Content**: the server understands the content type and the syntax is correct, but it can't process the contained instructions.
- **For 422:** it gives clients a machine-distinguishable split between *"you sent garbage"* (400, a bug in the client) and *"you sent a valid request the business refuses"* (422, which a user can act on).
- **Against 422 (Zalando):** the split is arbitrary at the margins, and clients rarely branch on it.
- requirements.md has **already agreed 422** for SELL-2, CANCEL-3 and COULD-3, so this spec **extends that split consistently** rather than reopening it. The defensible line is: **shape → 400; rule ID → 422 (or 409/404 where agreed)**.

**R6. Rejected without deep research (named so the interview can probe them):**
- **HATEOAS links:** no client needs them.
- **PATCH `status: Cancelled`:** it hides a calculation with side effects behind a field write, and there's nowhere to put the cancellation date and refund result.
- **`PUT` for renew:** a renewal isn't idempotent replacement.
- **Google AIP-136 `:cancel` custom methods:** the same family as R3's actions.

## Options

**O1. Cancellation route**

| Option | Shape | For | Against |
|---|---|---|---|
| a | `POST /policies/{ref}/cancellation` (singleton noun) → 201 | Noun (Zalando #138). Singular because there's at most one per policy. A second POST = 409 is natural ("it already exists", CANCEL-4). Per RFC 9110, a 201 with no `Location` identifies the target URI, which *is* the resource | We don't serve `GET` on that URI (see O5) |
| b | `POST /policies/{ref}/cancellations` (collection) | Zalando #134 plural | Implies many. 409 on the second reads oddly |
| c | `POST /policies/{ref}/cancel` or `/{ref}:cancel` (action) → 200 | Microsoft action style. Reads like the brief | A verb in the URL (Zalando #141). `:` in routes is awkward in ASP.NET attribute routing |

**O2. Quote route (COULD-1):**
- **a:** `GET /policies/{ref}/cancellation-quote?cancellationDate=YYYY-MM-DD`. It is safe and cacheable, the noun is clear, and it is the read-only twin of O1a.
- **b:** `POST /policies/{ref}/cancellation?dryRun=true`. Rejected: an unsafe method used for a read.

**O3. Renewal route:**
- **a:** `POST /policies/{ref}/renewals`. A noun; each renewal is an event.
- **b:** `POST /policies/{ref}/terms`. The most "honest" REST, since renewal *is* appending a term (RENEW-4), but it hides the business word that the brief, Postman and the interview all use.

**O4. Claims (COULD-2):** `POST /policies/{ref}/claims`. The route is already fixed in requirements.md.

**O5. Location for sub-resource creates.** We don't build `GET` endpoints for terms, claims or the cancellation (that's over-building, §4 guard).
- **a:** 201 with `Location: /policies/{ref}`. The policy representation contains the created thing. Pragmatic, but `Location` is meant to name the *created* resource.
- **b:** 201 with no `Location` for the singleton `cancellation` (RFC-correct). 201 with `Location: /policies/{ref}` for renewals and claims, with the compromise documented.
- **c:** add the `GET` sub-resources so that every `Location` is pure.

**O6. Error-type URIs.**
- **a:** ASP.NET defaults (RFC 9110 section links) everywhere. Not rule-specific.
- **b:** relative `/problems/{slug}` (Zalando #176). Non-resolvable.
- **c:** absolute links into the **public repo's** `docs/requirements.md` anchors, e.g. `https://github.com/<owner>/Uinsure.Policies/blob/main/docs/requirements.md#sell-must-1`. It is resolvable for free once the repo is public, and it traces rule → requirement. **Against:** the anchors are section-level, not per rule, and the URI couples to the GitHub path.

**O7. Several violations at once** (e.g. SELL-4 and SELL-5 in one request).
- **a:** one problem: the first violated rule in a fixed, documented order (RFC 9457 R1).
- **b:** a `violations: [{ruleId, detail}]` extension.

**O8. Enum spelling:**
- **a:** C# names as-is: `Household`, `BuyToLet`, `DirectDebit`.
- **b:** camelCase.
- **c:** UPPER_SNAKE_CASE (Zalando #240).

**O9. Money:**
- **a:** a plain JSON number (`decimal`) with GBP implied and documented.
- **b:** the Zalando Money object `{amount, currency}`.
- **c:** a string `"123.45"`.

**O10. Versioning:**
- **a:** none (Zalando #113). State "breaking change ⇒ `/v2` or `api-version`" in the README.
- **b:** `/v1` prefix.
- **c:** `Asp.Versioning` with an `api-version` query parameter (Microsoft).

## Recommendation

1. **Routes:** O1a, O2a, O3a, O4. The base path is `/policies`, with no `/api` prefix and no version (**O10a**). All segments are lowercase kebab-case.
   - *Why:* nouns everywhere. The singleton `cancellation` makes CANCEL-4's 409 self-explanatory. The renewal noun keeps the brief's vocabulary.
   - *Versioning:* is YAGNI for a single client, and Zalando #113 says the same. The interview answer is "I'd add `Asp.Versioning` when the first breaking change came, and I can say exactly where it plugs in".
2. **Location: O5b.** `POST /policies` returns 201 + `Location: /policies/{ref}` via `CreatedAtAction`. For renewals and claims, the deviation to `Location: /policies/{ref}` is called out in the README.
3. **Status split (extends requirements consistently):**

   | Status | When |
   |---|---|
   | 400 | **Shape**: malformed JSON, wrong types, missing required fields, unknown enum value, postcode > 8, field lengths. It is `ValidationProblemDetails` with an `errors` map, and carries **no** `ruleId` |
   | 404 | Unknown reference, on every route |
   | 409 | State conflicts: SELL-1, CANCEL-4, RENEW-6 |
   | 422 | Every other rule ID: SELL-2, SELL-4, SELL-5, RENEW-1/2, RENEW-5, CANCEL-3, COULD-3 |

   - SELL-4 (1–3 holders) *could* be a DataAnnotation 400. It is recommended as 422, so that **every numbered rule produces a `ruleId`**, which is the "informative responses" ask.
4. **Problem body:** RFC 9457 members plus two extensions, `ruleId` (stable code, R3's lesson) and `traceId`. `detail` uses the actual values: dates, counts and limits. `instance` is the request path.
   - **Type URIs: O6b**, `/problems/{rule-id-lowercase}` (e.g. `/problems/sell-2`). It is the simplest that's spec-legal, and it is stable. O6c is a nice upgrade if David wants resolvable links.
   - **O7a**: one problem per response, with rules checked in ID order.
5. **Formats:**

   | What | Format |
   |---|---|
   | JSON fields | camelCase, the System.Text.Json web default |
   | Dates | `DateOnly`, i.e. `"2026-11-01"` (RFC 3339 full-date; STJ supports it natively) |
   | Money | **O9a**, a JSON number. `decimal` end to end, rounded once (CANCEL-6). A currency field would be gold-plating for a GBP-only brief |
   | Enums | Strings via `JsonStringEnumConverter(allowIntegerValues: false)`, so `"type": 2` is rejected with 400 and doesn't silently bind. Spelled **O8a**, the C# names, because there's no naming policy to explain |
   | Validation keys | `errors` keys follow the JSON names (`SystemTextJsonValidationMetadataProvider`), so they read `policyholders[0].dateOfBirth`, not `PolicyHolders[0].DateOfBirth` |

6. **Idempotency:** not required.
   - `POST /policies` is *naturally* idempotent-safe, because the client-supplied reference (SELL-1) is Zalando #231's secondary key, so a retried sell gets 409, not a duplicate.
   - Cancellation is guarded by CANCEL-4.
   - Renewals are guarded by the window: after a renewal, the next window is about 11 months away, so a retry gets 422.
   - Claims are **not** idempotent. Name this in the README; there's no `Idempotency-Key`.
7. **Informative success bodies:** the cancellation and quote responses include `ruleApplied` (`CANCEL-1a`, `CANCEL-1b`, `CANCEL-1c` or `COULD-2`) and the day counts, so a reviewer can verify the arithmetic from the response alone.

## Decision — DECIDED 2026-10-17

- [x] (accepted as a block, David 2026-10-17) D1 Cancellation route: O1a `POST /policies/{ref}/cancellation` (rec.) / b / c
- [x] D2 Quote: O2a `GET …/cancellation-quote?cancellationDate=` (rec.)
- [x] D3 Renewal: O3a `/renewals` (rec.) / O3b `/terms`
- [x] D4 Location policy: O5b (rec.) / a / c
- [x] **D5: decided (David, 2026-10-17).**
  - 400 for shape: malformed JSON, wrong type, missing field, unknown enum, postcode > 8, or a reference not matching `^[A-Z0-9-]{3,20}$` after trim and upper-casing. It's `ValidationProblemDetails` with an `errors` map.
  - 422 for every numbered rule, including SELL-4, SELL-5 and RENEW-5, always with a `ruleId`.
  - 409 for state conflicts: SELL-1, CANCEL-4 and RENEW-6.
  - This adds statuses to requirements.md.
- [x] D6 Type URIs: O6b `/problems/{rule-id}` (rec.) / O6c repo anchors
- [x] **D7: decided (David, 2026-10-17). Report every violation (O7b).**
  - A 422 carries `violations: [{ruleId, detail}]`. The top-level `ruleId` is the first violation in rule-ID order, and `detail` summarises ("2 rules failed").
  - The domain collects violations rather than returning early, so `Result` holds a list.
  - *Derived:* a 409 conflict (SELL-1, CANCEL-4, RENEW-6) or a 404 short-circuits. Those checks run before rule evaluation and return on their own, because "already exists" or "already cancelled" makes the other rules moot.
  - A 400 shape failure is also returned before any rule runs.
- [x] D8 Enums as C# names (O8a, rec.), integers rejected
- [x] D9 Money as a JSON number, GBP implied (O9a, rec.)
- [x] D10 No versioning (O10a, rec.)
- [x] D11 Amend requirements.md: "RFC 7807" → "RFC 9457 (obsoletes 7807)"
- [x] D12 Q1–Q8 resolved. See Rulings. Q5: an empty cancel body is allowed and means today

## Contract — PENDING (DRAFT endpoint table + example payloads)

### Endpoints

| # | Method & route | Success | Feature |
|---|---|---|---|
| E1 | `POST /policies` | 201, `Location: /policies/{reference}`, body `PolicyResponse` | F1 |
| E2 | `GET /policies/{reference}` | 200 `PolicyResponse` | F2 |
| E3 | `POST /policies/{reference}/cancellation` | 201 `CancellationResponse` (no Location) | F3 |
| E4 | `GET /policies/{reference}/cancellation-quote?cancellationDate=` | 200 `CancellationQuoteResponse` | F3 |
| E5 | `POST /policies/{reference}/claims` | 201, `Location: /policies/{reference}`, body `ClaimResponse` | F3 |
| E6 | `POST /policies/{reference}/renewals` | 201, `Location: /policies/{reference}`, body `PolicyResponse` | F4 |

Every error is `Content-Type: application/problem+json`. A 500 carries a generic `detail` and `traceId`, never a stack trace outside Development.

### Rule × status matrix (the integration-test and Postman checklist)

| Endpoint | Rule | Status | type | title | detail (example) | extensions |
|---|---|---|---|---|---|---|
| E1 | shape | 400 | RFC 9110 §15.5.1 default | One or more validation errors occurred. | — | `errors{}` |
| E1 | SELL-1 | 409 | `/problems/sell-1` | Policy reference already exists | A policy with reference 'HH-0001' already exists. | `reference` |
| E1 | SELL-2 | 422 | `/problems/sell-2` | Start date outside the sale window | Start date 2026-12-31 is 75 days from today (2026-10-17); a policy can start from today up to 60 days ahead (2026-12-16). | `earliest`, `latest` |
| E1 | SELL-4 | 422 | `/problems/sell-4` | Wrong number of policyholders | 4 policyholders were supplied; a policy must have between 1 and 3. | `count` |
| E1 | SELL-5 | 422 | `/problems/sell-5` | Policyholder under 16 | Policyholder 2 (born 2011-01-01) is 15 on the start date 2026-11-01; all policyholders must be 16 or older. | `index` |
| E2 | GET-1 | 404 | `/problems/get-1` | Policy not found | No policy with reference 'HH-9999'. | — |
| E3/E4 | GET-1 | 404 | as above | | | |
| E3/E4 | shape | 400 | default | | e.g. `cancellationDate` not `YYYY-MM-DD` | `errors{}` |
| E3/E4 | CANCEL-3 | 422 | `/problems/cancel-3` | Cancellation date in the past | Cancellation date 2026-10-01 is before today (2026-10-17). | — |
| E3/E4 | CANCEL-4 | 409 | `/problems/cancel-4` | Policy already cancelled | Policy 'HH-0001' was already cancelled on 2026-10-10. | `cancelledOn` |
| E5 | GET-1 | 404 | as above | | | |
| E5 | shape | 400 | default | | | `errors{}` |
| E6 | GET-1 | 404 | as above | | | |
| E6 | shape | 400 | default | | | `errors{}` |
| E6 | RENEW-6 | 409 | `/problems/renew-6` | Cancelled policy cannot be renewed | Policy 'HH-0001' was cancelled on 2026-10-10 and cannot be renewed. | `cancelledOn` |
| E6 | RENEW-1/2 | 422 | `/problems/renew-1-2` | Outside the renewal window | Renewal is allowed from 2027-10-01 to 2027-10-31; today is 2026-10-17. | `windowStart`, `windowEnd` |
| E6 | RENEW-5 | 422 | `/problems/renew-5` | Renewal amount must be greater than zero | Amount 0.00 is not valid; the new term's amount must be greater than 0. | — |
| E6 | COULD-3 | 422 | `/problems/could-3` | Cheque not allowed for auto-renewal | Policy 'HH-0001' auto-renews, so its renewal cannot be paid by Cheque. | — |

Rule IDs with **no error path** still get a success-path test row:
- SELL-3: `endDate` asserted, plus a 29 Feb case.
- SELL-6: a payment is present.
- CANCEL-1a/b/c, CANCEL-2, CANCEL-5, CANCEL-6 and COULD-2: `ruleApplied`, amounts and the refund `type`.
- RENEW-3a/b and RENEW-4.
- COULD-1: the quote equals the later cancel.

### Payloads

**E1 request** (`SellPolicyRequest`):
```json
{
  "reference": "HH-0001",
  "type": "Household",
  "startDate": "2026-11-01",
  "amount": 365.00,
  "autoRenew": true,
  "paymentMethod": "DirectDebit",
  "policyholders": [{ "firstName": "Ada", "lastName": "Lovelace", "dateOfBirth": "1985-12-10" }],
  "property": { "addressLine1": "1 High St", "addressLine2": null, "addressLine3": null, "postcode": "M1 1AA" }
}
```
There is no `endDate` (SELL-3), and `hasClaims` isn't accepted (false at sale). Unknown fields are ignored (the STJ default; see Q7).

**`PolicyResponse`** (E1, E2, E6):
```json
{
  "reference": "HH-0001", "type": "Household", "status": "Active",
  "startDate": "2026-11-01", "endDate": "2027-10-31", "amount": 365.00,
  "hasClaims": false, "autoRenew": true,
  "policyholders": [{ "firstName": "Ada", "lastName": "Lovelace", "dateOfBirth": "1985-12-10" }],
  "property": { "addressLine1": "1 High St", "addressLine2": null, "addressLine3": null, "postcode": "M1 1AA" },
  "terms": [{ "termNumber": 1, "startDate": "2026-11-01", "endDate": "2027-10-31", "amount": 365.00, "paymentReference": "PAY-…" }],
  "payments": [{ "reference": "PAY-…", "type": "DirectDebit", "amount": 365.00 }],
  "refunds": [],
  "cancellation": null
}
```
`status` is derived (`Active` | `Cancelled`; see Q2), not stored.

**E3 request** (body optional; Q5): `{ "cancellationDate": "2026-11-20" }`.

**E3 `CancellationResponse`.** E4 returns the same shape minus `refunds[].reference`, plus `"quotedOn"`:
```json
{
  "reference": "HH-0001", "cancellationDate": "2027-03-01", "ruleApplied": "CANCEL-1c",
  "totalRefund": 244.00,
  "terms": [{ "termNumber": 1, "daysInTerm": 365, "daysUsed": 121, "unusedDays": 244, "refundAmount": 244.00 }],
  "refunds": [{ "reference": "REF-…", "type": "DirectDebit", "amount": 244.00 }]
}
```
For CANCEL-5, `terms[]` has two entries and `ruleApplied` is reported per term.

**E5 request:** `{ "claimDate": "2026-12-03", "description": "Burst pipe" }` (fields TBD, Q3). **Response** `ClaimResponse`: `{ "reference": "CLM-…", "claimDate": "…", "description": "…" }`.

**E6 request:** `{ "amount": 389.50, "paymentMethod": "Card" }`. `paymentMethod` is optional and defaults to the current term's (RENEW-3a). It is ignored when `autoRenew` is false (RENEW-3b; Q6).

**422 example** (E1, SELL-2):
```json
{
  "type": "/problems/sell-2",
  "title": "Start date outside the sale window",
  "status": 422,
  "detail": "Start date 2026-12-31 is 75 days from today (2026-10-17); a policy can start from today up to 60 days ahead (2026-12-16).",
  "instance": "/policies",
  "ruleId": "SELL-2",
  "earliest": "2026-10-17", "latest": "2026-12-16",
  "traceId": "00-4bf92f…-01"
}
```

**400 example** (shape):
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": { "property.postcode": ["The postcode must be at most 8 characters."] },
  "traceId": "00-…"
}
```

### What derives from this contract

- **Integration tests** (`WebApplicationFactory` + `FakeTimeProvider`): one test per matrix row. Each asserts the status, the `application/problem+json` content type, `ruleId` and the extension values. Success tests assert 201 + `Location`, round-trip E1 → E2, and the success-path rows. The F0 walking skeleton needs only E1's stub.
- **Postman:**
  - one folder per feature (F1–F4), each with a happy-path request and one example per matrix row
  - the environment holds `baseUrl`
  - pre-request scripts generate a **unique `reference`** (SELL-1) and compute **dates relative to the real today**, because Postman can't fake time. Rows that need time travel (CANCEL-1b/1c, RENEW window) are **integration-test only** and marked as such in the QA script
- **QA scripts:** one step per matrix row reachable in real time, with the expected status and `ruleId`.

## Rulings (David, 2026-10-17)

- **Q1 → current term.**
  - `startDate`, `endDate` and `amount` describe the term in force today, or the latest term if today is before it starts or after it ends.
  - `terms[]` holds the history. The renewal window is measured from the latest term's end.
  - This replaces architecture's earlier "first start, last end".
- **Q2 → `Pending | Active | Expired | Cancelled`**, derived from today:
  - Pending: today < first start.
  - Active: within cover.
  - Expired: today > last end.
  - Cancelled overrides the others.
- **Q3 → claims.**
  - Body: `claimDate` (required) and `description` (optional, max 500 characters).
  - New rules:
    - **COULD-2a:** `claimDate` not in the future → 422.
    - **COULD-2b:** `claimDate` within a term's cover → 422.
    - **COULD-2c:** no claims on a cancelled policy → 409.
  - Any claim sets `hasClaims = true`.
- **Q4 → new rule CANCEL-7.**
  - The cancellation date must be on or before the latest term's end, or the API returns 422.
  - Together with CANCEL-3, this blocks cancelling an expired policy.
- **Q6.**
  - **RENEW-3b:** sending `paymentMethod` when `autoRenew` is false returns 422 RENEW-3b, because the renewal is unpaid.
  - **COULD-3:** a *defaulted* Cheque is rejected like an explicit one with a 422, and the detail asks for a different method.
- **Q7 → strict input.** Unknown JSON fields (`UnmappedMemberHandling.Disallow`) and unknown query parameters (an action filter) both return 400 with an `errors` entry. The aim is that a typo never silently becomes a default.
- **Q8 → superseded by architecture's SELL-1 ruling:** trim and upper-case the reference, then require `^[A-Z0-9-]{3,20}$`.

## Open questions

- **Q1.** Policy-level `startDate`/`endDate`/`amount` after renewal: are they the *current* term's (rec.: they mirror the appendix fields, while `terms[]` holds the history), or the whole span? Architecture's domain model decides.
- **Q2.** Should `status` include `Pending` (before start) and `Expired`, derived from today? It is informative but adds clock-dependent output to GET. Rec.: `Active`/`Cancelled` only, unless David wants more.
- **Q3.** Claim fields aren't specified in requirements. Rec.: `claimDate` (not in the future, not after cancellation) and an optional `description`.
  - **New rule IDs are needed** if those constraints are kept, e.g. COULD-2a/2b → 422.
  - Can a claim be added to a cancelled policy? Rec.: no, 409. That is also a new rule.
- **Q4.** Cancellation date after the policy's end date: is it 422? Not covered by CANCEL-*; it needs a **new rule ID** (e.g. CANCEL-7).
- **Q5.** E3 with no body: it needs `[FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)]`, otherwise ASP.NET returns 400/415. Is `{}` acceptable to require instead? Rec.: allow an empty body.
- **Q6.** RENEW-3b: if `paymentMethod` is sent while `autoRenew` is false, ignore it (rec.) or 422? COULD-3 with the *defaulted* method: if the current term was paid by Cheque and auto-renew is on, the default is Cheque, so does the renewal get 422 (rec.: yes, and the detail says to supply a method)?
- **Q7.** Reject unknown JSON fields (`JsonSerializerOptions.UnmappedMemberHandling = Disallow` → 400)? Microsoft's guidelines do; rec.: yes, because it is cheap, catches client typos, and is OWASP API3-adjacent (mass assignment).
- **Q8.** Reference format: max length, allowed characters (it must be URL-safe because it's a route segment), and case sensitivity on lookup. Rec.: `^[A-Za-z0-9-]{1,20}$`, case-insensitive uniqueness. **This changes SELL-1's interpretation**, so it needs David.
