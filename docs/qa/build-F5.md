# QA script — build / F5 — IaC and pipeline (evidence, never deployed)

Rule IDs: none (F5 is an evidence node; the step rows carry `—` in the Rule column and name the acceptance item they prove)
Branch: feature/build/F5   Worktree: .claude/worktrees/build-F5
Commit under test: a14af3f (HEAD when this script was re-run. It is the last infrastructure change, the inv-002 fix, and the A3 reviewer and security verdicts are PASS at it. The first run was at b92de26; see "Re-run" below. This script's own commit changes only this file. Record the SHA you test in the sign-off)
API URL: none (nothing is served or deployed)   Environment: offline, from the worktree root

F5 adds `infrastructure/`: hand-written Bicep (`main.bicep` at subscription scope and four modules), one `.bicepparam`
per environment, and an Azure DevOps pipeline (`azure-pipelines.yml` plus one `stage-template.yml`) with an offline
checker (`check-pipeline.py` against `pipeline.schema.json`). F5 changes nothing in `src/` or `tests/`. It is
evidence only: the pipeline has never been connected to Azure DevOps, `az` is never run, and nothing is deployed. So
every step below is offline. It either compiles or checks a file, or it reads a file at a line.

Since a14af3f the stage template emits **two stages per environment**: `WhatIf_<env>` (a regular job on a hosted
agent, no environment, so no approval) and then `Deploy_<env>` (`dependsOn: WhatIf_<env>`, no condition, the only
stage on the `policies-<env>` environment). So the stage chain is Build → WhatIf_dev → Deploy_dev → WhatIf_test →
Deploy_test → WhatIf_prod → Deploy_prod.

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

# C8: the grant step's SID transform (stage-template.yml lines 135-136), on a fixed sample client id
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
| 6 | — | AC2 · pipeline schema-checked offline | `python -I infrastructure/pipelines/check-pipeline.py; $LASTEXITCODE` | `pipeline check passed`, then `0`. Both YAML files match `pipeline.schema.json` (whose stage shape, lines 37-45, has no `condition` key), and every rule the schema can't express holds: template order, AzureCLI@2 inputs, the what-if in its own environment-free stage that the deploying stage depends on with no condition (90-110), environment, migrations, smoke test, App Insights, SID grant, private SQL, NSGs. See the docstring and lines 37-258 | `pipeline check passed`, exit `0` | PASS |
| 7 | — | AC2 · the migration script is idempotent | Command C7 | It runs (exit 0) and prints a script whose every block is guarded by `IF NOT EXISTS(SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261019014058_InitialCreate')`: `Count : 19`. The script also creates `__EFMigrationsHistory` only `IF OBJECT_ID(...) IS NULL`. Re-running it on a migrated database therefore changes nothing | Exit `0`; 19 guarded blocks; the first SQL line, after the tool's `Build started…`/`Build succeeded.` lines and its version warning (note 3), is `IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL` | PASS |
| 8 | — | AC3 · the app's database user is created by SID | Command C8: both lines | Both print `0x67452301AB89EFCD0123456789ABCDEF`: the template's byte swap equals SQL Server's `uniqueidentifier` byte order for the identity's client id | Both printed `0x67452301AB89EFCD0123456789ABCDEF` | PASS |
| 9 | — | AC3 · no secrets in `infrastructure/` | `git grep -niE "password\|secret\|accountkey\|sharedaccess\|instrumentationkey\|@secure\|keyvault" -- infrastructure ":(exclude)infrastructure/pipelines/check-pipeline.py"` | Six lines, every one a comment that states an absence or a requirement of absence: `appservice.bicep:49`, `sql.bicep:11`, line 10 of each `.bicepparam` ("Not a secret"), and `stage-template.yml:21` (the variable group "must stay secret-free"). No value, no `@secure()` parameter, no Key Vault | Exactly those six comment lines | PASS |
| 10 | — | AC4 · `az` appears only in the never-run template | `git grep -nE "(^\|[^[:alnum:]_-])az (deployment\|webapp\|login\|group\|sql\|account\|resource)" -- src tests infrastructure .githooks` | Five lines. Three are in `infrastructure/pipelines/stage-template.yml` inside AzureCLI@2 inline scripts: 42 (`az deployment sub what-if`), 83 (`az deployment sub create`), 148 (`az webapp deploy`). Two are string needles in the offline checker, which reads the YAML and calls nothing: `check-pipeline.py` 94 and 98. Nothing in `src/`, `tests/` or the git hooks calls `az` | Exactly those five lines | PASS |
| 11 | — | AC4 · `az` is not even installed here | `Get-Command az -ErrorAction SilentlyContinue` | Prints nothing | Run as `which az` in Git Bash: `no az in (…)`, exit 1. The Azure CLI is not on this machine's `PATH` | PASS |
| 12 | — | AC4 · F5 touches no app code | `git merge-base HEAD main`, then `git diff --stat <that sha> HEAD -- src tests` | The merge base is `67e3a4c`. The diff prints nothing | Merge base `67e3a4c1bc788edc1bbcfee5a00cafb47dea8434`; the diff printed nothing. The branch's whole diff is 18 files: `infrastructure/` (12) plus the F4 context and trace files, `build.dag.yaml`, `inv-002.plan.md` and this script | PASS |
| 13 | — | regression · the suite is unchanged | `dotnet test --solution Uinsure.Policies.slnx` | 637 passed, 0 failed, 0 skipped (F4 signed off 637; F5 changed no test) | total 637, failed 0, succeeded 637, skipped 0 (all four test projects passed) | PASS |

### B. Read-throughs (open the file at the line)

Each row says where the acceptance item lives and what to look for. Actual records what was at those lines at a14af3f.
The Bicep, parameter files, `src/` and `Directory.Packages.props` are byte-identical to b92de26 (`git diff b92de26
a14af3f` over them is empty), so their line numbers are unchanged. Every line in the two pipeline YAML files and in
the checker was re-cited.

| # | Rule | Check | Steps / input | Expected | Actual | Pass/Fail |
|---|---|---|---|---|---|---|
| 14 | — | AC1 · subscription scope, hand-written modules | `infrastructure/main.bicep` 1, 39-43, 45-95 | Line 1 `targetScope = 'subscription'`. 39-43 create `rg-policies-<env>`. The four modules (`monitoring`, `network`, `sql`, `app`) are local files in `infrastructure/modules/`, each with `scope: rg` (46, 56, 67, 83). No registry or AVM module reference | As expected. All four `module` lines point at `'modules/<name>.bicep'` | PASS |
| 15 | — | AC2 · build and test before any deployment | `infrastructure/pipelines/azure-pipelines.yml` 18-59 | Stage `Build`: `dotnet build … -warnaserror` (31), `dotnet test --solution` (34), the idempotent script (37-46), publish and zip the app with `infrastructure/` (48-55), one `drop` artifact (57-59). Trigger is `main` only (5-7), `pr: none` (9) | As expected | PASS |
| 16 | — | AC2 · one stage template for dev, test and prod | `azure-pipelines.yml` 61-80; `stage-template.yml` 14, 22-24, 49-51 | Three `- template: stage-template.yml` uses with `environmentName` dev, test, prod and `dependsOn` Build (66), Deploy_dev (73), Deploy_test (80). The template emits two stages per use: `WhatIf_${{ parameters.environmentName }}` (22), which takes the passed `dependsOn` (24), and `Deploy_${{ parameters.environmentName }}` (49), which depends on its own what-if stage (51). No environment has its own deployment steps | As expected. The three uses differ only in `environmentName`, the two service-connection names and `dependsOn`. The chain is Build → WhatIf_dev → Deploy_dev → WhatIf_test → Deploy_test → WhatIf_prod → Deploy_prod | PASS |
| 17 | — | AC2 · AzureCLI@2, what-if in its own stage before create | `stage-template.yml` 15-16, 22-47, 49-51, 75-95; `check-pipeline.py` 90-110; `pipeline.schema.json` 37-45 | Stage `WhatIf_<env>` (22-47) holds one regular `job: WhatIf` (26) on a hosted pool (`vmImage: ubuntu-latest`, 28-29) with **no `environment`**. It reads the variable group (30-31), downloads the `drop` artifact (33-34), and runs AzureCLI@2 `What-if` (35-47) with all four inputs (38-41): `az deployment sub what-if` with the drop's `main.bicep` and `<env>.bicepparam` (42-47). Stage `Deploy_<env>` has `dependsOn: WhatIf_<env>` (51) and no `condition` key (the schema at 37-45 forbids one at stage level), so it starts only after the what-if stage succeeds. Its first step, AzureCLI@2 `Deploy infrastructure` (75-95), runs `az deployment sub create` with the same name, location, template and parameter file (83-89), then reads the six outputs into variables (90-95). The checker asserts this stage layout by key, not by position (90-110) | As expected. Both pass `--parameters sqlAdminGroupObjectId=$(sqlAdminGroupObjectId)` (47, 88). The what-if reads `$(Pipeline.Workspace)/drop/…` and create reads `$(drop)/…`, which is defined as `$(Pipeline.Workspace)/drop` (68-69): the same files. The comment at 15-16 states why the stage is separate. See O1 (resolved) | PASS |
| 18 | — | AC2 · prod approval on `policies-prod`, after the what-if | `stage-template.yml` 17-18, 26-31, 53, 62-63; `azure-pipelines.yml` 3-4 | The what-if job (26-31) declares no environment. The deploying job is a `deployment` job (53) with `environment: policies-${{ parameters.environmentName }}` (63), the only environment in the template; the comment (62) says only `policies-prod` has an approval check. The header (3-4) says each environment is two stages and prod waits for the `policies-prod` approval after `WhatIf_prod`. The comment at 17-18 says the check must live on the environment only, never on the ARM service connection or the variable group that the what-if stage also uses, or that stage would pause first. The check itself is Azure DevOps configuration and cannot be in the repo (step 32) | As expected | PASS |
| 19 | — | AC2 · migrations applied from the idempotent script | `stage-template.yml` 113-123, 141-152 | AzureCLI@2 `Apply migrations` runs `sqlcmd … --authentication-method ActiveDirectoryAzCli -b -i $(drop)/migrations.sql` (121-123), under the SQL service connection (117), before the app is deployed (141-152). `-b` fails the step on a SQL error | As expected. The script is the one step 7 generates (Build stage, `azure-pipelines.yml` 42-45); dotnet-ef pinned to 10.0.13 (40), equal to `Microsoft.EntityFrameworkCore.Design` in `Directory.Packages.props` line 12 | PASS |
| 20 | — | AC2 · `curl /health/ready` smoke test | `stage-template.yml` 154-158; `src/Uinsure.Policies.Api/Program.cs` 127; `appservice.bicep` 43 | The last step is `curl --fail --silent --show-error --retry 10 --retry-delay 15 --retry-all-errors https://$(appHostName)/health/ready` (155-157); `appHostName` comes from the `main.bicep` output (99) via 92. The app maps `/health/ready` to the `ready`-tagged checks. App Service probes the same path | As expected | PASS |
| 21 | — | AC3 · App Insights as an app setting | `appservice.bicep` 44-48; `monitoring.bicep` 15-27; `main.bicep` 90; `Program.cs` 74-77 | `APPLICATIONINSIGHTS_CONNECTION_STRING` is an `appSettings` entry (47) whose value is the Application Insights component's connection string, passed as a module output. It is not a pipeline variable and not a Key Vault reference. The app enables Azure Monitor only when the setting is non-empty | As expected. The component is workspace-based (`WorkspaceResourceId`, monitoring.bicep 22) | PASS |
| 22 | — | AC3 · user-assigned identity to Entra-only SQL, user by SID | `appservice.bicep` 10-15, 30-34, 49-56, 76; `sql.bicep` 11, 20-27; `stage-template.yml` 125-139 | A user-assigned identity is attached to the site. The `Policies` connection string is `Authentication=Active Directory Managed Identity;User Id=<clientId>` with no password; `Database__Provider` is `SqlServer` (46), and the name `Policies` is what `PersistenceServiceCollectionExtensions.cs` 20 reads. SQL has an Entra group admin with `azureADOnlyAuthentication: true` and no `administratorLogin`. The grant step builds the SID (135-136) and creates the user `WITH SID = $sid, TYPE = E` (139; no `FROM EXTERNAL PROVIDER`), then adds `db_datareader` and `db_datawriter` only. The `CREATE USER` is guarded by `IF NOT EXISTS`, so a re-run is harmless | As expected. The clientId flows `appservice.bicep` 76 → `main.bicep` 102 → `stage-template.yml` 95 → 135. Step 8 checks the byte order | PASS |
| 23 | — | AC3 · private endpoint, no public SQL | `sql.bicep` 12, 19, 42-88; `appservice.bicep` 38; `network.bicep` 72-108 | `publicNetworkAccess: 'Disabled'`, no firewall rule. A private endpoint (`groupIds: ['sqlServer']`) in `snet-private-endpoints`, a `privatelink<sqlServerHostname>` private DNS zone linked to the VNet, and a zone group on the endpoint. The app joins `snet-app-integration` by `virtualNetworkSubnetId` | As expected. The zone name uses `environment().suffixes.sqlServerHostname` (61), not a hard-coded host | PASS |
| 24 | — | AC3 · NSGs (read by eye) | `network.bicep` 11-33, 35-70, 79-103 | `snet-app-integration` has an NSG that denies all inbound at priority 4000. `snet-private-endpoints` has an NSG with **one** Allow rule (priority 100, Inbound, Tcp, port 1433, sources exactly `[appIntegrationPrefix, agentAddressPrefix]`, destination the PE subnet) and a deny-all inbound at 4000. `privateEndpointNetworkPolicies: 'Enabled'` (98) makes the NSG apply to the endpoint. Read this by eye: the checker does not prove "only" (note O2) | As expected: one Allow rule with exactly the two sources, then deny-all. No other rule in either NSG | PASS |
| 25 | — | AC3 · no secrets, by design | `params/*.bicepparam` 8-12; `stage-template.yml` 19-21, 30-31, 47, 64-67, 88; `appservice.bicep` 61-72; `monitoring.bicep` 26 | No parameter is a secret: the admin group's object id is a placeholder overridden from the `policies-<env>` variable group at deploy time (read in both stages, 30-31 and 64-67; passed at 47 and 88), and the agent range is committed as "not a secret". The variable group is read by the unapproved what-if stage, so it must stay secret-free (19-21). Basic-auth publishing (`scm`, `ftp`) is `allow: false`. The App Insights connection string is described as an ingestion endpoint, not a credential. Every SQL connection is Entra (steps 9, 22) | As expected | PASS |
| 26 | — | AC4 · stated as never deployed | `azure-pipelines.yml` 1 | `# Evidence only: never connected to Azure DevOps, never run.` | As expected. Steps 10 and 11 back it | PASS |

### C. Out-of-repo prerequisites a real deployment needs

None of these exists, and none can be checked from here. Each row checks only that the repo **states** the
prerequisite at the cited lines, so that a reader could stand the deployment up. PASS means "stated there".

| # | Rule | Check | Steps / input | Expected | Actual | Pass/Fail |
|---|---|---|---|---|---|---|
| 27 | — | AC2 · three self-hosted agent pools | `stage-template.yml` 55-61 | Pools `policies-dev-vnet`, `policies-test-vnet`, `policies-prod-vnet`, one per environment, for the deploying job. Their agents have `az`, `jq`, go-sqlcmd (`sqlcmd`) and `curl`. Microsoft-hosted agents can't reach the private endpoint | Stated at 55-61 (pool name at 61, tools at 59). The what-if job runs on a Microsoft-hosted agent (28-29): it talks to ARM only, never to SQL | PASS |
| 28 | — | AC3 · network reach: peering, DNS, agent range | `stage-template.yml` 55-58, 111; `params/*.bicepparam` 10-12; `network.bicep` 5-6, 78 | The agents' network is peered to (or is) `vnet-policies-<env>` (10.20.0.0/16) and resolves `privatelink.database.windows.net`. The agents' address range is inside `agentAddressPrefix`, and the `10.30.0.0/24` placeholder in each param file is replaced with the real, narrow range before the first deployment | Stated at those lines. Not stated: the private DNS zone is linked only to `vnet-policies-<env>` (`sql.bicep` 66-75), so the agents' own VNet needs its own zone link or DNS forwarding, made outside the repo. The peering can only be made after the first infrastructure deployment creates the VNet (note O4) | PASS |
| 29 | — | AC2 · ARM service connections | `azure-pipelines.yml` 64, 71, 78; `stage-template.yml` 19-20, 38, 74, 79, 100-102, 144 | `sc-policies-dev`, `sc-policies-test`, `sc-policies-prod`, with subscription-scope rights (the deployment creates the resource group); they run the what-if (38), deploy resources (79) and the app zip (144) only. The template states the residual risks of that connection: its rights (100-102), and its use by the unapproved what-if stage (19-20) | Stated (see O8) | PASS |
| 30 | — | AC3 · SQL service connections, least privilege | `azure-pipelines.yml` 65, 72, 79; `stage-template.yml` 97-102 | `sc-policies-<env>-sql`, separate from the ARM ones. Its principal is a database-scoped user with `db_owner` on `policies` and is not in the server admin group. The residual risk of the ARM connection is stated and accepted | Stated | PASS |
| 31 | — | AC3 · Entra admin groups and variable groups | `params/*.bicepparam` 7-9; `stage-template.yml` 30-31, 64-67 | Entra groups `sg-policies-sql-admins-<env>`. Variable groups `policies-<env>`, each holding `sqlAdminGroupObjectId` (the group's object id) | Stated | PASS |
| 32 | — | AC2 · Azure DevOps environments and the prod approval | `stage-template.yml` 17-18, 62-63; `azure-pipelines.yml` 3-4 | Environments `policies-dev`, `policies-test`, `policies-prod`; an approval check on `policies-prod` only, and no check on the ARM service connection or the variable group | Stated | PASS |
| 33 | — | AC3 · the one-off SQL bootstrap per environment | `stage-template.yml` 103-110 | After the first infrastructure deployment, a member of the admin group, connected to the `policies` database, runs `CREATE USER [<SQL connection principal>] FROM EXTERNAL PROVIDER; ALTER ROLE db_owner ADD MEMBER [...]`. Until then the first run fails at `Apply migrations` | Stated, with both statements (107-108) and the expected first-run failure (103-104) | PASS |

## Exploratory notes / defects

### Re-run, 2026-10-19 (tester agent, t4, at a14af3fa1e6572bd2c81e5a8af96cd7240a9c7e4)

**33 of 33 step rows PASS, 0 FAIL.** No defect raised.

This re-run follows inv-002 (accepted recommendation 4) and closes the A2.4 reviewer's R3 (stale line references).
Every executable step (1-13) was run again at a14af3f, and every pipeline and checker line was re-cited.

What changed from the b92de26 run:

- **Rewritten for the two-stage layout:** steps 16, 17 and 18, and O1 (now resolved).
- **Expected changed because the files changed, not because a check was relaxed:**
  - Step 9 now finds six comment lines, not five. The new one is `stage-template.yml:21`, which says the variable
    group must stay secret-free.
  - Step 10 now finds five lines, not three. The two new ones are the checker's string needles
    (`check-pipeline.py` 94, 98), which read the YAML and run nothing. The three `az` calls are still the only ones,
    all in the template (42, 83, 148).
- **Re-cited only, same content:** steps 6, 15, 19, 20, 22, 25 and 27-33, C8, and O2. Steps 1-5, 7, 8, 11-14,
  21, 23, 24 and 26 cite files that are unchanged since b92de26.

1. **Shell.** The commands were run from Git Bash, not PowerShell: `> /dev/null; echo $?` for `> $null; $LASTEXITCODE`,
   `grep -cF` for `Select-String … | Measure-Object` (step 7), `which az` for `Get-Command az` (step 11), and
   `git grep` exactly as written (steps 9, 10, 12). Steps 1, 3, 4 and 5 left stderr on the console, so a Bicep
   diagnostic would have shown; none did.
2. **No build output written.** `bicep build` and `build-params` ran with `--stdout`, and step 7's script went to
   the console, so no `main.json`, `.parameters.json` or `migrations.sql` was left in the worktree.
3. **dotnet-ef version.** The tool on this machine is 9.0.9. Step 7 prints "The Entity Framework tools version
   '9.0.9' is older than that of the runtime '10.0.13'" on stdout, then the script. The pipeline installs 10.0.13
   (`azure-pipelines.yml` 40). The script I counted came from the 9.0.9 tool over the 10.0.13 design-time assembly.
4. **Order.** Step 13 (the full suite) ran first, then steps 1-12, then the read-throughs against the files at
   a14af3f.
5. **Not run.** There is no mutation step in this script. The checker's mutation proofs (the c2af74d one-stage
   layout, and a deploy stage with `condition: always()`) belong to the builder's red commit 5defb7f and the A3
   reviewer's receipts, not to this QA run. Step 17 reads the stage layout by eye as well.

Observations for David (not step rows, not raised as findings):

- **O1. Resolved: the prod approval now comes after the what-if.** At b92de26 the what-if ran inside the
  approval-gated deployment job, so the prod approver approved before any what-if output existed. You ruled it
  fix-now at the QA gate. It was fixed in two rounds:
  - **6fe2b8d (red) and c2af74d (fix)** moved the what-if into its own job, but in the same stage as the gated job.
    The A2.4 reviewer failed that (R1, R2). Azure DevOps evaluates every check on every resource a stage uses
    before the stage starts, so the approval still came first.
  - **inv-002** traced the cause to a job-level restatement of this observation in the fix brief.
  - **5defb7f (red) and a14af3f (fix, `Fixes-Finding: A2.4/I1`, `A2.4/I2`)** made each environment two stages:
    - `WhatIf_<env>` (`stage-template.yml` 22-47) is a regular job with no environment.
    - `Deploy_<env>` (49-158) has `dependsOn: WhatIf_<env>` (51) and no condition. It is the only stage on
      `policies-<env>` (63).

  So the `policies-prod` approval is requested when `Deploy_prod` is about to start, after `WhatIf_prod` has
  printed. A failed what-if stops `Deploy_prod`, because a stage with no condition runs only after its dependencies
  succeed. The checker asserts this layout (90-110), and the schema rejects a stage-level `condition` (37-45). The
  A3 reviewer and security verdicts are PASS at a14af3f. Steps 17 and 18 read it.

  Three things still hold:
  - The what-if gates nothing by itself. It is the approver who must open the `WhatIf_prod` log before approving.
  - In dev and test there is no approval, so the deployment follows the what-if at once there.
  - The ordering rests on documented Azure DevOps behaviour (checks evaluated at stage start). I can't verify it
    offline, and the pipeline has never run.
- **O2. The checker's NSG assertion is weaker than its comment.** This is the A2.3 reviewer's carried P2
  (`A2.3/R1`). `check-pipeline.py` 246-251 filters the Allow rules to Inbound Tcp 1433, then requires at least one,
  and requires any one of them to name both sources. So it still passes a second Allow rule, an extra source in
  the allow, or an Allow on port `*`. The infrastructure at a14af3f is right (step 24 reads it by eye). But step 6
  alone does not prove "1433 only from the app subnet and the agent range".
- **O3. Two placeholders differ in kind.** `sqlAdminGroupObjectId` (all zeros) is overridden at deploy time from the
  variable group. `agentAddressPrefix` (`10.30.0.0/24`, the same in all three files) is **not** overridden: it must
  be edited in each `.bicepparam` before the first real deployment, as its comment says.
- **O4. The three environments share one address space.** Every `vnet-policies-<env>` is `10.20.0.0/16`
  (`network.bicep` 78). One agent network therefore can't be peered to more than one of them. The one-pool-per-
  environment design fits that, but a shared hub would not.
- **O5. The runtime string is unconfirmed.** `linuxFxVersion: 'DOTNETCORE|10.0'` (`appservice.bicep` 40) is
  inferred, not checked with `az webapp list-runtimes`. This is already tracked as A11 in `docs/readme-checklist.md`
  26.
- **O6. The what-if step's AzureCLI@2 inputs are not fully checked (A3 reviewer R1, carried P2).** The checker's
  required-inputs loop (`check-pipeline.py` 81-86) covers only the deployment job's AzureCLI@2 steps. Since the
  what-if left that job (c2af74d), the checker only verifies that its `azureSubscription` is a template parameter
  (205-208). It no longer checks `scriptType`, `scriptLocation` or `inlineScript`, although the docstring (line 8)
  promises an "AzureCLI@2 inputs" check. At a14af3f the what-if step has all four inputs (`stage-template.yml`
  38-41; step 17 reads them by eye).
- **O7. Some of the checker's stage assertions are redundant (A3 reviewer R2, carried P2).** Lines 109-110 assert
  the what-if stage's position in the YAML. Azure DevOps ignores that position when `dependsOn` is explicit; the real
  order comes from 106-107 (`dependsOn`) and 108 (no condition). Lines 102-103 follow from 98 and 100, and line 105
  follows from 78 and 101. None of these is wrong or weaker than intended, but each is one more line to explain.
  Accepted inv-002 recommendation 2 asked for the redundant ordering check to be dropped.
- **O8. Accepted residual risk: the unapproved what-if stage uses the full ARM connection.** `WhatIf_<env>` runs on
  `${{ parameters.azureServiceConnection }}` (`stage-template.yml` 38), the same subscription-scope connection that
  deploys (79, 144). So a commit that reaches `main` runs `WhatIf_prod` with prod ARM rights before any approval.
  - This was the A2.4 security P2 S1 and inv-002 finding I4. inv-002 recommendation 3 (a separate Reader plus
    what-if connection per environment) was **rejected**.
  - The risk is recorded in the template (19-21). The mitigations are the trigger on `main` only with `pr: none`
    (`azure-pipelines.yml` 5-9) and the fact that only you merge.
  - The obvious alternative, an approval check on the ARM connection, is ruled out in the template (17-18). Checks
    apply to every protected resource a stage uses, so it would gate the what-if stage and bring O1's defect back.
    The same holds for the `policies-<env>` variable group. That group is also read before approval, so it must
    stay secret-free (20-21).

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
