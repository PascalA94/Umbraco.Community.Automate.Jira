#!/usr/bin/env bash
# usage: start-site.sh <17|18> [widen]   (sentinel from file via env var; never printed)
S=$(cd "$(dirname "$0")" && pwd); L=$1
export ASPNETCORE_ENVIRONMENT=Development
export Umbraco__Automate__Jira__Credentials__spike__ApiToken="$(cat $S/sentinel.txt)"
if [ "${2:-}" = widen ]; then
  export Umbraco__Automate__AllowedConfigurationKeyPrefixes__0="Umbraco:Automate:Secrets"
  export Umbraco__Automate__AllowedConfigurationKeyPrefixes__1="Umbraco:Automate:Variables"
  export Umbraco__Automate__AllowedConfigurationKeyPrefixes__2="Umbraco:Automate"
fi
export Umbraco__Automate__Variables__T21Var="$(cat $S/varmarker.txt)"
cd $S/work/CredentialIsolation/site$L
exec dotnet run --no-launch-profile
