#!/usr/bin/env bash
# Usage: scripts/vulnerable-check.sh <line> [float]
# Exits 1 when any direct or transitive package has a known vulnerability, 2 when the check itself failed.
#   float: check the floating "latest" dependency set (AutomateFloat=true) instead of the locked floor.
#
# Why this is a script: `dotnet list package --vulnerable` exits 0 even when it finds vulnerabilities, so the
# report text decides. And `dotnet list package` rejects `-p:` properties (SDK 10), so the line and the float
# switch go in as environment variables, which MSBuild reads as properties.
set -uo pipefail

if [[ $# -lt 1 || ( $# -ge 2 && "$2" != "float" ) ]]; then
  echo "usage: $0 <line> [float]" >&2
  exit 2
fi

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

export AutomateMajor="$1"
if [[ "${2:-}" == "float" ]]; then
  export AutomateFloat=true
fi

out="$(dotnet list Umbraco.Community.Automate.Jira.slnx package --vulnerable --include-transitive 2>&1)"
status=$?
echo "$out"

# A failed listing (restore error, no network) must not read as "no vulnerabilities".
if [[ $status -ne 0 ]]; then
  echo "vulnerable-check: dotnet list package failed with exit code $status" >&2
  exit 2
fi

# Fail closed when the audit data could not be fetched: `dotnet list package` still exits 0 then (NU1900),
# and a silent audit-source outage must never turn the required check green.
if grep -qE "NU190[0-9]|[Ee]rror occurred while getting package vulnerability data" <<<"$out"; then
  echo "vulnerable-check: vulnerability data could not be fetched on line $AutomateMajor" >&2
  exit 2
fi

# Require positive evidence of a scan: one verdict sentence per project in the solution.
projects=$(grep -c '<Project Path=' Umbraco.Community.Automate.Jira.slnx)
verdicts=$(grep -cE "has no vulnerable packages given the current sources|has the following vulnerable packages" <<<"$out")
if [[ "$verdicts" -lt "$projects" ]]; then
  echo "vulnerable-check: expected a verdict for $projects projects, saw $verdicts" >&2
  exit 2
fi

if grep -q "has the following vulnerable packages" <<<"$out"; then
  echo "vulnerable-check: vulnerable packages found on line $AutomateMajor" >&2
  exit 1
fi
echo "vulnerable-check: none found on line $AutomateMajor"
