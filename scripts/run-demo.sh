#!/usr/bin/env bash
# Runs the demo site against the published package from nuget.org, for dogfooding (plan Task 22).
# Usage: scripts/run-demo.sh <17|18>
#   JIRA_EMAIL and JIRA_SITE_URL may be set in the environment to skip those prompts.
#   The API token is only ever read silently from the terminal (or stdin): it is never echoed, never written
#   to disk and never placed on a command line, where `ps` would show it. It reaches the site as an
#   environment variable of the dotnet process only.
set -uo pipefail
set +x

line="${1:-}"
if [[ "$line" != "17" && "$line" != "18" ]]; then
  echo "Usage: $0 <17|18>" >&2
  exit 2
fi

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"

email="${JIRA_EMAIL:-}"
site="${JIRA_SITE_URL:-}"
if [[ -z "$email" ]]; then read -r -p "Jira email: " email; fi
if [[ -z "$site" ]]; then read -r -p "Jira site URL (for example https://pascal-sandbox.atlassian.net): " site; fi
read -r -s -p "Jira API token (hidden): " token
echo

if [[ -z "$email" || -z "$site" || -z "$token" ]]; then
  echo "Email, site URL and token are all required." >&2
  exit 1
fi
site="${site%/}"

# Keys follow the package's credential shape (README, Credentials). The site URL is not a secret and the
# package reads it from the connection, so it is only checked here, not exported.
export Umbraco__Automate__Jira__Credentials__agency__Email="$email"
export Umbraco__Automate__Jira__Credentials__agency__ApiToken="$token"
unset token
unset JIRA_EMAIL JIRA_SITE_URL

# Seeds and the Kestrel endpoints in appsettings.Development.json only apply in Development, and the demo has
# no launchSettings.json to set it.
export ASPNETCORE_ENVIRONMENT=Development

echo "Jira site: ${site}  (enter it on the connection in the back office)"
echo "Demo: http://localhost:5200/umbraco  (Automate ${line}, package from nuget.org ${line}.0.0-beta.1)"
cd "$root/demo/Umbraco.Community.Automate.Jira.TestSite" || exit 1
exec dotnet run --no-launch-profile -p:AutomateMajor="$line" -p:JiraPackageSource=nuget
