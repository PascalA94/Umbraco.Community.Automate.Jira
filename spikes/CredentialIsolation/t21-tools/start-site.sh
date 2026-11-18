#!/usr/bin/env bash
# usage: start-site.sh <17|18|19> [widen]   (sentinel from file via env var; never printed)
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
# Line 19 shares Spike.CredentialStore with 18, so the whole build must be told the line (and run on the .NET 11 SDK).
# Restore ignores a project reference's AdditionalProperties, hence a global property. 17 and 18 run as before.
extra=""; [ "$L" = 19 ] && extra="-p:AutomateMajor=19"
exec dotnet run --no-launch-profile $extra
