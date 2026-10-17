# deployment.spec.md

Status: DECIDED — contract final (2026-10-17)
Researched: 2026-10-17 · Feature: F5 (last, time-boxed, first to cut)

**Scope guard.** One Azure DevOps multi-stage YAML pipeline, written as evidence. It's **never connected** to an Azure DevOps
organisation, never run, and has no service connections or environments created. "Validated locally" means only what can honestly
be checked offline (see R8), and the README says exactly that.

---

## Dependencies

| Depends on | Assumption made here |
|---|---|
| `infrastructure.spec` | Subscription-scope `main.bicep` + `env/<env>.bicepparam`. Outputs `resourceGroupName`, `appName`, `appHostName`, `sqlServerFqdn`, `sqlDatabaseName`, `appPrincipalName`. Host is App Service Linux (decided), so the build publishes the Api project (one of four src projects) as a zip, with no image |
| `architecture.spec` | `/health/live` and `/health/ready` exist. Migrations are **SQL Server only**, never applied at app start-up. Config keys are `Database__Provider` and `ConnectionStrings__Policies` |
| `testing.spec` | Assumed suites: unit, integration (`WebApplicationFactory` on SQLite), architecture. All run by `dotnet test` on the solution. Postman is **not** run in the pipeline (it belongs to the local hook/HITL decision) |
| `guardrails.spec` | No secrets in YAML. There are no pipeline secrets at all with workload identity federation |
| requirements.md §3 | "No CI" (local git hooks do the real gating). This pipeline is *evidence of how we'd do CI/CD at Uinsure*, not a CI we rely on |

---

## Research

### R1. Service connections: workload identity federation

Microsoft recommends **workload identity federation** (WIF) for Azure Resource Manager service connections, either through an app registration
(automatic) or a user-assigned managed identity. It "eliminates the need for secrets". New WIF connections use the
Entra issuer by default, and the old `vstoken.dev.azure.com` issuer retires 1 Jul 2027. **Don't** tick "grant access to all
pipelines". Authorise each pipeline instead.
Source: [ARM service connection](https://learn.microsoft.com/en-us/azure/devops/pipelines/library/connect-to-azure) (2026-08-18).
→ One connection per environment (`sc-uinsurepolicies-dev|test|prod`), each scoped to its own subscription/RG, so a dev
credential can't touch prod.

### R2. Environments, approvals and "push-button" prod

- A `deployment` job targets an **Environment** (`environment: uinsurepolicies-prod`). This gives deployment history and commit/work-item traceability.
- **Approvals and checks are configured on the Environment in the ADO UI, not in YAML.** Resource owners
  "define approvals and checks that must be satisfied before a stage consuming that resource can begin".
  Source: [Environments](https://learn.microsoft.com/en-us/azure/devops/pipelines/process/environments) (2026-06-17).
- **Friction to name:** the prod gate is invisible in the repo. Evidence of it has to be prose (README: "the
  `uinsurepolicies-prod` environment has an Approval check: approver = release manager, timeout 72h"). That's a real limitation of the
  platform, not something we skipped.
- An environment referenced from YAML edited outside the web editor **isn't auto-created, and the run fails**. So the README
  lists "create 3 environments + approval on prod" as a one-time setup step.
- "Push-button" = merge to `main` runs Build → dev → test automatically, and prod waits on one Approve click.

### R3. Bicep deploy step: three task options

From [Integrate Bicep with Azure Pipelines](https://learn.microsoft.com/en-us/azure/azure-resource-manager/bicep/add-template-to-azure-pipelines) (2026-06-11):

| Task | Notes |
|---|---|
| `BicepDeploy@0` | **Microsoft now calls it "the recommended option for new pipelines".** It deploys `.bicep`/`.bicepparam` directly, supports `operation: whatIf`, validation, deployment stacks and output masking. Version **0**, so it's new |
| `AzureResourceManagerTemplateDeployment@3` | `.bicepparam` is supported from 3.235.0. RG-centric |
| `AzureCLI@2` + `az deployment sub what-if` / `create` | Plain CLI. The same commands run on a laptop. `.bicepparam` only, **no parameter overrides**. Watch out: the Learn sample uses PowerShell backtick line-continuation inside a `bash` script, which is a doc bug. Don't copy it |

### R4. App deploy

`AzureWebApp@1` with `appType: webAppLinux`, `package: $(Pipeline.Workspace)/drop/app.zip`, `runtimeStack: 'DOTNETCORE|10.0'`
(the runtime is already set by Bicep, so this is belt-and-braces). **(?)** I didn't recheck the task's current input names on Learn.
Confirm them against the task reference when writing.

### R5. EF Core migrations: strategy

From [Applying migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying) (2026-08-05):

| Strategy | Learn's recommended use | Review SQL first | EF migration lock |
|---|---|---|---|
| **Idempotent SQL script** (`dotnet ef migrations script --idempotent -o migrations.sql`) | "DBA-controlled or **review-gated** deployment" | **Yes** | No |
| **Migration bundle** (`dotnet ef migrations bundle --self-contained -r linux-x64`) | "**For automated deployment, use a migration bundle**" | No: "a bundle does not currently provide a way to inspect the SQL" | Yes |
| Runtime `MigrateAsync()` | Apps that accept start-up trade-offs | No | Yes (EF 9+) |

Also from the same page:
- "Use a separate identity for deployment that has permission to change the schema. The identity used by the application at run time should normally have only the permissions [it] needs."
- Run `dotnet ef migrations has-pending-model-changes` in CI. Since EF 9, `Migrate()` throws on pending model changes.
- Set `ASPNETCORE_ENVIRONMENT` explicitly when generating artifacts, or design-time defaults to `Development`.
- The idempotent script is provider-dependent. **SQLite can't produce one**, which fits architecture's SQL-Server-only migration set.

**Honest tension:** Learn's default for automation is the *bundle*. Our story is *review-gated prod*: the approver can open
`migrations.sql` in the run's artifacts before clicking Approve. That's exactly the case where Learn says to use a script.
Both are defensible. The script is the one that makes the approval gate *mean* something.

### R6. Running SQL from a hosted agent (migrations + `CREATE USER`)

- The Bicep can't create the app identity's DB user (see infrastructure R2). A T-SQL step must run, as the SQL Entra admin:
  `IF NOT EXISTS (...) CREATE USER [app-uinsurepolicies-<env>] FROM EXTERNAL PROVIDER; ALTER ROLE db_datareader/db_datawriter ADD MEMBER ...`
  (idempotent, checked into `infrastructure/sql/grant-app-identity.sql`).
- Inside `AzureCLI@2` the agent is signed in as the WIF identity. **(?)** `sqlcmd` (go-sqlcmd) with
  `--authentication-method ActiveDirectoryDefault` (or `-G`) should pick up the Azure CLI credential. Alternatively, fetch a token with
  `az account get-access-token --resource https://database.windows.net/` and pass it. I haven't confirmed whether go-sqlcmd is
  preinstalled on `ubuntu-latest`. Plan for an install step.
- Reachability: hosted agents reach SQL through the "Allow Azure services" firewall rule (infrastructure I6-A). With private networking you'd
  need a self-hosted / Managed DevOps Pool agent in the VNet. Name that, don't build it.
- The deploy identity must be in the SQL **Entra admin group**. That's a one-time setup line in the README.

### R7. Smoke test

After the app deploy, `curl --fail --retry 10 --retry-delay 6 --retry-all-errors https://$(appHostName)/health/ready` proves
the app started, *and* that MI → SQL works (the readiness check hits the DB). `/health/live` alone would hide a broken DB grant.
Postman CLI against dev is a possible extension. **Cut**, because it adds an API key and a collection-env per stage.

### R8. Validating the YAML without Azure DevOps (limits stated honestly)

| Check | What it proves | What it can't |
|---|---|---|
| YAML parse (any parser, e.g. Python `yaml.safe_load`) | Well-formed YAML | Anything Azure-specific |
| **JSON-schema** check against Microsoft's `service-schema.json` (from `microsoft/azure-pipelines-vscode`), e.g. `check-jsonschema` (Python 3.13 is on this machine; there's a pre-commit hook id `check-azure-pipelines`) | Keywords and structure (stages/jobs/steps, known task names) | **Template expansion (`template:` + `${{ }}`), expression evaluation, task input validity, whether service connections/environments exist.** The schema is permissive |
| VS Code + Red Hat YAML extension mapped to the same schema | Inline editor errors | Same limits |
| **Real** validation: ADO web editor "Validate", or the Runs REST API with `previewRun: true` (returns the expanded YAML) | Full compile incl. templates | **Needs an ADO org + pipeline. Out of bounds here** |

**(?)** The exact `--builtin-schema` name in `check-jsonschema` wasn't confirmed (the hook id `check-azure-pipelines` was).
Sources: [check-jsonschema pre-commit](https://check-jsonschema.readthedocs.io/en/stable/precommit_usage.html),
[Baeldung local validation](https://www.baeldung.com/ops/azure-devops-pipeline-local-validation) (secondary source).
**Conclusion:** the strongest honest claim is "schema-valid, templates not expanded, never compiled by Azure DevOps".
Keeping templates few and shallow means the schema check covers more of the real file.

---

## Options

| # | Decision | A (recommended) | B | C |
|---|---|---|---|---|
| D1 | Bicep task | **`AzureCLI@2` + `az deployment sub what-if/create`** | `BicepDeploy@0` (Learn's current recommendation) | `AzureResourceManagerTemplateDeployment@3` |
| D2 | Migrations | **Idempotent SQL script, built in CI, published as artifact, applied by `sqlcmd` per env** | Self-contained `efbundle` (Learn's automation default) | `MigrateAsync()` at start-up (architecture already rejects this) |
| D3 | Prod gate | **Environment approval + separate `Prod_Plan` stage (what-if) before the gated `Prod` stage** | Approval only (what-if runs *after* approval, inside prod stage) | Manual-trigger-only prod (`trigger: none` param) |
| D4 | Reuse | **One `templates/deploy-env.yml` stage template, called 3× with `env` + `serviceConnection`** | Copy-paste 3 stages | Job + step templates (deeper nesting) |
| D5 | Smoke | **`curl` `/health/ready` with retries** | + Postman CLI against dev | none |
| D6 | Local YAML check | **`check-jsonschema` against service-schema in `infrastructure/validate.ps1`** | YAML parse only | nothing (state "unvalidated") |
| D7 | Agent | **`ubuntu-latest` Microsoft-hosted; `UseDotNet@2` with `useGlobalJson: true`** | Windows agent | self-hosted |

Why D1-A over Learn's B: it's the same command David can show on a laptop, it's a stable v2 task, and the SQL step needs `AzureCLI@2` anyway,
so it's one task type to explain. B is the right answer if Uinsure already uses it. Mention that it exists and that Learn recommends it.
Why D3-A: approval happens *before* a stage starts, so with B the approver approves blind. A `Prod_Plan` stage that runs
`what-if` + publishes `migrations.sql` gives the approver something to read. It costs about 15 lines and is the first thing to cut.
Why D2-A over B: see R5's tension. If David prefers B, the bundle is runnable under `AzureCLI@2` with
`--connection "...;Authentication=Active Directory Default"` and needs no `sqlcmd` for migrations (the grant script still does).

---

## Recommendation

D1-A, D2-A, D3-A, D4-A, D5-A, D6-A, D7-A.

**Smallest credible version (if time is short), in cut order:** drop `Prod_Plan` (D3→B) → drop the schema check (D6→B, and say
so) → collapse the stage template into three explicit stages (D4→B). **Never cut:** the env approval on prod, WIF service
connections, migrations as a pipeline step (not at start-up), the `/health/ready` smoke test. Those four are the
Uinsure-relevant claims.

---

## Decision — DECIDED 2026-10-17 (David)

- [x] **D1–D7, naming and the README one-time setup list:** accepted as recommended, in a block.
  - `AzureCLI@2` with what-if.
  - An idempotent migrations script.
  - A plan stage plus prod approval.
  - One stage template used three times.
  - A `curl /health/ready` smoke test.
  - `ubuntu-latest` + `global.json`.
  - The YAML is schema-checked offline.
  - Environments are named `uinsurepolicies-dev|test|prod`.
- [x] **Final rulings (David, 2026-10-17):**
  - OQ3: the YAML and Bicep checks stay in the manual `validate.ps1`. They are in no git hook.
  - Accepted as proposed: the deployed dev host environment is named `Dev`, never `Development` (infrastructure).

---

## Contract

Binding for F5. It's written as evidence. **Never connected to Azure DevOps, and never run.**

### Files

```text
infrastructure/
  pipelines/
    azure-pipelines.yml         trigger: main · pr: none · pool: vmImage ubuntu-latest · stages below
    templates/
      deploy-env.yml            stage template; parameters: env, serviceConnection, dependsOn
  sql/
    grant-app-identity.sql      idempotent CREATE USER [$(AppName)] FROM EXTERNAL PROVIDER + db_datareader/db_datawriter
  validate.ps1                  shared with infrastructure.spec; step 4 = YAML schema check
```

### Stages

```text
Build ──► Dev ──► Test ──► Prod_Plan ──► [Approval on env uinsurepolicies-prod] ──► Prod
          (auto)  (auto)   (what-if,       (configured in the ADO UI)                (deploy)
                            no env)
```

`Dev`, `Test` and `Prod` are the **same stage template** called three times:

```yaml
- template: templates/deploy-env.yml
  parameters: { env: dev, serviceConnection: sc-uinsurepolicies-dev, dependsOn: Build }
# test → dependsOn: Dev · prod → dependsOn: Prod_Plan
```

### Stage `Build` (plain job)

1. `UseDotNet@2` with `useGlobalJson: true`. The SDK version and the MTP test runner both come from `global.json`.
2. `dotnet restore`, then `dotnet build -c Release --no-restore -warnaserror`.
3. `dotnet test -c Release --no-build -- --report-trx` (MTP), then `PublishTestResults@2` (VSTest/TRX format). Covers Domain.Tests,
   Services.Tests, Api.Tests and Architecture.Tests. Postman isn't run.
4. `dotnet tool restore` (dotnet-ef pinned in `.config/dotnet-tools.json`), then
   `dotnet ef migrations has-pending-model-changes --project src/Uinsure.Policies.Persistence --startup-project src/Uinsure.Policies.Api`.
   It fails if there are changes.
5. With `ASPNETCORE_ENVIRONMENT=Production`, `dotnet ef migrations script --idempotent --project src/Uinsure.Policies.Persistence --startup-project src/Uinsure.Policies.Api -o $(Build.ArtifactStagingDirectory)/migrations.sql`.
6. `dotnet publish src/Uinsure.Policies.Api -c Release -o publish` (only the Api project is published; it carries Services,
   Persistence and Domain), then `ArchiveFiles@2` → `app.zip`.
7. `PublishPipelineArtifact@1` publishes `drop`, which holds `app.zip`, `migrations.sql` and `infrastructure/` (bicep + sql).

### Template `deploy-env.yml` (one `deployment` job, `environment: uinsurepolicies-${{ parameters.env }}`, `strategy: runOnce`)

1. **Infra:** `AzureCLI@2` (bash) runs `az deployment sub what-if --location uksouth --parameters bicep/env/<env>.bicepparam`, then
   `az deployment sub create` with the same arguments. It reads the outputs (`appName`, `appHostName`, `sqlServerFqdn`,
   `sqlDatabaseName`) into variables with `##vso[task.setvariable]`. The what-if output stays in the log as the record of the change.
2. **SQL:** `AzureCLI@2` installs go-sqlcmd if it's absent. Then, as the deploy identity (`--authentication-method ActiveDirectoryDefault`):
   - `grant-app-identity.sql -v AppName=$(appName)` runs first, because the system-assigned MI exists only after step 1.
   - Then `migrations.sql`. The script is **idempotent**: each migration is guarded by `__EFMigrationsHistory`, so a re-run is a no-op.
     Migrations are never applied at app start-up.
3. **App:** `AzureWebApp@1` with `appType: webAppLinux` deploys `app.zip` to `$(appName)`. Confirm the input names against the task
   reference when writing.
4. **Smoke:** `curl --fail --retry 10 --retry-delay 6 --retry-all-errors https://$(appHostName)/health/ready`. A non-200 fails the stage.
   `/health/ready` hits the DB, so it proves the MI grant too.

### Stage `Prod_Plan` (plain job, prod service connection, no environment)

`az deployment sub what-if` for prod, plus a run summary pointing at `migrations.sql` in the `drop` artifact. It makes **no changes**.
It exists so that the prod approver reads the infra diff and the exact SQL **before** clicking Approve. Approval happens before a stage
starts, so without this stage the approver would approve blind.

**Prod approval:** an Approval check on environment `uinsurepolicies-prod` (approver: release manager, timeout 72 h). It lives in
the ADO UI, not the YAML, so the README is its only evidence. That's a platform limit, not a gap.

**Variables:** none are secret, and there is no variable group. Per-environment values come only from `.bicepparam` and template parameters.

### One-time setup (README list; none of it done)

1. Create an ADO project and a pipeline from `infrastructure/pipelines/azure-pipelines.yml`.
2. Create three ARM service connections using **workload identity federation**: `sc-uinsurepolicies-dev|test|prod`. Authorise each one
   for this pipeline only, never "all pipelines".
3. Give each connection's identity rights at **subscription scope** to its own subscription (Contributor, or a narrower custom role),
   because `main.bicep` creates the RG.
4. Create one Entra group per environment as the SQL admin, holding that environment's deploy identity plus a break-glass human.
   Put its object ID and name in `<env>.bicepparam`.
5. Create the environments `uinsurepolicies-dev`, `uinsurepolicies-test` and `uinsurepolicies-prod`. They aren't auto-created
   from YAML. Add the Approval check on prod.

### Offline schema check (`validate.ps1` step 4)

`check-jsonschema` (Python), run against Microsoft's Azure Pipelines `service-schema.json` (the schema behind the
`check-azure-pipelines` pre-commit hook), on both YAML files. **The claim is exactly:** "the YAML is valid against the schema;
template expansion, `${{ }}` expressions, task inputs, service connections and environments are not checked". The flag name is
confirmed when the step is written (R8 **(?)**). `validate.ps1` (Bicep and YAML checks) is run by hand; it is in no git hook.

**Acceptance (F5 deployment done):** `validate.ps1` passes the schema check on both files, and the README statement below is present.

### README statement (verbatim)

> **Never connected, never run.** This pipeline has never been connected to an Azure DevOps organisation or run. No service
> connections, environments or approvals exist. The Bicep is linted and built locally, and the YAML is schema-checked locally.
> Neither what-if nor a real pipeline compile was run, because both need Azure or Azure DevOps. The repo's real gate is its local
> git hooks (requirements §3, "No CI"). This pipeline shows how CI/CD would look at Uinsure.

### Cut order

- **Plan level:** F7 is cut before F5.
- **Within deployment:** `Prod_Plan` (what-if moves inside Prod) → the schema check (the README says "unvalidated") → the stage
  template (three explicit stages).
- **Never cut:** the prod environment approval, the WIF connections, migrations as a pipeline step, and the `/health/ready` smoke test.

---

## Open questions

1. ~~Script vs bundle~~: **resolved**. It's the idempotent script, because it's review-gated: the approver reads `migrations.sql`.
   The talking point is that Learn's default for automation is the bundle, and we chose the script deliberately.
2. **(open)** Is go-sqlcmd preinstalled on `ubuntu-latest`, and does `ActiveDirectoryDefault` pick up the `AzureCLI@2` login? It's
   unverifiable offline. The contract installs it if absent. The fallback is `az account get-access-token --resource https://database.windows.net/`.
3. Resolved (David, 2026-10-17): no. The YAML/Bicep check stays the manual `validate.ps1` and is in no git hook.
4. Resolved in reconciliation (2026-10-17): requirements.md §3 now says "validated locally and never connected to Azure". Amend requirements.md §3's "validated locally" to match the README statement. That's a follow-up outside this spec.
5. ~~"No CI" vs a Build stage~~: **resolved**. The README statement's last two sentences cover it.
