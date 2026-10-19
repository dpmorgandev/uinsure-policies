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
build_text = json.dumps(pipeline["stages"][0])
for needle in ("dotnet build", "dotnet test", "migrations script --idempotent"):
    check(needle in build_text, f"Build stage must run: {needle}")

# Deploy steps.
deploy = template["stages"][0]["jobs"][0]
steps = deploy["strategy"]["runOnce"]["deploy"]["steps"]
az = [s for s in steps if s.get("task") == "AzureCLI@2"]
check(len(az) >= 3, "deploy needs AzureCLI@2 steps")
for s in az:
    inputs = s["inputs"]
    for key in ("azureSubscription", "scriptType", "scriptLocation", "inlineScript"):
        check(key in inputs, f"AzureCLI@2 '{s.get('displayName')}' missing input {key}")
whatif = next((i for i, s in enumerate(steps) if "what-if" in s.get("inputs", {}).get("inlineScript", "")), None)
create = next((i for i, s in enumerate(steps) if "deployment sub create" in s.get("inputs", {}).get("inlineScript", "")), None)
check(whatif is not None and create is not None and whatif < create, "what-if must come before deployment sub create")
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
check("Microsoft.Network/privateEndpoints" in sql and "privatelink.database.windows.net" in sql, "I5: SQL needs a private endpoint and a privatelink.database.windows.net private DNS zone")
check("virtualNetworkSubnetId" in bicep, "I5: the app needs VNet integration (virtualNetworkSubnetId)")


def sql_jobs(doc):
    for job in doc["stages"][0]["jobs"]:
        step_list = job.get("steps") or job.get("strategy", {}).get("runOnce", {}).get("deploy", {}).get("steps", [])
        if any("sqlcmd" in json.dumps(s) for s in step_list):
            yield job


found = list(sql_jobs(template))
check(bool(found) and all("vmImage" not in (j.get("pool") or {}) for j in found), "I5: SQL steps must run in a job whose pool is not a Microsoft-hosted vmImage")

# I6: no basic-auth publishing credentials.
for kind in ("scm", "ftp"):
    check(re.search(rf"basicPublishingCredentialsPolicies[^\n]*'{kind}'[\s\S]*?allow:\s*false", bicep) is not None,
          f"I6: site must declare basicPublishingCredentialsPolicies '{kind}' with allow: false")

# I8: a separate, least-privilege SQL service connection, and the bootstrap is documented.
sql_steps = [s for s in steps if s.get("displayName") in ("Apply migrations", "Grant the app's managed identity database access")] + \
    [s for j in found for s in (j.get("steps") or j.get("strategy", {}).get("runOnce", {}).get("deploy", {}).get("steps", [])) if "sqlcmd" in json.dumps(s)]
arm_ref = "${{ parameters.azureServiceConnection }}"
check(bool(sql_steps) and all(s.get("inputs", {}).get("azureSubscription") not in (None, arm_ref) for s in sql_steps),
      "I8: migration and grant steps must use a SQL service connection variable, not the ARM deployment one")
lower = text.lower()
check("db_owner" in lower and "database-scoped" in lower and "not a member of the server admin group" in lower,
      "I8: template must state the SQL principal is a database-scoped user (db_owner), not a member of the server admin group")
check("bootstrap" in lower, "I8: template must describe the one-off bootstrap an admin runs")

if errors:
    print("pipeline check FAILED")
    for e in errors:
        print(" -", e)
    sys.exit(1)
print("pipeline check passed")
