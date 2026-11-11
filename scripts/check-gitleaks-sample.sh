#!/usr/bin/env bash
# Usage: scripts/check-gitleaks-sample.sh [gitleaks binary]
# Proves the sample rules in eng/gitleaks-jira-credentials.toml catch what the README says they catch, and stay
# quiet on Umbraco's own settings. Fixture values are fake and marked for the repo's own gitleaks scan.
set -euo pipefail

gitleaks="${1:-gitleaks}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

fixture() { mkdir -p "$work/$1/$(dirname "$2")"; printf '%s\n' "$3" > "$work/$1/$2"; }

# Expected: one finding each.
fixture nested appsettings.json '{ "Umbraco": { "Automate": { "Jira": { "Credentials": { "acme": { "Email": "svc@example.test", "ApiToken": "fakevalue-sample-1" } } } } } }' # gitleaks:allow
fixture webhook appsettings.Production.json '{ "Credentials": { "acmewh": { "WebhookSecret": "fakevalue-sample-2" } } }' # gitleaks:allow
fixture flat appsettings.json '{ "Umbraco:Automate:Jira:Credentials:agency:ApiToken": "fakevalue-sample-3" }' # gitleaks:allow
fixture launch Properties/launchSettings.json '{ "environmentVariables": { "Umbraco__Automate__Jira__Credentials__agency__ApiToken": "fakevalue-sample-4" } }' # gitleaks:allow
fixture dotenv .env 'Umbraco__Automate__Jira__Credentials__agency__WebhookSecret=fakevalue-sample-5' # gitleaks:allow
# Expected: none.
fixture notifications appsettings.json '{ "Umbraco": { "CMS": { "Content": { "Notifications": { "Email": "noreply@example.test" } } } } }'
fixture empty appsettings.json '{ "Umbraco": { "Automate": { "Jira": { "Credentials": { "acme": { "Email": "svc@example.test", "ApiToken": "" } } } } } }'

failed=0
check() {
  local name="$1" expected="$2" count
  "$gitleaks" detect --no-git --no-banner --source "$work/$name" --config "$root/eng/gitleaks-jira-credentials.toml" \
    --report-format json --report-path "$work/$name.json" >/dev/null 2>&1 || true
  count="$( { grep -o '"RuleID": *"umbraco-automate-jira-credential-[a-z]*"' "$work/$name.json" || true; } | wc -l | tr -d ' ')"
  if [[ "$count" != "$expected" ]]; then
    echo "check-gitleaks-sample: $name gave $count findings, expected $expected" >&2
    failed=1
  fi
}

for name in nested webhook flat launch dotenv; do check "$name" 1; done
for name in notifications empty; do check "$name" 0; done

[[ $failed -eq 0 ]] && echo "check-gitleaks-sample: ok (5 caught, 2 ignored)"
exit $failed
