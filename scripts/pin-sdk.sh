#!/usr/bin/env bash
# Usage: scripts/pin-sdk.sh <major>
# CI only. global.json says rollForward latestMajor, so a developer with only the 10 or only the 11 SDK can work on
# any line. On a hosted runner that also lets a newer preinstalled SDK (11 on ubuntu-24.04) build lines 17 and 18,
# whatever setup-dotnet installed. This pins this checkout's global.json to the newest installed SDK of <major>
# (patch roll-forward only, no prerelease) and fails unless dotnet then reports that major. The edit is never committed.
set -euo pipefail

major="${1:-}"
[[ "$major" =~ ^[0-9]+$ ]] || { echo "usage: $0 <major>" >&2; exit 2; }

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

sdk="$(dotnet --list-sdks | awk '{print $1}' | grep -E "^${major}\.[0-9]+\.[0-9]+$" | sort -V | tail -1 || true)"
if [[ -z "$sdk" ]]; then
  echo "pin-sdk: no stable .NET $major SDK is installed" >&2
  exit 1
fi

tmp="$(mktemp)"
jq --arg v "$sdk" '.sdk.version = $v | .sdk.rollForward = "latestPatch" | .sdk.allowPrerelease = false' global.json > "$tmp"
mv "$tmp" global.json

actual="$(dotnet --version)"
if [[ "$actual" != "$major".* ]]; then
  echo "pin-sdk: expected a .NET $major SDK, got $actual" >&2
  exit 1
fi
echo "pin-sdk: .NET SDK $actual"
