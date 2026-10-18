# api-contract.spec.md

Status: DECIDED — contract final (2026-10-17)
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
- [x] **Final rulings (David, 2026-10-17):**
  - Q10: new rule **SELL-7**: a policy amount must be > 0, otherwise 422 with `ruleId` SELL-7.
  - Q11: no Refund record is created for £0 (a COULD-2 claim, or an unpaid RENEW-3b term). The cancellation response still lists each term with `refundAmount` 0.00 and `ruleApplied` `COULD-2` or `UNPAID-TERM`.
  - Accepted as proposed: an unknown reference in a URL returns 404 GET-1.

## Contract

Binding. Agents build and test against this section only, delivered as the `architecture` skill's `api.md` (agents never read this spec). Rule IDs are those in `docs/requirements.md` §2. CANCEL-7, COULD-2a/2b/2c, SELL-7 and the RENEW-3b 422 are new from the Rulings and must be added there (see Open questions).

### Conventions

- Base path `/policies`. No `/api` prefix, no versioning. Lowercase kebab-case segments.
- JSON: camelCase. Dates `YYYY-MM-DD` (`DateOnly`). Money is a JSON number in GBP, rounded once by `Money.Round` (2dp, AwayFromZero; CANCEL-6).
- Enums are strings spelled as the C# names. Integers are rejected with 400.
  - `PolicyType`: `Household`, `BuyToLet` (requirements §2: Household, Buy to Let).
  - `PaymentMethod`: `Card`, `DirectDebit`, `Cheque` (requirements §2: Card, Direct Debit, Cheque).
  - `PolicyStatus`: `Pending`, `Active`, `Expired`, `Cancelled` (response only).
- Strict input. Unknown JSON fields (`UnmappedMemberHandling.Disallow`) and unknown query parameters (action filter) return 400.
- "Today" is `UkCalendar.Today(TimeProvider)`, Europe/London, computed once per request.
- `{reference}` in a route is trimmed and upper-cased before lookup. A reference that doesn't exist, including one that fails the SELL-1 pattern, is 404 GET-1.

### Endpoints

| # | Method & route | Success | Feature |
|---|---|---|---|
| E1 | `POST /policies` | 201, `Location: /policies/{reference}` (normalised), body `PolicyResponse` | F1 |
| E2 | `GET /policies/{reference}` | 200 `PolicyResponse` | F1 (bare), F2 (full) |
| E3 | `POST /policies/{reference}/cancellation` | 201 `CancellationResponse`, no `Location` | F3 |
| E4 | `GET /policies/{reference}/cancellation-quote?cancellationDate=` | 200 `CancellationQuoteResponse` | F3 |
| E5 | `POST /policies/{reference}/claims` | 201, `Location: /policies/{reference}`, body `ClaimResponse` | F3 |
| E6 | `POST /policies/{reference}/renewals` | 201, `Location: /policies/{reference}`, body `PolicyResponse` | F4 |

### Evaluation order (per request)

1. **400 shape.** Returned before anything else, as `ValidationProblemDetails` with an `errors` map. No `ruleId`.
2. **404 GET-1** (E2–E6).
3. **409 state conflicts.** SELL-1, CANCEL-4, RENEW-6, COULD-2c. Each short-circuits on its own.
4. **422 numbered rules.** All are evaluated, and every failure is reported in `violations[]` in rule-ID order: SELL-2, SELL-4, SELL-5, SELL-7 · CANCEL-3, CANCEL-7 · RENEW-1/2, RENEW-3b, RENEW-5 · COULD-2a, COULD-2b, COULD-3. The top-level `ruleId` is the first one.

### Rule × status matrix (the integration-test and Postman checklist)

Every error is `application/problem+json` (RFC 9457) with `type`, `title`, `status`, `detail`, `instance` (request path) and `traceId`. Rule errors add `ruleId`. A 422 always adds `violations[]`, even when it holds one entry. `type` is `/problems/{rule-id-lowercase}`, and `RENEW-1/2` becomes `renew-1-2`.

| Endpoint | Rule | Status | type | title | detail (example) | extensions |
|---|---|---|---|---|---|---|
| E1, E3, E5, E6 | shape: malformed JSON, wrong type, missing field | 400 | ASP.NET default (RFC 9110 §15.5.1) | One or more validation errors occurred. | — | `errors{}` |
| E1, E6 | shape: unknown enum string, integer enum | 400 | default | as above | — | `errors{}` |
| E1, E3, E5, E6 | shape: **unknown JSON field** | 400 | default | as above | — | `errors{"$.colour": [...]}` |
| E2, E4 (any GET; any route) | shape: **unknown query parameter** | 400 | default | as above | — | `errors{"cancelationDate": ["Unknown query parameter."]}` |
| E1 | shape: postcode > 8, address line 1 missing, names/DOB missing | 400 | default | as above | — | `errors{}` |
| E1 | shape: reference not matching `^[A-Z0-9-]{3,20}$` after trim + upper-case | 400 | default | as above | — | `errors{"reference": [...]}` |
| E4 | shape: `cancellationDate` missing or not `YYYY-MM-DD` | 400 | default | as above | — | `errors{}` |
| E5 | shape: `claimDate` missing, `description` > 500 | 400 | default | as above | — | `errors{}` |
| E2–E6 | GET-1 | 404 | `/problems/get-1` | Policy not found | No policy with reference 'HH-9999'. | `reference` |
| E1 | SELL-1 | 409 | `/problems/sell-1` | Policy reference already exists | A policy with reference 'HH-0001' already exists. (`hh-0001` is the same policy) | `reference` |
| E1 | SELL-2 | 422 | `/problems/sell-2` | Start date outside the sale window | Start date 2026-12-31 is 75 days from today (2026-10-17); a policy can start from today up to 60 days ahead (2026-12-16). Also: a start date before today. | `earliest`, `latest` |
| E1 | SELL-4 | 422 | `/problems/sell-4` | Wrong number of policyholders | 4 policyholders were supplied; a policy must have between 1 and 3. Also: 0. | `count` |
| E1 | SELL-5 | 422 | `/problems/sell-5` | Policyholder under 16 | Policyholder 2 (born 2011-01-01) is 15 on the start date 2026-11-01; all policyholders must be 16 or older. | `index` |
| E1 | SELL-7 | 422 | `/problems/sell-7` | Policy amount must be greater than zero | Amount 0.00 is not valid; a policy's amount must be greater than 0. | `amount` |
| E3, E4 | CANCEL-4 | 409 | `/problems/cancel-4` | Policy already cancelled | Policy 'HH-0001' was already cancelled with effect from 2026-10-20. | `cancellationDate` |
| E3, E4 | CANCEL-3 | 422 | `/problems/cancel-3` | Cancellation date in the past | Cancellation date 2026-10-01 is before today (2026-10-17). | `today` |
| E3, E4 | CANCEL-7 | 422 | `/problems/cancel-7` | Cancellation date after the end of cover | Cancellation date 2027-11-15 is after the policy's last day of cover (2027-10-31). | `latestEndDate` |
| E5 | COULD-2c | 409 | `/problems/could-2c` | Claim on a cancelled policy | Policy 'HH-0001' was cancelled with effect from 2026-10-20; claims cannot be recorded on a cancelled policy. | `cancellationDate` |
| E5 | COULD-2a | 422 | `/problems/could-2a` | Claim date in the future | Claim date 2026-10-18 is after today (2026-10-17). | `today` |
| E5 | COULD-2b | 422 | `/problems/could-2b` | Claim date outside cover | Claim date 2026-10-16 is not within any term of cover (2026-10-17 to 2027-10-16). | `coverStart`, `coverEnd` |
| E6 | RENEW-6 | 409 | `/problems/renew-6` | Cancelled policy cannot be renewed | Policy 'HH-0001' was cancelled with effect from 2026-10-20 and cannot be renewed. | `cancellationDate` |
| E6 | RENEW-1/2 | 422 | `/problems/renew-1-2` | Outside the renewal window | Renewal is allowed from 2027-10-01 to 2027-10-31; today is 2026-10-17. | `windowStart`, `windowEnd` |
| E6 | RENEW-3b | 422 | `/problems/renew-3b` | Payment method sent for an unpaid renewal | Policy 'HH-0001' does not auto-renew, so the renewal is created unpaid; remove `paymentMethod`. | — |
| E6 | RENEW-5 | 422 | `/problems/renew-5` | Renewal amount must be greater than zero | Amount 0.00 is not valid; the new term's amount must be greater than 0. | `amount` |
| E6 | COULD-3 (explicit) | 422 | `/problems/could-3` | Cheque not allowed for auto-renewal | Policy 'HH-0001' auto-renews, so its renewal cannot be paid by Cheque. | `paymentMethod` |
| E6 | COULD-3 (defaulted from RENEW-3a) | 422 | `/problems/could-3` | as above | The current term was paid by Cheque, which cannot pay an auto-renewal; send a `paymentMethod` of Card or DirectDebit. | `paymentMethod` |
| E1, E3, E5, E6 | concurrency (`DbUpdateConcurrencyException`) | 409 | `/problems/concurrency` | The policy was changed by another request | Retry the request. | — (no `ruleId`) |
| any (not `/health/*`), outside Development | transport: request not over HTTPS (after forwarded headers) | 400 | `/problems/https-required` | HTTPS required | This API accepts HTTPS requests only. | — (no `ruleId`) |
| E1, E3, E5, E6 | transport: body over 64 KB (`BadHttpRequestException`) | 413 | default | Request body too large | The request body is too large. (no limit figure) | — (no `ruleId`) |
| E1, E3, E5, E6 | transport: body too slow or truncated (`BadHttpRequestException`) | its `StatusCode` (408, 400) | default | fixed per status | fixed per status; never the exception message | — (no `ruleId`) |
| any (not `/health/*`) | rate limit exceeded (per client IP) | 429 + `Retry-After` | `/problems/rate-limited` | Too many requests | Too many requests. Retry after the number of seconds in the Retry-After header. | — (no `ruleId`) |
| any | unhandled | 500 | default | An error occurred while processing your request. | generic; full details in logs only | `traceId` |

### Success-path rows (rules with no error path)

| Rule | Endpoint | Assert |
|---|---|---|
| SELL-3 | E1 → E2 | `endDate` = start + 1 year − 1 day: 2026-11-01 → 2027-10-31; 1 Mar 2027 → 29 Feb 2028 |
| SELL-3 (named test: 29 Feb start) | E1 → E2 | 29 Feb 2028 → **28 Feb 2029** |
| SELL-5 boundary | E1 | a 16th birthday on the start date → 201 |
| SELL-6 | E1 → E2 | one `payments[]` entry for the full amount, with the request's method and a generated reference; term 1 links to it |
| GET-1 | E2 | full shape; `status` `Pending` / `Active` / `Expired` / `Cancelled`; current-term fields |
| CANCEL-1a | E3, E4 | cancel before start → full refund, `ruleApplied: CANCEL-1a` |
| CANCEL-1b | E3, E4 | cancel on start and on start + 14 → full refund, `CANCEL-1b` |
| CANCEL-1c | E3, E4 | start + 15 → pro-rata, cancellation day counted as used, 365/366-day term |
| CANCEL-2 | E3 | each refund has the method of the payment it reverses |
| CANCEL-5 | E3 | renewed policy: current term by the normal rules, future term refunded in full; two `terms[]` entries |
| CANCEL-6 | E3, E4 | an amount whose refund lands on x.xx5 rounds away from zero |
| CANCEL-3 default | E3 | empty body, `{}` and a null date all mean today |
| COULD-1 | E4 then E3 | the quote equals the later cancel for the same date; E4 changes nothing (E2 unchanged) |
| COULD-2 | E5 → E3 | claim then cancel → £0 refund, `ruleApplied: COULD-2`, overriding CANCEL-1a/1b; `hasClaims: true`; `refunds[]` empty (no £0 Refund record) |
| RENEW-3b × cancel | E6 → E3, E4 | cancelling a policy whose renewed term is unpaid → that term has `ruleApplied: UNPAID-TERM`, `refundAmount` 0.00, and no Refund record |
| RENEW-3a | E6 | auto-renew on: payment created with the request's method, else the current term's |
| RENEW-3b | E6 | auto-renew off, no `paymentMethod`: term created with `paymentReference: null`, no payment |
| RENEW-4 | E6 → E2 | term 2 starts the day after term 1 ends; same reference; renewal window measured from the latest term's end |

### Payloads

**E1 request** (`SellPolicyRequest`). There's no `endDate` (SELL-3) and no `hasClaims` (false at sale). Sending either is an unknown field, so 400.
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

**`PolicyResponse`** (E1, E2, E6). `status` is derived from today and never stored: `Pending` (today < first term's start), `Active` (within cover), `Expired` (today > latest term's end), `Cancelled` (overrides the rest). `startDate`, `endDate` and `amount` are the **current term's**: the term in force today, or the latest term when today is outside every term. `terms[]` holds the history.
```json
{
  "reference": "HH-0001", "type": "Household", "status": "Active",
  "currentTermNumber": 1, "startDate": "2026-11-01", "endDate": "2027-10-31", "amount": 365.00,
  "hasClaims": false, "autoRenew": true,
  "policyholders": [{ "firstName": "Ada", "lastName": "Lovelace", "dateOfBirth": "1985-12-10" }],
  "property": { "addressLine1": "1 High St", "addressLine2": null, "addressLine3": null, "postcode": "M1 1AA" },
  "terms": [{ "termNumber": 1, "startDate": "2026-11-01", "endDate": "2027-10-31", "amount": 365.00, "paymentReference": "PAY-…" }],
  "payments": [{ "reference": "PAY-…", "type": "DirectDebit", "amount": 365.00 }],
  "refunds": [],
  "claims": [],
  "cancellation": null
}
```
After a cancel, `cancellation` is `{ "cancellationDate": "2027-03-01", "ruleApplied": "CANCEL-1c", "totalRefund": 244.00 }`, and `status` is `Cancelled`.

**E3 request** (`CancelPolicyRequest`). The body is optional: no body, `{}` or `"cancellationDate": null` all mean today (CANCEL-3).
```json
{ "cancellationDate": "2027-03-01" }
```

**E3 `CancellationResponse`.** `ruleApplied` is per term: `CANCEL-1a`, `CANCEL-1b`, `CANCEL-1c`, `COULD-2` or `UNPAID-TERM`. The top-level `ruleApplied` is the current term's. **No Refund record is created for £0:** `refunds[]` holds one entry per term with `refundAmount` > 0, and `terms[]` still lists every term.
```json
{
  "reference": "HH-0001", "cancellationDate": "2027-03-01", "ruleApplied": "CANCEL-1c",
  "totalRefund": 244.00,
  "terms": [{ "termNumber": 1, "ruleApplied": "CANCEL-1c", "daysInTerm": 365, "daysUsed": 121, "unusedDays": 244, "refundAmount": 244.00 }],
  "refunds": [{ "reference": "REF-…", "type": "DirectDebit", "amount": 244.00, "paymentReference": "PAY-…" }]
}
```
- CANCEL-5: two `terms[]` entries, e.g. term 1 `CANCEL-1c` and term 2 `CANCEL-1a`, with one refund per paid term whose `refundAmount` > 0.
- COULD-2: `ruleApplied` is `COULD-2` on every paid term, every `refundAmount` is 0.00, and `refunds[]` is empty.
- Unpaid term (RENEW-3b): `ruleApplied` is `UNPAID-TERM` and `refundAmount` is 0.00, with no refund, because there is no payment to reverse. It takes precedence over COULD-2 for that term. `UNPAID-TERM` is a `ruleApplied` value, not a rule ID.

**E4 `CancellationQuoteResponse`.** The same shape as E3 plus `"quotedOn"` (today). `refunds[]` entries carry no `reference`, because nothing is created. Nothing is persisted.

**E5 request** (`RecordClaimRequest`):
```json
{ "claimDate": "2026-12-03", "description": "Burst pipe" }
```
`claimDate` is required. `description` is optional, with a maximum of 500 characters.

**E5 `ClaimResponse`.** Recording any claim sets the policy's `hasClaims` to true (COULD-2).
```json
{ "reference": "CLM-…", "policyReference": "HH-0001", "claimDate": "2026-12-03", "description": "Burst pipe", "hasClaims": true }
```

**E6 request** (`RenewPolicyRequest`):
```json
{ "amount": 389.50, "paymentMethod": "Card" }
```
- `amount` is required and must be > 0 (RENEW-5).
- With auto-renew on, `paymentMethod` is optional and defaults to the current term's (RENEW-3a). It's still subject to COULD-3.
- With auto-renew off, `paymentMethod` must be absent (RENEW-3b, 422).
- The response is `PolicyResponse` with the new term appended (RENEW-4).

**422 example** (E1, SELL-4 and SELL-5 in one request):
```json
{
  "type": "/problems/sell-4",
  "title": "Wrong number of policyholders",
  "status": 422,
  "detail": "2 rules failed.",
  "instance": "/policies",
  "ruleId": "SELL-4",
  "violations": [
    { "ruleId": "SELL-4", "detail": "4 policyholders were supplied; a policy must have between 1 and 3." },
    { "ruleId": "SELL-5", "detail": "Policyholder 2 (born 2011-01-01) is 15 on the start date 2026-11-01; all policyholders must be 16 or older." }
  ],
  "traceId": "00-4bf92f…-01"
}
```
With a single violation, `detail` is that violation's detail, the rule-specific extensions (e.g. `earliest`/`latest`) sit at the top level, and `violations[]` has one entry.

**400 example** (shape, including an unknown field):
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "instance": "/policies",
  "errors": {
    "property.postcode": ["The postcode must be at most 8 characters."],
    "$.colour": ["The JSON property 'colour' could not be mapped to any .NET member."]
  },
  "traceId": "00-…"
}
```

### What derives from this contract

- **Integration tests** (`WebApplicationFactory`, SQLite in-memory, `FakeTimeProvider`).
  - One test per matrix row and per success-path row, each tagged `[Trait("Rule", "<ID>")]`.
  - Error tests assert the status, `application/problem+json`, `type`, `ruleId`, `violations[]` and the extension values.
  - Success tests assert 201 + `Location` and an E1 → E2 round trip.
  - Every Sell test is a POST → GET round trip.
  - The multi-violation 422 has its own test.
  - F0b's walking skeleton needs only E1's stub.
- **Postman** (v2.1, Postman CLI, run manually).
  - There's one folder per feature (F1–F4), and the environment holds `baseUrl`.
  - Pre-request scripts generate a unique `reference` and compute dates from the **real** today, because Postman can't fake time.
  - Because SELL-2 bars backdating, only rows reachable from a policy starting today to today + 60 can run live:

  | Real time (Postman + QA script) | Integration-test only (needs a fake clock) |
  |---|---|
  | All 400 rows, GET-1, SELL-1, SELL-2, SELL-4, SELL-5 (+ boundary), SELL-6, SELL-7 | SELL-3 29 Feb case (unreachable within 60 days of today) |
  | `status` Pending (future start) and Active (start today) | `status` Expired |
  | CANCEL-1a (future start), CANCEL-1b (start today, cancel today), CANCEL-1c (start today, future `cancellationDate` ≥ start + 15) | CANCEL-5 (needs a renewal) |
  | CANCEL-2, CANCEL-3, CANCEL-4, CANCEL-6, CANCEL-7 (date after end), COULD-1 | RENEW success rows: RENEW-3a, RENEW-3b unpaid term, RENEW-4, COULD-3 *alone* |
  | COULD-2 £0 refund, COULD-2a, COULD-2b, COULD-2c (start today, claim today) | the concurrency 409 |
  | RENEW-6, RENEW-1/2; RENEW-3b, RENEW-5 and COULD-3 appear **alongside** RENEW-1/2 in `violations[]` | the 500 row |

- **QA script** (`docs/qa/`, written by the tester after PASS). One step per real-time row above, giving the request, the expected status and `ruleId` (plus `violations[]` where several apply). The integration-only rows are listed as "covered by test `<name>`" so that David can run them with `dotnet test`.

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

## Rulings (David, 2026-10-18): transport hardening (F1 steer D1)

The F0b security P2s (S1 HTTPS/HSTS, S2 body limit) were addressed in F1, with rate limiting added. Verified
against Microsoft Learn on 2026-10-18 ([enforcing SSL](https://learn.microsoft.com/en-us/aspnet/core/security/enforcing-ssl?view=aspnetcore-10.0),
[rate limiting](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0),
[App Service ASP.NET Core](https://learn.microsoft.com/en-us/azure/app-service/configure-language-dotnetcore?pivots=platform-linux)).

- **H1 → reject plain HTTP, don't redirect.** Learn: API clients "might not understand or obey redirects from
  HTTP to HTTPS, and they might send information over HTTP"; APIs should not listen on HTTP or should answer
  400. Outside Development, a request that isn't HTTPS (judged after forwarded headers) gets 400
  `/problems/https-required`. `/health/*` is exempt, because the App Service health probe's scheme is
  unverified. HSTS is on outside Development (it skips loopback by default). No `UseHttpsRedirection()`.
  Transport itself is App Service's `httpsOnly: true` and `minTlsVersion: '1.2'` (infrastructure).
- **H2 → forwarded headers, trusted from App Service's private ranges only.** App Service terminates TLS at
  its load balancer, so the app sees HTTP from the balancer's address. `UseForwardedHeaders()` runs first, with
  `XForwardedFor | XForwardedProto`, the known networks 10.0.0.0/8, 172.16.0.0/12 and 192.168.0.0/16 (Learn's
  App Service guidance), and the default forward limit of 1. Never `ASPNETCORE_FORWARDEDHEADERS_ENABLED`: it
  trusts any proxy, so a client could spoof its IP past the rate limiter.
- **H3 → a 64 KB request body limit, server-wide** (Kestrel `MaxRequestBodySize = 65536`). An oversized body is
  413, not 500.
- **H4 → `BadHttpRequestException` keeps its own status.** It is a client error (400, 408, 413), answered as
  ProblemDetails with a fixed title and detail per status. The exception message (which names the byte limit)
  and the limit figure never reach the body. It's logged at Warning, not Error, so a client can't flood the
  error logs.
- **H5 → rate limiting, per client IP, built-in middleware (no package).** A global fixed-window limiter
  (default 100 requests per 60 s) and a stricter `writes` policy on every POST (default 10 per 60 s), both
  from the `RateLimiting` configuration section, with no queue. Rejections are 429 (the framework default is
  503) with `Retry-After` and a `/problems/rate-limited` body. `/health/*` is exempt. The client IP is never
  logged (it's personal data). Stated limits, for the README: counters are in memory per instance; IP is a
  crude identity behind NAT; the production answer at scale is the edge (Front Door WAF rate-limit rules or
  API Management `rate-limit-by-key`), which this infrastructure doesn't build.

## Open questions

Q1–Q8 were resolved in the Rulings and are folded into the Contract. What remains:

- **Q9. Amend requirements.md.** Resolved in reconciliation (2026-10-17): requirements.md now has CANCEL-7, COULD-2a–c, RENEW-3b, COULD-3 defaulted, RFC 9457 and the status summary. It needs:
  - the new rule IDs CANCEL-7, COULD-2a, COULD-2b and COULD-2c
  - RENEW-3b's 422, and COULD-3's defaulted-Cheque case
  - the statuses added by D5
  - "RFC 7807" → "RFC 9457" (D11)

  The rule-coverage check reads rule IDs from requirements.md, so the new rules won't be proven covered until they're added there.
- **Q10. Sell `amount` ≤ 0.** Resolved (David, 2026-10-17): new rule SELL-7, 422 with `ruleId` SELL-7 (matrix, evaluation order, requirements.md §2).
- **Q11. Refund rows for £0.** Resolved (David, 2026-10-17): no Refund record for £0 (COULD-2, or an unpaid RENEW-3b term). The cancellation response still lists each term with `refundAmount` 0.00 and `ruleApplied` `COULD-2` or `UNPAID-TERM` (E3 `CancellationResponse`).
