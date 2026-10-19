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
check("SystemAssigned" in bicep, "App Service must have a system-assigned identity")
sql = (HERE.parent / "modules" / "sql.bicep").read_text(encoding="utf-8")
check("azureADOnlyAuthentication: true" in sql and "administratorLoginPassword" not in sql, "SQL must be Entra-only with no password")

if errors:
    print("pipeline check FAILED")
    for e in errors:
        print(" -", e)
    sys.exit(1)
print("pipeline check passed")
