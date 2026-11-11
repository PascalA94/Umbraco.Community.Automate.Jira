#!/usr/bin/env bash
# Usage: scripts/pack.sh <line> <out-dir> [<minor> <patch> [<suffix>]]
# Packs the package for one Automate line as <line>.<minor>.<patch>[-<suffix>] from the locked floor restore.
# Only digits are accepted for line, minor and patch, and [0-9A-Za-z.-] for the suffix, so a tag name cannot
# reach MSBuild or a shell command unchecked.
set -euo pipefail

if [[ $# -lt 2 ]]; then
  echo "usage: $0 <line> <out-dir> [<minor> <patch> [<suffix>]]" >&2
  exit 2
fi

line="$1"; out="$2"; minor="${3:-0}"; patch="${4:-0}"; suffix="${5:-}"
for n in "$line" "$minor" "$patch"; do
  [[ "$n" =~ ^[0-9]+$ ]] || { echo "pack.sh: not a number: $n" >&2; exit 2; }
done
[[ -z "$suffix" || "$suffix" =~ ^[0-9A-Za-z][0-9A-Za-z.-]*$ ]] || { echo "pack.sh: bad suffix: $suffix" >&2; exit 2; }

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

project=src/Umbraco.Community.Automate.Jira/Umbraco.Community.Automate.Jira.csproj
props=(-p:AutomateMajor="$line" -p:PackageMinor="$minor" -p:PackagePatch="$patch" -c Release)
[[ -n "$suffix" ]] && props+=(-p:VersionSuffix="$suffix")

dotnet restore "$project" --locked-mode -p:AutomateMajor="$line"
dotnet pack "$project" --no-restore "${props[@]}" -o "$out" -v q
