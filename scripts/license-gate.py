#!/usr/bin/env python3
"""Free-only supply chain gate: every package in the dependency graph must carry an
allowlisted OSS license expression. Run after `dotnet restore`."""
import json
import os
import pathlib
import re
import sys

ALLOW = {"MIT", "Apache-2.0", "BSD-2-Clause", "BSD-3-Clause", "0BSD", "MS-PL", "PostgreSQL"}

# licenseUrl-era packages (no SPDX expression in the nuspec); verified by hand.
KNOWN_LEGACY: dict[str, str] = {
    "xunit.abstractions": "Apache-2.0",
}

root = pathlib.Path(__file__).resolve().parent.parent
nuget_root = pathlib.Path(os.environ.get("NUGET_PACKAGES", pathlib.Path.home() / ".nuget/packages"))

packages: dict[str, str] = {}
for assets_path in root.glob("*/*/obj/project.assets.json"):
    assets = json.loads(assets_path.read_text())
    for key in assets.get("libraries", {}):
        name, _, version = key.partition("/")
        if assets["libraries"][key].get("type") == "package":
            packages[name] = version

failures: list[tuple[str, str, str]] = []
unknown: list[tuple[str, str, str]] = []
for pkg_id, version in sorted(packages.items()):
    nuspec = nuget_root / pkg_id.lower() / version / f"{pkg_id.lower()}.nuspec"
    if not nuspec.exists():
        unknown.append((pkg_id, version, "nuspec not found"))
        continue

    match = re.search(r'<license type="expression">([^<]+)</license>', nuspec.read_text())
    expression = match.group(1).strip() if match else None
    if expression is None:
        if pkg_id in KNOWN_LEGACY:
            continue
        unknown.append((pkg_id, version, "no license expression"))
        continue

    # SPDX semantics: any OR-branch fully allowed suffices; AND requires every term.
    def branch_ok(branch: str) -> bool:
        return {t.strip().strip("()") for t in re.split(r"\bAND\b", branch)} <= ALLOW

    if not any(branch_ok(b) for b in re.split(r"\bOR\b", expression)):
        failures.append((pkg_id, version, expression))

print(f"license-gate: {len(packages)} packages checked, allowlist={sorted(ALLOW)}")
for pkg_id, version, expr in failures:
    print(f"  FAIL  {pkg_id} {version}: {expr}")
for pkg_id, version, why in unknown:
    print(f"  CHECK {pkg_id} {version}: {why}")
if failures or unknown:
    sys.exit(1)
print("license-gate: GREEN — the dependency graph is entirely free/OSS")
