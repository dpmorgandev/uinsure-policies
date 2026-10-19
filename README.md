# Uinsure Policies API

A C# REST API for home insurance policies: sell a policy, retrieve it, cancel it (with a refund and a refund
quote), record claims and renew it. It is a .NET 10 ASP.NET Core controller API with EF Core (SQLite locally,
SQL Server when deployed), problem-details errors (RFC 9457) and OpenAPI with Scalar. It was built outside-in
with TDD, one feature at a time. Every rule, its ID and the interpretation taken for it are in
[docs/requirements.md](docs/requirements.md).

## How to run it

**Prerequisites:** the .NET 10 SDK (`global.json` pins 10.0.100 and rolls forward to the latest installed 10.0 feature band, so any newer 10.0 SDK is accepted).
The Postman CLI is only needed for the Postman collection.

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

At F6 that was 637 tests: 173 domain, 65 services, 332 API acceptance and 67 architecture. All pass.

**Run the Postman collection** (109 requests; start the API first):

```sh
postman collection run tests/postman/Uinsure.Policies.postman_collection.json \
  -e tests/postman/local.postman_environment.json
```

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
- **No £0 Refund record is created.** The cancellation response still lists every term with `refundAmount` 0.00.
- **Status is derived** from today (Pending, Active, Expired, Cancelled) and never stored.
- **Errors:** `400` for shape, `404` unknown reference, `409` state conflicts, `422` for the business rules (every numbered rule that is not a `404` or `409`),
  with all broken rules listed together. Unknown JSON fields and query parameters are a `400`.
- **No authentication** (out of scope). `Location` on renewals and claims points at the policy.

## Known limits

- No authentication, so the reference is the only key. See the [security scope](docs/design.md#security-scope).
- Rate limits are per instance. Claims are not idempotent and have no cap per policy.
- The tests run on SQLite. SQL Server behaviour is proved only as far as the migration script generating.
- The Bicep and Azure DevOps pipeline were never deployed or run. See
  [infrastructure/README.md](infrastructure/README.md).

The full lists are in [docs/design.md](docs/design.md#known-limits) and
[docs/design.md](docs/design.md#behaviours-to-review).

## More detail

- [docs/design.md](docs/design.md): endpoints, errors, tests, design decisions, security scope, known limits and
  behaviours to review.
- [infrastructure/README.md](infrastructure/README.md): the Bicep and pipeline, networking, secrets, one-time
  setup and what the evidence does and does not prove.
- [docs/requirements.md](docs/requirements.md): every rule with its agreed interpretation.
