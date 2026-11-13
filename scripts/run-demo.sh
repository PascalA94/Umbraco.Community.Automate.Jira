#!/usr/bin/env bash
# Runs the demo site against the published package from nuget.org, for dogfooding (plan Task 22).
# Usage: scripts/run-demo.sh <17|18> [--fresh]
#   --fresh moves the demo database aside (timestamped, not deleted) and installs a new one unattended, asking
#   for the admin password silently. The admin login is demo-admin@example.invalid.
#   JIRA_EMAIL and JIRA_SITE_URL may be set in the environment to skip those prompts.
#   The API token is only ever read silently from the terminal (or stdin): it is never echoed, never written
#   to disk and never placed on a command line, where `ps` would show it. It reaches the site as an
#   environment variable of the dotnet process only.
set -uo pipefail
set +x

line="${1:-}"
if [[ "$line" != "17" && "$line" != "18" ]]; then
  echo "Usage: $0 <17|18> [--fresh]" >&2
  exit 2
fi
fresh="${2:-}"
if [[ -n "$fresh" && "$fresh" != "--fresh" ]]; then
  echo "Usage: $0 <17|18> [--fresh]" >&2
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

site_dir="$root/demo/Umbraco.Community.Automate.Jira.TestSite"
if [[ "$fresh" == "--fresh" ]]; then
  if lsof -nP -iTCP:5500 -sTCP:LISTEN >/dev/null 2>&1; then
    echo "Something is already listening on port 5500. Stop the running demo first." >&2
    exit 1
  fi
  read -r -s -p "New back office admin password (hidden, at least 10 characters): " admin_password
  echo
  if [[ ${#admin_password} -lt 10 ]]; then
    echo "The password must be at least 10 characters." >&2
    exit 1
  fi
  db="$site_dir/umbraco/Data/Umbraco.sqlite.db"
  if [[ -f "$db" ]]; then
    stamp="$(date +%Y%m%dT%H%M%S)"
    for f in "$db" "$db-shm" "$db-wal"; do
      [[ -f "$f" ]] && mv "$f" "$f.$stamp.bak"
    done
    echo "Moved the old demo database aside (*.$stamp.bak)."
  fi
  export Umbraco__CMS__Unattended__InstallUnattended=true
  export Umbraco__CMS__Unattended__UnattendedUserName="Demo Admin"
  export Umbraco__CMS__Unattended__UnattendedUserEmail="demo-admin@example.invalid"
  export Umbraco__CMS__Unattended__UnattendedUserPassword="$admin_password"
  unset admin_password
  # The install writes a generated imaging HMAC key into the tracked appsettings.json when none is configured.
  # Supplying one for this process keeps that secret out of the working tree (and out of commits).
  Umbraco__CMS__Imaging__HMACSecretKey="$(openssl rand -base64 64 | tr -d '\n')"
  export Umbraco__CMS__Imaging__HMACSecretKey
fi

# Seeds and the Kestrel endpoints in appsettings.Development.json only apply in Development, and the demo has
# no launchSettings.json to set it.
export ASPNETCORE_ENVIRONMENT=Development

echo "Jira site: ${site}  (enter it on the connection in the back office)"
echo "Demo: https://localhost:5500/umbraco  (the back office login needs HTTPS; Automate ${line}, package from nuget.org ${line}.0.0-beta.1)"
echo "Back office login: demo-admin@example.invalid"
cd "$site_dir" || exit 1
exec dotnet run --no-launch-profile -p:AutomateMajor="$line" -p:JiraPackageSource=nuget
