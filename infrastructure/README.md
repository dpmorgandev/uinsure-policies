# Infrastructure and deployment

> **Never connected, never run.** This pipeline has never been connected to an Azure DevOps organisation or run.
> No service connections, environments or approvals exist. The Bicep is linted and built locally, and the YAML
> is schema-checked locally. Neither what-if nor a real pipeline compile was run, because both need Azure or
> Azure DevOps. The repo's real gate is its local git hooks (requirements §3, "No CI"). This pipeline shows
> how CI/CD would look at Uinsure.

Everything is under `infrastructure/`. `az` is never run and nothing is deployed. Back to the
[project README](../README.md).

## Status

The Bicep is linted and built locally and never deployed. what-if was not run, because it needs a
signed-in subscription.

## Shape

- Bicep at subscription scope: `main.bicep` creates the resource group `rg-policies-<env>` and
  calls four local modules (`monitoring`, `network`, `sql`, `appservice`).
- Per environment that is the resource group plus 16 resources: a Log Analytics workspace and Application
  Insights; a VNet and two NSGs; an Azure SQL server and database, a private endpoint and private DNS (zone,
  VNet link, zone group); a user-assigned identity, an App Service plan (Linux), the web app and two
  publishing-credential policies.
- Three `.bicepparam` files: `dev`, `test` and `prod`. Region `uksouth`.

| | App Service plan | SQL database |
|---|---|---|
| dev | B1 | Basic |
| test | B1 | Basic |
| prod | P1v3 | General Purpose, `GP_S_Gen5_1` |

## No secrets

The app signs in to Entra-only Azure SQL with a **user-assigned** managed identity (the connection string
uses `Authentication=Active Directory Managed Identity`). The SQL server has no administrator login or
password, so there are no passwords. **Key Vault was considered and rejected**: there is nothing to store.
It arrives with the first real secret, such as a payment API key. Basic-auth publishing (`scm`, `ftp`) is
disabled on the app. The Application Insights connection string reaches the app as the setting
`APPLICATIONINSIGHTS_CONNECTION_STRING`.

## Networking

SQL has no public endpoint and no "Allow Azure services" rule. It is reached through a private endpoint in
`snet-private-endpoints`, and the app uses VNet integration (`snet-app-integration`). An NSG on the endpoint
subnet allows port 1433 only from the app subnet and the deployment agents' range, and denies the rest.
Deployment agents must therefore be self-hosted, inside a network that reaches the VNet (see
[Deployment prerequisites](#deployment-prerequisites-outside-the-repo)).

Still to do: restrict outbound traffic from the integration subnet (an allow-list), which would be extra
hardening and is not built.

## Runtime

`linuxFxVersion` is `DOTNETCORE|10.0`. That value is inferred from the documented pattern and a Nov 2025 Q&A.
It wasn't confirmed with `az webapp list-runtimes`.

## The pipeline

`infrastructure/pipelines/azure-pipelines.yml` has a CI trigger on pushes to `main` and `pr: none`, so a push to
another branch or a pull request does not start it. The YAML does not stop someone with queue rights from
running it by hand against another branch (see the branch control step in [One-time setup](#one-time-setup-none-of-it-done)):

1. **Build** (hosted agent): `dotnet build -warnaserror`, `dotnet test`, the idempotent migration script
   (`dotnet-ef` 10.0.13), then a published app zip and the `infrastructure/` folder as one `drop` artifact.
2. Then, for `dev`, `test` and `prod` in that order, from one `stage-template.yml`: a **`WhatIf_<env>`** stage
   (hosted agent, no environment, so it runs unapproved), then **`Deploy_<env>`** (self-hosted pool
   `policies-<env>-vnet`, the only stage on the environment `policies-<env>`). Deploy runs the infrastructure
   deployment, applies the migration script with `sqlcmd`, creates the app's database user (by SID, with
   `db_datareader` and `db_datawriter` only), deploys the zip, and smoke-tests `/health/ready` with `curl`.

## Schema check

The claim is exactly this: the YAML is valid against the schema; template expansion, `${{ }}` expressions,
task inputs, service connections and environments are not checked against Azure DevOps.

The checker does assert that each `AzureCLI@2` step of the deploying stage has the inputs `azureSubscription`,
`scriptType`, `scriptLocation` and `inlineScript`, and that every `azureSubscription` is a template parameter. It
does not validate those values against Azure DevOps.

`python -I infrastructure/pipelines/check-pipeline.py` (needs PyYAML and jsonschema) validates both YAML files
against `pipeline.schema.json` and asserts a set of structural rules the schema cannot express: the stage
order, a what-if stage with no environment, the deploy environment name `policies-<env>`, the migration step,
the smoke test, and the NSG rules. It exits 0 with "pipeline check passed" on this branch (run on 2026-10-19).

## Prod approval

The Approval check on the environment `policies-prod` (approver: release manager, timeout 72 h) lives in the
Azure DevOps UI, not the YAML. This README is its only evidence. That is a platform limit, not a gap. The
template comment records why the what-if has its own stage: Azure DevOps evaluates an environment's checks when a
stage starts, so the approver sees the what-if before approving (`stage-template.yml`). That Azure DevOps
behaviour was not observed here, because the pipeline never ran. The template comment also says the check must sit
on the environment only, and that one put on the ARM service connection or on the `policies-<env>` variable group
would pause the what-if stage first.

## One-time setup (none of it done)

1. An Azure DevOps project, and a pipeline from `infrastructure/pipelines/azure-pipelines.yml`.
2. Three ARM service connections using workload identity federation, `sc-policies-dev`, `sc-policies-test`
   and `sc-policies-prod`, each authorised for this pipeline only. Three more, `sc-policies-<env>-sql`, are for
   the SQL steps (see below).
3. Each ARM connection's identity gets rights at subscription scope on its own subscription (Contributor, or a
   narrower custom role), because `main.bicep` creates the resource group.
4. One Entra group per environment as the SQL admin (that environment's deploy identity plus a break-glass
   human), named `sg-policies-sql-admins-<env>`. Its object ID goes in the variable group `policies-<env>` as
   `sqlAdminGroupObjectId`. The `.bicepparam` files hold an all-zero placeholder.
5. The environments `policies-dev`, `policies-test` and `policies-prod`, with the Approval check on prod.
6. A branch control check on each environment `policies-<env>`, on the three agent pools and on the service
   connections, allowing `refs/heads/main` only. Without it, nothing in the repo stops a manual run of another
   branch from reaching the privileged agents and the connections.

## Deployment prerequisites outside the repo

None of this exists and none of it can be checked from the repo.

- **Three self-hosted agent pools**, `policies-dev-vnet`, `policies-test-vnet`, `policies-prod-vnet`. Their
  agents need `az`, `jq`, go-sqlcmd (`sqlcmd`) and `curl`. **The agent hosts are privileged**: they can reach
  the private SQL endpoint and run with the service connections. The CI trigger is `main` only with
  `pr: none`; nothing in the YAML limits a manual run to `main` (see setup step 6).
- **Network peering and DNS.** The agents' network must be peered to (or be) `vnet-policies-<env>` and resolve
  `privatelink.database.windows.net`. The private DNS zone is linked only to the environment's own VNet, so
  the agents' network needs its own zone link or DNS forwarding. The peering can only be made after the first
  infrastructure deployment creates the VNet.
- **A narrow `agentAddressPrefix`.** Each `.bicepparam` holds the placeholder `10.30.0.0/24`. Replace it with
  the real agent subnet before the first deployment. Never `0.0.0.0/0` or a `/8`. There is no CIDR validation
  in the Bicep, so the narrowness rests on review. If the agents' real range is outside the placeholder, the NSG
  rule blocks them: it does not open SQL.
- **The SQL admin group** (item 4 above) and **ARM and SQL service connections**. The SQL connections
  (`sc-policies-<env>-sql`) are separate from the ARM ones. Each one's principal is a database-scoped user with
  `db_owner` on the `policies` database and is not in the server admin group. That is how the setup is meant
  to be done; the repo cannot check it.
- **A one-off database bootstrap per environment.** After the first infrastructure deployment, a member of the
  admin group, connected to the `policies` database, runs
  `CREATE USER [<SQL service connection principal name>] FROM EXTERNAL PROVIDER;` and
  `ALTER ROLE db_owner ADD MEMBER [<SQL service connection principal name>];`.
  **The template comment says the first run fails at the migrations step until this is done**
  (`stage-template.yml`; never run).

## Accepted risks and notes

- **The unapproved what-if stage shares the deploy ARM connection.** `WhatIf_<env>` uses the same
  subscription-scope ARM connection as the deployment. A separate lower-privilege what-if connection was
  considered and rejected. It only runs `az deployment sub what-if`. The variable group it reads must stay
  secret-free, because that stage has no approval before it.
- **The ARM connection could rewrite the SQL admin.** The template records it as an accepted residual risk: the
  ARM connection holds subscription-scope rights, so a leaked ARM token could still rewrite the SQL server's Entra
  admin through ARM (`stage-template.yml`). A subscription-scope deployment needs those rights.
  The pipeline's SQL steps use the separate SQL connection (`sqlServiceConnection`). The template states how it is
  meant to be set up: a database-scoped `db_owner` user, not in the admin group. The repo cannot check how it is
  set up.
- **`httpsOnly` and forwarded headers: configured, not tested on App Service.** The web app sets
  `httpsOnly: true`. What App Service does with plain HTTP at its front end has not been checked. The API honours
  `X-Forwarded-For` and `X-Forwarded-Proto` only from private ranges (10.0.0.0/8, 172.16.0.0/12,
  192.168.0.0/16), and the code does not use `ASPNETCORE_FORWARDEDHEADERS_ENABLED`. A plain-HTTP request that
  sends `X-Forwarded-Proto: https` from an address outside those ranges gets the `400 /problems/https-required`
  (tested from untrusted addresses, outside Development). Whether App Service Linux sets that environment variable
  itself is unchecked. None of this has been tested on App Service.
- **SQL connection policy is not set.** `sql.bicep` does not set it. The NSG on the endpoint subnet opens port
  1433 only. Whether private-endpoint traffic needs any other port has not been checked.
- **One VNet range for all environments.** Every `vnet-policies-<env>` is `10.20.0.0/16`. The pipeline uses one
  agent pool per environment. A single shared agent network would have to peer with three VNets that have the
  same address range. That has not been tried. The QA script `docs/qa/build-F5.md` (observation O4) says one agent
  network can't be peered to more than one of them.

## Known limits of the evidence

The offline checker proves structure, not runtime. A green check means the files are shaped as described. It
does not mean the pipeline would run. Three known gaps in the checker were left as they are:

- **The NSG assertions do not prove "only".** They require at least one matching Allow rule
  (`len(allows) >= 1`, `check-pipeline.py`). The Bicep itself has one allow rule, then deny-all (`network.bicep`),
  which has to be read by eye.
- **The "AzureCLI@2 inputs" check covers the deploying stage only.** The what-if stage's AzureCLI@2 step is not
  checked for `scriptType`, `scriptLocation` or `inlineScript`.
- **Two assertions overlap.** The checker asserts both the what-if stage's position in the template and the
  deploy stage's `dependsOn` on it (`check-pipeline.py`).

The reasoning for the stage layout is in `docs/plans/active/inv-002.plan.md`.
