# Infrastructure and deployment

> **Never connected, never run.** This pipeline has never been connected to an Azure DevOps organisation or run.
> No service connections, environments or approvals exist. The Bicep is linted and built locally, and the YAML
> is schema-checked locally. Neither what-if nor a real pipeline compile was run, because both need Azure or
> Azure DevOps. The repo's real gate is its local git hooks (requirements §3, "No CI"). This pipeline shows
> how CI/CD would look at Uinsure.

Everything is under `infrastructure/`. It was built (F5) and was not cut. `az` is never run and nothing is
deployed. Back to the [project README](../README.md).

## Status

The Bicep is linted and built locally and never deployed. what-if was not run, because it needs a
signed-in subscription.

## Shape

- Hand-written Bicep at subscription scope: `main.bicep` creates the resource group `rg-policies-<env>` and
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
| prod | P1v3 | General Purpose serverless, `GP_S_Gen5_1` |

## No secrets

The app signs in to Entra-only Azure SQL with a **user-assigned** managed identity (the connection string
uses `Authentication=Active Directory Managed Identity`). The SQL server has no administrator login or
password, so there are no passwords. **Key Vault was considered and rejected**: there is nothing to store.
It arrives with the first real secret, such as a payment API key. Basic-auth publishing (`scm`, `ftp`) is
disabled on the app. The Application Insights connection string is an ingestion endpoint, not a credential.

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

`infrastructure/pipelines/azure-pipelines.yml` runs on pushes to `main` only (`pr: none`):

1. **Build** (hosted agent): `dotnet build -warnaserror`, `dotnet test`, the idempotent migration script
   (`dotnet-ef` 10.0.13), then a published app zip and the `infrastructure/` folder as one `drop` artifact.
2. Then, for `dev`, `test` and `prod` in that order, from one `stage-template.yml`: a **`WhatIf_<env>`** stage
   (hosted agent, no environment, so it runs unapproved), then **`Deploy_<env>`** (self-hosted pool
   `policies-<env>-vnet`, the only stage on the environment `policies-<env>`). Deploy runs the infrastructure
   deployment, applies the migration script with `sqlcmd`, creates the app's database user (by SID, with
   `db_datareader` and `db_datawriter` only), deploys the zip, and smoke-tests `/health/ready` with `curl`.

## Schema check

The claim is exactly this: the YAML is valid against the schema; template expansion, `${{ }}` expressions,
task inputs, service connections and environments are not checked.

`python -I infrastructure/pipelines/check-pipeline.py` (needs PyYAML and jsonschema) validates both YAML files
against `pipeline.schema.json` and asserts a set of structural rules the schema cannot express: the stage
order, a what-if stage with no environment, the approval environment, the migration step, the smoke test, and
the NSG rules.

## Prod approval

The Approval check on the environment `policies-prod` (approver: release manager, timeout 72 h) lives in the
Azure DevOps UI, not the YAML. This README is its only evidence. That is a platform limit, not a gap. Because
checks are evaluated when a stage starts, the what-if has its own stage, so the approver sees it before
approving. The check must sit on the environment only. If it were put on the ARM service connection or on the
`policies-<env>` variable group, the what-if stage would pause first.

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
5. The environments `policies-dev`, `policies-test` and `policies-prod`, which aren't auto-created from YAML,
   with the Approval check on prod.

The names above are the ones in the repo. The original brief used `sc-uinsurepolicies-*` and
`uinsurepolicies-*`; the YAML says `sc-policies-*` and `policies-*`.

## Deployment prerequisites outside the repo

None of this exists and none of it can be checked from the repo.

- **Three self-hosted agent pools**, `policies-dev-vnet`, `policies-test-vnet`, `policies-prod-vnet`. Their
  agents need `az`, `jq`, go-sqlcmd (`sqlcmd`) and `curl`. **The agent hosts are privileged**: they can reach
  the private SQL endpoint and run with the service connections. Only merged code reaches them (`main` only,
  `pr: none`).
- **Network peering and DNS.** The agents' network must be peered to (or be) `vnet-policies-<env>` and resolve
  `privatelink.database.windows.net`. The private DNS zone is linked only to the environment's own VNet, so
  the agents' network needs its own zone link or DNS forwarding. The peering can only be made after the first
  infrastructure deployment creates the VNet.
- **A narrow `agentAddressPrefix`.** Each `.bicepparam` holds the placeholder `10.30.0.0/24`. Replace it with
  the real agent subnet before the first deployment. Never `0.0.0.0/0` or a `/8`. There is no CIDR validation,
  so the narrowness rests on review. The placeholder fails closed: it blocks the agents, it does not open SQL.
- **The SQL admin group** (item 4 above) and **ARM and SQL service connections**. The SQL connections
  (`sc-policies-<env>-sql`) are separate from the ARM ones. Each one's principal is a database-scoped user with
  `db_owner` on the `policies` database and is not in the server admin group.
- **A one-off database bootstrap per environment.** After the first infrastructure deployment, a member of the
  admin group, connected to the `policies` database, runs
  `CREATE USER [<SQL service connection principal name>] FROM EXTERNAL PROVIDER;` and
  `ALTER ROLE db_owner ADD MEMBER [<SQL service connection principal name>];`.
  **The first run fails at the migrations step until this is done.**

## Accepted risks and notes

- **The unapproved what-if stage shares the deploy ARM connection.** `WhatIf_<env>` uses the same
  subscription-scope ARM connection as the deployment. A separate lower-privilege what-if connection was
  considered and rejected. It only runs `az deployment sub what-if`, which creates nothing. The variable group
  it reads must stay secret-free, because it is read before approval.
- **The ARM connection could rewrite the SQL admin.** It holds subscription-scope rights, so a leaked ARM token
  could change the SQL server's Entra admin through ARM. A subscription-scope deployment needs those rights.
  The pipeline's SQL steps use the separate SQL connection, which cannot administer the server.
- **`httpsOnly` and forwarded headers.** The web app sets `httpsOnly: true`, so App Service redirects plain
  HTTP at its front end and the API's own `400 /problems/https-required` path is never reached there. The API
  trusts `X-Forwarded-For` and `X-Forwarded-Proto` only from private ranges (10.0.0.0/8, 172.16.0.0/12,
  192.168.0.0/16) and never uses `ASPNETCORE_FORWARDEDHEADERS_ENABLED`. App Service Linux may set that
  variable itself, and the API's design says not to rely on it. This interaction has not been tested on App
  Service.
- **SQL connection policy is `Default`.** `sql.bicep` does not set it. Private-endpoint traffic is then
  expected to use port 1433 only, which is all the NSG opens. If the policy were set to Redirect, ports
  11000-11999 would also need to be opened. This is an expectation, not something verified.
- **One VNet range for all environments.** Every `vnet-policies-<env>` is `10.20.0.0/16`. One agent network
  cannot be peered to more than one environment, which is why the pipeline uses one pool per environment.

## Known limits of the evidence

The offline checker proves structure, not runtime. A green check means the files are shaped as described. It
does not mean the pipeline would run. Three review findings on the checker were carried and not fixed:

- **F5 A2.3 R1:** the NSG assertions do not prove "only". A second Allow rule, or an extra source in the
  existing allow, would still pass. The Bicep itself is correct: one allow rule, then deny-all. It must be
  read by eye.
- **F5 A3 R1:** the "AzureCLI@2 inputs" check covers the deploying stage only. The what-if stage's AzureCLI@2
  step is not checked for `scriptType`, `scriptLocation` or `inlineScript`.
- **F5 A3 R2:** the checker has a few redundant assertions, which the author would have to explain.
  Each is harmless.

The investigations that shaped F2 and F5 are in `docs/plans/active/inv-001.plan.md` and
`docs/plans/active/inv-002.plan.md`. The second moved the what-if into its own stage, because an approval check
on the environment ran before the what-if in the first layout.
