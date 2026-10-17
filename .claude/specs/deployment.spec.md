# deployment.spec.md

Status: RESEARCH COMPLETE — decision pending
Researched: 2026-10-17 · Feature: F5 (last, time-boxed, first to cut)

**Scope guard.** One Azure DevOps multi-stage YAML pipeline, written as evidence. It's **never connected** to an Azure DevOps
organisation, never run, and has no service connections or environments created. "Validated locally" means only what can honestly
be checked offline (see R8), and the README says exactly that.

---

## Dependencies

| Depends on | Assumption made here |
|---|---|
| `infrastructure.spec` | Subscription-scope `main.bicep` + `env/<env>.bicepparam`. Outputs `resourceGroupName`, `appName`, `appHostName`, `sqlServerFqdn`, `sqlDatabaseName`, `appPrincipalName`. Host is App Service Linux (if it becomes Container Apps, the app-deploy step changes, and so does the build: image push instead of zip) |
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

---

## Contract — PENDING (DRAFT)

```text
infrastructure/
  pipelines/
    azure-pipelines.yml           trigger: main · pool ubuntu-latest · stages below
    templates/
      deploy-env.yml              parameters: env, serviceConnection, dependsOn · one deployment job
  sql/
    grant-app-identity.sql        idempotent CREATE USER FROM EXTERNAL PROVIDER + datareader/datawriter
  validate.ps1                    (shared with infrastructure.spec) + YAML schema check
```

```text
Build ──► Dev ──► Test ──► Prod_Plan ──► [Approval on env uinsurepolicies-prod] ──► Prod
          (auto)  (auto)   (what-if,       (UI-configured check)                    (deploy)
                            no env)
```

**Stage `Build`** (job, not deployment):
1. `UseDotNet@2` (`useGlobalJson: true`)
2. `dotnet restore` · `dotnet build -c Release -warnaserror`
3. `dotnet test -c Release --no-build --logger trx` → `PublishTestResults@2` (unit + integration + architecture)
4. `dotnet tool restore` (dotnet-ef pinned in `.config/dotnet-tools.json`) · `dotnet ef migrations has-pending-model-changes` (fail if true)
5. `ASPNETCORE_ENVIRONMENT=Production dotnet ef migrations script --idempotent -o $(Build.ArtifactStagingDirectory)/migrations.sql`
6. `dotnet publish src/Uinsure.Policies.Api -c Release -o publish` → zip → `app.zip`
7. `PublishPipelineArtifact@1` `drop` = `app.zip`, `migrations.sql`, `infrastructure/` (bicep + sql)

**Template `deploy-env.yml`** (deployment job → `environment: uinsurepolicies-${{ parameters.env }}`, `strategy: runOnce`):
1. `AzureCLI@2`: `az deployment sub what-if` then `az deployment sub create --location uksouth --parameters env/${env}.bicepparam`;
   read outputs into pipeline variables (`##vso[task.setvariable ...]`)
2. `AzureCLI@2`: install go-sqlcmd if absent · run `grant-app-identity.sql` · run `migrations.sql` (Entra auth as deploy identity)
3. `AzureWebApp@1`: `app.zip` → `$(appName)`
4. `bash`: curl `/health/ready` with retries (fail the stage if not 200)

**Stage `Prod_Plan`** (plain job, prod service connection, no environment): `az deployment sub what-if` + echo the path of `migrations.sql`
in the summary. It makes **no changes**.

**Variables:** none secret. Per-env values come from `.bicepparam` and template parameters only.

**Acceptance (F5 definition of done for deployment):** `infrastructure/validate.ps1` passes the YAML schema check on both YAML files,
and the README states plainly: *"Never connected to Azure DevOps; templates not expanded by a real compiler; approvals are
configured in the ADO UI and documented here."*

---

## Open questions

1. D2 is the real interview conversation: **script (review-gated) vs bundle (Learn's automation default)**. Which does David want to defend?
2. Is go-sqlcmd preinstalled on `ubuntu-latest`, and does `ActiveDirectoryDefault` pick up the `AzureCLI@2` login? **(?)** Unverified. The
   contract includes an install step either way.
3. Should the YAML schema check join the local git hook, or stay a manual `validate.ps1`? (`guardrails.spec` / `testing.spec` own hooks.)
4. requirements.md §3 says "validated locally". **Suggest amending it** to "Bicep linted and built locally; pipeline YAML schema-checked
   locally. Neither what-if nor a pipeline compile is possible without Azure/ADO, and neither was run", so the claim matches the receipt.
5. requirements.md §3 "No CI" vs a pipeline with a Build+test stage: worth one sentence in the README so a reviewer doesn't read
   it as a contradiction (the pipeline is the Uinsure-shaped design; the hooks are what actually gate this repo).
