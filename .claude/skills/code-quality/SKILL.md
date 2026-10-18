---
name: code-quality
description: >-
  Code quality at this project's size: SOLID without ceremony, DI only at real seams, the rule of three,
  and the slop list. Applied by the builder when writing and by the reviewer when judging.
user-invocable: false
---
# Code quality

The code must be explainable line by line by David. Simple and correct beats clever. Done beats perfect.

## SOLID at this size

- **Single responsibility:** a type has one reason to change. `PolicyService` orchestrates (load → today
  → domain → save) and holds no rules. Rules live in the domain. Controllers map HTTP to the service and back.
- **Dependency injection at the layer seams:** `IPolicyService` (Api → Services), `IPolicyRepository`
  (Services → Persistence), `IReferenceGenerator` and the clock (`TimeProvider`). Anything else is concrete.
- **Interfaces at layer seams only** (David, 2026-10-18), never inside the Domain. No generic repository,
  no Unit of Work, no mediator. A model per layer, mapped with Mapperly (compile-time); never AutoMapper.
- **Open/closed** isn't a licence for extension points nobody asked for. Add one when the second case
  exists, not before.

## Rule of three

Duplicate once and tolerate it. Extract on the **third** occurrence, and only when the three copies change
for the same reason. Two similar-looking test setups are not a reason to build an abstraction; builders
already exist for that.

## Naming

- Use the domain language of `docs/requirements.md`: policy, term, policyholder, cancellation, refund,
  claim, renewal, cooling-off.
- No `Manager`, `Helper`, `Util` or `Processor` types, and no abbreviations that the requirements don't use.
- Name a boolean for what is true (`HasClaims`, `AutoRenew`), not for the negation.

## Slop list (each one is a finding)

- Dead code: unused members, parameters, branches or usings, and commented-out code.
- Comments that restate the code. A comment explains *why*: a non-obvious rule interpretation or a
  boundary choice. Use the rule ID when one applies (`// CANCEL-1b: cooling-off is start + 14 inclusive`).
- Speculative generality: options, flags, abstractions or configuration for cases no rule needs.
- Invented requirements: behaviour no rule row or contract line asks for.
- Catch-all handlers: `catch (Exception)` outside `UnexpectedExceptionHandler`, a swallowed exception,
  or catching to make a test pass.
- Magic dates and money: a literal date or amount in production code where a rule value or `today`
  belongs. Money is `decimal`, rounded only by `Money.Round`.
- Unused packages, and packages outside the approved list.
- Console output or string-interpolated log messages. Use `ILogger` templates, and never log personal data.
- A large method doing several things where named steps would read better, and a tiny wrapper that adds
  nothing.

## Refactor step

- Make minimal-diff changes with no behaviour change, and keep every test green.
- Refactor only code this node touched. Don't reformat or rename unrelated files.
- A refactor that can't be explained in one line is too big for this step.
