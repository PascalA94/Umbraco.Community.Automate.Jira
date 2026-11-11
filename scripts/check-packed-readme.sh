#!/usr/bin/env bash
# Usage: scripts/check-packed-readme.sh <nupkg>
# Fails when the README packed into the package differs from the repo README, or when the package carries the
# demo site or an automation seed (plan review P1: the use case 4 recipe's warnings must ship as reviewed, and
# the disabled demo seed must never reach a site).
set -euo pipefail

if [[ $# -ne 1 || ! -f "$1" ]]; then
  echo "usage: $0 <nupkg>" >&2
  exit 2
fi

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
pkg="$1"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

listing="$(unzip -Z1 "$pkg")"

if grep -qiE '(^|/)demo/|\.automation\.json$' <<<"$listing"; then
  echo "check-packed-readme: the package contains demo or seed files:" >&2
  grep -iE '(^|/)demo/|\.automation\.json$' <<<"$listing" >&2
  exit 1
fi

readme="$(grep -E '(^|/)README\.md$' <<<"$listing" || true)"
if [[ "$(grep -c . <<<"$readme")" -ne 1 ]]; then
  echo "check-packed-readme: expected exactly one README.md in the package, found: ${readme:-none}" >&2
  exit 1
fi

unzip -p "$pkg" "$readme" > "$work/README.md"
if ! cmp -s "$work/README.md" "$root/README.md"; then
  echo "check-packed-readme: the packed $readme differs from the repo README.md" >&2
  exit 1
fi

echo "check-packed-readme: ok ($readme matches, no demo or seed files)"
