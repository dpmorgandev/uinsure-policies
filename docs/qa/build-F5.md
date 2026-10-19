# QA script — build / F5 — IaC and pipeline (evidence, never deployed)

Rule IDs: none (F5 is an evidence node; the step rows carry `—` in the Rule column and name the acceptance item they prove)
Branch: feature/build/F5   Worktree: .claude/worktrees/build-F5
Commit under test: b92de26 (HEAD when this script was written and run. The last infrastructure change is 88ff4bb, and the A2.3 reviewer and security verdicts are PASS. This script's own commit changes only this file. Record the SHA you test in the sign-off)
API URL: none (nothing is served or deployed)   Environment: offline, from the worktree root

F5 adds `infrastructure/`: hand-written Bicep (`main.bicep` at subscription scope and four modules), one `.bicepparam`
per environment, and an Azure DevOps pipeline (`azure-pipelines.yml` plus one `stage-template.yml`) with an offline
checker (`check-pipeline.py` against `pipeline.schema.json`). F5 changes nothing in `src/` or `tests/`. It is
evidence only: the pipeline has never been connected to Azure DevOps, `az` is never run, and nothing is deployed. So
every step below is offline. It either compiles or checks a file, or it reads a file at a line.

## Acceptance items (from `docs/plans/active/build.dag.yaml`, node F5)

| Tag | Acceptance item |
|---|---|
| AC1 | Hand-written Bicep modules at subscription scope; `bicep build` and `bicep build-params` succeed for dev, test and prod |
| AC2 | Pipeline: build/test → dev → test → prod from one stage template; AzureCLI@2 with what-if; prod approval; idempotent migrations; curl `/health/ready` smoke; schema-checked offline |
| AC3 | `APPLICATIONINSIGHTS_CONNECTION_STRING` supplied as an app setting; managed identity to Entra-only SQL; no secrets |
| AC4 | `az` never run; nothing deployed |

## Preconditions

- [x] Bicep CLI on `PATH` (`bicep --version`). Run with: Bicep CLI version 0.48.1.
- [x] Python with PyYAML and jsonschema. Run with: Python 3.13.2, PyYAML 6.0.2, jsonschema 4.23.0.
- [x] `dotnet-ef` on `PATH`, for step 7 only. Run with: 9.0.9 (see note 3).
- [x] No API is running from this worktree (a running API locks the build output that step 13 rebuilds).
- [x] Nothing else is needed. No Azure sign-in, no Azure DevOps, no network access.

Commands are for PowerShell, run from the worktree root. Inside the tables, `\|` is a Markdown escape: type a plain
`|`. The longer commands with a pipe are written out here, and the table rows cite them by name:

```powershell
# C2: shape of the compiled template
bicep build infrastructure/main.bicep --stdout | python -I -c "import json,sys; d=json.load(sys.stdin); print(next(v for k,v in d.items() if k.endswith('schema'))); print({k: v['type'] for k, v in d['parameters'].items()}); r=d['resources']; r=list(r.values()) if isinstance(r, dict) else r; print(len(r), [x['type'] for x in r]); print(sorted(d['outputs']))"

# C3: the values one parameter file compiles to (replace dev with test or prod)
bicep build-params infrastructure/params/dev.bicepparam --stdout | python -I -c "import json,sys; p=json.loads(json.load(sys.stdin)['parametersJson'])['parameters']; print({k: v['value'] for k, v in p.items()})"

# C7: the Build stage's idempotent script, to the console (no file written); count the guarded blocks
dotnet ef migrations script --idempotent --project src/Uinsure.Policies.Persistence --startup-project src/Uinsure.Policies.Api | Select-String -SimpleMatch "WHERE [MigrationId] = N'20261019014058_InitialCreate'" | Measure-Object

# C8: the grant step's SID transform (stage-template.yml lines 115-116), on a fixed sample client id
#   in Git Bash:
g=$(echo 01234567-89ab-cdef-0123-456789abcdef | tr -d '-' | tr 'a-f' 'A-F'); echo "0x${g:6:2}${g:4:2}${g:2:2}${g:0:2}${g:10:2}${g:8:2}${g:14:2}${g:12:2}${g:16:16}"
#   the reference, SQL Server's uniqueidentifier byte order (little-endian first three groups):
python -I -c "import uuid; print('0x' + uuid.UUID('01234567-89ab-cdef-0123-456789abcdef').bytes_le.hex().upper())"
```

## Steps

### A. Offline commands

| # | Rule | Check | Steps / input | Expected | Actual | Pass/Fail |
|---|---|---|---|---|---|---|
| 1 | — | AC1 · `main.bicep` compiles | `bicep build infrastructure/main.bicep --stdout > $null; $LASTEXITCODE` | `0`. No error or warning line on the console (stdout goes to `$null`, so any line shown is a diagnostic) | `0`, no diagnostic line (Bicep 0.48.1) | PASS |
| 2 | — | AC1 · subscription scope, compiled | Command C2 | Schema `…/2018-05-01/subscriptionDeploymentTemplate.json#`. Nine parameters, every one `string`, none `securestring` (`environmentName`, `location`, `workload`, `appServiceSku`, `sqlSkuName`, `sqlSkuTier`, `sqlAdminGroupName`, `sqlAdminGroupObjectId`, `agentAddressPrefix`). Five top-level resources: one `Microsoft.Resources/resourceGroups` and four `Microsoft.Resources/deployments` (the modules). Six outputs: `appHostName`, `appName`, `identityClientId`, `resourceGroupName`, `sqlDatabaseName`, `sqlServerFqdn` | Exactly as expected: the subscription schema, nine `string` parameters, `5 ['Microsoft.Resources/resourceGroups', 'Microsoft.Resources/deployments' ×4]`, the six outputs | PASS |
| 3 | — | AC1 · dev parameters compile | `bicep build-params infrastructure/params/dev.bicepparam --stdout > $null; $LASTEXITCODE`, then command C3 | `0`, no diagnostic. Values: `environmentName` dev, `appServiceSku` B1, `sqlSkuName` Basic, `sqlSkuTier` Basic, `sqlAdminGroupName` sg-policies-sql-admins-dev, `sqlAdminGroupObjectId` 00000000-0000-0000-0000-000000000000 (placeholder), `agentAddressPrefix` 10.30.0.0/24 (placeholder) | `0`, no diagnostic; the seven values exactly as expected | PASS |
| 4 | — | AC1 · test parameters compile | As step 3 with `test.bicepparam` (C3 with `test`) | `0`, no diagnostic. As step 3 but `environmentName` test and `sqlAdminGroupName` sg-policies-sql-admins-test | `0`, no diagnostic; values as expected | PASS |
| 5 | — | AC1 · prod parameters compile | As step 3 with `prod.bicepparam` (C3 with `prod`) | `0`, no diagnostic. `environmentName` prod, `appServiceSku` P1v3, `sqlSkuName` GP_S_Gen5_1, `sqlSkuTier` GeneralPurpose, `sqlAdminGroupName` sg-policies-sql-admins-prod, the same two placeholders | `0`, no diagnostic; values as expected | PASS |
| 6 | — | AC2 · pipeline schema-checked offline | `python -I infrastructure/pipelines/check-pipeline.py; $LASTEXITCODE` | `pipeline check passed`, then `0`. Both YAML files match `pipeline.schema.json`, and every rule the schema can't express holds (template order, what-if before create, AzureCLI@2 inputs, environment, migrations, smoke test, App Insights, SID grant, private SQL, NSGs; see the docstring and lines 37-212) | `pipeline check passed`, exit `0` | PASS |
| 7 | — | AC2 · the migration script is idempotent | Command C7 | It runs (exit 0) and prints a script whose every block is guarded by `IF NOT EXISTS(SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261019014058_InitialCreate')`: `Count : 19`. The script also creates `__EFMigrationsHistory` only `IF OBJECT_ID(...) IS NULL`. Re-running it on a migrated database therefore changes nothing | Exit `0`; 19 guarded blocks; line 2 is `IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL` | PASS |
| 8 | — | AC3 · the app's database user is created by SID | Command C8: both lines | Both print `0x67452301AB89EFCD0123456789ABCDEF`: the template's byte swap equals SQL Server's `uniqueidentifier` byte order for the identity's client id | Both printed `0x67452301AB89EFCD0123456789ABCDEF` | PASS |
| 9 | — | AC3 · no secrets in `infrastructure/` | `git grep -niE "password\|secret\|accountkey\|sharedaccess\|instrumentationkey\|@secure\|keyvault" -- infrastructure ":(exclude)infrastructure/pipelines/check-pipeline.py"` | Five lines, every one a comment that states an absence: `appservice.bicep:49`, `sql.bicep:11`, and line 10 of each `.bicepparam` ("Not a secret"). No value, no `@secure()` parameter, no Key Vault | Exactly those five comment lines | PASS |
| 10 | — | AC4 · `az` appears only in the never-run template | `git grep -nE "(^\|[^[:alnum:]_-])az (deployment\|webapp\|login\|group\|sql\|account\|resource)" -- src tests infrastructure .githooks` | Three lines, all in `infrastructure/pipelines/stage-template.yml` inside AzureCLI@2 inline scripts: 48 (`az deployment sub what-if`), 63 (`az deployment sub create`), 128 (`az webapp deploy`). Nothing in `src/`, `tests/` or the git hooks calls `az` | Exactly those three lines | PASS |
| 11 | — | AC4 · `az` is not even installed here | `Get-Command az -ErrorAction SilentlyContinue` | Prints nothing | Run as `which az` in Git Bash: `no az in (…)`. The Azure CLI is not on this machine's `PATH` | PASS |
| 12 | — | AC4 · F5 touches no app code | `git merge-base HEAD main`, then `git diff --stat <that sha> HEAD -- src tests` | The merge base is `67e3a4c`. The diff prints nothing | Merge base `67e3a4c1bc788edc1bbcfee5a00cafb47dea8434`; the diff printed nothing. The branch's whole diff is `infrastructure/` (12 files) plus plan, context and F4 trace files | PASS |
| 13 | — | regression · the suite is unchanged | `dotnet test --solution Uinsure.Policies.slnx` | 637 passed, 0 failed, 0 skipped (F4 signed off 637; F5 changed no test) | total 637, failed 0, succeeded 637, skipped 0 (all four test projects passed) | PASS |

### B. Read-throughs (open the file at the line)

Each row says where the acceptance item lives and what to look for. Actual records what was at those lines at b92de26.

| # | Rule | Check | Steps / input | Expected | Actual | Pass/Fail |
|---|---|---|---|---|---|---|
| 14 | — | AC1 · subscription scope, hand-written modules | `infrastructure/main.bicep` 1, 39-43, 45-95 | Line 1 `targetScope = 'subscription'`. 39-43 create `rg-policies-<env>`. The four modules (`monitoring`, `network`, `sql`, `app`) are local files in `infrastructure/modules/`, each with `scope: rg` (46, 56, 67, 83). No registry or AVM module reference | As expected. All four `module` lines point at `'modules/<name>.bicep'` | PASS |
| 15 | — | AC2 · build and test before any deployment | `infrastructure/pipelines/azure-pipelines.yml` 17-58 | Stage `Build`: `dotnet build … -warnaserror` (30), `dotnet test --solution` (33), the idempotent script (36-45), publish and zip the app with `infrastructure/` (47-54), one `drop` artifact (56-58). Trigger is `main` only (4-6), `pr: none` (8) | As expected | PASS |
| 16 | — | AC2 · one stage template for dev, test and prod | `azure-pipelines.yml` 60-79; `stage-template.yml` 14-17 | Three `- template: stage-template.yml` uses with `environmentName` dev, test, prod and `dependsOn` Build, Deploy_dev, Deploy_test. The template defines one stage, `Deploy_${{ parameters.environmentName }}`. No environment has its own deployment steps | As expected. The three uses differ only in `environmentName`, the two service-connection names and `dependsOn` | PASS |
| 17 | — | AC2 · AzureCLI@2, what-if before create | `stage-template.yml` 41-53, 55-75 | Step 1 is AzureCLI@2 `What-if`: `az deployment sub what-if` with the drop's `main.bicep` and `<env>.bicepparam`. Step 2 is AzureCLI@2 `Deploy infrastructure`: `az deployment sub create` with the same name, location, template and parameters, then the six outputs read into variables (70-75) | As expected. Both pass `--parameters sqlAdminGroupObjectId=$(sqlAdminGroupObjectId)` (53, 68). See note O1 on what the what-if does and does not gate | PASS |
| 18 | — | AC2 · prod approval on `policies-prod` | `stage-template.yml` 19, 28-29; `azure-pipelines.yml` 3 | The job is a `deployment` job (19) with `environment: policies-${{ parameters.environmentName }}` (29). The comment (28) and the header (3) say the approval check is on `policies-prod` only. The check itself is Azure DevOps configuration and cannot be in the repo (step 32) | As expected | PASS |
| 19 | — | AC2 · migrations applied from the idempotent script | `stage-template.yml` 93-103 | AzureCLI@2 `Apply migrations` runs `sqlcmd … --authentication-method ActiveDirectoryAzCli -b -i $(drop)/migrations.sql`, under the SQL service connection (97), before the app is deployed (121-132). `-b` fails the step on a SQL error | As expected. The script is the one step 7 generates (Build stage, `azure-pipelines.yml` 41-44); dotnet-ef pinned to 10.0.13 (39), equal to `Microsoft.EntityFrameworkCore.Design` in `Directory.Packages.props` line 12 | PASS |
| 20 | — | AC2 · `curl /health/ready` smoke test | `stage-template.yml` 134-138; `src/Uinsure.Policies.Api/Program.cs` 127; `appservice.bicep` 43 | The last step is `curl --fail --silent --show-error --retry 10 --retry-delay 15 --retry-all-errors https://$(appHostName)/health/ready`; `appHostName` comes from the `main.bicep` output (99) via 72. The app maps `/health/ready` to the `ready`-tagged checks. App Service probes the same path | As expected | PASS |
| 21 | — | AC3 · App Insights as an app setting | `appservice.bicep` 44-48; `monitoring.bicep` 15-27; `main.bicep` 90; `Program.cs` 74-77 | `APPLICATIONINSIGHTS_CONNECTION_STRING` is an `appSettings` entry (47) whose value is the Application Insights component's connection string, passed as a module output. It is not a pipeline variable and not a Key Vault reference. The app enables Azure Monitor only when the setting is non-empty | As expected. The component is workspace-based (`WorkspaceResourceId`, monitoring.bicep 22) | PASS |
| 22 | — | AC3 · user-assigned identity to Entra-only SQL, user by SID | `appservice.bicep` 10-15, 30-34, 49-56, 76; `sql.bicep` 11, 20-27; `stage-template.yml` 105-119 | A user-assigned identity is attached to the site. The `Policies` connection string is `Authentication=Active Directory Managed Identity;User Id=<clientId>` with no password; `Database__Provider` is `SqlServer` (46), and the name `Policies` is what `PersistenceServiceCollectionExtensions.cs` 20 reads. SQL has an Entra group admin with `azureADOnlyAuthentication: true` and no `administratorLogin`. The grant step creates the user `WITH SID = $sid, TYPE = E` (no `FROM EXTERNAL PROVIDER`), then adds `db_datareader` and `db_datawriter` only. The `CREATE USER` is guarded by `IF NOT EXISTS`, so a re-run is harmless | As expected. The clientId flows `appservice.bicep` 76 → `main.bicep` 102 → `stage-template.yml` 75 → 115. Step 8 checks the byte order | PASS |
| 23 | — | AC3 · private endpoint, no public SQL | `sql.bicep` 12, 19, 42-88; `appservice.bicep` 38; `network.bicep` 72-108 | `publicNetworkAccess: 'Disabled'`, no firewall rule. A private endpoint (`groupIds: ['sqlServer']`) in `snet-private-endpoints`, a `privatelink<sqlServerHostname>` private DNS zone linked to the VNet, and a zone group on the endpoint. The app joins `snet-app-integration` by `virtualNetworkSubnetId` | As expected. The zone name uses `environment().suffixes.sqlServerHostname` (61), not a hard-coded host | PASS |
| 24 | — | AC3 · NSGs (read by eye) | `network.bicep` 11-33, 35-70, 79-103 | `snet-app-integration` has an NSG that denies all inbound at priority 4000. `snet-private-endpoints` has an NSG with **one** Allow rule (priority 100, Inbound, Tcp, port 1433, sources exactly `[appIntegrationPrefix, agentAddressPrefix]`, destination the PE subnet) and a deny-all inbound at 4000. `privateEndpointNetworkPolicies: 'Enabled'` (98) makes the NSG apply to the endpoint. Read this by eye: the checker does not prove "only" (note O2) | As expected: one Allow rule with exactly the two sources, then deny-all. No other rule in either NSG | PASS |
| 25 | — | AC3 · no secrets, by design | `params/*.bicepparam` 8-12; `stage-template.yml` 30-33, 53, 68; `appservice.bicep` 61-72; `monitoring.bicep` 26 | No parameter is a secret: the admin group's object id is a placeholder overridden from the `policies-<env>` variable group at deploy time, and the agent range is committed as "not a secret". Basic-auth publishing (`scm`, `ftp`) is `allow: false`. The App Insights connection string is described as an ingestion endpoint, not a credential. Every SQL connection is Entra (steps 9, 22) | As expected | PASS |
| 26 | — | AC4 · stated as never deployed | `azure-pipelines.yml` 1 | `# Evidence only: never connected to Azure DevOps, never run.` | As expected. Steps 10 and 11 back it | PASS |

### C. Out-of-repo prerequisites a real deployment needs

None of these exists, and none can be checked from here. Each row checks only that the repo **states** the
prerequisite at the cited lines, so that a reader could stand the deployment up. PASS means "stated there".

| # | Rule | Check | Steps / input | Expected | Actual | Pass/Fail |
|---|---|---|---|---|---|---|
| 27 | — | AC2 · three self-hosted agent pools | `stage-template.yml` 21-27 | Pools `policies-dev-vnet`, `policies-test-vnet`, `policies-prod-vnet`, one per environment. Their agents have `az`, `jq`, go-sqlcmd (`sqlcmd`) and `curl`. Microsoft-hosted agents can't reach the private endpoint | Stated at 21-27 (pool name at 27, tools at 25) | PASS |
| 28 | — | AC3 · network reach: peering, DNS, agent range | `stage-template.yml` 21-24, 91; `params/*.bicepparam` 10-12; `network.bicep` 5-6, 78 | The agents' network is peered to (or is) `vnet-policies-<env>` (10.20.0.0/16) and resolves `privatelink.database.windows.net`. The agents' address range is inside `agentAddressPrefix`, and the `10.30.0.0/24` placeholder in each param file is replaced with the real, narrow range before the first deployment | Stated at those lines. Not stated: the private DNS zone is linked only to `vnet-policies-<env>` (`sql.bicep` 66-75), so the agents' own VNet needs its own zone link or DNS forwarding, made outside the repo. The peering can only be made after the first infrastructure deployment creates the VNet (note O4) | PASS |
| 29 | — | AC2 · ARM service connections | `azure-pipelines.yml` 63, 70, 77; `stage-template.yml` 40, 80-82 | `sc-policies-dev`, `sc-policies-test`, `sc-policies-prod`, with subscription-scope rights (the deployment creates the resource group); they deploy resources and the app zip only | Stated | PASS |
| 30 | — | AC3 · SQL service connections, least privilege | `azure-pipelines.yml` 64, 71, 78; `stage-template.yml` 77-82 | `sc-policies-<env>-sql`, separate from the ARM ones. Its principal is a database-scoped user with `db_owner` on `policies` and is not in the server admin group. The residual risk of the ARM connection is stated and accepted | Stated | PASS |
| 31 | — | AC3 · Entra admin groups and variable groups | `params/*.bicepparam` 7-9; `stage-template.yml` 30-33 | Entra groups `sg-policies-sql-admins-<env>`. Variable groups `policies-<env>`, each holding `sqlAdminGroupObjectId` (the group's object id) | Stated | PASS |
| 32 | — | AC2 · Azure DevOps environments and the prod approval | `stage-template.yml` 28-29; `azure-pipelines.yml` 3 | Environments `policies-dev`, `policies-test`, `policies-prod`; an approval check on `policies-prod` only | Stated | PASS |
| 33 | — | AC3 · the one-off SQL bootstrap per environment | `stage-template.yml` 83-90 | After the first infrastructure deployment, a member of the admin group, connected to the `policies` database, runs `CREATE USER [<SQL connection principal>] FROM EXTERNAL PROVIDER; ALTER ROLE db_owner ADD MEMBER [...]`. Until then the first run fails at `Apply migrations` | Stated, with both statements and the expected first-run failure | PASS |

## Exploratory notes / defects

### Execution run, 2026-10-19 (tester agent, t4, at b92de26bb5c9f0de91e256049a9e2f0daf967721)

**33 of 33 step rows PASS, 0 FAIL.** No defect raised.

1. **Shell.** The commands were run from Git Bash, not PowerShell: `> /dev/null; echo $?` for `> $null; $LASTEXITCODE`,
   `grep -c` for `Select-String … | Measure-Object` (step 7), `which az` for `Get-Command az` (step 11), and
   `git grep` exactly as written (steps 9, 10, 12). Steps 1, 3, 4 and 5 left stderr on the console, so a Bicep
   diagnostic would have shown; none did.
2. **No build output written.** `bicep build` and `build-params` ran with `--stdout`, and step 7's script went to
   the console, so no `main.json`, `.parameters.json` or `migrations.sql` was left in the worktree.
3. **dotnet-ef version.** The tool on this machine is 9.0.9. Step 7 prints "The Entity Framework tools version
   '9.0.9' is older than that of the runtime '10.0.13'" on stdout, then the script. The pipeline installs 10.0.13
   (`azure-pipelines.yml` 39). The script I counted came from the 9.0.9 tool over the 10.0.13 design-time assembly.
4. **Order.** Step 13 (the full suite) ran first, then steps 1-12, then the read-throughs against the files at
   b92de26.

Observations for David (not step rows, not raised as findings):

- **O1. The prod approval comes before the what-if, and the what-if gates nothing.** In Azure DevOps, an
  environment's checks are evaluated when the stage starts. So the prod approver approves before the prod what-if
  has printed anything. The what-if step only prints: create follows it in the same job with no pause and no
  `--exit-code`-style gate (`stage-template.yml` 41-75). The F5 acceptance asks for what-if before create and for
  a prod approval, and both hold. But if you meant "approve after reading the prod what-if", this layout doesn't
  do it. That would need a what-if-only stage before the prod deployment stage. I base this on my understanding
  of Azure DevOps checks. I can't verify it offline.
- **O2. The checker's NSG assertion is weaker than its comment.** The A2.3 reviewer's carried P2 (`A2.3/R1`):
  `check-pipeline.py` 171 and 200-212 pass a second Allow rule, an extra source in the allow, or an Allow on port
  `*`. The infrastructure at b92de26 is right (step 24 reads it by eye), but step 6 alone does not prove "1433 only
  from the app subnet and the agent range".
- **O3. Two placeholders differ in kind.** `sqlAdminGroupObjectId` (all zeros) is overridden at deploy time from the
  variable group. `agentAddressPrefix` (`10.30.0.0/24`, the same in all three files) is **not** overridden: it must
  be edited in each `.bicepparam` before the first real deployment, as its comment says.
- **O4. The three environments share one address space.** Every `vnet-policies-<env>` is `10.20.0.0/16`
  (`network.bicep` 78). One agent network therefore can't be peered to more than one of them. The one-pool-per-
  environment design fits that, but a shared hub would not.
- **O5. The runtime string is unconfirmed.** `linuxFxVersion: 'DOTNETCORE|10.0'` (`appservice.bicep` 40) is
  inferred, not checked with `az webapp list-runtimes`. This is already tracked as A11 in `docs/readme-checklist.md`
  26.

## Sign-off

| | |
|---|---|
| Tester | |
| Date | |
| Commit signed off | |
| `dotnet test` result | |
| Offline checks (`bicep build`, `build-params` ×3, `check-pipeline.py`) | |
| Steps passed | |
| Defects raised (finding ids) | |
| Verdict | ☐ Reject (findings above)  ☐ Accept F5 |
| Signature | |
