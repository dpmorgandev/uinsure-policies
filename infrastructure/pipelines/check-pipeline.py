"""Offline check of the Azure DevOps pipeline YAML. Never contacts Azure or Azure DevOps.

Run from the repo root:  python -I infrastructure/pipelines/check-pipeline.py
Needs PyYAML and jsonschema. Exit 0 = pass, 1 = a check failed.

1. Both files parse as YAML and match pipeline.schema.json.
2. Rules the schema cannot express: one stage template used for dev, test and prod in order, what-if before
   create, AzureCLI@2 inputs, prod approval environment, idempotent migrations, /health/ready smoke test,
   App Insights setting supplied by Bicep, and no secret-looking literals.
"""
import json
import re
import sys
from pathlib import Path

import yaml
from jsonschema import Draft202012Validator

HERE = Path(__file__).resolve().parent
errors = []


def check(condition, message):
    if not condition:
        errors.append(message)


def load(name):
    return yaml.safe_load((HERE / name).read_text(encoding="utf-8"))


schema = json.loads((HERE / "pipeline.schema.json").read_text(encoding="utf-8"))
validator = Draft202012Validator(schema)
pipeline = load("azure-pipelines.yml")
template = load("stage-template.yml")

for name, doc in (("azure-pipelines.yml", pipeline), ("stage-template.yml", template)):
    for e in validator.iter_errors(doc):
        errors.append(f"{name}: schema: {'/'.join(map(str, e.absolute_path))}: {e.message}")

# One stage template, dev -> test -> prod.
uses = [s for s in pipeline["stages"] if "template" in s]
check([u["parameters"]["environmentName"] for u in uses] == ["dev", "test", "prod"], "template must be used for dev, test, prod in order")
check(all(u["template"] == "stage-template.yml" for u in uses), "every deploy stage must use stage-template.yml")
check([u["parameters"]["dependsOn"] for u in uses] == ["Build", "Deploy_dev", "Deploy_test"], "stages must chain Build -> dev -> test -> prod")
check(any(s.get("stage") == "Build" for s in pipeline["stages"]), "missing Build stage")

# Build stage: build, test, idempotent script.
build_stage = next((s for s in pipeline["stages"] if s.get("stage") == "Build"), None)
build_text = json.dumps(build_stage) if build_stage else ""
for needle in ("dotnet build", "dotnet test", "migrations script --idempotent"):
    check(needle in build_text, f"Build stage must run: {needle}")

# Deploy steps.
def job_steps(job):
    return job.get("steps") or job.get("strategy", {}).get("runOnce", {}).get("deploy", {}).get("steps", [])


def runs(step, needle):
    return needle in step.get("inputs", {}).get("inlineScript", "")


def stage_jobs(stage):
    return stage.get("jobs") or []


def as_list(value):
    return [value] if isinstance(value, str) else list(value or [])


# Stages and jobs are found by key, across every stage the template emits, never by position.
tstages = template["stages"]
all_jobs = [j for st in tstages for j in stage_jobs(st)]
deploy_stages = [st for st in tstages if any("deployment" in j and "environment" in j for j in stage_jobs(st))]
check(len(deploy_stages) == 1, "QA: exactly one stage must hold the deployment job on an environment")
deploy_stage = deploy_stages[0] if deploy_stages else {}
deploy_jobs = [j for j in stage_jobs(deploy_stage) if "deployment" in j]
check(len(deploy_jobs) == 1, "QA: the deploying stage must hold exactly one deployment job")
deploy = deploy_jobs[0] if deploy_jobs else {}
steps = job_steps(deploy)
az = [s for s in steps if s.get("task") == "AzureCLI@2"]
check(len(az) >= 3, "deploy needs AzureCLI@2 steps")
for s in az:
    inputs = s["inputs"]
    for key in ("azureSubscription", "scriptType", "scriptLocation", "inlineScript"):
        check(key in inputs, f"AzureCLI@2 '{s.get('displayName')}' missing input {key}")
create = next((i for i, s in enumerate(steps) if runs(s, "deployment sub create")), None)
check(create is not None, "the deployment job must run deployment sub create")

# QA O1: Azure DevOps evaluates an environment's checks when a STAGE starts. The what-if must therefore run in its
# own stage with no environment, and the stage holding the deployment job (the only one on policies-<env>) must
# depend on it with no condition, so the approval is asked after the what-if has printed.
def stage_runs_whatif(stage):
    return any(runs(s, "az deployment sub what-if") for j in stage_jobs(stage) for s in job_steps(j))


whatif_stages = [st for st in tstages if stage_runs_whatif(st)]
check(len(whatif_stages) == 1, "QA: exactly one stage must run az deployment sub what-if")
whatif_stage = whatif_stages[0] if whatif_stages else {}
check(not any("environment" in j for j in stage_jobs(whatif_stage)), "QA: the what-if stage must declare no environment on any job (no approval gate)")
check(not any("deployment" in j for j in stage_jobs(whatif_stage)), "QA: the what-if stage must contain no deployment job")
check(not any(any("environment" in j for j in stage_jobs(st)) and stage_runs_whatif(st) for st in tstages),
      "QA: no what-if may run in any stage that declares an environment")
wi_stage_name = whatif_stage.get("stage")
check(wi_stage_name is not None and whatif_stage is not deploy_stage, "QA: the what-if and the deployment job must be in different stages")
check(wi_stage_name is not None and wi_stage_name in as_list(deploy_stage.get("dependsOn")),
      "QA: the deploying stage must list the what-if stage in its stage-level dependsOn")
check("condition" not in deploy_stage, "QA: the deploying stage must have no condition (it must not run after a failed what-if)")
check(bool(whatif_stage) and bool(deploy_stage) and tstages.index(whatif_stage) < tstages.index(deploy_stage),
      "QA: the what-if stage must come before the deploying stage in the template")
text = (HERE / "stage-template.yml").read_text(encoding="utf-8")
check("environment: policies-${{ parameters.environmentName }}" in text, "deployment must target environment policies-<env> (prod approval lives there)")
check("/health/ready" in steps[-1].get("script", ""), "last step must curl /health/ready")
check("curl" in steps[-1].get("script", "") and "--fail" in steps[-1].get("script", ""), "smoke test must be curl --fail")
check("migrations.sql" in text, "migrations.sql must be applied")
check("ActiveDirectoryAzCli" in text, "SQL access must use Entra authentication")

# Secrets and the App Insights setting.
combined = text + (HERE / "azure-pipelines.yml").read_text(encoding="utf-8")
check(not re.search(r"(?i)password\s*[=:]|pwd=|accountkey|sharedaccesskey|instrumentationkey=", combined), "secret-looking literal found")
bicep = (HERE.parent / "modules" / "appservice.bicep").read_text(encoding="utf-8")
check("APPLICATIONINSIGHTS_CONNECTION_STRING" in bicep, "Bicep must set APPLICATIONINSIGHTS_CONNECTION_STRING")
check("Active Directory Managed Identity" in bicep, "Bicep must use a managed identity connection string")
sql = (HERE.parent / "modules" / "sql.bicep").read_text(encoding="utf-8")
check("azureADOnlyAuthentication: true" in sql and "administratorLoginPassword" not in sql, "SQL must be Entra-only with no password")

# Review A1 (I1-I8). Added red; each assertion fails on the infrastructure as reviewed.
ROOT = HERE.parent
main_bicep = (ROOT / "main.bicep").read_text(encoding="utf-8")
pipeline_text = (HERE / "azure-pipelines.yml").read_text(encoding="utf-8")


def strip_comments(s):
    return "\n".join(line for line in s.splitlines() if not line.lstrip().startswith(("#", "//")))


template_code = strip_comments(text)

# I1: user-assigned identity; the database user is created by SID from its clientId, never FROM EXTERNAL PROVIDER.
check("UserAssigned" in bicep, "I1: the app must have a user-assigned identity (Bicep exposes its clientId)")
check(re.search(r"output\s+\w*client\w*\s+string", bicep, re.I) is not None, "I1: appservice.bicep must output the identity clientId")
check(re.search(r"(?is)CREATE USER.*WITH SID\s*=.*TYPE\s*=\s*E\b", template_code) is not None, "I1: grant step must CREATE USER ... WITH SID = ..., TYPE = E from the identity clientId")
check("FROM EXTERNAL PROVIDER" not in (template_code + strip_comments(pipeline_text) + strip_comments(sql)), "I1: FROM EXTERNAL PROVIDER must not appear (needs a Graph-privileged server identity)")

# I2/I7: dotnet-ef pinned, equal to the EF Core Design package version.
packages = (HERE.parents[1] / "Directory.Packages.props").read_text(encoding="utf-8")
design = re.search(r'Include="Microsoft\.EntityFrameworkCore\.Design"\s+Version="([^"]+)"', packages)
installs = re.findall(r"dotnet tool install[^\n]*dotnet-ef[^\n]*", pipeline_text)
check(bool(installs), "I2: Build stage must install dotnet-ef")
for line in installs:
    pinned = re.search(r"--version\s+(\S+)", line)
    check(pinned is not None, f"I2: dotnet-ef install is unversioned: {line.strip()}")
    check(pinned is not None and design is not None and pinned.group(1) == design.group(1),
          f"I2/I7: dotnet-ef version must equal Microsoft.EntityFrameworkCore.Design {design.group(1) if design else '?'}")

# I3: no dead outputs.
module_names = dict(re.findall(r"^module\s+(\w+)\s+'modules/(\w+)\.bicep'", main_bicep, re.M))
for sym, file in module_names.items():
    mod_text = (ROOT / "modules" / f"{file}.bicep").read_text(encoding="utf-8")
    for out in re.findall(r"^output\s+(\w+)\s", mod_text, re.M):
        check(f"{sym}.outputs.{out}" in main_bicep, f"I3: module output {file}.{out} is never referenced by main.bicep as {sym}.outputs.{out}")
for out in re.findall(r"^output\s+(\w+)\s", main_bicep, re.M):
    check(f".{out}.value" in text, f"I3: main.bicep output {out} is not consumed by the pipeline")

# I4: the tenant comes from the deployment context.
check(not re.search(r"^param\s+tenantId\b", main_bicep, re.M), "I4: main.bicep must not take a tenantId parameter")
check("tenantId=" not in combined, "I4: the pipeline must not pass tenantId=")

# I5: SQL is private; the pipeline's SQL steps run from inside the network.
check("publicNetworkAccess: 'Disabled'" in sql, "I5: SQL publicNetworkAccess must be 'Disabled'")
check("0.0.0.0" not in sql, "I5: no 0.0.0.0 (Allow Azure services) firewall rule")
check("Microsoft.Network/privateEndpoints" in sql and ("privatelink.database.windows.net" in sql or "'privatelink${environment().suffixes.sqlServerHostname}'" in sql), "I5: SQL needs a private endpoint and a privatelink.database.windows.net private DNS zone")
check("virtualNetworkSubnetId" in bicep, "I5: the app needs VNet integration (virtualNetworkSubnetId)")


def sql_jobs(doc):
    for job in (j for st in doc["stages"] for j in stage_jobs(st)):
        step_list = job_steps(job)
        if any("sqlcmd" in json.dumps(s) for s in step_list):
            yield job


found = list(sql_jobs(template))
check(bool(found) and all("vmImage" not in (j.get("pool") or {}) for j in found), "I5: SQL steps must run in a job whose pool is not a Microsoft-hosted vmImage")

# I6: no basic-auth publishing credentials.
policy_blocks = re.findall(r"resource \w+ '[^']*basicPublishingCredentialsPolicies@[^']*' = \{(.*?)\n\}", bicep, re.S)
for kind in ("scm", "ftp"):
    check(any(re.search(rf"name:\s*'{kind}'", blk) and re.search(r"allow:\s*false", blk) for blk in policy_blocks),
          f"I6: site must declare basicPublishingCredentialsPolicies '{kind}' with allow: false")

# I8: a separate, least-privilege SQL service connection, and the bootstrap is documented.
sql_steps = [s for s in steps if s.get("displayName") in ("Apply migrations", "Grant the app's managed identity database access")] + \
    [s for j in found for s in job_steps(j) if "sqlcmd" in json.dumps(s)]
arm_ref = "${{ parameters.azureServiceConnection }}"
check(bool(sql_steps) and all(s.get("inputs", {}).get("azureSubscription") not in (None, arm_ref) for s in sql_steps),
      "I8: migration and grant steps must use a SQL service connection variable, not the ARM deployment one")
lower = text.lower()
check("db_owner" in lower and "database-scoped" in lower and "not a member of the server admin group" in lower,
      "I8: template must state the SQL principal is a database-scoped user (db_owner), not a member of the server admin group")
check("bootstrap" in lower, "I8: template must describe the one-off bootstrap an admin runs")

# Re-review A2 (R2, R3, S1). Added red.
# R2: service connections resolve at compile time, so every azureSubscription is a template parameter, never $(...).
all_steps = [s for j in all_jobs for s in job_steps(j)]
subs = [s["inputs"]["azureSubscription"] for s in all_steps if "azureSubscription" in s.get("inputs", {})]
check(bool(subs) and all(re.fullmatch(r"\$\{\{\s*parameters\.\w+\s*\}\}", str(v)) for v in subs),
      "A2/R2: every azureSubscription must be a template parameter expression, not a runtime $(...) macro: " + ", ".join(map(str, subs)))
check("$(sqlServiceConnection)" not in text and "sqlServiceConnection" in pipeline_text.replace("$(sqlServiceConnection)", ""),
      "A2/R2: azure-pipelines.yml must pass the SQL service connection as a template parameter for each environment")

# R3: the deployment job's pool is a named self-hosted pool, policies-<env>-vnet.
deploy_pool = deploy.get("pool")
check(isinstance(deploy_pool, dict) and re.fullmatch(r"policies-\$\{\{\s*parameters\.environmentName\s*\}\}-vnet", str(deploy_pool.get("name", ""))) is not None,
      "A2/R3: the deployment job's pool must have name policies-<env>-vnet")

# S1: NSGs on both subnets; SQL NSG allows 1433 only from the app subnet and an agent range, denies the rest.
net = strip_comments((ROOT / "modules" / "network.bicep").read_text(encoding="utf-8"))
check("Microsoft.Network/networkSecurityGroups" in net, "A2/S1: network.bicep must declare a network security group")
check(len(re.findall(r"networkSecurityGroup\s*:", net)) >= 2, "A2/S1: each subnet must be associated with a network security group")
check(re.search(r"privateEndpointNetworkPolicies:\s*'Enabled'", net) is not None, "A2/S1: the private-endpoint subnet needs privateEndpointNetworkPolicies 'Enabled'")
check(re.search(r"(?i)param\s+\w*agent\w*\s+string", net) is not None, "A2/S1: network.bicep needs an agent address-range parameter")


# A2.2/R1: the rules are read from the NSG actually associated with the private-endpoint subnet.
def resource_block(src, symbol):
    m = re.search(rf"resource {re.escape(symbol)} '[^']*' = \{{(.*?)\n\}}", src, re.S)
    return m.group(1) if m else None


def rules_of(block):
    return [chunk for chunk in re.split(r"\n\s*name:\s*'", block)[1:] if "priority:" in chunk]


def field(rule, key):
    m = re.search(rf"\b{key}:\s*(\[[^\]]*\]|[^\n]+)", rule)
    return m.group(1).strip().strip("'") if m else None


subnet = re.search(r"name:\s*'snet-private-endpoints'(.*?)\n\s{6}\}", net, re.S)
nsg_ref = re.search(r"networkSecurityGroup:\s*\{\s*id:\s*(\w+)\.id\s*\}", subnet.group(1)) if subnet else None
check(nsg_ref is not None, "A2.2/R1: the private-endpoint subnet must reference its NSG (networkSecurityGroup: { id: <nsg>.id })")
sql_nsg = resource_block(net, nsg_ref.group(1)) if nsg_ref else None
check(sql_nsg is not None, "A2.2/R1: the NSG referenced by snet-private-endpoints must be declared in network.bicep")
sql_rules = rules_of(sql_nsg) if sql_nsg else []
allows = [r for r in sql_rules if field(r, "direction") == "Inbound" and field(r, "access") == "Allow"
          and field(r, "protocol") == "Tcp" and field(r, "destinationPortRange") == "1433"]
check(len(allows) >= 1, "A2/S1: the SQL NSG (on snet-private-endpoints) must allow inbound Tcp 1433")
check(any(re.search(r"appIntegrationPrefix|10\.20\.1\.0/24", field(r, "sourceAddressPrefixes") or "")
          and "agentAddressPrefix" in (field(r, "sourceAddressPrefixes") or "") for r in allows),
      "A2/S1: the SQL NSG's 1433 allow must take its sources from the app-integration prefix and agentAddressPrefix")
denies = [r for r in sql_rules if field(r, "direction") == "Inbound" and field(r, "access") == "Deny"
          and field(r, "sourceAddressPrefix") == "*" and field(r, "destinationPortRange") == "*"]
check(len(denies) >= 1, "A2/S1: the SQL NSG (on snet-private-endpoints) must deny all other inbound traffic")
allow_prios = [int(field(r, "priority")) for r in allows]
deny_prios = [int(field(r, "priority")) for r in denies]
check(bool(allow_prios) and bool(deny_prios) and min(deny_prios) > max(allow_prios),
      "A2/S1: the SQL NSG's deny-all must have a higher priority number than the 1433 allow")
# A2/R4 (comment wording about the leaked pipeline token) cannot be tested; the fix dispatch rewords it.

if errors:
    print("pipeline check FAILED")
    for e in errors:
        print(" -", e)
    sys.exit(1)
print("pipeline check passed")
