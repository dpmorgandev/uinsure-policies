---
name: owasp-api-security
description: >-
  The security checklist for this API: OWASP API Security Top 10 2023 mapped In/Out, a grep per check,
  the .NET extras, and the P0/P1/P2 severity rubric.
user-invocable: false
---
# OWASP API Security Top 10 (2023) for this API

Check the node's diff and the files it touches. Every item gets a result:
- **In** items are checked, and a failure is a finding.
- **Out** items are recorded as `n/a` in `notes`, with the reason below. Never skip one silently, and
  never fail the node for out-of-scope auth.

## Checklist

| ID | Item | Here | Check | Grep (start here, then read) |
|---|---|---|---|---|
| API1:2023 | Broken Object Level Authorization | **Out**: no auth or tenancy; the reference is the only key, client-chosen and guessable (the README says so) | — | — |
| API2:2023 | Broken Authentication | **Out**: auth is out of scope | — | — |
| API3:2023 | Broken Object Property Level Authorization | **In** | Request DTOs are separate from entities, with no binding to EF entities. The client can't set `EndDate`, `HasClaims`, payment or refund references, `Cancellation` or `Version`. Responses leak no internal ids or concurrency tokens | `\[FromBody\]` on Domain or EF types; `Version` in `Contracts/` |
| API4:2023 | Unrestricted Resource Consumption | **In (light)** | `MaxRequestBodySize` is sane. Collections are bounded (1–3 policyholders, enforced before processing). String max lengths (postcode 8, description 500) are on both the DTO and the EF column. No unbounded list endpoint. Rate limiting: note only | `MaxLength\|StringLength\|HasMaxLength`; any `GET /policies` list route |
| API5:2023 | Broken Function Level Authorization | **Out**: no roles. Note that `POST /claims` would be an admin function in real life | — | — |
| API6:2023 | Unrestricted Access to Sensitive Business Flows | **In: the key item** | Refund abuse is impossible: cancelling twice → 409 (CANCEL-4); renew-then-cancel can't refund more than was paid; a claim → £0; refund total ≤ the sum of payments per term; the quote endpoint changes no state; a concurrent double-cancel → 409 via the `Version` token | `SaveChanges` in the quote path; `IsConcurrencyToken`; refund sum logic |
| API7:2023 | SSRF | **Out**: no outbound URL fetching | confirm no `HttpClient` in `src/` | `HttpClient\|WebRequest` |
| API8:2023 | Security Misconfiguration | **In** | ProblemDetails has no stack traces or exception messages outside Development. No `UseDeveloperExceptionPage` on the production path. No connection strings or secrets committed. HTTPS redirection and HSTS are on. OpenAPI and Scalar are Development only | `UseDeveloperExceptionPage\|Exception.Message\|StackTrace`; `Password=\|AccountKey=` |
| API9:2023 | Improper Inventory Management | **In (light)** | The OpenAPI doc matches the actual routes. No stub or hard-coded endpoint is left after the stub step | canned values in controllers; routes vs E1–E6 |
| API10:2023 | Unsafe Consumption of APIs | **Out**: no upstream APIs (pricing is client-supplied) | — | — |

## .NET extras (always In)

- **SQL:** EF Core LINQ only. No `FromSqlRaw` or `ExecuteSqlRaw` with interpolated or concatenated input.
  Grep `FromSqlRaw\|ExecuteSqlRaw\|SqlQueryRaw`.
- **Money:** `decimal` throughout, never `double` or `float`, rounded only by `Money.Round`.
  Grep `double\|float` near amount, refund or price names.
- **Packages:** nothing outside the approved list. A package with a known vulnerability is a finding. The
  orchestrator's gate receipts may include `dotnet list package --vulnerable`; read them.
- **Secrets:** no `.env`, keys, tokens or connection strings with credentials in the diff.
- **Logging:** no personal data (names, dates of birth, addresses, postcodes) and no request bodies in
  logs. Grep `Log(Information|Warning|Error|Debug)` and inspect the arguments.

## Severity rubric

| Severity | Meaning | Examples |
|---|---|---|
| `P0` | Exploitable now, or money or data integrity broken | Refund exceeds payment; double refund possible; secret committed; mass assignment of `HasClaims` |
| `P1` | A real weakness that must be fixed before merge | Stack trace in a non-Development error; unbounded string column; raw SQL with interpolation |
| `P2` | Hardening or hygiene, not blocking | Missing HSTS note; rate-limiting note |

`PASS` requires no P0 or P1. Each finding's `rule_id` is the OWASP id (for example `API6:2023`), or `NET`
for a .NET extra.
