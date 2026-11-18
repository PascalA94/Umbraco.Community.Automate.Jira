# Contributing

Thanks for helping. Open an issue first for anything bigger than a small fix.

## Branches

`main` holds every package line (17, 18 and 19); there are no per-line branches. Work on a feature branch and open a pull request to `main`. Pull requests are squash-merged.

## Build and test

Lines 17 and 18 need the .NET 10 SDK, line 19 (net11.0) the .NET 11 SDK; `global.json` accepts either. The scripts call plain `dotnet`, so put the right SDK first on `PATH` for each line:

```bash
./scripts/test.sh                      # unit and integration tests, once per line in eng/lines.txt
./scripts/test.sh --filter "FullyQualifiedName~CreateIssue"
./scripts/vulnerable-check.sh 18       # known-vulnerable packages on the 18 line
./scripts/docs-guard.sh

# One line at a time, for example 19 with the .NET 11 SDK first on PATH:
dotnet test Umbraco.Community.Automate.Jira.slnx -p:AutomateMajor=19
./scripts/vulnerable-check.sh 19
```

`scripts/test.sh` runs every line with one `dotnet`, so it needs an SDK that can build and run both net10.0 and net11.0; otherwise run the lines one by one as above.

The live Jira contract tests skip unless `JIRA_CONTRACT_SITE`, `JIRA_CONTRACT_EMAIL`, `JIRA_CONTRACT_TOKEN` and `JIRA_CONTRACT_PROJECT` (and `JIRA_CONTRACT_CLOUDID` for a scoped token) are set. Use a sandbox-only service account.

Every PR runs the fast checks: every line at its floor and latest versions (19 floats `19.*-*` while only release candidates exist), the packed-README check, gitleaks, osv-scanner and docs-guard. The slow checks (live contract tests, SQL Server) are in the `release-checks` workflow, run by hand before a release.

Mark fake secrets in tests with `// gitleaks:allow` in the same commit.

## Demo site

`demo/Umbraco.Community.Automate.Jira.TestSite` is a SQLite Umbraco site with the package referenced:

```bash
dotnet run --project demo/Umbraco.Community.Automate.Jira.TestSite -p:AutomateMajor=18
# line 19, with the .NET 11 SDK first on PATH
dotnet run --project demo/Umbraco.Community.Automate.Jira.TestSite -p:AutomateMajor=19
```

Set credentials with `dotnet user-secrets` in the demo project, never in its `appsettings*.json`.

## Test a local pack

NuGet caches the first build of any version, so give each local pack a unique version:

```bash
./scripts/pack.sh 18 ./out 0 0 "local.$(date +%Y%m%d%H%M%S)"
```

Then add `./out` as a package source in the test site and install that exact version.

## Releases (maintainer)

1. Cut the release PR from a `release/<version>` branch. Add a `## <version>` heading line to `CHANGELOG.md`. Run `release-checks` on that branch (Actions → release-checks → Run workflow). Never dispatch it on a branch holding unreviewed outside code: its contract job has the sandbox token.
2. Squash-merge, then tag the merge commit. Two tag groups:
   - `v18.<minor>.<patch>[-suffix]`, for example `v18.0.0-beta.1`, packs lines 17 and 18 with that minor, patch and suffix (`18.0.0-beta.1` and `17.0.0-beta.1`).
   - `v19.<minor>.<patch>[-suffix]`, for example `v19.0.0-rc.1`, packs line 19 only.
   Any other major is rejected. `publish.yml` publishes after approval.
3. `gh release create` for each line, and mark the 18 line's release as Latest. A 19 release candidate is `--prerelease` and never Latest.

## Repository settings the release relies on (maintainer)

The workflows assume these settings. They are not in the repository, so check them after any change to GitHub or NuGet settings (M4 security review SEC-M4-1, SEC-M4-2).

- **Tag ruleset:** only admins can create, update or delete `v*` tags. A `v*` tag runs `publish.yml` from the tagged commit, so whoever can push a tag controls what that workflow does.
- **Environment `nuget`:** a required reviewer, and a deployment rule allowing `v*` tags only. Secret `NUGET_USER` (the nuget.org account name).
- **NuGet trusted publishing policy** (nuget.org → account → Trusted publishing): repository `PascalA94/Umbraco.Community.Automate.Jira`, workflow file `publish.yml`, environment `nuget`, limited to the package `Umbraco.Community.Automate.Jira`. Without the environment the policy does not check it.
- **Environment `jira-sandbox`:** a required reviewer, and deployment branches limited to `main` and protected `release/*` branches. Secrets `JIRA_CONTRACT_SITE`, `JIRA_CONTRACT_EMAIL`, `JIRA_CONTRACT_TOKEN`, `JIRA_CONTRACT_PROJECT` and `JIRA_CONTRACT_CLOUDID`, for a sandbox-only service account. GitHub creates a missing environment with no protection, so create it before the first run.
- **Branch ruleset on `main`:** PR required, the fast checks required (`build-test (<line>, floor|latest)` for 17, 18 and 19, so `build-test (19, floor)` and `build-test (19, latest)` too, `gitleaks`, the osv-scanner check by its exact name from a PR run, `docs-guard`), no force-push or deletion.
