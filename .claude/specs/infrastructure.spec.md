# infrastructure.spec.md

Status: DECIDED — contract final (2026-10-17)
Researched: 2026-10-17 · Feature: F5 (last, time-boxed, first to cut)

**Scope guard.** This is evidence, not a deployment. The Bicep is written for dev, test and prod, validated
locally with `bicep lint` / `bicep build` / `bicep build-params`, and never deployed. No `az login`, no
subscription, no what-if run. Every line must be explainable in the interview, so this spec prefers fewer,
hand-written lines over a larger generated surface.

---

## Dependencies

| Depends on | What this spec relies on |
|---|---|
| `architecture.spec` | The app is host-agnostic. It reads `Database:Provider` and `ConnectionStrings:Policies` from config or env, exposes `/health/live` (no DB) and `/health/ready` (DB), and reaches Azure SQL with `Authentication=Active Directory Default`. Migrations are **SQL Server only**. If those keys or paths change, the app settings in `modules/app.bicep` change with them |
| `deployment.spec` | Consumes this spec's outputs (`appName`, `sqlServerFqdn`, `sqlDatabaseName`, `resourceGroupName`) and runs what-if, deploy, migrations, app deploy and smoke test |
| `guardrails.spec` | No secrets in the repo. This design has **no secrets at all**, so there's nothing to protect in `.bicepparam` files |
| requirements.md §3 | "Evidence, not deployed". Validation is local only |

---

## Research

### R1. Hosting: App Service (Linux) vs Container Apps

- **App Service Linux** runs a code deployment (zip) on a built-in .NET stack, selected by `siteConfig.linuxFxVersion`.
  For .NET 8 the docs use `DOTNETCORE|8.0`, so .NET 10 is `DOTNETCORE|10.0` by the same pattern.
  Source: [Configure ASP.NET Core apps on App Service](https://learn.microsoft.com/en-us/azure/app-service/configure-language-dotnetcore?pivots=platform-linux).
- **.NET 10 on App Service: (?) partly verified.** .NET 10 went GA on 11 Nov 2025. A Microsoft Q&A thread from
  Nov 2025 says App Service listed ".NET 10 (LTS) (Preview)" while regional rollout finished
  ([Q&A](https://learn.microsoft.com/en-us/answers/questions/5619307/how-can-i-create-an-azure-web-app-using-net-10-(lt)).
  Azure Functions lists .NET 10 as GA. I found **no Learn page that says App Service Linux .NET 10 is GA as of Oct 2026**.
  The authoritative check is `az webapp list-runtimes --os linux`, which needs a signed-in CLI, so it's out of bounds here.
  It's very likely GA eleven months on, but that's inference, not a receipt. **Say so in the interview.**
- Context worth knowing: .NET 8 (LTS) and .NET 9 (STS) both reach end of support on **10 Nov 2026**, three weeks after
  this submission ([Functions supported languages](https://learn.microsoft.com/en-us/azure/azure-functions/supported-languages)).
  That's a live reason for the role's ".NET 8 upgrading to 10" line.
- **Container Apps** needs, on top of the app: a container registry (`cr`), a Container Apps environment (`cae`),
  an image build and push in the pipeline, and AcrPull for the identity. `dotnet publish /t:PublishContainer`
  removes the Dockerfile but not the registry. It's roughly 2 extra resources and 1 extra pipeline job.
- **Health:** App Service has a built-in health check (`siteConfig.healthCheckPath`) that pulls unhealthy
  instances out of rotation. Pointing it at `/health/ready` wires up the architecture spec's endpoint with one line.

### R2. Azure SQL sizing

- **Serverless General Purpose** (`GP_S_Gen5_*`) auto-scales vCores and can auto-pause (`autoPauseDelay`, `minCapacity`).
  It's the natural tier for dev and test.
- **Free offer:** up to 10 GP serverless DBs per subscription, each with 100,000 vCore-seconds, 32 GB of data and 32 GB of backup
  per month, set to auto-pause until the next month when the limit is used up. Limits: max 4 vCores, PITR 7 days, LRS backups, no
  failover groups, and **every free DB in a subscription is pinned to one region**
  ([free offer](https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer)). The Bicep properties are `useFreeLimit` and
  `freeLimitExhaustionBehavior`. **(?)** I recall those names but didn't check them on a Learn page. `bicep build` type-checks them
  against the API version, so a wrong name shows up as a warning.
- **Passwordless:** the server is set to **Entra-only authentication** (`administrators.azureADOnlyAuthentication: true`), with an
  Entra group or principal as admin. The app's managed identity connects with
  `Server=tcp:<server>.database.windows.net;Database=<db>;Authentication=Active Directory Default;` and needs **no password**
  ([MI → Azure SQL tutorial](https://learn.microsoft.com/en-us/azure/app-service/tutorial-connect-msi-sql-database)).
- **Friction: Bicep can't create the database user.** `CREATE USER [<app-identity>] FROM EXTERNAL PROVIDER` plus role grants is
  T-SQL, run by the Entra admin. ARM has no resource for it. So it goes in the **deployment** pipeline as a SQL step next to
  the migrations script (see `deployment.spec`). The tutorial grants `db_datareader` and `db_datawriter`, and also `db_ddladmin`
  because it migrates from the app. **We migrate from the pipeline, so the app identity drops `db_ddladmin`.** That's the
  least-privilege split the EF docs recommend
  ([applying migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying)).
- **Network:** the simplest credible version uses public network access with the "Allow Azure services" firewall rule
  (`0.0.0.0`–`0.0.0.0`). This lets both App Service and Microsoft-hosted pipeline agents reach SQL. Private endpoints + VNet
  integration are the production-grade answer, but they cost about 4 more resources. **Name it as the next step and don't build it.**

### R3. Secrets: Key Vault or none

With managed identity to SQL and a workload-identity pipeline, **there are no secrets**. The App Insights connection string is
an identifier, not a credential (ingestion can require Entra auth instead). A Key Vault with nothing in it is the kind of
declared-but-unwired machinery that requirements §4 warns about. Key Vault is added the day a real secret appears, such as a
third-party payment API key.

### R4. Observability

A workspace-based Application Insights (`appi`) linked to a Log Analytics workspace (`log`). The app gets
`APPLICATIONINSIGHTS_CONNECTION_STRING`. Wiring the SDK (`Azure.Monitor.OpenTelemetry.AspNetCore`) is an **app** change, so it
belongs to `architecture.spec`'s logging decision, not here. Retention: 30 days in every environment (the default, and cheapest).

### R5. Bicep structure: hand-written modules vs Azure Verified Modules (AVM)

- **AVM** is Microsoft's official, versioned module library, pulled from the public registry (`br/public:avm/res/...`).
  Current tags (MCR, checked 2026-10-17): `avm/res/web/site` **0.24.0**, `avm/res/web/serverfarm` **0.7.0**,
  `avm/res/sql/server` **0.22.1**. All of them are still **0.x**, so minor bumps can break.
  Sources: `https://mcr.microsoft.com/v2/bicep/avm/res/web/site/tags/list` (and `/serverfarm/`, `/sql/server/`).
- AVM cost: `bicep build` must **restore the modules from MCR over the network** (it's not Azure auth, but it's a network
  dependency), and each module exposes 50+ parameters you didn't write and may not be able to explain.
- **Hand-written** `Microsoft.Web/sites`, `serverfarms`, `Sql/servers`, `databases`, `Insights/components`,
  `OperationalInsights/workspaces`: about 150–200 lines in total, every one of them explainable, and builds offline.
- **Scope:** `targetScope = 'subscription'` in `main.bicep` creates the resource group, then calls modules scoped to it.
  One deployment and one what-if covers everything, including the RG.

### R6. Parameters per environment

`.bicepparam` files (`using './main.bicep'`) are type-checked against the template, and `bicep build-params` validates
them locally. The Azure CLI task takes only the `.bicepparam`. Note that "you can't override parameters" with `az deployment ... --parameters x.bicepparam`
([Bicep + Azure Pipelines](https://learn.microsoft.com/en-us/azure/azure-resource-manager/bicep/add-template-to-azure-pipelines)).
So everything that varies goes in the param file, which is a good thing for evidence.

### R7. Naming and tags

- CAF abbreviations ([resource abbreviations](https://learn.microsoft.com/en-us/azure/cloud-adoption-framework/ready/azure-best-practices/resource-abbreviations)):
  `rg`, `asp` (App Service plan), `app` (web app), `sql` (SQL server), `sqldb`, `appi`, `log`, `kv`, `id` (managed identity),
  `ca` / `cae` / `cr` (Container Apps path).
- Pattern: `<abbr>-<workload>-<env>-<region>`, so for example `app-uinsurepolicies-dev-uks`. Globally unique names (`app`, `sql`) get a
  `uniqueString(subscription().id)` suffix trimmed to 4–6 chars, so two reviewers' deployments wouldn't collide.
- Tags on every resource from one `tags` object in `main.bicep`: `workload`, `environment`, `owner`, `managedBy: bicep`,
  `repo`. Applied with `union()` so modules can't drift.

### R8. Local validation tooling on this machine (checked 2026-10-17)

| Command | Result |
|---|---|
| `az --version` | **not installed** (`command not found`; not on PATH, not in `Program Files\Microsoft SDKs\Azure\CLI2`, `winget list` finds no `Microsoft.AzureCLI`) |
| `az bicep version` | **not available** (no `az`) |
| `bicep --version` | **not installed** (no `Microsoft.Bicep` in winget, no `%USERPROFILE%\.azure\bin\bicep.exe`) |
| also present | winget v1.29.380, Python 3.13.2, VS Code 1.138, Visual Studio 18 (2026) + 2022, .NET SDK 10.0.402 |

Install (from [Install Bicep tools](https://learn.microsoft.com/en-us/azure/azure-resource-manager/bicep/install)):
- **Minimum for this spec:** `winget install -e --id Microsoft.Bicep` (the standalone Bicep CLI, on the user PATH, no admin).
  That's enough for `bicep lint`, `bicep build`, `bicep build-params` and `bicep format`. **None of them need Azure sign-in.**
- **Optional:** `winget install -e --id Microsoft.AzureCLI`. It bundles its own Bicep (`az bicep install` / `az bicep version`) and is
  only needed for what-if or deploy, which we're **not** doing. Install it only if you want `az bicep` parity with the pipeline.
- **Editor:** the Bicep extension for VS 2026 or VS Code shows linter diagnostics inline.
- `bicepconfig.json` sets the linter rules. Bump key rules (`no-hardcoded-env-urls`, `secure-secrets-in-params`,
  `use-recent-api-versions`, `no-unused-params`) to `error` so `bicep lint` fails loudly in the git hook.
- **What-if is not possible locally.** It needs a signed-in principal and a real subscription. It's written into the pipeline
  as evidence only.

---

## Options

| # | Decision | A (recommended) | B | C |
|---|---|---|---|---|
| I1 | Host | **App Service Linux, code (zip) deploy, `DOTNETCORE|10.0`** | Container Apps + ACR (`PublishContainer`) | App Service for containers |
| I2 | Bicep source | **Hand-written modules** (≈4 files) | AVM `br/public:` modules (pinned versions above) | Single flat `main.bicep` |
| I3 | Secrets | **None: MI + Entra-only SQL, no Key Vault** | Key Vault + Key Vault references (empty today) | SQL auth password in KV |
| I4 | Identity | **System-assigned MI on the web app** | User-assigned MI (`id-…`) created first, granted before app exists | — |
| I5 | SQL dev/test | **Serverless `GP_S_Gen5_1`, auto-pause 60 min, min 0.5 vCore** | Free offer (`useFreeLimit`); 10/sub, single region | Basic DTU |
| I6 | SQL network | **Public + Allow-Azure-services rule**, next step named | Private endpoint + VNet integration | — |
| I7 | Scope | **Subscription scope, creates RG** | RG scope, pipeline creates RG with `az group create` | — |
| I8 | Validation | **Standalone Bicep CLI: lint + build + build-params, in a git hook or script** | + Azure CLI installed | + PSRule for Azure (rules engine) |

Why the I1 A over B: B adds a registry, an environment and an image job, for no benefit to a single stateless API. Uinsure lists
App Service-friendly "push-button deployments". Container Apps is the answer if they say they're moving to containers.
Why I4 A: system-assigned is fewest lines, and its lifecycle is tied to the app. **Trade-off to name:** the `CREATE USER` step must
run *after* the app exists (it does, because the SQL step runs after the Bicep deploy). User-assigned is the pick when several apps share
an identity or when slots are used.

**Sizing per environment (recommendation):**

| | dev | test | prod |
|---|---|---|---|
| App Service plan | B1 ×1 | B1 ×1 | **P0v3 ×1** (slots + better CPU; P1v3 ×3 if zone redundancy wanted) |
| `alwaysOn` | false | false | true |
| SQL | GP_S_Gen5_1, auto-pause 60 | GP_S_Gen5_1, auto-pause 60 | GP_S_Gen5_2, **auto-pause off** (`-1`), min 1 vCore |
| SQL backup redundancy | Local | Local | Zone or Geo |
| Log retention | 30 d | 30 d | 30 d (90 d if audit policy demands) |

**(?)** I didn't check SKU pricing or regional P0v3 availability. Region: `uksouth` (UK insurer), and that's an assumption.

---

## Recommendation

I1-A, I2-A, I3-A, I4-A, I5-A, I6-A, I7-A, I8-A. Total: **6 resources per environment** (RG, plan, web app, SQL server + DB,
Log Analytics, App Insights), 4 modules, 3 param files, about 200 lines. Install only the standalone Bicep CLI.

**If time runs short, cut in this order:** prod-specific sizing differences (make all three the same) → the App Insights
module → the tags union. Keep: the 3 param files, MI + Entra-only SQL, and a local `bicep lint` that passes, because those are the
points a Uinsure reviewer would probe.

---

## Decision — DECIDED 2026-10-17 (David)

- [x] **I1 → A: App Service Linux**, deployed as code with no Dockerfile.
- [x] **I2–I8, region, naming and sizing:** accepted as recommended, in a block.
  - Hand-written modules, with no AVM.
  - No Key Vault and no secrets.
  - System-assigned managed identity.
  - Serverless SQL with auto-pause for dev and test.
  - A public endpoint plus the Azure-services rule. Private networking is named in the README as the next step.
  - Subscription scope, creating the resource group.
  - `uksouth`, with names in the form `<abbr>-uinsurepolicies-<env>-uks`.
  - David installs the Bicep CLI via winget for local lint. `az` is never run.

---

## Contract

Binding for F5. Builder, tester and reviewer get this section through the `architecture` skill, never the spec itself.

### File tree

```text
infrastructure/
  bicep/
    bicepconfig.json            linter rules; no-hardcoded-env-urls, secure-secrets-in-params,
                                use-recent-api-versions, no-unused-params → error
    main.bicep                  targetScope = 'subscription'; creates the RG; one tags object; calls modules; outputs
    modules/
      monitoring.bicep          log-… (Log Analytics, 30 d) + appi-… (workspace-based)
      sql.bicep                 sql-… (Entra-only, admin = Entra group) + sqldb-… + AllowAzureServices rule
      app.bicep                 asp-… (Linux) + app-… (system-assigned MI, app settings below)
    env/
      dev.bicepparam            using '../main.bicep'
      test.bicepparam
      prod.bicepparam
  sql/
    grant-app-identity.sql      → deployment.spec (runs after the Bicep deploy)
  pipelines/                    → deployment.spec
  validate.ps1                  local validation (below); non-zero exit on any error
```

### `main.bicep`

- `targetScope = 'subscription'`. It creates `rg-uinsurepolicies-<env>-uks`, then calls the three modules with `scope: rg`.
  One deployment and one what-if cover everything.
- **Parameters:** `environmentName` (`@allowed(['dev','test','prod'])`), `location` (default `uksouth`), `workload`
  (default `uinsurepolicies`), `owner`, `sqlAdminGroupObjectId`, `sqlAdminGroupName`, `appServiceSku`, `appServiceCapacity`,
  `alwaysOn`, `sqlSkuName`, `sqlAutoPauseDelay`, `sqlMinCapacity`, `sqlBackupRedundancy`.
  **No `@secure()` parameter exists. That absence is the point.**
- **Naming:** `<abbr>-${workload}-${environmentName}-uks`, built once in `main.bicep` and passed down. Modules never build names.
- **Tags:** one object (`workload`, `environment`, `owner`, `managedBy: 'bicep'`, `repo`), applied to every resource with `union()`.
- **Outputs (consumed by the pipeline):** `resourceGroupName`, `appName`, `appHostName`, `sqlServerFqdn`, `sqlDatabaseName`,
  `appPrincipalName` (equal to `appName` for a system-assigned MI; used in `CREATE USER`).
- **`.bicepparam` files** hold every per-environment value, because the CLI can't override parameters (R6). The Entra group object
  ID in them is an identifier, not a secret. Until one-time setup, it is the all-zero GUID, with a comment saying so.

### Resources per environment (6, region `uksouth`)

| Resource | Name | dev | test | prod |
|---|---|---|---|---|
| Resource group | `rg-uinsurepolicies-<env>-uks` | ✓ | ✓ | ✓ |
| App Service plan (Linux, `reserved: true`) | `asp-uinsurepolicies-<env>-uks` | B1 ×1 | B1 ×1 | P0v3 ×1 |
| Web app | `app-uinsurepolicies-<env>-uks` | `alwaysOn: false` | `alwaysOn: false` | `alwaysOn: true` |
| SQL server | `sql-uinsurepolicies-<env>-uks` | Entra-only | Entra-only | Entra-only |
| SQL database | `sqldb-uinsurepolicies-<env>-uks` | `GP_S_Gen5_1`, auto-pause 60, min 0.5 | `GP_S_Gen5_1`, auto-pause 60, min 0.5 | `GP_S_Gen5_2`, auto-pause `-1`, min 1 |
| SQL backup redundancy | (property) | Local | Local | Geo |
| Log Analytics | `log-uinsurepolicies-<env>-uks` | 30 d | 30 d | 30 d |
| Application Insights | `appi-uinsurepolicies-<env>-uks` | workspace-based | workspace-based | workspace-based |

The 6 resources are the RG, the plan, the web app, the SQL server with its database, Log Analytics and App Insights.
`app` and `sql` names must be globally unique. `uinsurepolicies` is distinctive enough that no `uniqueString()` suffix is added.
If a name collides on first deploy, the fix is a one-line `workload` change.

**Web app `siteConfig`:** `linuxFxVersion: 'DOTNETCORE|10.0'`, `healthCheckPath: '/health/ready'`, `httpsOnly: true`,
`minTlsVersion: '1.2'`, `ftpsState: 'Disabled'`, `identity.type: 'SystemAssigned'`.

### Managed identity → Azure SQL (Entra-only)

1. `sql.bicep` sets `administrators` with `azureADOnlyAuthentication: true`, `principalType: 'Group'`, `login: sqlAdminGroupName`,
   `sid: sqlAdminGroupObjectId`. There is no SQL login and no password anywhere.
2. The admin is an Entra group per environment that holds the pipeline's deploy identity plus one human break-glass account.
3. The firewall rule `AllowAllWindowsAzureIps` (`0.0.0.0`–`0.0.0.0`) lets App Service and hosted agents reach SQL.
4. Bicep can't create the DB user. `infrastructure/sql/grant-app-identity.sql` (run by the pipeline as the admin group, after the deploy)
   does an idempotent `CREATE USER [<appName>] FROM EXTERNAL PROVIDER` plus `db_datareader` and `db_datawriter`.
   **No `db_ddladmin`**, because migrations run from the pipeline, not the app.

### App settings (set by `app.bicep`) and what the API reads

| App setting (env var) | Config key the API reads | Value |
|---|---|---|
| `Database__Provider` | `Database:Provider` | `SqlServer` |
| `ConnectionStrings__Policies` | `ConnectionStrings:Policies` | `Server=tcp:${sqlServerFqdn},1433;Database=${sqlDatabaseName};Authentication=Active Directory Default;Encrypt=True;` |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | same name | `appi.properties.ConnectionString` |
| `ASPNETCORE_ENVIRONMENT` | host environment | `Dev` / `Test` / `Production` |

- The App Insights connection string is passed as a plain app setting. It's an identifier, not a credential (R3).
- Deployed dev is **`Dev`, not `Development`**. `Development` turns on Scalar/OpenAPI and developer error detail, and those
  must never be reachable on a public endpoint.
- **App side, owned by the plan's logging task, not by F5:** the API registers `Azure.Monitor.OpenTelemetry.AspNetCore` only
  when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set, and otherwise logs to the console. So the app runs the same with or
  without F5.

### Local validation (no `az`, ever)

`infrastructure/validate.ps1` runs, in order, and stops at the first failure:

1. `bicep lint infrastructure/bicep/main.bicep`
2. `bicep build infrastructure/bicep/main.bicep --stdout > $null` (type-checks the modules against their API versions)
3. `bicep build-params infrastructure/bicep/env/<env>.bicepparam --stdout > $null`, once each for dev, test and prod
4. the YAML schema check (→ deployment.spec)

The only tool needed is the standalone Bicep CLI (`winget install -e --id Microsoft.Bicep`). Nothing signs in to Azure.
`az`, what-if and deploy are never run from this repo or this machine.

**Acceptance (F5 infra done):** `validate.ps1` exits 0, and the README paragraphs below are present.

### README paragraphs (F6 checklist)

1. **Status:** "The Bicep is linted and built locally and never deployed. what-if was not run, because it needs a signed-in
   subscription."
2. **Shape:** subscription scope, 6 resources per environment, 3 modules, 3 `.bicepparam` files, plus the sizing table.
3. **No secrets:** a system-assigned MI connects to Entra-only SQL, so there are no passwords. **Key Vault was considered and rejected**:
   there is nothing to store. It arrives with the first real secret, such as a payment API key.
4. **Next step, networking:** private endpoint for SQL, VNet integration for the app, public network access off, and a
   self-hosted or Managed DevOps Pool agent in the VNet.
5. **.NET 10 runtime:** "`DOTNETCORE|10.0` is inferred from the documented pattern and a Nov 2025 Q&A. It wasn't confirmed with
   `az webapp list-runtimes`."

### Cut order

- **Plan level:** F7 is cut before F5. If F5 is cut, the README says so and keeps paragraph 3 as the design intent.
- **Within F5 infra:** prod sizing differences (make all three the same) → the monitoring module (and its app setting) → the tags `union()`.
- **Never cut:** the 3 param files, MI + Entra-only SQL, and a passing `validate.ps1`.

---

## Open questions

1. **(open)** Is .NET 10 GA on App Service Linux? It's unverifiable without `az`, which is barred. README paragraph 5 states the
   limit honestly.
2. ~~Free-offer property names~~: **resolved**, moot. I5-A (serverless, not the free offer) was decided.
3. ~~Who is the SQL Entra admin?~~: **resolved**. It's an Entra group per environment with the deploy identity plus a break-glass human
   (the recommendation, accepted in the block decision).
4. ~~Networking next-step paragraph?~~: **resolved**. Yes, README paragraph 4.
5. ~~Key Vault row~~: **resolved** for the README (paragraph 3). Resolved: requirements.md §4 row 8 now says Key Vault was rejected. Updating requirements.md §4 row 8 to "considered and rejected"
   is a follow-up outside this spec.
6. ~~Tooling~~: **resolved**. David installs `Microsoft.Bicep` via winget before F5. `az` is not installed, and never will be for this repo.
