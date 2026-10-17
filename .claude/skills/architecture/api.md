# API contract

Binding routes, rule × status matrix and payloads. Part of the architecture skill. Rule IDs are those in `docs/requirements.md`.

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
