# Jira for Umbraco Automate (MVP) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build and ship `Umbraco.Community.Automate.Jira` 1.0 (M0 to M5): a Jira Cloud connection on package-owned credentials, three outbound actions with retry-safe dedupe, a signed and replay-guarded webhook authenticator, a webhook parser action and a "Find content by Jira key" action, released as `17.x` and `18.x` packages from one codebase.

**Architecture:** One Razor class library laid out like HQ's `Umbraco.Automate.Slack` add-on. Credentials are read only by an internal `JiraCredentialStore` from `Umbraco:Automate:Jira:Credentials:{name}:*`, a prefix no Automate step can resolve. All HTTP goes through one `IJiraClient` that owns base URL, auth, JSON and error mapping. Text going to Jira passes through an ADF converter with a single allowlist safety pass. An MSBuild property `AutomateMajor` (17 or 18) picks the Automate and CMS dependency sets, the closed version ranges and an `AUTOMATE_17`/`AUTOMATE_18` constant.

**Tech Stack:** .NET (TFM set per Automate major at S0; `net10.0` on both lines per the Automate.Core nuspecs), Umbraco CMS `Core` + `Infrastructure` (Examine, `IContentService`), Umbraco Automate `Core` + `Testing`, Markdig `[1.5.1, 2.0.0)`, System.Text.Json, xUnit 2.9.3, Moq 4.20.72, Microsoft.NET.Test.Sdk 17.13.0 (HQ's pins at `5318996`), `Meziantou.Framework.AtlassianDataFormat` (tests only), GitHub Actions.

**Spec:** `/Users/tpxi676/Documents/Projects/Umbraco Packages/04-automate-jira/design.md` (its "Resolved by Pascal (2026-10-30)", "Security review changes (2026-10-30)" and "Plan review changes (2026-10-30)" sections are final and override older text). The credential helper is specified by `/Users/tpxi676/Documents/Projects/Umbraco Packages/credential-helper-contract.md` (shared with Teams; Task 6a builds to it and freezes it). Background: `scope.md`, `security-review.md` and `plan-review.md` in the same folder, `../README.md`, and the F-02 council verdict `../council-output/council-transcript-2026-10-30T005858.md`. Only `design.md` and `plan.md` are ever copied into the public repo.

**Task numbering:** tasks added on 2026-10-30 carry letter suffixes (0, 1a, 6a, 7a, 12a, 17a, 20a) so that the task numbers cited in `security-review.md` and `plan-review.md` stay valid. Order: 0, 1, 1a, 2, 3, 4, 5, 6, 6a, 7, 7a, 8 to 12, 12a, 13 to 17, 17a, 18 to 20, 20a, 21, 22 (29 tasks).

**Execution (portfolio README, "Build approach"):** hybrid. `superpowers:executing-plans`, with a fresh per-task review on Task 0, Task 6a and Task 16 (`qa-reviewer`, plus `security-analyst` for 6a and 16) because Teams reuse them.

**Reference:** HQ's Slack add-on at `umbraco/Umbraco.Automate` path `Umbraco.Automate.Slack/` (read-only clone may be at `/private/tmp/claude-501/-Users-tpxi676/4657ce01-e8b2-45ba-b9fd-fde7f9d4fde9/scratchpad/automate/repo`; otherwise `gh api repos/umbraco/Umbraco.Automate/contents/Umbraco.Automate.Slack`). Read `~/.claude/references/umbraco-pitfalls.md` before Task 1.

## Global Constraints

- Package ID `Umbraco.Community.Automate.Jira`. Display title "Jira for Umbraco Automate" (or "Jira connector for Umbraco Automate" if Task 5 says so). Never renamed.
- Aliases, permanent once `-beta.1` ships: connection `community.jira.cloud`; authenticator `community.jira.signature`; actions `community.jira.createIssue`, `community.jira.addComment`, `community.jira.transitionIssue`, `community.jira.readWebhookEvent`, `community.jira.findContentByKey`. Settings and output field names freeze at the same point (including `CredentialName` on the connection and the authenticator).
- Dependency ranges, closed both ends:
  - 18 line: `Umbraco.Automate.Core [18.5.1, 19.0.0)`; `Umbraco.Cms.Core` and `Umbraco.Cms.Infrastructure` `[18.3.0, 19.0.0)`.
  - 17 line: `Umbraco.Automate.Core [17.5.1, 18.0.0)`; `Umbraco.Cms.Core` and `Umbraco.Cms.Infrastructure` `[17.8.0, 18.0.0)`.
  - The `0.0.1-alpha` uses Automate `[18.5.1, 19.0.0)` and CMS `[18.3.0, 19.0.0)`.
  - No open-ended range anywhere. CMS floors are a portfolio decision and never lowered. Every Automate.Core release on each line accepts these CMS floors (NuGet nuspecs, 2026-10-30), but none enforces them, hence the direct CMS references.
- Versions: `$(AutomateMajor).$(PackageMinor).$(PackagePatch)`; pre-release `17.0.0-beta.1`/`18.0.0-beta.1`; GA `17.0.0` + `18.0.0`. One tag ships both lines.
- Jira Cloud only, REST API v3, ADF. Never the deprecated `createmeta?expand=` endpoint.
- `SiteUrl` regex `^https://[a-z0-9-]+\.atlassian\.net/?\z` (case-insensitive, `\z` not `$`), no bindings, followed by `Uri` checks (https, no userinfo, default port, path `/`, no query or fragment, one label before `.atlassian.net`). `CloudId` must be a GUID. Gateway host is the constant `https://api.atlassian.com/ex/jira/{cloudId}`. The named client never follows redirects (3xx → `InvalidResponse`).
- **Credentials (F-02 council verdict):** the email, API token and webhook secret live only in config under `Umbraco:Automate:Jira:Credentials:{name}:Email|ApiToken|WebhookSecret`, read only by `JiraCredentialStore`, which implements `credential-helper-contract.md` 1.0.0 exactly (name rule `^[a-z0-9]{1,32}\z` after trim, ASCII check and lower-casing; per-call read; the segment-aware guard of contract §1.4; startup check; errors name the key only). The connection and authenticator hold a non-sensitive `CredentialName`. No `$Umbraco:Automate:Secrets` mode in 1.0. Never hand-roll secret masking. If Task 0 fails, design §7.6 "Fallback" replaces this line (option 2).
- **Secrets never appear** in a log line, run record, error message, step output, export, Deploy payload, the database or `ToString()`. Log only method, host, path template, status, Jira `errorMessages` (each cut to 200 characters), request ID. Never the `Authorization` header, request/response bodies or raw `HttpRequestException` text. The named client has default HTTP logging removed. Errors about credentials name the config key, never a value.
- **Identifiers (F-09):** issue key `^[A-Z][A-Z0-9_]*-[1-9][0-9]*$` (case-insensitive) or numeric; project key `^[A-Z][A-Z0-9_]{1,254}$` or numeric; transition ID numeric. Checked by `JiraIdentifiers` before any URL is built; `Validation` with no HTTP call on failure. JQL values quoted and escaped.
- **Dedupe (F-05, design §6.2 as revised by P3 and P18):** Create issue and Add comment always run a dedupe check. Effective key: `DedupeKey` (trimmed) when set; otherwise `"{AutomationId}:{RunId}:{StepId}:{IterationId or -}:{InputFingerprint}"`, where `IterationId` is the ForEach item identity if Task 3 finds one on `ActionContext`, and `InputFingerprint` is 16 lower-case hex of SHA-256 over the action's resolved identity inputs. The marker is the first 16 lower-case hex of HMAC-SHA256 keyed with the connection ID's bytes. A failed dedupe search fails the step and never POSTs.
- **ADF:** `AdfSanitiser` is an allowlist (design §7.5); Markdown and AdfJson inputs over 64 KB fail with `Validation`; nesting capped at 16; `JsonDocumentOptions.MaxDepth = 32` wherever JSON from outside is parsed.
- Use case 4 is never documented without Request Approval, and its demo automation is seeded disabled.
- Pitfall P2 (Critical): no static fields holding scoped services or mutable state. Caching only through `IMemoryCache`. Enforced by `ArchitectureTests` (Task 7).
- Pitfall S4: unsigned or wrongly signed webhooks never start a run.
- Pitfall S1 (Critical): the MVP ships no controllers. Any later controller derives from `ManagementApiControllerBase`. Enforced by `ArchitectureTests`.
- Pitfalls P7/P11: no network or heavy work at startup or in constructors.
- Pitfalls P3/P4/P9: `findContentByKey` uses one Examine query and one `IContentService.GetByIds` call; no tree traversal.
- Supply chain: central package management, `RestorePackagesWithLockFile` with one lock file per line (`NuGetLockFilePath` = `packages.$(AutomateMajor).lock.json`, because one lock file cannot hold two Automate versions). CI from Task 1a: per line, a **floor** leg (`--locked-mode`, the pinned floor versions) and a **latest** leg (floating `$(AutomateMajor).*` for Automate and CMS, no lock file), so the isolation suite re-runs on every Automate release. Vulnerable-package check and gitleaks on every PR. No org PR-check enrolment: the repo's own CI is the merge boundary (Pascal, 2026-10-30).
- **Private repo until disclosure** (Wave-1 decision, supersedes "public from Task 1"). The repo stays private until Umbraco HQ responds to the private Automate report, agrees to disclosure, or 90 days have passed since it was sent. Then it goes public. `docs/` holds only `design.md`, `plan.md`, `spikes.md` and `runbook.md`. `security-review.md`, `plan-review.md`, council transcripts and the S7 evidence for the core gap never enter the repo; they stay in the private workspace.
- Never retry a POST inside the client. GETs retry once on 429 only when `Retry-After` ≤ 5 s.
- `IJiraClient` stays an interface (Data Center may come later; do not build for it).
- Path segments go through `Uri.EscapeDataString`; never concatenate raw input into a URL.
- Error categories and meanings follow design §7.4 exactly.
- Licence MIT. SourceLink, `snupkg`, deterministic build, README and icon packed. NuGet tags `umbraco umbraco-marketplace umbraco-automate jira atlassian`.
- Claude never runs `git commit`. Every commit is a **Checkpoint (Pascal commits)** step. No AI attribution in commits, PRs, docs or comments.
- Test command for both lines: `./scripts/test.sh` (Task 1), which runs `dotnet test -p:AutomateMajor=<n>` for every line in `eng/lines.txt`. Expected tail on success, once per line: `Passed!  - Failed:     0`.
- Plain UK English in docs and user-facing messages.

## Review Focus

1. **F-01: a Jira user gets content published with no Umbraco approval, or the wrong node is matched.** A later edit drops Request Approval from the seeded recipe, enables it, or a node renamed to an issue key is matched. Expect the seed disabled, every path to Publish Content passing Request Approval and the post-approval `UpdateDate` re-check, and matching by the `jiraKey` property only, proven against a real index. Pinned in Task 17a (`UseCase4Seed_IsDisabled`, `UseCase4Seed_EveryPublishPathPassesApprovalAndRecheck`, `FindContentByKeyIntegrationTests`) and Task 20 (`Readme_UseCase4_WarningPrecedesRecipe`, packed README check).
2. **F-02: a credential reachable from an Automate step, or the guard gone after an Automate upgrade.** Expect the HTTP Request step never to carry the package value, with the isolation controls proving the harness can see a leak, on the floor and latest Automate of each line. Pinned in Task 6a (contract suites C01 to C18 and I01 to I06) and run by Task 1a's CI legs.
3. **F-03: replayed, forged or racing webhooks.** A non-constant-time comparison, a malformed `timestamp` that throws (500, not 401), or two identical deliveries at once both passing. Expect 401 and no run in each case. Pinned in Task 16 (`JiraSignature_ComparesWithFixedTimeEqualsOnly`, `Validate_TimestampOutOfRangeOrWrongType_Returns401NotThrow`, `Validate_ConcurrentSameBody_OnlyOneAccepted`).
4. **F-05: a dedupe key that changes between retries or collides across steps, runs or ForEach items.** Collision is silent data loss (every item returns the first ticket). Expect stable keys across retries and distinct keys otherwise; a failing search posts nothing. Pinned in Task 13 (`DedupeKeyTests`, `Dedupe_SearchFails_NoPost`).
5. **F-04: `AdditionalFieldsJson` smuggling a protected key or unsanitised ADF** through unicode escapes, case variants, duplicate keys, ADF nested in arrays or objects, or a bare `customfield_`. Expect `Validation` with no request, or sanitised ADF. Pinned in Task 12 (`AdditionalFields_BypassShapes_Rejected`, `AdditionalFields_NestedAdf_Sanitised`).

Also pinned, lower down the list: a credential leaking through `ToString()` or exception text (Task 7 `JiraTarget_ToString_ExcludesCredentials`, Task 7a `Exception_FromNetworkError_ExcludesInnerMessage`); non-ASCII webhook bodies (Task 16 `Validate_NonAsciiBody_Passes`, proven against a real delivery from Task 4); truncation splitting a surrogate pair (Task 9, Task 12a); bound keys with stray white space (Tasks 14, 15); site URLs typed the way people type them (Task 6).

---

## File Structure

Repo `~/Projects/Umbraco.Community.Automate.Jira` (all paths below are relative to it):

```
Directory.Build.props        AutomateMajor switch, versions, ranges, constants, package metadata
Directory.Packages.props     central versions (Automate version is a property set per line)
Umbraco.Community.Automate.Jira.slnx
global.json  NuGet.config  LICENSE  README.md  CHANGELOG.md  SECURITY.md  umbraco-marketplace.json
BannedSymbols.txt            Roslyn banned-API list (Task 16: SequenceEqual banned)
.github/dependabot.yml
assets/logo-128.png
eng/lines.txt                lines to build/test ("17\n18", or "18" if Task 0 or Task 2 defers 17)
eng/gitleaks-jira-credentials.toml   sample rule users copy (Task 20)
scripts/test.sh  scripts/pack.sh  scripts/vulnerable-check.sh  scripts/docs-guard.sh  scripts/check-packed-readme.sh
docs/design.md  docs/plan.md  docs/spikes.md  docs/runbook.md      (nothing else, enforced by docs-guard)
spikes/CredentialIsolation/  Task 0 only: echo listener + notes; not packed, deleted after GA
src/Umbraco.Community.Automate.Jira/
  Umbraco.Community.Automate.Jira.csproj
  UmbracoCommunityAutomateJiraSchema.cs           appsettings schema root
  Configuration/  JiraComposer.cs  JiraServiceCollectionExtensions.cs  JiraOptions.cs
  Credentials/    JiraCredentialStore.cs (contract file: store + startup check)  JiraCredentialPart.cs  JiraCredentialSourceCheck.cs
  Connection/     JiraConnectionType.cs  JiraConnectionSettings.cs  JiraSiteUrl.cs  JiraIdentifiers.cs  JiraTarget.cs
  Client/         IJiraClient.cs  JiraClient.cs  JiraRequestException.cs  JiraResponseHandler.cs
                  IssueTypeResolver.cs  Dtos.cs
  Adf/            JiraTextFormat.cs  IAdfConverter.cs  AdfConverterFactory.cs  PlainTextAdfConverter.cs
                  MarkdownAdfConverter.cs  AdfJsonPassthrough.cs  AdfSanitiser.cs  AdfToText.cs  AdfLimits.cs
  Actions/        CreateIssue{Action,Settings,Output}.cs  AddComment{…}.cs  TransitionIssue{…}.cs
                  ReadWebhookEvent{…}.cs  FindContentByKey{…}.cs  DedupeKey.cs  ActionFailures.cs  AdditionalFields.cs
  Webhooks/       JiraSignatureWebhookAuthenticator.cs  JiraSignatureSettings.cs  JiraSignature.cs  WebhookReplayGuard.cs
  buildTransitive/Umbraco.Community.Automate.Jira.props
tests/Umbraco.Community.Automate.Jira.Tests/
  Support/        FakeJiraHandler.cs  CapturingLoggerProvider.cs  TestTargets.cs  TestHost.cs
  Fixtures/       webhooks/*.json  adf/*.golden.json  adf-schema.json  synthetic-allowlist.txt
  CredentialHelperContract/  CredentialStoreContractTests.cs  CredentialIsolationContractTests.cs (verbatim, contract §5)
                             JiraCredentialStoreContractTests.cs (subclasses, contract §5.3)
  (one test file per production file) + ArchitectureTests.cs + SecretLeakTests.cs + FixtureScannerTests.cs
  + UseCase4SeedTests.cs + ReadmeTests.cs
tests/Umbraco.Community.Automate.Jira.IntegrationTests/  real InternalIndex (Task 17a; route from Task 2)
tests/Umbraco.Community.Automate.Jira.ContractTests/     env-gated live tests
demo/Umbraco.Community.Automate.Jira.TestSite/          SQLite, not packed; Seeds/usecase4.automation.json
.github/workflows/ build-test.yml (Task 1a)  pack-release.yml  nightly-contract.yml (Task 19)
```

---

## M0: Spikes and gates

### Task 0: Credential isolation spike S7 (M0 gate 1, go/no-go for Jira and Teams)

Time box: 1 day. Nothing else in this plan starts until this gate is recorded. Jira hosts the helper and the spike for both packages (council "The One Thing to Do First").

**Files:**
- Create: `spikes/CredentialIsolation/Spike.Credentials/` (class library, one file `JiraCredentialStore.cs` written to `credential-helper-contract.md` §1, no tests yet), `spikes/CredentialIsolation/EchoListener/` (minimal ASP.NET app that logs each request's URL and headers to stdout), `spikes/CredentialIsolation/site17/` and `site18/` (`dotnet new umbraco`, SQLite), `docs/spikes.md`
- Create (private workspace, never the repo): `~/Documents/Projects/Umbraco Packages/04-automate-jira/s7-core-gap-evidence.md`
- Not packed. Kept until GA as evidence, then deleted.

**Interfaces:**
- Produces: the gate row (step 8 table) in `docs/spikes.md`, consumed by Tasks 1, 6, 6a, 8, 16, 18, 20 and by the 03 plan.
- Produces: rotation behaviour per provider (JSON with `reloadOnChange`, env var), Deploy/Cloud behaviour, and the list of surfaces checked for council test 10, consumed by Tasks 20 and 20a.
- Produces: the in-process route to Automate's real reference resolver and HTTP Request action (step 7), consumed by Task 6a's isolation suite and written into contract §5.2.

- [ ] **Step 1: Clone and copy docs.** `gh repo clone PascalA94/Umbraco.Community.Automate.Jira ~/Projects/Umbraco.Community.Automate.Jira`, then copy **only** `design.md` and `plan.md` from `~/Documents/Projects/Umbraco Packages/04-automate-jira/` into `docs/`. Never `security-review.md`, `plan-review.md`, `scope.md` or council transcripts. Expected: `ls docs` shows exactly those two files.
- [ ] **Step 2: Two clean sites.** `site17`: `Umbraco.Cms 17.8.0` + `Umbraco.Automate 17.5.1`. `site18`: `Umbraco.Cms 18.3.0` + `Umbraco.Automate 18.5.1`. Both reference `Spike.Credentials`, which registers `JiraCredentialStore` as a singleton and a tiny test-only action `spike.readCredential` that returns `TryGet("spike", ApiToken)` success as a bool (never the value). In each site's `appsettings.Development.json`: `Umbraco:Automate:Secrets:SpikeX = "SECRET-SPIKE-X"` and `Umbraco:Automate:Jira:Credentials:spike:ApiToken = "SECRET-SPIKE-PKG"`. Expected: both sites boot and the backoffice loads.
- [ ] **Step 3: Council test 1 (isolation).** Start `EchoListener` on `http://localhost:5099`. On each site build an automation with manual trigger → HTTP Request to `http://localhost:5099/?q=$Umbraco:Automate:Jira:Credentials:spike:ApiToken` with header `X-Test: $Umbraco:Automate:Jira:Credentials:spike:ApiToken`, and a second step with `$Umbraco:Automate:Secrets:SpikeX` in the same places. Run it. Expected: the listener shows `SECRET-SPIKE-X` (the known gap) and **never** `SECRET-SPIKE-PKG`; the package reference arrives literal or the step fails "not permitted". `spike.readCredential` returns true. Record in `docs/spikes.md` only the package-prefix result ("arrived literal" or "not permitted"). The listener output showing the `Secrets` value goes to the private `s7-core-gap-evidence.md`, because the repo goes public at Task 1 and the core gap is under private disclosure.
- [ ] **Step 4: Council test 2 (guard).** Add `"Umbraco:Automate"` to `Umbraco:Automate:AllowedConfigurationKeyPrefixes` (keeping the defaults) and restart. Expected: the HTTP Request step now **does** leak `SECRET-SPIKE-PKG` (proves the allow-list is the barrier) **and** `spike.readCredential` returns false with an error naming `Umbraco:Automate`. Remove the entry.
- [ ] **Step 5: Council test 6 (rotation).** Change `…:spike:ApiToken` in `appsettings.Development.json` while the site runs; run `spike.readCredential` with a log of the value's SHA-256 prefix (8 hex, never the value). Then set `Umbraco__Automate__Jira__Credentials__spike__ApiToken` as an env var, restart, change it without restart. Expected: JSON change picked up without restart; env var needs a restart. Record both.
- [ ] **Step 6: Council test 5, per-environment resolution.** On Pascal's Umbraco Cloud sandbox (Deploy enabled), deploy `site18` with `Spike.Credentials` to two environments, set `Umbraco__Automate__Jira__Credentials__spike__ApiToken` as a Cloud secret with a different value in each, and run `spike.readCredential` with the SHA-256-prefix log in both. Expected: each environment resolves its own value. **Owner: Pascal. Deadline: before Task 20a starts** (end of M4). If no Cloud sandbox is available on the spike day, record UNKNOWN here; Task 20a closes it, and Task 21 may not start while it is UNKNOWN. Task 20a also proves the Deploy round-trip of a real connection and authenticator (council test 4).
- [ ] **Step 7: Council test 10, other surfaces, and the resolver route.** On both sites, run Settings > Health checks (all), open Settings > Log viewer, the Automate section's health and run views, and every Management API endpoint listed by Swagger in Development. Expected: no value from `Umbraco:Automate:Jira:Credentials` appears. Then confirm with ILSpy that the 17.5.1 and 18.5.1 `Umbraco.Automate.Core` assemblies contain `ConfigurationReferenceResolver` reading `AllowedConfigurationKeyPrefixes` in its constructor and no other type taking `IConfiguration` that serves step settings (on `v18/dev` at `5318996` the other `IConfiguration` users are persistence and options setup only). Record, for each line, which route builds the real resolver and the built-in HTTP Request action in a test process, trying in order: Automate's own service registration in a `ServiceCollection`; the `Umbraco.Automate.Testing` host; reflection on the internal type. Expected: one route named per line in `docs/spikes.md`. This list of surfaces is re-run by Task 21 step 1 and the runbook's upgrade checklist (Task 20).
- [ ] **Step 8: Gate.** Apply the first matching row. Steps 3 and 4 are the deciding evidence; step 7's surfaces count as step 3 failures if they show the package value. Record the row, date and evidence in `docs/spikes.md`.

  | Outcome | Jira | Teams (03) |
  |---|---|---|
  | Both lines pass | Go: package-owned credentials; continue with Task 1. | Go: copy the contract once frozen (Jira Task 6a). |
  | 18 passes, 17 fails | Go on 18 only: `eng/lines.txt` = `18`; the 17 line waits for an Automate 17.x release that passes steps 3 and 4 (same rule as Task 2's fallback). Never option 2 on one line only. | Same: 18 only. |
  | 18 fails (17 either way) | No-go: design §7.6 "Fallback" (option 2 with the Steward's conditions): skip Task 6a, restore `Email` and `ApiToken` (`IsSensitive`, `$Umbraco:Automate:Secrets` references) on the connection, use `SigningSecret` (`IsSensitive`, never `Secret`, F-15) on the authenticator, and add the Steward tests (design §7.6) to Task 18. | Option 2 per its own design's fallback. |
  | Steps 3 and 4 pass on both, step 5 or 6 UNKNOWN | Go, with the UNKNOWN carried by name to Task 20a (owner Pascal); the README rotation section says "not verified" until closed. | Go, same residual. |
  | Time box expired before steps 3 and 4 finished on a line | Treat the unfinished line as failed (fail closed) and apply the matching row above, unless Pascal extends the time box once, by one day at most, in writing in `docs/spikes.md`. | Follows Jira's row. |

- [ ] **Step 9 (Pascal):** record the gate row in the portfolio `README.md` "Security-review decisions", and add a one-line "S7 gate result" (row, date, link to `docs/spikes.md`) to the top of `03-automate-teams/plan.md` so their owners act on it. (The private HQ report is dropped: Pascal decided not to report, 2026-11-04.); send `s7-core-gap-evidence.md` as a follow-up if it adds anything.
- [ ] **Checkpoint (Pascal commits):** `git add spikes docs` / suggested message `docs: record credential isolation spike S7`

### Task 1: Scaffold the repo and prove the build switch (spike S0, go/no-go)

**Files:**
- Create: everything in the tree above down to empty folders, plus `src/.../Actions/NoOpSpikeAction.cs` and `src/.../Webhooks/JiraSignatureWebhookAuthenticator.cs` (stub returning `false`)
- Create: `tests/.../BuildSwitchTests.cs`, `docs/spikes.md`

**Interfaces:**
- Produces: `AutomateMajor` MSBuild property; constants `AUTOMATE_17` / `AUTOMATE_18`; `scripts/test.sh`; `eng/lines.txt`; `JiraComposer : IComposer` registering the authenticator with `builder.AutomateWebhookAuthenticators().Add<JiraSignatureWebhookAuthenticator>()`.

- [ ] **Step 1: Confirm Task 0 is recorded as go (or the fallback is chosen)** in `docs/spikes.md`. The repo is already cloned by Task 0.
- [ ] **Step 2: Write `Directory.Build.props`** mirroring Slack's (metadata, SourceLink, `snupkg`, nullable, docs file) with owner Pascal Meyer, MIT, plus: `AutomateMajor` default `18`; `AutomateVersion` `18.5.1` / `17.5.1`; `AutomateRange` `[18.5.1, 19.0.0)` / `[17.5.1, 18.0.0)`; `UmbracoCmsVersion` `18.3.0` / `17.8.0`; `UmbracoCmsRange` `[18.3.0, 19.0.0)` / `[17.8.0, 18.0.0)`; `VersionPrefix` `$(AutomateMajor).$(PackageMinor).$(PackagePatch)` with `PackageMinor`/`PackagePatch` default `0`; `DefineConstants` `AUTOMATE_$(AutomateMajor)`; `Deterministic` true; `RestorePackagesWithLockFile` true; `NuGetLockFilePath` `packages.$(AutomateMajor).lock.json`; `AutomateFloat` default `false`, and when `true` the Automate and CMS references use `$(AutomateMajor).*` instead of the ranges (the CI latest leg, Task 1a). In the csproj, reference `Umbraco.Automate.Core` with `Version="$(AutomateRange)"` and `Umbraco.Cms.Core` + `Umbraco.Cms.Infrastructure` with `Version="$(UmbracoCmsRange)"`, and keep Slack's `UseProjectReferences` toggle (default `false` here). Central package management pins `Markdig` `[1.5.1, 2.0.0)`.
- [ ] **Step 3: Write the failing test** `BuildSwitchTests.Constant_MatchesReferencedAutomateMajor`: asserts `typeof(Umbraco.Automate.Core.Actions.ActionBase<,>).Assembly.GetName().Version!.Major` equals 17 under `#if AUTOMATE_17` and 18 under `#if AUTOMATE_18`.
- [ ] **Step 4: Run** `dotnet test -p:AutomateMajor=18` then `-p:AutomateMajor=17`. Expected: compile fails until the test project references `Umbraco.Automate.Testing` at the same range.
- [ ] **Step 5: Add the no-op action and authenticator stub**, `scripts/test.sh` (loops `eng/lines.txt`, runs `dotnet test -p:AutomateMajor=$n`, exits non-zero on any failure), `eng/lines.txt` = `17` and `18`.
- [ ] **Step 6: Run** `./scripts/test.sh`. Expected: `Passed!  - Failed:     0` twice, and `dotnet pack -p:AutomateMajor=17 -o out` / `=18` produce `Umbraco.Community.Automate.Jira.17.0.0.nupkg` and `.18.0.0.nupkg`; `unzip -p out/*.18.0.0.nupkg '*.nuspec' | grep -E 'Umbraco\.(Automate|Cms)\.'` shows `Umbraco.Automate.Core` `version="[18.5.1, 19.0.0)"` and `Umbraco.Cms.Core`/`Umbraco.Cms.Infrastructure` `version="[18.3.0, 19.0.0)"`; the 17 package shows `[17.5.1, 18.0.0)` and `[17.8.0, 18.0.0)`.
- [ ] **Step 7: Gate S0 (time box 1 day).** Record in `docs/spikes.md`: the TFM for each line, the files that needed `#if`. **No-go** if more than ~3 files need `#if` or the two lines need TFMs one test project cannot cover. **Fallback (builder takes it without waiting):** `main` = 18 only, create a `v17` branch, cherry-pick fixes, set `eng/lines.txt` to `18` on `main`, and write why in README. All later tasks are unchanged.
- [ ] **Step 8: Pre-public check.** `ls docs` shows only `design.md`, `plan.md`, `spikes.md`. `git log --all --name-only --format= | sort -u | grep -iE "security-review|plan-review|council|scope\.md|s7-core-gap"` returns nothing. `grep -rn "SECRET-SPIKE-X" docs` returns nothing. `ls packages.17.lock.json packages.18.lock.json` (per project) shows both.
- [ ] **Checkpoint (Pascal commits):** `git add -A` / suggested message `chore: scaffold package with AutomateMajor build switch`
- [ ] **Step 9 (Pascal): make the repo public** (the disclosure rule was dropped on 2026-11-03): `gh repo edit PascalA94/Umbraco.Community.Automate.Jira --visibility public --accept-visibility-change-consequences`. Then enable CodeQL default setup (C#) and secret scanning with push protection in the repo settings. Expected: `gh repo view --json visibility -q .visibility` prints `PUBLIC`.

### Task 1a: CI from day one (build-test, vulnerable check, gitleaks, docs guard)

Moved forward from Task 19 (plan review P12) so M1 to M3 run under CI, as the security review requires for M1. The release and nightly workflows stay in Task 19.

**Files:** Create `.github/workflows/build-test.yml`, `.github/dependabot.yml`, `scripts/vulnerable-check.sh`, `scripts/docs-guard.sh`.

**Interfaces:**
- Consumes: `eng/lines.txt`, `AutomateMajor`, `AutomateFloat`, the per-line lock files (Task 1).
- Produces: required status checks named `build-test (<line>, floor)`, `build-test (<line>, latest)`, `gitleaks`, `docs-guard`. Every later task's tests run in them automatically, including Task 6a's isolation suite on both legs.

- [ ] **Step 1: Write `scripts/vulnerable-check.sh <line>`:** runs `dotnet list package --vulnerable --include-transitive -p:AutomateMajor=<line>` and exits 1 when the output contains `has the following vulnerable packages` (the command exits 0 with CVEs present). Test it locally: `./scripts/vulnerable-check.sh 18; echo $?`. Expected: `0`, or `1` with the package named (then raise the transitive pin in `Directory.Packages.props` before continuing).
- [ ] **Step 2: Write `scripts/docs-guard.sh`:** fails if `git ls-files docs` lists anything outside `docs/design.md`, `docs/plan.md`, `docs/spikes.md`, `docs/runbook.md`, or if any tracked file name matches `security-review|plan-review|council-transcript|s7-core-gap`. Test: `touch docs/security-review.md && git add -N docs/security-review.md && ./scripts/docs-guard.sh; echo $?` → `1`; undo, re-run → `0`.
- [ ] **Step 3: Write `build-test.yml`.** Triggers: `pull_request`, `push` to `main`, and a daily `schedule` (so a new Automate release is tested without a PR). Jobs:
  - `build-test`: matrix `line` from `eng/lines.txt` × `leg` in `floor`, `latest`. **Floor:** `dotnet restore --locked-mode -p:AutomateMajor=${{ matrix.line }}`, then `dotnet test --no-restore -p:AutomateMajor=${{ matrix.line }}`, then `scripts/vulnerable-check.sh ${{ matrix.line }}`. **Latest:** `dotnet restore --force-evaluate -p:AutomateMajor=${{ matrix.line }} -p:AutomateFloat=true -p:RestorePackagesWithLockFile=false`, then the same test and vulnerable steps, then print the resolved `Umbraco.Automate.Core` version with `dotnet list package -p:AutomateMajor=${{ matrix.line }} -p:AutomateFloat=true | grep Umbraco.Automate.Core`. `--locked-mode` is used on the floor leg only (a floating version cannot be locked).
  - `gitleaks`: full-history checkout (`fetch-depth: 0`), the gitleaks CLI at a pinned release verified by its published SHA-256 checksum, `gitleaks detect --redact --exit-code 1` (a personal-account public repo, so no action licence question).
  - `docs-guard`: runs `scripts/docs-guard.sh`.
  - All actions pinned to commit SHAs; `permissions: contents: read`.
- [ ] **Step 4: Write `dependabot.yml`:** NuGet and GitHub Actions, weekly.
- [ ] **Step 5: Verify** by pushing a branch and opening a draft PR. Expected: one green `build-test` job per line and leg, the vulnerable step passing in each, green `gitleaks` and `docs-guard`. Record the run URL and each leg's resolved Automate version in `docs/spikes.md`. This run is the M1 CI run that resolves the security review's UNKNOWN on transitive CVEs (F-16).
- [ ] **Step 6 (Pascal):** add a branch ruleset on `main`: require a PR, require the four checks above, block force pushes. This replaces org PR-check enrolment (Pascal, 2026-10-30).
- [ ] **Checkpoint (Pascal commits):** `git add .github scripts docs/spikes.md` / suggested message `ci: build and test both lines on floor and latest Automate, with CVE, secret and docs checks`

### Task 2: Spike S1, secret features on Automate 17.5.1 (go/no-go for the 17 line)

**Files:** Create `tests/.../Spikes/SecretFeatureSpikeTests.cs` (kept as regression tests), modify `docs/spikes.md`, maybe `eng/lines.txt`.

**Interfaces:** Produces the decision "17 line ships at 1.0: yes/no" and "an action may omit `ConnectionTypeAlias`: yes/no", both recorded in `docs/spikes.md` and consumed by Tasks 3, 17, 17a and 21. Produces the integration-test route for Task 17a (step 3a). (Secret-reference behaviour is Task 0's job now; this task checks only what the package still relies on.)

- [ ] **Step 1: Write tests** that run under both lines:
  - `EditableModelField_HasIsSensitive`: reflection finds a writable `IsSensitive` bool on the `Field`/`EditableModelFieldAttribute`.
  - `WebhookAuthenticatorBase_HasCredentialHeaderNames`: `typeof(WebhookAuthenticatorBase<>)` has a virtual `CredentialHeaderNames`.
  - `ActionAttribute_ConnectionTypeAlias_IsOptional`: `new ActionAttribute("x","y")` compiles and `ConnectionTypeAlias` is null.
- [ ] **Step 2: Run** `./scripts/test.sh`. Record which pass per line.
- [ ] **Step 3: Create the demo site** `demo/…TestSite`: `dotnet new umbraco` on SQLite, CMS 17.8.0 or 18.3.0 per line, package project reference. Expected: boots on both lines. (Only needed for later manual checks; if Task 0 fell back to option 2, also repeat the original check: a sensitive field holding `$Umbraco:Automate:Secrets:SpikeToken` is stored as the reference only, masked in run logs and stripped on export.)
- [ ] **Step 3a: Integration-test route (plan review P2).** Check whether `Umbraco.Cms.Tests.Integration` is published on NuGet at `17.8.0` and `18.3.0` and restores into a fresh xUnit project under each `AutomateMajor` (`dotnet add package Umbraco.Cms.Tests.Integration --version <v>` then `dotnet build`). Expected: record "consumable: yes/no" per line. **If no on a line**, Task 17a uses the replacement route on that line: `tests/Umbraco.Community.Automate.Jira.IntegrationTests` boots `demo/…TestSite` in-process with `WebApplicationFactory<Program>` on a temporary SQLite file, with Examine indexing synchronously, and seeds content through `IContentService`. Name the chosen route per line in `docs/spikes.md`.
- [ ] **Step 4: Gate.** All three reflection tests pass on 17.5.1 → keep `eng/lines.txt` as `17 18`. **Any fails → Pascal's resolved fallback:** set `eng/lines.txt` to `18`, ship 18 first, add 17 when an Automate 17.x NuGet release has the features. Never hand-roll masking. Record the outcome and the `ConnectionTypeAlias` answer in `docs/spikes.md`.
- [ ] **Checkpoint (Pascal commits):** `git add tests docs eng demo` / suggested message `test: spike S1 secret features per Automate line`

### Task 3: Spikes S2, S5, S6 on the demo site

**Files:** Modify `docs/spikes.md`.

**Interfaces:** Produces: whether `trigger.body.*` binds; whether `context.Body` equals the raw bytes for non-ASCII, BOM and invalid-UTF-8 bodies; how the authenticator obtains `IMemoryCache`, `TimeProvider` and `JiraCredentialStore` (constructor injection or `RequestServices`), and which route values, headers and IDs `WebhookAuthenticationContext` exposes, consumed by Task 16; the ForEach iteration identity on `ActionContext`, consumed by Task 13; Automate's default retry delays, consumed by Task 4 step 6a; the automation export JSON shape, consumed by Task 17a; the Forms "form submitted" trigger alias per line; S6 re-checked on the 17 line.

- [ ] **Step 1: S2.** Built-in Webhook trigger (plain-secret auth) → a log step bound to `${ trigger.body.issue.key }`. POST a Jira-shaped JSON with `curl`. Expected per design: does not bind. Then confirm `${ trigger.headers }` contents. At `5318996` `WebhookTriggerOutput` has `Headers`; check whether `X-Hub-Signature` is listed when declared in `CredentialHeaderNames`.
- [ ] **Step 2: S2 encoding check.** Temporarily make the stub authenticator log `Encoding.UTF8.GetBytes(context.Body)` length and the request `Content-Length` for three bodies: one with `é` and an emoji, one starting with a UTF-8 BOM, one with an invalid byte sequence. Expected: equal for the first. If any differ, Task 16 reads the buffered `Request.Body` bytes instead of `Body`. Remove the logging after.
- [ ] **Step 3: Authenticator dependencies.** Give the stub a constructor taking `IMemoryCache` and `TimeProvider`; register it as in `JiraComposer`. Expected: it is constructed and a request reaches `Validate`. If construction fails, record "use `context.Request.HttpContext.RequestServices`" for Task 16. Also log, once, the request's route values (`ctx.Request.RouteValues` keys and values), `ctx.Request.Path`, and every header name (names only) that reaches `Validate`. Record them. Expected: an `automationId` (or equivalent automation or trigger ID) route value. **Fallback key if none (plan review P16):** the replay key uses `SHA-256(lower-cased ctx.Request.Path)` in place of the automation ID, because each webhook URL is unique to its automation. Record which one Task 16 uses.
- [ ] **Step 3a: ForEach identity (plan review P3).** Read `ActionContext` (ILSpy on both lines, and `v18/dev` at `5318996`) and run a ForEach over three items with a stub action that logs every `ActionContext` property name and value. Record: whether any property identifies the iteration or item (index, item key, iteration ID), and whether `StepId` differs per iteration. Expected: one of "iteration ID = <property>" or "none". Task 13 includes it in the default dedupe key when present.
- [ ] **Step 3b: Retry delays and export shape.** Record Automate's default step retry delays (first and second retry, from `StepConfiguration` or the retry policy source) for Task 4 step 6a. Export one automation with a Condition branch and a Request Approval step from the backoffice and record the JSON shape of steps, branches and action aliases in `docs/spikes.md` (Task 17a's seed and seed test use it).
- [ ] **Step 4: S5.** Install Umbraco Forms for each line on the demo site and record the "form submitted" trigger alias from the backoffice trigger picker.
- [ ] **Step 5: S6 on the 17 line only.** v18 is already answered: no action-level retry opt-out, terminal categories are `Validation`, `ConfigurationError`, `Authentication`, `Cancelled` (security review F-05). Confirm the same on 17.5.1 by reading `StepConfiguration`, `DefaultStepErrorClassifier` and `ActionAttribute` in ILSpy. Record. Task 13 is in 1.0 regardless (Pascal, 2026-10-30).
- [ ] **Checkpoint (Pascal commits):** `git add docs/spikes.md` / suggested message `docs: record spikes S2, S5, S6`

### Task 4: Spikes S3, S4 against a sandbox Jira (go/no-go for scoped tokens)

**Files:** Create `scripts/spike-jira.sh` (curl calls, token read from env `JIRA_SPIKE_TOKEN`, never echoed), `tests/.../Fixtures/adf-schema.json`, `tests/.../Fixtures/webhooks/*.json`, `tests/.../Fixtures/synthetic-allowlist.txt`, `tests/.../FixtureScannerTests.cs`; modify `docs/spikes.md`.

**Interfaces:** Produces: `CloudId` required yes/no; the exact scope list (including `search/jql` and comment listing); whether `/mypermissions` needs an extra scope (decides the admin warning in Task 8); whether `/_edge/tenant_info` returns `cloudId`; `AdfLimits.MaxTextLength` (default `32767`); the dedupe marker (`property` or `label`) for Task 13; whether comment `properties` work (decides Task 14's strategy); search lag after create; the webhook `timestamp` unit and whether retries reuse the body (Task 16); scrubbed real payloads for Tasks 16 and 17.

- [ ] **Step 1:** Pascal creates a free Jira Cloud site, project key `SBX`, a service account, one scoped and one classic token. Tokens stay in env vars.
- [ ] **Step 2: S3.** For each token, call `GET /rest/api/3/myself`, `GET /issue/createmeta/SBX/issuetypes`, `POST /search/jql`, `POST /issue`, `GET /issue/{key}/comment?expand=properties`, `POST /issue/{key}/comment`, `GET /issue/{key}/transitions`, `POST /issue/{key}/transitions` and `GET /mypermissions?permissions=ADMINISTER,ADMINISTER_PROJECTS,SYSTEM_ADMIN` on both `https://<site>.atlassian.net` and `https://api.atlassian.com/ex/jira/{cloudId}`. Record status codes and the minimum scopes, and separately whether `/mypermissions` needs a scope the other calls don't. Record `GET https://<site>.atlassian.net/_edge/tenant_info` (sent with no `Authorization`).
- [ ] **Step 3: S4.** POST a 40,000-char description and an ADF doc with `extension`, `inlineCard` and `mention` nodes; record the actual cap and Jira's response. Download Atlassian's published ADF JSON schema into `Fixtures/adf-schema.json` with its source URL and date in `docs/spikes.md`.
- [ ] **Step 4: Dedupe probes.** (a) `POST /issue` with `properties: [{"key":"community.jira.dedupe","value":{"key":"x"}}]` in the body, then `POST /search/jql` with `issue.property[community.jira.dedupe].key = "x"`; record whether it matches. (b) `POST /issue` with label `cj-dedupe-x`, then search `labels = "cj-dedupe-x"`; poll every second and record how long until it matches (search lag). (c) `POST /issue/{key}/comment` with a `properties` array, then `GET …/comment?expand=properties`; record whether the property comes back. Marker for Task 13: property if (a) matches, else label. Task 14 strategy: read-first if (c) works, else the terminal fallback.
- [ ] **Step 5: Capture webhooks.** Register a Jira admin webhook to a tunnel on the demo site; capture `jira:issue_created`, `jira:issue_updated` with a status change, `jira:issue_updated` with only a comment (no status change), `comment_created`, `comment_updated` (edit that comment), with their `X-Hub-Signature` and `X-Atlassian-Webhook-Identifier`. Make the tunnel return 500 once and capture Jira's retry. Record: the `timestamp` unit, and whether the retry's body (including `timestamp`) is byte-identical. Scrub names, emails and account IDs (synthetic replacements only), then save under `Fixtures/webhooks/`. Keep one body + signature + test secret (32+ characters) triple as `issue_updated.signed.json` with only fake data in it, for Task 16.
- [ ] **Step 5a: Fixture scanner (plan review P9, F-06).** Write `FixtureScannerTests.Fixtures_ContainOnlySyntheticPersonalData`: walk every file under `Fixtures/`, extract emails (`[^\s"@]+@[^\s"@]+\.[^\s"@]+`), Atlassian account IDs (`\b[0-9a-f]{24}\b` and `\b\d{6}:[0-9a-f-]{36}\b`) and `displayName` values; fail on any value not listed in `Fixtures/synthetic-allowlist.txt` (one value per line, all invented, e.g. `ann.example@example.invalid`, `Ann Example`), naming the file and the value's line number only. Run `dotnet test --filter FixtureScannerTests`. Expected: PASS after scrubbing; add a real-looking email to a scratch fixture and it FAILs; remove it.
- [ ] **Step 6: Gate S3.** Scoped tokens work for all calls (on either route) → `CloudId` rules as design §6.1. **Scoped fails → Pascal's resolved fallback:** classic API token on the same service account; README says to switch to scoped when it works. Record the route that works and the scope list.
- [ ] **Step 6a: Search-lag rule (plan review P17).** Compare step 4(b)'s worst observed lag with Automate's first retry delay from Task 3 step 3b. **If the lag is longer**, a retry can still duplicate: record the residual (both numbers) in `docs/spikes.md` and add it to Task 20's README list under the dedupe behaviour ("a retry within N seconds of a timeout can create a duplicate"). Otherwise record "lag below first retry delay". Expected: one of the two lines in `docs/spikes.md`.
- [ ] **Checkpoint (Pascal commits):** `git add scripts/spike-jira.sh tests docs/spikes.md` / suggested message `docs: record spikes S3, S4 and capture Jira fixtures`

### Task 5: Trademark check and alpha reservation (Pascal gate)

**Files:** Modify `docs/spikes.md`, `README.md` (placeholder text), csproj `Description`.

- [ ] **Step 1 (Pascal, 15 minutes):** read Atlassian's trademark guidance. Decide the display title: "Jira for Umbraco Automate" if allowed, else "Jira connector for Umbraco Automate" with "not affiliated with Atlassian" and no logo. Keep the ID either way. Record in `docs/spikes.md`.
- [ ] **Step 2:** set `Description` to "Placeholder, not functional. Reserves the package ID." Remove `NoOpSpikeAction` from the packed build so the alpha has no aliases.
- [ ] **Step 3: Pack** `dotnet pack -p:AutomateMajor=18 -p:VersionPrefix=0.0.1 -p:VersionSuffix=alpha -o out`. Expected: `Umbraco.Community.Automate.Jira.0.0.1-alpha.nupkg`, nuspec ranges Automate `[18.5.1, 19.0.0)` and CMS `[18.3.0, 19.0.0)`.
- [ ] **Step 4 (Pascal):** create a NuGet API key scoped to `Umbraco.Community.Automate.Jira` only, push scope, expiry 90 days or less (F-14). Push, then unlist on nuget.org. Expected: package page shows "unlisted".
- [ ] **Checkpoint (Pascal commits):** `git add -A` / suggested message `chore: reserve package ID with 0.0.1-alpha`

---

## M1: Connection, client, ADF, Create issue

### Task 6: Site URL, CloudId and identifier validation

**Files:** Create `Connection/JiraSiteUrl.cs`, `Connection/JiraIdentifiers.cs`, `Connection/JiraConnectionSettings.cs`; Test `JiraSiteUrlTests.cs`, `JiraIdentifiersTests.cs`.

**Interfaces:**
- Produces: `JiraConnectionSettings { string SiteUrl; string CredentialName; string? CloudId }` with `[Field]` attributes per design §6.1 (all three: no bindings, not sensitive). (If Task 0 was no-go: `{ SiteUrl; Email; ApiToken (IsSensitive); CloudId }` instead.)
- Produces: `static bool JiraSiteUrl.TryNormalise(string? input, out Uri? siteUri, out string? error)` and `static bool JiraSiteUrl.TryParseCloudId(string? input, out Guid? cloudId, out string? error)` (blank → true with `null`).
- Produces: `static class JiraIdentifiers` with `bool TryIssueKeyOrId(string? input, out string? value, out string? error)`, `bool TryIssueKey(string? input, out string? value, out string? error)` (keys only, upper-cased), `bool TryProjectKeyOrId(…)`, `bool IsNumericId(string? input)`, `string QuoteJql(string value)`. All trim first; regexes from Global Constraints with `\z`.

- [ ] **Step 1: Write failing theory tests.** Accept and normalise to `https://acme.atlassian.net`: `https://acme.atlassian.net`, `https://acme.atlassian.net/`, `HTTPS://ACME.Atlassian.net`, `"  https://acme.atlassian.net  "`. Reject: `http://acme.atlassian.net`, `https://acme.atlassian.net/jira`, `https://evil.com/.atlassian.net`, `https://user@acme.atlassian.net`, `https://acme.atlassian.net.evil.com`, `https://acme.atlassian.net:8443`, `https://a_b.atlassian.net`, `https://acme.atlassian.net/?x=1`, `https://acme.atlassian.net/#f`, `https://a.b.atlassian.net`, `""`, null. `SiteUrl_TrailingNewlineWithoutTrim_Rejected`: call the regex-plus-`Uri` validator directly (bypassing trim) with `"https://acme.atlassian.net\n"` → rejected. CloudId: accepts a GUID with surrounding spaces; rejects `../x`, `abc`.
- [ ] **Step 2: Write failing identifier tests.** `IssueKey_DotSegments_Rejected` (`.`, `..`, `SUP-1/..`, `SUP-1?x`, `SUP-0`, `-1`); accepts `sup-17` → `SUP-17`, `10042`. `ProjectKey_Invalid_Rejected` (`A/B`, `1A`, `..`); accepts `SUP`, `10000`. `QuoteJql_EscapesQuoteAndBackslash` (`a"b\c` → `"a\"b\\c"`).
- [ ] **Step 3: Run** `dotnet test --filter "JiraSiteUrlTests|JiraIdentifiersTests"`. Expected: FAIL (types missing).
- [ ] **Step 4: Implement.** Site URL: trim, regex, `Uri.TryCreate`, the asserts in Global Constraints, then lower-case and strip the trailing slash.
- [ ] **Step 5: Run** `./scripts/test.sh`. Expected: `Failed: 0` per line.
- [ ] **Checkpoint (Pascal commits):** `git add src/*/Connection tests` / suggested message `feat: validate Jira site URL, cloud ID and identifiers`

### Task 6a: Package-owned credential store (`JiraCredentialStore`) to the shared contract

Skip if Task 0 was no-go. Council conditions 1, 2 and 3; council tests 1, 2, 3, 6 and 7. **Per-task review** (portfolio "Build approach"): `qa-reviewer` and `security-analyst`, fresh context, before the checkpoint, because Teams copy this task's output verbatim.

The contract is `/Users/tpxi676/Documents/Projects/Umbraco Packages/credential-helper-contract.md` (version 1.0.0). It fixes the API, the name rule, the guard, the startup check, the error texts and the two generic test suites. This task implements it for Jira, runs both suites, and freezes it.

**Files:** Create `Credentials/JiraCredentialStore.cs` (the contract §1.1 file: `ICredentialStore<TPart>`, the store and `JiraCredentialStoreStartupCheck`; port from `spikes/CredentialIsolation`), `Credentials/JiraCredentialPart.cs`, `Configuration/JiraServiceCollectionExtensions.cs`; modify `Configuration/JiraComposer.cs` and the test csproj (embedded store source, contract §4 item 6); Test `CredentialHelperContract/CredentialStoreContractTests.cs`, `CredentialHelperContract/CredentialIsolationContractTests.cs`, `CredentialHelperContract/JiraCredentialStoreContractTests.cs`.

**Interfaces:**
- Consumes: Task 0 step 7's resolver route; `AutomateOptions` from `Umbraco.Automate.Core`.
- Produces: `enum JiraCredentialPart { Email, ApiToken, WebhookSecret }`.
- Produces: `internal sealed class JiraCredentialStore(IConfiguration configuration, IOptionsMonitor<AutomateOptions> automateOptions) : ICredentialStore<JiraCredentialPart>`, registered singleton, with `public const string Prefix = "Umbraco:Automate:Jira:Credentials"` and the contract §1.1 members: `bool TryNormaliseName(string? name, out string? normalised, out string? error)`, `bool MatchesNameRule(string candidate)`, `bool TryGetExposingPrefix(out string? entry)`, `bool TryGet(string? name, JiraCredentialPart part, out string? value, out string? error)`, `string KeyFor(string normalisedName, JiraCredentialPart part)`. Behaviour and error texts exactly as contract §1.2 to §1.6.
- Produces: `JiraCredentialStoreStartupCheck : INotificationHandler<UmbracoApplicationStartedNotification>` (contract §1.5).
- Produces: `static IServiceCollection JiraServiceCollectionExtensions.AddJiraCore(this IServiceCollection services, IConfiguration configuration)`, which `JiraComposer` calls. It holds every service registration except the authenticator and notification handler (those need `IUmbracoBuilder`), so tests compose the real services through it (Tasks 7, 7a, 18). This task registers the store in it.
- Produces (test support, in the verbatim contract files): `RecordingConfiguration`, `TestOptionsMonitor<T>`, `CapturingLogger`, `AutomateResolverHost`, `ContractCapturingHandler`.

- [ ] **Step 1: Copy the contract suites into the test project.** Write `CredentialStoreContractTests.cs` (tests C01 to C18, contract §5.1) and `CredentialIsolationContractTests.cs` (I01 to I06, §5.2), each starting with `// credential-helper-contract: 1.0.0`, with `CanonicalHash = "PENDING-JIRA-TASK-6A"` for now. Write `AutomateResolverHost` using the route recorded in Task 0 step 7 (per line, `#if AUTOMATE_17` only if the route differs). Write the two subclasses in `JiraCredentialStoreContractTests.cs` exactly as contract §5.3 (`SamplePart` = `JiraCredentialPart.Email`).
- [ ] **Step 2: Run** `dotnet test --filter "FullyQualifiedName~CredentialHelperContract"`. Expected: compile failure (store missing).
- [ ] **Step 3: Implement** the store and startup check to contract §1 and the enum. Register in `AddJiraCore` and `JiraComposer` exactly as contract §1.1. No caching; no statics beyond the constant and the `[GeneratedRegex]` method (pitfall P2).
- [ ] **Step 4: Run** `dotnet test --filter "FullyQualifiedName~CredentialHelperContract"`. Expected: every C and I test passes except C17 (hash still pending). The isolation controls I04 and I05 must **pass**, which proves the harness can see a leak; if either fails, fix the harness, never the assertion.
- [ ] **Step 5: Mutation check of the controls.** Temporarily add `Umbraco:Automate:Jira` to the test's default allow-list in `AutomateResolverHost` and run the isolation suite. Expected: I03 FAILs (leak detected). Revert. Temporarily change the regex anchor to `$`. Expected: C05 FAILs. Revert.
- [ ] **Step 6: Run** `./scripts/test.sh`. Expected: `Failed: 0` per line except C17. Push the branch: the Task 1a CI floor and latest legs run both suites on each line.
- [ ] **Step 7: Per-task review.** Dispatch `qa-reviewer` and `security-analyst` (fresh context) with the contract, the store file and both suites. Fix findings; a Security Critical is never argued down.
- [ ] **Step 8: Freeze the contract.** Compute the canonical hash with the contract §3 algorithm (C17's failure message prints it). Put it in `CanonicalHash` in `CredentialStoreContractTests.cs`, then in `credential-helper-contract.md` (the "Canonical hash" line), set the status line to **Frozen**, and write the chosen resolver route into contract §5.2. Re-run `./scripts/test.sh`. Expected: `Failed: 0` per line with C17 passing.
- [ ] **Checkpoint (Pascal commits):** `git add src tests` / suggested message `feat: add package-owned credential store to credential-helper-contract 1.0.0`
- [ ] **Step 9 (Pascal):** tag the commit `credential-helper-1.0.0` and push the tag. Tell the 03 owner the contract is frozen.

### Task 7: `JiraTarget`, options, named client composition and test host

Split from the old Task 7 (plan review P21). This task fixes what every request is built from and how the named client is composed; Task 7a adds the requests.

**Files:** Create `Connection/JiraTarget.cs`, `Configuration/JiraOptions.cs`, `Client/IJiraClient.cs` (declaration only); modify `Configuration/JiraServiceCollectionExtensions.cs`, `Configuration/JiraComposer.cs`; Test `JiraTargetTests.cs`, `JiraCompositionTests.cs`, `ArchitectureTests.cs`, `Support/FakeJiraHandler.cs`, `Support/CapturingLoggerProvider.cs`, `Support/TestTargets.cs`, `Support/TestHost.cs`.

**Interfaces:**
- Consumes: `JiraConnectionSettings`, `JiraSiteUrl`, `JiraIdentifiers` (Task 6); `JiraCredentialStore`, `AddJiraCore` (Task 6a).
- Produces: `sealed class JiraTarget` (not a record) with `Uri SiteBase`, `Uri ApiBase`, `Guid ConnectionId`, `bool UsesGateway`, `string CredentialName`, internal `string AuthorizationParameter`, `ToString()` → `"Jira {host}"`; `static bool JiraTarget.TryCreate(JiraConnectionSettings s, JiraCredentialStore credentials, Guid connectionId, out JiraTarget? target, out string? error)`. It resolves `Email` and `ApiToken` through the store **on every call** (never cached on a connection), and returns any store error as is, with no HTTP.
- Produces: `IJiraClient` declared with the full design §7.3 table (11 methods; signatures there). Task 7a implements the first two; later tasks implement the rest.
- Produces: `JiraOptions { int TimeoutSeconds = 30; int IssueTypeCacheMinutes = 10; int ReplayWindowMinutes = 120; }` bound from `Umbraco:Automate:Jira` (no `Credentials` member; `ReplayWindowMinutes` clamped to 10..1440).
- Produces in `AddJiraCore`: named client `JiraClient.HttpClientName = "Umbraco.Community.Automate.Jira"` with timeout from options, `RemoveAllLoggers()`, `ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })`, **no default request headers**; `services.AddScoped<IJiraClient, JiraClient>()` (registered here, implemented in 7a).
- Produces test support: `FakeJiraHandler` (queue of canned responses, records requests including headers); `CapturingLoggerProvider` (collects every formatted line and exception text at `LogLevel.Trace` and above); `TestTargets.Acme(token: "TOKEN-SENTINEL-1")` (in-memory credential `acme` and a store over it); `TestHost.Compose(Action<IConfigurationBuilder>? config = null, HttpMessageHandler? primary = null)` → `IServiceProvider` built by calling the real `AddJiraCore`, with logging at `Trace` into a `CapturingLoggerProvider`, and, when `primary` is given, `AddHttpClient(JiraClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => primary)` applied after `AddJiraCore`. Tasks 7a to 18 resolve `IJiraClient` and `IHttpClientFactory` only through `TestHost`, never by `new HttpClient(fake)`.

- [ ] **Step 1: Write failing tests.**
  - `TryCreate_NoCloudId_UsesSiteBase`; `TryCreate_CloudId_UsesGatewayBase` (`https://api.atlassian.com/ex/jira/{guid}/`); `TryCreate_BadSite_ReturnsErrorWithoutToken`; `TryCreate_MissingCredential_ReturnsKeyNamedError`.
  - `TryCreate_ReadsCredentialEachCall`: create, change the in-memory token and `Reload()`, create again → the second target's `AuthorizationParameter` carries the new token.
  - `JiraTarget_ToString_ExcludesCredentials`: no `TOKEN-SENTINEL`, no email.
  - `JiraOptions_HasNoCredentialsMember` (council test 10): reflection finds no property whose name contains `Credential`, `Token` or `Secret`.
  - `NamedClient_HasNoDefaultAuthorizationHeader` (plan review P4): from `TestHost.Compose()`, `IHttpClientFactory.CreateClient(JiraClient.HttpClientName)` has `DefaultRequestHeaders.Authorization == null` and no `Authorization` entry.
  - `NamedClient_HandlerChain_HasNoLoggingHandler_AndDisallowsRedirects` (plan review P8): from `TestHost.Compose()` (no primary override), walk `IHttpMessageHandlerFactory.CreateHandler(JiraClient.HttpClientName)` through `DelegatingHandler.InnerHandler`; no handler's type name contains `Logging`; the innermost is a `SocketsHttpHandler` with `AllowAutoRedirect == false`.
  - `CapturingLogger_CapturesTrace`: a `LogTrace` line written through the composed `ILoggerFactory` is captured.
  - `ArchitectureTests.NoMutableStaticFields` (non-const, non-readonly-immutable statics in the package assembly fail, excluding compiler-generated) and `ArchitectureTests.NoControllers` (no type deriving from `ControllerBase`).
- [ ] **Step 2: Run** `dotnet test --filter "JiraTargetTests|JiraCompositionTests|ArchitectureTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** to the interfaces above.
- [ ] **Step 4: Run** `./scripts/test.sh`. Expected: `Failed: 0`.
- [ ] **Checkpoint (Pascal commits):** `git add src tests` / suggested message `feat: add Jira target, options and named client composition`

### Task 7a: `JiraClient` core, error mapping and logging hygiene

**Files:** Create `Client/JiraClient.cs`, `Client/JiraRequestException.cs`, `Client/JiraResponseHandler.cs`, `Client/Dtos.cs`; Test `JiraClientTests.cs`, `JiraResponseHandlerTests.cs`.

**Interfaces:**
- Consumes: `JiraTarget`, `IJiraClient`, `TestHost`, `FakeJiraHandler`, `CapturingLoggerProvider` (Task 7).
- Produces: `Task<JiraUser> GetMyselfAsync(JiraTarget target, CancellationToken ct)` and `Task<Guid?> GetTenantCloudIdAsync(Uri siteBase, CancellationToken ct)` (unauthenticated `/_edge/tenant_info`, used by Task 8). The `Authorization` header is set on each `HttpRequestMessage` from `target.AuthorizationParameter`, never on the client.
- Produces DTOs (all in `Dtos.cs`, used by later tasks): `JiraUser(string AccountId, string DisplayName)`, `JiraIssueType(string Id, string Name)`, `JiraCreatedIssue(string Id, string Key)`, `JiraCreatedComment(string Id)`, `JiraTransition(string Id, string Name, string ToStatusName)`.
- Produces: `sealed class JiraRequestException : Exception` with `StepRunErrorCategory Category`, `int? StatusCode`, `string? RequestId`, `int? RetryAfterSeconds`, `bool RequestSent` (true once the request may have reached Jira: a response of any status, a timeout after send, or a network error after the request started; false for validation and connect failures). Message per design §7.4; never wraps the `HttpRequestException` as `InnerException`. Task 14's fallback uses `RequestSent`.

- [ ] **Step 1: Write failing tests** (every test resolves the client from `TestHost.Compose(primary: fake)`):
  - `Client_DoesNotFollowRedirects`: a 302 to `https://evil.example/` → `InvalidResponse`, exactly one request recorded.
  - `TenantInfo_RequestHasNoAuthorizationHeader`.
  - `Requests_CarryBasicAuth`: header equals `Basic ` + base64(`svc@x.com:TOKEN-SENTINEL-1`), set on the request message.
  - `Rotation_NextRequestUsesNewToken` (plan review P4, client level): call `GetMyselfAsync` with a target built from token 1; change the in-memory token, `Reload()`, build a new target, call again → the second request carries token 2 and no recorded request after the reload carries token 1.
  - `PathSegments_AreEscaped`: project `A/B?c` → request path contains `A%2FB%3Fc`.
  - `Jira400_ErrorsTruncatedTo200`: an `errors` message of 1,000 characters appears cut to 200.
  - Theory over §7.4: 401 → `Authentication` + "Jira rejected the credentials (401)…"; 403 → `Authentication` + `errorMessages`; 400 with `{"errorMessages":["a"],"errors":{"summary":"b"}}` → `Validation` containing `a` and `summary: b`; 404 → `Validation`; 429 `Retry-After: 7` → `RateLimiting`, `RetryAfterSeconds == 7`; 503 → `ServiceUnavailable`; 301/302/307 → `InvalidResponse`; 200 with `not json` → `InvalidResponse`. Each has `RequestSent == true`.
  - `Exception_FromNetworkError_ExcludesInnerMessage`: handler throws `HttpRequestException("secret-ish TOKEN-SENTINEL-1")` → `ServiceUnavailable`, message "Couldn't reach Jira (acme.atlassian.net).", `InnerException` null. `Timeout_IsServiceUnavailable_RequestSent`: a `TaskCanceledException` not caused by the caller's token maps the same way with `RequestSent == true`; caller cancellation propagates as `OperationCanceledException`. `ConnectFailure_RequestNotSent`: `HttpRequestException` with `HttpRequestError.ConnectionError` → `RequestSent == false`.
  - `Get_429_ShortRetryAfter_RetriesOnce`, `Get_429_LongRetryAfter_DoesNotRetry`, `Post_429_NeverRetries` (one request recorded).
  - `RequestId_FromXARequestId_IsInException` (absent header → null).
  - `Logs_ContainNoSentinel_AtTrace` (plan review P8): across all the above, the composed `CapturingLoggerProvider` lines at every level down to `Trace` contain no `TOKEN-SENTINEL` and no `Authorization`.
- [ ] **Step 2: Run** `dotnet test --filter "JiraClientTests|JiraResponseHandlerTests"`. Expected: FAIL.
- [ ] **Step 3: Implement.** JSON options: camelCase, ignore nulls when writing. Log one line per call: method, host, path template (e.g. `/rest/api/3/issue/{key}`), status, request ID.
- [ ] **Step 4: Run** `./scripts/test.sh`. Expected: `Failed: 0`.
- [ ] **Checkpoint (Pascal commits):** `git add src tests` / suggested message `feat: add Jira client with error mapping and safe logging`

### Task 8: Connection type and Validate

**Files:** Create `Connection/JiraConnectionType.cs`, `Credentials/JiraCredentialSourceCheck.cs`; modify `Client/IJiraClient.cs`, `Client/JiraClient.cs` (`GetHeldPermissionsAsync`); Test `JiraConnectionTypeTests.cs`, `JiraCredentialSourceCheckTests.cs`.

**Interfaces:**
- Consumes: `JiraTarget.TryCreate`, `TestHost` (Task 7); `IJiraClient.GetMyselfAsync`, `GetTenantCloudIdAsync` (Task 7a); `JiraCredentialStore` (Task 6a).
- Produces: `[ConnectionType("community.jira.cloud", "Jira Cloud (API token)", …)] sealed class JiraConnectionType : ConnectionTypeBase<JiraConnectionSettings>` overriding `ValidateAsync(object? settings, CancellationToken)`.
- Produces: `internal sealed class JiraCredentialSourceCheck(IConfiguration configuration)` with `string? FindCommittedFile(string key)` (returns the `appsettings*.json` file name when the winning provider for `key` is a JSON file provider for such a file; null for user secrets, env vars, Key Vault or anything else).
- Produces: `Task<IReadOnlyList<string>> IJiraClient.GetHeldPermissionsAsync(JiraTarget, IReadOnlyList<string> keys, CancellationToken)`, called only if Task 4 found `/mypermissions` needs no extra scope.

- [ ] **Step 1: Write failing tests.**
  - `Validate_Success_ShowsSiteNameRouteAndClaims`: `/myself` returns `displayName` "Svc Bot" → Success; message contains "Connected to acme.atlassian.net as Svc Bot via", the route ("site" or "Atlassian gateway"), "site-wide" and "Automate access to this workspace" (design §6.1).
  - `Validate_Message_ShowsRoute` and `Validate_SiteRoute_WarnsClassicToken`.
  - `Validate_AdminPermissions_Warns` (only if Task 4 allows): `/mypermissions` reports `ADMINISTER` → message warns to remove admin rights.
  - `Validate_CredentialFromAppsettings_Warns`: config built with a JSON provider for `appsettings.json` → warning names the file; with an env-var provider → no warning. Neither message contains the value.
  - `Validate_401_MessageWithoutToken`: Failure, message starts "Jira rejected the credentials (401)", no sentinel in message or logs.
  - `Validate_BadSiteUrl_NoRequestSent` and `Validate_BadCloudId_NoRequestSent`: `FakeJiraHandler.Requests` empty.
  - Only if Task 4 says CloudId is required: `Validate_BlankCloudId_FetchesTenantInfoAndAsksUser` → Failure whose message contains the GUID from `/_edge/tenant_info` and "paste it into Cloud ID"; settings unchanged.
  - `Validate_MissingCredential_FailsNamingKeyOnly` (council test 7): no `…:acme:ApiToken` → Failure naming the key, no request sent.
  - `Validate_PrefixAllowListed_FailsClosed` (council test 2): allow-list contains `Umbraco:Automate` → Failure naming it, no request sent.
  - `Validate_BadCredentialName_FailsBeforeLookup` (council test 3), theory over `a-b`, `"a\nb"` and `"agéncy"` (plan review P15): Failure with the contract §1.2 text, no config read, no request.
  - No save-time check: Automate offers no connection save hook this package relies on, so design §6.1's criteria say "validate or run", not "save" (plan review P15).
- [ ] **Step 2: Run** `dotnet test --filter "JiraConnectionTypeTests|JiraCredentialSourceCheckTests"`. Expected: FAIL.
- [ ] **Step 3: Implement.** Map `JiraRequestException` to a Failure result with its message. Warnings are appended to the success message.
- [ ] **Step 4: Run** `./scripts/test.sh`. Expected: `Failed: 0`.
- [ ] **Step 5: Manual check** on the demo site: set `Umbraco__Automate__Jira__Credentials__sandbox__Email` and `…__ApiToken` as env vars, create the connection with `CredentialName` `sandbox`, click Validate. Expected: "Connected to <site> as <name> via <route>" plus the claims; the database row and an export of the connection contain `sandbox` and no token (council test 4). Move the token into `appsettings.Development.json`: Validate shows the committed-file warning.
- [ ] **Checkpoint (Pascal commits):** `git add src tests` / suggested message `feat: add Jira Cloud connection with one-click validation`

### Task 9: ADF Plain text converter and the safety pass

**Files:** Create `Adf/JiraTextFormat.cs`, `Adf/IAdfConverter.cs`, `Adf/AdfConverterFactory.cs`, `Adf/PlainTextAdfConverter.cs`, `Adf/AdfSanitiser.cs`, `Adf/AdfLimits.cs`; Test `PlainTextAdfConverterTests.cs`, `AdfSanitiserTests.cs`, `Support/AdfSchemaAssert.cs`.

**Interfaces:**
- Produces: `enum JiraTextFormat { PlainText, Markdown, AdfJson }`.
- Produces: `interface IAdfConverter { AdfResult Convert(string? input); }`; `sealed record AdfResult(JsonObject? Document, IReadOnlyList<string> Warnings)` (`Document` null for empty or whitespace input).
- Produces: `interface IAdfConverterFactory { IAdfConverter For(JiraTextFormat format); }` registered as singleton.
- Produces: `static void AdfSanitiser.Sanitise(JsonObject doc, ICollection<string> warnings)`, an **allowlist** per design §7.5: nodes `doc`, `paragraph`, `text`, `hardBreak`, `heading`, `bulletList`, `orderedList`, `listItem`, `codeBlock`, `blockquote`, `rule`; marks `strong`, `em`, `strike`, `code`, `link`; attributes `heading.level` (1..6), `orderedList.order`, `codeBlock.language` (≤ 32, `^[A-Za-z0-9+#.-]+$`), `link.href` only. Anything else is unwrapped to its text with a warning. `href` must be trimmed, free of control characters, and parse as an absolute `Uri` with scheme `http`, `https` or `mailto`. Enforces `AdfLimits.MaxTextLength` with suffix " (truncated)". Iterative or depth-capped at `AdfLimits.MaxDepth`.
- Produces: `static string AdfLimits.TruncateOnCodePoint(string s, int max)`; `AdfLimits.MaxTextLength = 32767` unless Task 4 found a different cap; `AdfLimits.MaxInputBytes = 65536`; `AdfLimits.MaxNesting = 16`; `AdfLimits.MaxJsonDepth = 32`.

- [ ] **Step 1: Write failing tests.** `"a\n\nb"` → two paragraphs; `"a\nb"` → one paragraph with `hardBreak`; `"\r\n"` treated as `\n`; `"# not a heading [x](javascript:alert(1))"` → literal text, no marks; `"   "` → `Document` null. Sanitiser: `javascript:`, `data:`, `vbscript:` link marks removed with text kept; `mailto:` kept; `extension` node unwrapped plus a warning; total text over the cap ends with " (truncated)". `Sanitiser_Allowlist_UnwrapsInlineCardBlockCardEmbedCardMention` (the mention's `attrs.text` survives as text); `Sanitiser_UnknownNodeType_Unwrapped` (`{"type":"futureThing","content":[text "x"]}` → text "x"); `Sanitiser_LinkMark_ExtraAttrsDropped` (`title`, `id`, `collection` removed); `Sanitiser_UnknownMark_Dropped` (plan review P7: `underline`, `textColor`, `subsup`, `backgroundColor` and an invented `futureMark` removed, text kept, one warning each); `Sanitiser_Href_WithControlCharsOrRelative_Dropped` (`"java\tscript:x"`, `"/relative"`, `" https://x\u0000"`). `Truncate_DoesNotSplitSurrogatePair` (emoji straddling the cap). Every produced doc passes `AdfSchemaAssert.Valid` against `Fixtures/adf-schema.json`.
- [ ] **Step 2: Run** `dotnet test --filter "PlainTextAdfConverterTests|AdfSanitiserTests"`. Expected: FAIL.
- [ ] **Step 3: Implement.** The sanitiser walks the tree once; every converter calls it last.
- [ ] **Step 4: Run** `./scripts/test.sh`. Expected: `Failed: 0`.
- [ ] **Checkpoint (Pascal commits):** `git add src/*/Adf tests` / suggested message `feat: add plain-text ADF converter and safety pass`

### Task 10: Markdown converter

**Files:** Create `Adf/MarkdownAdfConverter.cs`, `Adf/AdfInputTooLargeException.cs`; Test `MarkdownAdfConverterTests.cs`, `Fixtures/adf/*.md` + `*.golden.json`.

**Interfaces:** Consumes `IAdfConverter`, `AdfSanitiser`, `AdfLimits` (Task 9). Produces `MarkdownAdfConverter : IAdfConverter` using Markdig. Produces `sealed class AdfInputTooLargeException(string message) : Exception`, also thrown by Task 11's passthrough.

- [ ] **Step 1: Write golden tests**, one fixture pair each: paragraph; headings 1 to 6; bullet list; ordered list (start number kept as `order`); nested list; fenced code block with language; blockquote; thematic break → `rule`; marks `**strong**`, `*em*`, `~~strike~~`, `` `code` ``, `[link](https://x)`. Unsupported (table, image, raw HTML `<script>`) → plain text, no `media` node. `[x](javascript:alert(1))` → text "x" without link. `Markdown_DisallowedLinkForms_NoLinkMark` theory (plan review P7): autolink `<javascript:alert(1)>`, reference-style `[x][r]` with `[r]: javascript:alert(1)`, `[x](data:text/html,a)`, `[x]( javascript:alert(1))` → no `link` mark anywhere in the output. `Markdown_WwwAutolink_IsPlainText` (`www.evil.example` stays text: the autolink extension is not enabled). `Markdown_LinkTitle_Dropped` (`[x](https://a.example "t")` → link with `href` only). Oracle test: Meziantou ADF→Markdown of each golden output contains the source's text content. `Markdown_Over64KB_FailsValidation` (throws `AdfInputTooLargeException`, mapped to `Validation` by the actions). `Markdown_Nested200Levels_DoesNotCrash`: 200 nested `>` and 200 nested list levels, run on a `Thread` with a 256 KB stack; completes, nesting beyond 16 flattened to text.
- [ ] **Step 2: Run** `dotnet test --filter MarkdownAdfConverterTests`. Expected: FAIL.
- [ ] **Step 3: Implement** with a Markdig pipeline that enables only emphasis extras (strikethrough). Raw HTML disabled.
- [ ] **Step 4: Run** `./scripts/test.sh`. Expected: `Failed: 0`; all outputs schema-valid.
- [ ] **Checkpoint (Pascal commits):** `git add src/*/Adf tests` / suggested message `feat: add Markdown to ADF converter`

### Task 11: ADF JSON passthrough and ADF to text

**Files:** Create `Adf/AdfJsonPassthrough.cs`, `Adf/AdfToText.cs`; Test `AdfJsonPassthroughTests.cs`, `AdfToTextTests.cs`.

**Interfaces:**
- Produces: `AdfJsonPassthrough : IAdfConverter`; throws `AdfFormatException(string message)` when the root is not `{ "type": "doc", "version": 1, "content": [...] }` or not JSON.
- Produces: `static string AdfToText.Flatten(JsonNode? adf)`.

- [ ] **Step 1: Write failing tests.** Passthrough: valid doc unchanged except sanitised; `version: 2`, missing `content`, an array root and `not json` each throw `AdfFormatException`; JSON nested 40 deep throws `AdfFormatException` (`MaxDepth = 32`); over 64 KB throws `AdfInputTooLargeException`; an `inlineExtension` inside a paragraph is unwrapped with a warning, not a failure. Flatten: two paragraphs → `"a\nb"`; list items → one per line; marks dropped; link → its text; mention with `attrs.text` "@Ann" → `@Ann`; null → `""`.
- [ ] **Step 2: Run** `dotnet test --filter "AdfJsonPassthroughTests|AdfToTextTests"`. Expected: FAIL.
- [ ] **Step 3: Implement**; register all three converters in `AdfConverterFactory`.
- [ ] **Step 4: Run** `./scripts/test.sh`. Expected: `Failed: 0`.
- [ ] **Checkpoint (Pascal commits):** `git add src/*/Adf tests` / suggested message `feat: add ADF JSON passthrough and ADF flattening`

### Task 12: Issue-type resolution, `AdditionalFieldsJson` rules and action failure helpers

Split from the old Task 12 (plan review P21). F-04's controls live here so the security review's Task 12 citation still points at them.

**Files:** Create `Client/IssueTypeResolver.cs`, `Actions/ActionFailures.cs`, `Actions/AdditionalFields.cs`; modify `Client/JiraClient.cs` (add `GetIssueTypesAsync`); Test `IssueTypeResolverTests.cs`, `AdditionalFieldsTests.cs`, `ActionFailuresTests.cs`.

**Interfaces:**
- Consumes: `IJiraClient`, `JiraTarget`, `TestHost` (Tasks 7, 7a); `AdfSanitiser` (Task 9); `JiraCredentialStore` (Task 6a).
- Produces: `Task<IReadOnlyList<JiraIssueType>> IJiraClient.GetIssueTypesAsync(JiraTarget target, string projectKey, CancellationToken ct)`.
- Produces: `IIssueTypeResolver.ResolveAsync(JiraTarget target, string projectKey, string nameOrId, CancellationToken ct) : Task<string>` (singleton with `IMemoryCache`, key `jira:types:{connectionId}:{projectKey}`, lifetime `IssueTypeCacheMinutes`; numeric input returned as is; unknown name throws `JiraRequestException` with `Validation` listing valid names).
- Produces: `static class ActionFailures` with `ActionResult FromJira(JiraRequestException ex)`, `ActionResult Validation(string message)`, `bool TryGetTarget(ActionContext ctx, JiraCredentialStore credentials, out JiraTarget? target, out ActionResult? failure)` (credential errors become `ConfigurationError`). Tasks 12a, 13, 14 and 15 use these.
- Produces: `static class AdditionalFields` with `bool TryMerge(JsonObject fields, string? additionalFieldsJson, ICollection<string> warnings, out string? error)`, rules exactly as design §6.2 (revised by plan review P6):
  - Parse with `MaxDepth = 32` and **duplicate property names rejected at any depth** (`JsonDocumentOptions.AllowDuplicateProperties = false` where the TFM has it, else detected while walking the `JsonDocument`); error "duplicate key {key}".
  - Root must be an object.
  - Keys are matched **ordinally and case-sensitively** after JSON unescaping. Allowed: `^customfield_[0-9]+\z`, `components`, `fixVersions`, `versions`, `duedate`, `environment`, `labels`. Everything else fails, so case variants (`Project`, `CUSTOMFIELD_1`), escaped spellings that decode to a protected key, and a bare `customfield_` are rejected. Protected keys (`project`, `issuetype`, `summary`, `description`, `reporter`, `security`, `parent`) get the message "{key} is set by its own field".
  - A key already present in `fields` → "set {key} in one place".
  - Then `AdfSanitiser.Sanitise` on **every** value shaped `{ "type": "doc", … }` at any depth, including inside arrays and nested objects.

- [ ] **Step 1: Write failing tests.**
  - Resolver: `UnknownIssueType_ListsValidTypes` (message contains "Bug, Task"); `IssueTypeCache_HitsOnce` (two calls, one createmeta request; separate `ConnectionId` → two requests); `NumericIssueType_NoRequest`.
  - `AdditionalFields_CustomFieldMerged` (`{"customfield_10010":5,"duedate":"2026-12-01"}` appears in `fields`); `AdditionalFields_NotObject_Fails` for `[1]`, `"x"`, `{bad`.
  - `AdditionalFields_ProtectedKey_Fails` theory over `project`, `issuetype`, `summary`, `description`, `reporter`, `security`, `parent`.
  - `AdditionalFields_UnlistedKey_Fails` (`assignee`, `priority`, `x`).
  - `AdditionalFields_BypassShapes_Rejected` theory (plan review P6): `{"project":{"key":"X"}}`, `{"Project":{}}`, `{"PROJECT":{}}`, `{"CUSTOMFIELD_10010":1}`, `{"customfield_":1}`, `{"customfield_abc":1}`, `{"customfield_10010":1,"customfield_10010":2}`, `{"labels":["a"],"labels":["b"]}`, `{"customfield_1":{"x":1,"x":2}}` → false with an error; `fields` unchanged.
  - `AdditionalFields_LabelsAlsoTyped_Fails`.
  - `AdditionalFields_AdfValuedCustomField_IsSanitised` (an `inlineCard` inside `customfield_10020` is unwrapped).
  - `AdditionalFields_NestedAdf_Sanitised` (plan review P6): an ADF doc with a `javascript:` link and an `extension` node inside `{"customfield_1":[{"type":"doc",…}]}` and inside `{"customfield_2":{"value":{"type":"doc",…}}}` → both sanitised, warnings recorded.
  - `ActionFailures_CredentialError_IsConfigurationError`.
- [ ] **Step 2: Run** `dotnet test --filter "IssueTypeResolverTests|AdditionalFieldsTests|ActionFailuresTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** to the interfaces above.
- [ ] **Step 4: Run** `./scripts/test.sh`. Expected: `Failed: 0`.
- [ ] **Checkpoint (Pascal commits):** `git add src tests` / suggested message `feat: add issue-type resolver and AdditionalFieldsJson rules`

### Task 12a: Create issue action

**Files:** Create `Actions/CreateIssueAction.cs`, `Actions/CreateIssueSettings.cs`, `Actions/CreateIssueOutput.cs`, `tests/.../Fixtures/requests/create-issue.json`; modify `Client/JiraClient.cs` (add `CreateIssueAsync`); Test `CreateIssueActionTests.cs`.

**Interfaces:**
- Consumes: `IIssueTypeResolver`, `ActionFailures`, `AdditionalFields` (Task 12); `IAdfConverterFactory` (Tasks 9 to 11); `TestHost` (Task 7).
- Produces: `Task<JiraCreatedIssue> IJiraClient.CreateIssueAsync(JiraTarget target, JsonObject fields, JsonArray? properties, CancellationToken ct)` (`properties` is the top-level create-body array; Task 13 fills it).
- Produces: `[Action("community.jira.createIssue", "Create Jira issue", ConnectionTypeAlias = "community.jira.cloud", …)] CreateIssueAction : ActionBase<CreateIssueSettings, CreateIssueOutput>`. Settings and output fields exactly as design §6.2 (`Description` uses `EditorUiAlias = "Umb.PropertyEditorUi.MarkdownEditor"`; `DescriptionFormat` and `AdditionalFieldsJson` no bindings; `ProjectKey` and `ParentKey` checked by `JiraIdentifiers`). Output `IssueId`, `IssueKey`, `IssueUrl` = `{SiteBase}/browse/{key}`. (`DedupeKey` and `WasExisting` arrive in Task 13.)

- [ ] **Step 1: Write failing tests** with `ActionTestHarness.For<CreateIssueAction>()`, services from `TestHost.Compose(primary: fake)`:
  - `Creates_GoldenRequest`: posted JSON equals `Fixtures/requests/create-issue.json` (project key, `issuetype.id`, summary, ADF description, labels, `priority.name`, `assignee.accountId`, `parent.key`). (Task 13 updates the fixture with the dedupe marker.)
  - `Summary_NewlinesAndLength`: `"a\r\nb"` → `"a b"`; 300 chars → 255. `Summary_WithEmojiAtBoundary_IsValidUtf16`.
  - `Labels_Normalised`: `" a b, ,A b,c "` → `["a-b","A-b","c"]` (dedupe is ordinal; Jira labels are case-sensitive).
  - `EmptyDescription_OmitsField`.
  - `AdditionalFieldsJson_AnyRejection_SendsNoRequest`: for each rejecting input from Task 12's theories, `FakeJiraHandler.Requests` is empty (plan review P6).
  - `AdditionalFieldsJson_HasNoBindings` (schema reflection on the `[Field]` attribute).
  - `ProjectKey_Invalid_FailsBeforeHttp`; `ParentKey_DotSegments_FailBeforeHttp`.
  - `Description_Markdown_Over64KB_FailsValidationBeforeHttp`.
  - `Jira400_ShowsFieldErrors`; `Jira429_IsRateLimiting`.
  - `MissingRequired_FailsBeforeHttp` for blank `ProjectKey`, `IssueType`, `Summary`.
  - `Rotation_NextRunUsesNewToken` (plan review P4, action level): run once with token 1; change the in-memory token and `Reload()`; run again → the second run's requests carry token 2 and none carries token 1.
- [ ] **Step 2: Run** `dotnet test --filter CreateIssueActionTests`. Expected: FAIL.
- [ ] **Step 3: Implement.** Build `fields` as a `JsonObject` from typed fields, then `AdditionalFields.TryMerge`. Nothing in `AdditionalFieldsJson` overrides a typed field. Build the `JiraTarget` per run through `ActionFailures.TryGetTarget`.
- [ ] **Step 4: Run** `./scripts/test.sh`. Expected: `Failed: 0`.
- [ ] **Step 5: Manual check** on the demo site: a manual-trigger automation creates an issue in `SBX` with Markdown description; `IssueUrl` opens it.
- [ ] **Checkpoint (Pascal commits):** `git add src tests` / suggested message `feat: add Create Jira issue action`

### Task 13: Create-issue dedupe (in 1.0, unconditional)

Retry duplication is confirmed (security review F-05; Pascal 2026-10-30). Not conditional on any spike; the marker type comes from Task 4 and the iteration identity from Task 3 step 3a.

**Key and hash choices (design §6.2, revised 2026-10-30 by plan review P3 and P18):**
- **Default key** `"{AutomationId}:{RunId}:{StepId}:{IterationId or -}:{InputFingerprint}"`. `IterationId` is the ForEach item identity if Task 3 step 3a found one, else `-`. `InputFingerprint` is 16 lower-case hex of SHA-256 over the action's resolved identity inputs (Create issue: `ProjectKey`, `IssueType`, `Summary`, `Description`; Add comment: `IssueKey`, `Body`), each length-prefixed so `("ab","c")` and `("a","bc")` differ. A retry of the same iteration resolves the same inputs, so its key is stable. Two ForEach items with different data get different keys even when `ActionContext` has no iteration identity, so no item silently returns another item's ticket. Residual (README): two ForEach items with identical inputs and no iteration identity collapse into one ticket; set `DedupeKey` to a per-item value (for example `${ item.id }`) inside ForEach.
- **Marker** = first 16 lower-case hex of **HMAC-SHA256 keyed with the connection ID's 16 bytes** over the UTF-8 effective key (P18). The connection ID is stable across token rotation and environments, and is not visible to Jira users, so a Jira user who sees the label cannot confirm a guessed `DedupeKey` such as an email address. It is not encryption: anyone with Umbraco backoffice access can recompute it. The README still says to prefer non-personal keys (a form entry ID, not an email). Recreating the connection changes the marker, so cross-run dedupe restarts; that is documented.

**Files:** Create `Actions/DedupeKey.cs`; modify `CreateIssueSettings.cs`, `CreateIssueOutput.cs`, `CreateIssueAction.cs`, `IJiraClient.cs`, `JiraClient.cs`, `Fixtures/requests/create-issue.json`; Test `CreateIssueDedupeTests.cs`, `DedupeKeyTests.cs`.

**Interfaces:**
- Consumes: `ActionFailures` (Task 12); `CreateIssueAction`, `CreateIssueAsync` (Task 12a); Task 3 step 3a's `IterationId` property (or none); Task 4's marker choice.
- Produces: optional setting `DedupeKey` (string, bindings). Output gains `WasExisting` (bool).
- Produces: `static class DedupeKey` with `string Effective(string? userKey, ActionContext ctx, IReadOnlyList<string?> identityInputs)` (trimmed user key when not blank; otherwise the default key above) and `string Marker(string effectiveKey, Guid connectionId)`. Task 14 uses both.
- Produces: `enum DedupeMarker { IssueProperty, Label }` and `const DedupeMarker DedupeKey.MarkerKind`, set from Task 4's probe (not user config).
- Produces: `Task<JiraCreatedIssue?> IJiraClient.FindByDedupeAsync(JiraTarget target, string projectKey, string marker, CancellationToken ct)`: `POST /rest/api/3/search/jql`, JQL `project = <JiraIdentifiers.QuoteJql(key)> AND issue.property[community.jira.dedupe].key = "<marker>"` or `labels = "cj-dedupe-<marker>"`, `maxResults: 1`, fields `key`.
- `CreateIssueAsync` body gains the marker in the same request: `properties: [{ "key": "community.jira.dedupe", "value": { "key": "<marker>" } }]` or the `cj-dedupe-<marker>` label appended to `fields.labels`.
- **A failed search fails the step** with the search's mapped category (401/403 `Authentication`, 5xx and network `ServiceUnavailable`, 429 `RateLimiting`) and **never POSTs** (P3). Automate's retry then repeats the search.

- [ ] **Step 1: Write failing `DedupeKeyTests`** (plan review P3):
  - `Effective_SameContextAndInputs_IsStable` (two calls, retry simulation → equal).
  - `Effective_DifferentStep_Differs`; `Effective_DifferentRun_Differs`; `Effective_DifferentAutomation_Differs`.
  - `Effective_ForEachItemsWithDifferentInputs_Differ` (same run and step, different `Summary`).
  - `Effective_ForEachIteration_Differs` (only if Task 3 found an iteration identity: same inputs, different iteration → differ; otherwise this test asserts the identical-inputs residual: same key).
  - `Effective_InputsAreLengthPrefixed` (`["ab","c"]` vs `["a","bc"]` differ).
  - `Effective_UserKey_IgnoresContextAndInputs`; `Effective_UserKey_Trimmed`; `Effective_BlankUserKey_UsesDefault`.
  - `Marker_Is16LowerHex`; `Marker_DependsOnConnectionId`; `Marker_IsNotPlainSha256` (differs from the first 16 hex of SHA-256 of the key, P18).
- [ ] **Step 2: Write failing `CreateIssueDedupeTests`:**
  - `Dedupe_DefaultsToContextAndInputs_WhenBlank`: blank `DedupeKey` → the search JQL and the create body carry `Marker(Effective(null, ctx, inputs), connectionId)`.
  - `Dedupe_SearchesBeforeEveryPost`: first request recorded is the search, second the POST.
  - `Dedupe_ExistingIssue_ReturnsItWithoutPost` (`WasExisting` true, no POST recorded).
  - `Post_TimeoutAfterSend_RetryFindsExisting_NoSecondPost`: run 1: search empty, POST times out (`ServiceUnavailable`); run the same `ActionContext` again (simulated retry): search returns the issue → success, `WasExisting` true, exactly one POST across both.
  - `Dedupe_SearchFails_NoPost` theory (P3): search returns 401, 403, 500, 503, 429 or a network error → the step fails with the mapped category and no POST is recorded.
  - `Dedupe_ForEachTwoItems_TwoPosts`: two runs sharing `RunId` and `StepId` with different `Summary`, search empty both times → two POSTs with different markers.
  - `Dedupe_MarkerInCreateBody` (property or label per Task 4).
  - `Dedupe_KeyIsHashed_RawValueNotSent`: `DedupeKey` `form-123 alice@example.com` never appears in any request body or URL.
  - `Dedupe_JqlValueQuotedAndEscaped`: project key from settings goes through `QuoteJql`.
- [ ] **Step 3: Run** `dotnet test --filter "DedupeKeyTests|CreateIssueDedupeTests"`. Expected: FAIL.
- [ ] **Step 4: Implement** to the interfaces above.
- [ ] **Step 5: Run** `./scripts/test.sh`. Expected: `Failed: 0`.
- [ ] **Checkpoint (Pascal commits):** `git add src tests` / suggested message `feat: make Create Jira issue safe to retry`

---

## M2: Add comment, Transition issue

### Task 14: Add comment action

**Files:** Create `Actions/AddComment{Action,Settings,Output}.cs`; modify `IJiraClient.cs`, `JiraClient.cs` (`AddCommentAsync`, and `FindCommentByDedupeAsync` if Task 4 probe (c) passed); Test `AddCommentActionTests.cs`.

**Interfaces:**
- Consumes: `ActionFailures`, `IAdfConverterFactory`, `IJiraClient.AddCommentAsync`, `JiraIdentifiers` (Task 6), `DedupeKey` (Task 13).
- Produces: `[Action("community.jira.addComment", "Add Jira comment", ConnectionTypeAlias = "community.jira.cloud")]`; settings `IssueKey`, `Body` (Markdown editor UI), `BodyFormat` (no bindings, default `PlainText`), `DedupeKey` (bindings); output `CommentId`, `CommentUrl` = `{SiteBase}/browse/{key}?focusedCommentId={id}`, `WasExisting`.
- Produces: `Task<JiraCreatedComment> IJiraClient.AddCommentAsync(JiraTarget target, string issueKey, JsonObject adfBody, string? dedupeMarker, CancellationToken ct)` (design §7.3); it adds the comment `properties` entry only when `dedupeMarker` is not null, which is only when probe (c) passed. Identity inputs for `DedupeKey.Effective`: `IssueKey`, `Body`.
- Produces (probe (c) passed): `Task<JiraCreatedComment?> IJiraClient.FindCommentByDedupeAsync(JiraTarget target, string issueKey, string dedupeMarker, CancellationToken ct)` over `GET /issue/{key}/comment?orderBy=-created&maxResults=50&expand=properties`. A failed listing fails the step and never POSTs (same rule as Task 13).
- Fallback (probe (c) failed): no search; a `ServiceUnavailable` from the POST with `JiraRequestException.RequestSent == true` (timeout, network error after send, 5xx) is re-mapped to `Validation` with "Jira may already have saved this comment on {key}. Check the issue before re-running." A 429 or a connection failure before send keeps its transient category.

- [ ] **Step 1: Write failing tests.** `Adds_GoldenRequest`; `Missing_404_SaysNotFoundOrNoPermission` (`Validation`, message "Issue SBX-9 not found or no permission"); `EmptyBodyAfterTrim_FailsBeforeHttp`; `IssueKey_Whitespace_IsTrimmed` (`" SBX-1\n"` → path `/issue/SBX-1/comment`); `IssueKey_Blank_FailsBeforeHttp`; `IssueKey_DotSegments_FailBeforeHttp` (`.`, `..`, `SUP-1/..`); `AddComment_TimeoutAfterSend_IsNotBlindlyReposted` (read-first strategy: the retry finds the comment by property, one POST in total; fallback strategy: the first attempt fails with `Validation` and the "check the issue" message); `AddComment_ConnectFailure_StaysTransient` (fallback strategy: `RequestSent == false` keeps `ServiceUnavailable`); `AddComment_ListingFails_NoPost` (read-first strategy: 401, 500 or network error on the listing → mapped failure, no POST); `Body_Markdown_Over64KB_FailsValidationBeforeHttp`.
- [ ] **Step 2: Run** `dotnet test --filter AddCommentActionTests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `./scripts/test.sh`. Expected: `Failed: 0`.
- [ ] **Checkpoint (Pascal commits):** `git add src tests` / suggested message `feat: add Add Jira comment action`

### Task 15: Transition issue action

**Files:** Create `Actions/TransitionIssue{Action,Settings,Output}.cs`; modify `JiraClient.cs` (`GetIssueStatusAsync`, `GetTransitionsAsync`, `DoTransitionAsync`); Test `TransitionIssueActionTests.cs`.

**Interfaces:**
- Produces (design §7.3): `Task<string> IJiraClient.GetIssueStatusAsync(JiraTarget target, string issueKey, CancellationToken ct)`, `Task<IReadOnlyList<JiraTransition>> GetTransitionsAsync(JiraTarget target, string issueKey, CancellationToken ct)`, `Task DoTransitionAsync(JiraTarget target, string issueKey, string transitionId, JsonObject? commentAdf, string? resolutionName, CancellationToken ct)`.
- Produces: `[Action("community.jira.transitionIssue", "Transition Jira issue", ConnectionTypeAlias = "community.jira.cloud")]`; settings `IssueKey`, `Transition`, `Comment` (plain text → ADF via `PlainTextAdfConverter`, sent as `update.comment[0].add.body`), `ResolutionName` (→ `fields.resolution.name`); output `FromStatus`, `ToStatus`, `TransitionId`.
- Produces: `static TransitionMatch TransitionMatcher.Match(IReadOnlyList<JiraTransition> available, string requested)` returning `Matched(JiraTransition)`, `Ambiguous(IReadOnlyList<JiraTransition>)` or `None`. Order: exact ID → transition name (case-insensitive) → target status name; first level with matches wins; more than one at that level is ambiguous.

- [ ] **Step 1: Write failing tests.** Matcher: ID beats name; name beats status; `"done"` matches status "Done" via transition "Close"; two transitions to "Done" at the status level → `Ambiguous` listing both. Action: `Moves_ToDone` (`ToStatus` "Done", posted body golden); `NotAvailable_ListsAvailable` (`Validation`, message lists names); `AlreadyInStatus_IsNoOp` (current status "Done", request "Done" → success, `FromStatus == ToStatus == "Done"`, `TransitionId` "", log line contains "already in status", no POST); `Comment_SentInSameCall`; `Resolution_Sent`; `IssueKey_Whitespace_IsTrimmed`; `IssueKey_DotSegments_FailBeforeHttp`; `TransitionId_NonNumeric_TreatedAsName` (`"../5"` is matched only as a name, never placed in a URL or body as an ID).
- [ ] **Step 2: Run** `dotnet test --filter TransitionIssueActionTests`. Expected: FAIL.
- [ ] **Step 3: Implement.** Already-in-status is checked against the requested value as a status name before matching transitions.
- [ ] **Step 4: Run** `./scripts/test.sh`. Expected: `Failed: 0`.
- [ ] **Step 5: Manual check** on the demo site: Content Published → Get Content Property (`jiraKey`) → Add comment → Transition to "Done" (use case 3).
- [ ] **Checkpoint (Pascal commits):** `git add src tests` / suggested message `feat: add Transition Jira issue action`

---

## M3: Inbound webhooks

### Task 16: Signature authenticator `community.jira.signature`

**Per-task review** (portfolio "Build approach"): `qa-reviewer` and `security-analyst`, fresh context, before the checkpoint, because Teams reuse the pattern.

**Files:** Modify `Webhooks/JiraSignatureWebhookAuthenticator.cs` (replace stub); create `Webhooks/JiraSignatureSettings.cs`, `Webhooks/JiraSignature.cs`, `Webhooks/WebhookReplayGuard.cs`, `BannedSymbols.txt`; modify the package csproj (reference `Microsoft.CodeAnalysis.BannedApiAnalyzers` as a private asset, `AdditionalFiles` `BannedSymbols.txt`); Test `JiraSignatureWebhookAuthenticatorTests.cs`, `JiraSignatureTests.cs`, `WebhookReplayGuardTests.cs`, `ArchitectureTests.cs`.

**Interfaces:**
- Consumes: `WebhookAuthenticatorBase<JiraSignatureSettings>`, `WebhookAuthenticationContext { HttpRequest Request; string? Body }` (observed at `5318996`); `JiraCredentialStore` (Task 6a); `JiraOptions.ReplayWindowMinutes` (Task 7); Task 3's answers on dependencies and on the replay key source (step 3).
- Produces: authenticator alias `community.jira.signature`, display "Jira webhook secret"; `JiraSignatureSettings { string CredentialName }` (not sensitive, no bindings; **never** a field named `Secret`, F-15); `RequiresBody => true`; `CredentialHeaderNames => ["X-Hub-Signature"]`; `protected override bool Validate(WebhookAuthenticationContext ctx, JiraSignatureSettings s)`. `Validate` wraps its whole body in a `try`/`catch (Exception)` that logs a warning (exception type only, no message) and returns false (fail closed).
- Produces: `internal static class JiraSignature` with `bool TryParseHeader(string? headerValue, out byte[]? signature)` (exact ordinal prefix `sha256=`, then exactly 64 hex characters, upper or lower case, nothing before or after: no trimming) and `bool Verify(ReadOnlySpan<byte> body, string secret, ReadOnlySpan<byte> signature)` (HMAC-SHA256, compared only with `CryptographicOperations.FixedTimeEquals`). Used directly by the Atlassian vector test.
- Produces: `internal sealed class WebhookReplayGuard(IMemoryCache cache, TimeProvider time, IOptions<JiraOptions> options)`, **registered singleton** in `AddJiraCore`, with `bool TryAccept(string replayScope, ReadOnlySpan<byte> body, out string? reason)`:
  - parses `timestamp` with `MaxDepth = 32`; accepts only a JSON **number** that is an integer in `[0, 253402300799999]` (unit per Task 4; epoch milliseconds expected). A string, float, negative, too-large, boolean, object or missing value → false, never an exception;
  - rejects outside `±ReplayWindowMinutes`;
  - **atomic check-and-add** (P5): inside `lock (_gate)` on a private instance object, reject if `jira:wh:{replayScope}:{sha256 hex}` is cached, else cache it with absolute expiry = the window.
  - `replayScope` is the `automationId` route value, or `SHA-256(lower-cased ctx.Request.Path)` if Task 3 step 3 found no automation ID (P16).
- `BannedSymbols.txt` bans `M:System.Linq.Enumerable.SequenceEqual``1(System.Collections.Generic.IEnumerable{``0},System.Collections.Generic.IEnumerable{``0})` and the `System.MemoryExtensions.SequenceEqual` overloads, each with the message "Compare secrets with CryptographicOperations.FixedTimeEquals" (P5).

- [ ] **Step 1: Write failing tests.**
  - `Verify_AtlassianVector_Passes` (secret `It's a Secret to Everybody`, body `Hello World!`, header from Atlassian's docs copied into the test with its source URL; calls `JiraSignature.Verify` directly because the vector secret is under 32 characters).
  - `Validate_CapturedJiraDelivery_Passes` (`Fixtures/webhooks/issue_updated.signed.json` from Task 4, with `FakeTimeProvider` set to its `timestamp`); `Validate_NonAsciiBody_Passes` (self-signed body with "é" and an emoji). The captured delivery is the non-circular proof; the self-signed one guards the encoding path.
  - Fails for: missing header, empty header, `sha1=…`, wrong secret, odd-length or non-hex hex, null body; `Validate_MultipleSignatureHeaders_Fails`; `Validate_SignatureNot64Hex_Fails` (63 and 65 hex); `Validate_SecretShorterThan32_Fails` (and a warning naming `…:{name}:WebhookSecret`, no value); `Validate_MissingCredential_Fails`; `Validate_PrefixAllowListed_Fails` (council test 2 on the inbound path).
  - `TryParseHeader_Variants` theory (P5): `sha512=<128 hex>`, `SHA256=<64 hex>`, `Sha256=<64 hex>`, `sha256=<64 hex> ` (trailing space), `sha256=<64 hex>\t`, ` sha256=<64 hex>`, `sha256= <64 hex>` → false; `sha256=<64 upper-case hex>` → true (hex case does not weaken the HMAC).
  - Council test 9: `Validate_ForgedSignature_Fails` (valid shape, signed with another secret) and missing HMAC (above).
  - Replay: `Validate_TimestampOutsideWindow_Fails` (window + 1 minute, both directions); `Validate_TimestampMissing_Fails`; `Validate_TimestampOutOfRangeOrWrongType_Returns401NotThrow` theory (P5): `"1700000000000"` (string), `1.5`, `-1`, `1e30`, `253402300800000`, `true`, `{}`, `null` → `Validate` returns false and no exception escapes; `Validate_SameBodyTwiceWithinWindow_SecondFails`; `Validate_SameBodyDifferentIdentifierHeader_SecondFails`; `Validate_JiraRetryHeaderPresent_FirstSeenPasses`; `Validate_SameBodyAfterWindow_Passes` (cache expiry via `FakeTimeProvider`); `Replay_KeyIsPerScope`; `Replay_FallbackScope_UsesPathHash` (no `automationId` route value → scope from the path).
  - `Validate_ConcurrentSameBody_OnlyOneAccepted` (P5): 32 parallel `Validate` calls with the same valid request → exactly one true.
  - `Validate_AnyException_FailsClosed` (P5): the store throws, and separately the request body accessor throws → false, a warning logged with the exception type and no message text.
  - `Validate_SecretRotated_UsesNewSecret` (P4): a request signed with secret 1 passes; change the in-memory `WebhookSecret` to secret 2 and `Reload()`; a new body signed with secret 1 fails and one signed with secret 2 passes.
  - `Settings_FieldNamedCredentialName_NoFieldNamedSecret` (reflection); `CredentialHeaderNames_ContainsXHubSignature`.
  - `ArchitectureTests.JiraSignature_ComparesWithFixedTimeEqualsOnly` (P5): scan the IL of every method on `JiraSignature` (`MethodBody.GetILAsByteArray()`, resolve `call`/`callvirt` tokens with `Module.ResolveMethod`): it calls `CryptographicOperations.FixedTimeEquals` and calls none of `string.Equals`, `string.op_Equality`, `string.Compare`, `string.CompareOrdinal`, `Enumerable.SequenceEqual`, `MemoryExtensions.SequenceEqual`.
- [ ] **Step 2: Run** `dotnet test --filter "JiraSignatureWebhookAuthenticatorTests|JiraSignatureTests|WebhookReplayGuardTests|ArchitectureTests"`. Expected: FAIL.
- [ ] **Step 3: Implement.** Order: one header → `TryParseHeader` → secret via store (≥ 32 chars) → HMAC over `Encoding.UTF8.GetBytes(ctx.Body)` (or the buffered raw bytes if Task 3 step 2 found a mismatch) → `Verify` → `WebhookReplayGuard.TryAccept`. The replay check runs only after the HMAC passes, so unauthenticated traffic can't fill the cache. Registration stays in `JiraComposer`; `TimeProvider.System` registered if absent.
- [ ] **Step 4: Run** `./scripts/test.sh`. Expected: `Failed: 0`. Then add `new byte[1].SequenceEqual(new byte[1])` to `JiraSignature.cs` and build. Expected: build error from the banned-API analyser. Remove it.
- [ ] **Step 5: Manual check** with the demo site and `curl`: unsigned and wrongly signed POSTs to the webhook URL return 401 and the Runs list shows no new run; a valid one starts a run whose stored trigger headers lack `X-Hub-Signature`; **re-sending that exact valid request returns 401 and starts no run**, also with a changed `X-Atlassian-Webhook-Identifier`; a body with `"timestamp":"x"` returns 401, not 500. Then uninstall-simulate by removing the package registration (comment out the `Add<…>()` line), restart, re-send: expected 401 (the built-in plain-secret fallback finds no `Secret`, F-15). Restore the line.
- [ ] **Step 6: Per-task review.** Dispatch `qa-reviewer` and `security-analyst` (fresh context) with design §6.5, this task and the diff. Fix findings.
- [ ] **Checkpoint (Pascal commits):** `git add src tests BannedSymbols.txt` / suggested message `feat: add Jira webhook signature authenticator`

### Task 17: Read Jira webhook event action

**Files:** Create `Actions/ReadWebhookEvent{Action,Settings,Output}.cs`; Test `ReadWebhookEventActionTests.cs`.

**Interfaces:**
- Consumes: `AdfToText.Flatten` (Task 11), Task 2's `ConnectionTypeAlias` answer.
- Produces: `[Action("community.jira.readWebhookEvent", "Read Jira webhook event")]`, with `ConnectionTypeAlias = "community.jira.cloud"` only if Task 2 found it mandatory (connection then ignored; no dummy connection type). Settings `Body` default binding `${ trigger.body }`. Output exactly design §6.6, with `Changes` as `IReadOnlyList<JiraChange>` where `JiraChange(string Field, string? From, string? To)`.

- [ ] **Step 1: Write failing tests** from `Fixtures/webhooks/`: `IssueUpdated_StatusChange_SetsStatusChangedTo`; `IssueUpdated_NoStatusChange_StatusChangedToNull` (the comment-only update captured in Task 4); `CommentCreated_SetsPlainCommentBodyAndIssueKey`; `CommentUpdated_SetsPlainCommentBodyAndIssueKey` (the `comment_updated` fixture from Task 4, plan review P17); `IssueCreated_CommonFields`; `UnknownEvent_StillParsesCommonFields`; `MissingOptionalParts_AreNull` (no `changelog`, no `user`); `NotJson_FailsValidation`; `NoWebhookEvent_FailsValidation`; `DeeplyNestedJson_FailsValidation` (40 levels; `MaxDepth = 32`); `Timestamp_Parsed`; `Changes_OnlyFieldFromTo` (extra changelog properties such as `from`/`to` IDs are not output); `UntrustedFields_DescriptionsSayUntrusted` (reflection on `[Description]` of `Summary`, `CommentBody`, `UserDisplayName`, `Changes`). Also `MakesNoHttpCalls` (no `IJiraClient` registered, still passes).
- [ ] **Step 2: Run** `dotnet test --filter ReadWebhookEventActionTests`. Expected: FAIL.
- [ ] **Step 3: Implement** with `JsonDocument` (`MaxDepth = 32`) and tolerant lookups.
- [ ] **Step 4: Run** `./scripts/test.sh`. Expected: `Failed: 0`.
- [ ] **Step 5:** (Use case 4 moved to Task 17a, which owns the new action and the recipe.)
- [ ] **Step 6 (Pascal):** raise the public HQ issue on `umbraco/Umbraco.Automate` asking the webhook endpoint to route custom `IWebhookTrigger`s, linking this repo. Record the issue URL in `docs/spikes.md`. Nothing about secret scoping goes in this issue (that is the private report from Task 0 step 9).
- [ ] **Checkpoint (Pascal commits):** `git add src tests docs` / suggested message `feat: add Read Jira webhook event action`

### Task 17a: Find content by Jira key action and the use case 4 recipe (F-01)

**Files:** Create `Actions/FindContentByKey{Action,Settings,Output}.cs`, `demo/…TestSite/Seeds/usecase4.automation.json` (the use case 4 automation in the export shape recorded by Task 3 step 3b, `Enabled: false`), `demo/…TestSite/Seeds/SeedComposer.cs` (demo-only `IComposer` that imports the seed on first boot; not packed); modify `demo/…TestSite` (document type `article` with invariant Textstring `jiraKey`); Test `FindContentByKeyActionTests.cs` (unit: input validation, authorisation and call-count checks only, Examine and `IContentService` mocked), `tests/Umbraco.Community.Automate.Jira.IntegrationTests/FindContentByKeyIntegrationTests.cs` (real `InternalIndex`, both lines, on the route Task 2 step 3a chose), `UseCase4SeedTests.cs` (reads the seed file, linked into the test project as content).

**Interfaces:**
- Consumes: `JiraIdentifiers.TryIssueKey` (Task 6); Task 2's `ConnectionTypeAlias` answer; Automate's `IAutomationActionAuthorizer.AuthorizeContentOrFailAsync`; CMS `IExamineManager`, `IContentService`, `IContentTypeService`.
- Produces: `[Action("community.jira.findContentByKey", "Find content by Jira key", Group = "Content", RequiredSections = [Constants.Applications.Content], RequiredPermissions = [ActionBrowse.ActionLetter])] sealed class FindContentByKeyAction : ActionBase<FindContentByKeySettings, FindContentByKeyOutput>` (not `ICmsAction`; `ConnectionTypeAlias` only if Task 2 found it mandatory).
- Produces: `FindContentByKeySettings { string IssueKey (bindings); string PropertyAlias = "jiraKey" (no bindings); string ContentTypes (content type picker CSV of keys, no bindings, required) }`.
- Produces: `FindContentByKeyOutput { Guid ContentKey; string Name; string ContentTypeAlias; bool IsPublished; DateTime UpdateDate }`.
- Constants: `MaxCandidates = 10`; `PropertyAlias` regex `^[A-Za-z][A-Za-z0-9_]{0,63}$`.

- [ ] **Step 1: Write the failing integration test first** (it settles design §6.7's [LOW] on indexing): `InternalIndex_IndexesTextstringUnderAlias`: save (not publish) an `article` with `jiraKey` = `SUP-17`; the action's query `+__IndexType:content +jiraKey:"sup-17" +(__NodeTypeAlias:article)` returns it. If it does not, record the actual field name in `docs/spikes.md` and use it.
- [ ] **Step 2: Write failing integration tests** (plan review P2: every name-match negative runs against the real `InternalIndex` and `IContentService` on both lines, so a query on the node-name field would fail them):
  - `OneMatch_ReturnsContentKey` (key ` sup-17 ` normalised).
  - `NodeNamedLikeKey_EmptyProperty_NotFound` (a node **named** `SUP-17`, `jiraKey` empty → "not found").
  - `NodeNamedLikeKey_OtherNodeHoldsKey_ReturnsTheHolder` (node named `SUP-17` with empty `jiraKey`, plus a node named `Other` with `jiraKey` `SUP-17` → returns `Other`'s key).
  - `PartialValues_NotMatched` (`SUP-170`, `SUP-17 old`, `XSUP-17`, `SUP-17-1`).
  - `LuceneSpecialValues_NotMatched` (nodes whose `jiraKey` is `SUP-17*`, `SUP?17`, `"SUP-17"`, `SUP-17~`, `SUP\-17`; querying `SUP-17` matches none of them, and a node with exactly `SUP-17` is still found).
  - `TrashedNode_Ignored` (a trashed node holding the key is not returned; with only that node → "not found").
  - `TwoMatches_FailsListingKeys_NoContentKey`; `TwoMatches_OneOutsideStartNode_StillFails` (count before permission filtering).
  - `VariantProperty_ConfigurationError`.
- [ ] **Step 2a: Write failing unit tests** (inputs and call shape only):
  - `NoContentTypes_FailsBeforeQuery`; `OnlyStaleContentTypes_ConfigurationError`.
  - `IssueKey_Injection_FailsBeforeQuery` (`SUP-17" OR x:*`, `*`, `SUP-17 OR 1`).
  - `PropertyAlias_Invalid_FailsBeforeQuery` (`a:b`, `1a`, `a b`).
  - `TenCandidates_FailsTooMany`.
  - `NotAuthorised_ReturnsAuthorizerFailure`.
  - `UsesOneQueryAndOneBatchLoad` (pitfall P4: one `GetByIds` call, no `GetPagedDescendants`).
  - `MakesNoHttpCalls`.
- [ ] **Step 2b: Write failing `UseCase4SeedTests`** (plan review P1; the Critical F-01 guard, automated):
  - `UseCase4Seed_IsDisabled`: the seed's enabled flag is `false`.
  - `UseCase4Seed_EveryPublishPathPassesApprovalAndRecheck`: enumerate every path through the step graph (branches included) from the trigger to each step whose action alias is `umbracoAutomate.publishContent`; on each path, `umbracoAutomate.requestApproval` comes first, then (on its approved branch) `community.jira.findContentByKey`, then a Condition comparing `UpdateDate` with the first Find's output, all before the publish (design §6.8 steps 6, 8, 9).
  - `UseCase4Seed_HasStatusChangeConditionBeforeFind` (design §6.8 step 3: `StatusChangedFrom != StatusChangedTo`).
  - `UseCase4Seed_UsesSignatureAuthenticator` (trigger authentication is `community.jira.signature`).
  - `UseCase4Seed_NeverUsesNameMatch` (no step uses the built-in Find Content action).
- [ ] **Step 3: Run** `dotnet test --filter "FindContentByKey|UseCase4Seed"` and `dotnet test tests/Umbraco.Community.Automate.Jira.IntegrationTests`. Expected: FAIL.
- [ ] **Step 4: Implement** per design §6.7 behaviour steps 1 to 6.
- [ ] **Step 5: Run** `./scripts/test.sh` (extend it to also run the integration test project per line). Expected: `Failed: 0`. Mutation check: set the seed's enabled flag to `true`, and separately delete the Request Approval step from the seed → `UseCase4SeedTests` FAIL each time. Revert. Task 1a's CI runs these on every PR.
- [ ] **Step 6: Manual check, use case 4 recipe** (design §6.8) on the demo site, in a **dedicated workspace** whose service account has publish rights only on a `Jira pages` subtree and whose only member is a publisher. Jira admin webhook JQL `project = SBX AND status = "Ready to publish"`, credential name `sbxwh` with a 32+ character secret. Enable the seeded automation for the check, then disable it again. Expected, each with the run log as evidence:
  - Move `SBX-n` into "Ready to publish": the run pauses at Request Approval; the log shows the Jira `accountId` and display name; nothing is published yet.
  - Approve: the page publishes; the log shows `ApprovedByUserKey`.
  - Reject: nothing publishes.
  - Comment on the issue while it sits in the status: the run ends at the condition; nothing publishes (F-07).
  - Two pages with `jiraKey` `SBX-n`: the run fails at Find; nothing publishes.
  - A page **named** `SBX-n` with no `jiraKey`: not found.
  - Edit the page after the approval request, then approve: the run ends with "content changed after the approval request".
  - A page with the key outside the service account's start node: Find fails with the authoriser's failure; nothing publishes.
  - Re-send the captured delivery with `curl`: 401, no run (F-03).
- [ ] **Checkpoint (Pascal commits):** `git add src tests demo` / suggested message `feat: add Find content by Jira key action and approval-gated publish recipe`

---

## M4: Hardening, docs, CI

### Task 18: Cross-cutting secret-leak test and appsettings schema

The credential isolation tests moved to Task 6a as the contract's isolation suite (plan review P12); this task keeps the leak tests and the schema.

**Files:** Create `tests/.../SecretLeakTests.cs`, `src/.../UmbracoCommunityAutomateJiraSchema.cs`, `buildTransitive/Umbraco.Community.Automate.Jira.props`; modify the csproj (schema generation target as in Slack's csproj, file `appsettings-schema.Umbraco.Community.Automate.Jira.json`).

**Interfaces:** Consumes every action, the authenticator, the connection type, `JiraCredentialStore` and `TestHost` (every leak test composes the real services through `TestHost.Compose(primary: fake)`, logging captured at `Trace`, plan review P8).

- [ ] **Step 1: Write failing tests.**
  - `SecretLeakTests.NoSentinelAnywhere`: theory over each action × each fake response (200, 400, 401, 429, 500, 302, network error, timeout) with token `TOKEN-SENTINEL-<guid>` and webhook secret `SECRET-SENTINEL-<guid>` in the credential config; assert neither appears in any captured log line (all levels), `ActionResult` error message, exception chain or serialised output.
  - `SecretLeakTests.Webhook_NoSentinelAnywhere`: a request signed with `SECRET-SENTINEL-…` (valid, invalid, replayed) leaves no sentinel in logs, the authenticator's result or the trigger output.
  - `SecretLeakTests.Webhook_SignatureHeaderDeclaredForStripping` (plan review P9; replaces the old `Webhook_StoredRunHasNoXHubSignature`, which a unit test cannot prove because core strips the header): asserts `CredentialHeaderNames` contains `X-Hub-Signature` (ordinal-ignore-case) and the test's doc comment cites the Task 3 step 1 record that core removes listed headers from the stored trigger output. The end-to-end proof is Task 16 step 5 and Task 22 step 5.
  - `SecretLeakTests.SerialisedSettings_HoldOnlyNames` (council test 4): the connection and authenticator settings serialised by Automate's `EditableModelSerializer` (as stored in the database and exported) contain the credential name and no sentinel.
  - `Schema_ContainsJiraOptions`: generated schema has `Umbraco.Automate.Jira.TimeoutSeconds`, `IssueTypeCacheMinutes` and `ReplayWindowMinutes`, and no property under `Credentials` with a default value.
- [ ] **Step 2: Run** `dotnet test --filter "SecretLeakTests|Schema_"`. Expected: schema test FAIL; fix any leak test that fails rather than loosening it. If Task 0 was no-go, add the Steward tests from design §7.6 "Fallback" here.
- [ ] **Step 3: Implement** the schema class and build target.
- [ ] **Step 4: Run** `./scripts/test.sh` and `dotnet pack -p:AutomateMajor=18 -o out`. Expected: `Failed: 0`; `unzip -l out/*.nupkg` lists `appsettings-schema.Umbraco.Community.Automate.Jira.json` and `buildTransitive/`.
- [ ] **Checkpoint (Pascal commits):** `git add src tests` / suggested message `test: assert secrets never leak; ship appsettings schema`

### Task 19: Release and nightly workflows, contract tests

`build-test.yml`, the vulnerable check, gitleaks and Dependabot already run from Task 1a.

**Files:** Create `.github/workflows/pack-release.yml`, `nightly-contract.yml`, `scripts/pack.sh`, `scripts/check-packed-readme.sh`, `tests/Umbraco.Community.Automate.Jira.ContractTests/` with `LiveRoundTripTests.cs`; modify `.github/workflows/build-test.yml` (packed-README step).

**Interfaces:** Consumes `eng/lines.txt`, `IJiraClient` (adds `DeleteIssueAsync(JiraTarget, string key, CancellationToken)` to the test project's own helper, not the package).

- [ ] **Step 1: Write the contract tests** (two): `LiveRoundTripTests.CreateCommentTransitionDelete` and `LiveDedupe_SecondCreateWithSameKeyFindsFirst` (one issue, `WasExisting` on the second; records search lag). Both are skipped unless `JIRA_CONTRACT_SITE`, `JIRA_CONTRACT_EMAIL`, `JIRA_CONTRACT_TOKEN`, `JIRA_CONTRACT_PROJECT` are set. The token belongs to a separate sandbox-only service account (F-14). The tests map these env vars into an in-memory `Umbraco:Automate:Jira:Credentials:contract:*` section so they go through `JiraCredentialStore`.
- [ ] **Step 2: Run** `dotnet test tests/Umbraco.Community.Automate.Jira.ContractTests` without env. Expected: `Skipped: 2`, `Failed: 0`. Then with Pascal's sandbox env locally. Expected: `Passed: 2`.
- [ ] **Step 3: Write `scripts/check-packed-readme.sh <nupkg>`** (plan review P1): extracts `README.md` from the package and `cmp`s it with the repo `README.md`; exits 1 on any difference or if the package contains anything under `demo/` or a `*.automation.json`. Add a step to `build-test.yml` (floor leg) that packs the line and runs it. Expected: green.
- [ ] **Step 4: Write workflows.** `pack-release.yml`: on tag `release/*`, run the Task 1a checks first, derive minor/patch/suffix from the tag, run `scripts/pack.sh` for every line, run `check-packed-readme.sh` on each package, attach `.nupkg`/`.snupkg` to a GitHub release (NuGet push through trusted publishing if the account supports it, otherwise Pascal's scoped key in a secret; manual approval environment). `nightly-contract.yml`: cron, secrets from repo settings, never on PRs. Pin actions to commit SHAs.
- [ ] **Step 5: Verify** with a push to a branch and `act -l`. Expected: the packed-README step green in `build-test`; `pack-release` and `nightly-contract` listed.
- [ ] **Checkpoint (Pascal commits):** `git add .github scripts tests` / suggested message `ci: add release packing and nightly contract tests`

### Task 20: README, Marketplace and package metadata

**Files:** Modify `README.md`, `CHANGELOG.md`; create `SECURITY.md`, `docs/runbook.md`, `eng/gitleaks-jira-credentials.toml`, `umbraco-marketplace.json`, `umbraco-marketplace-readme.md`, `assets/logo-128.png` (generic, no Atlassian logo), `tests/.../ReadmeTests.cs` (README linked into the test project as content); modify csproj `Description`/`Title`.

- [ ] **Step 0: Write failing `ReadmeTests`** (plan review P1):
  - `Readme_UseCase4_WarningPrecedesRecipe`: the README has exactly one heading starting `Advanced: Jira-driven publishing`; the first non-blank block after it is a blockquote whose text contains "Anyone who can move a Jira issue into this status"; that blockquote comes before the first numbered recipe step in the section.
  - `Readme_PublishContent_OnlyInsideAdvancedSection`: "Publish Content" and `publishContent` appear only inside that section.
  - `Readme_UseCase4Recipe_ContainsRequestApproval`: the section's numbered steps include "Request Approval" before "Publish Content".
  Run `dotnet test --filter ReadmeTests`. Expected: FAIL until step 1 is written.

- [ ] **Step 1: Write README sections:**
  - Install per line (`17.x` vs `18.x`) and the version-line table with both Automate and CMS ranges.
  - **Credentials:** the config shape (design §7.6), with setup for Umbraco Cloud secrets, environment variables (`Umbraco__Automate__Jira__Credentials__<name>__ApiToken`), Azure Key Vault (`Umbraco--Automate--Jira--Credentials--<name>--ApiToken`) and `dotnet user-secrets`; verify the Cloud secret naming against Umbraco's current Cloud docs and cite the URL and date. Rotation results from Task 0 (which providers reload without a restart). The gitleaks rule in `eng/gitleaks-jira-credentials.toml` and the Validate warning for credentials in `appsettings*.json`.
  - **What the package does and does not protect:** the five claims in design §7.6, word for word in meaning.
  - **Service account requirements** (F-10, all mandatory): this site only, Jira product access only, project roles on target projects only, never a Jira or org admin, scoped token through the gateway with the exact scope list from Task 4, expiry 90 days or less. What the Validate warnings mean.
  - Connection fields; each action's inputs and outputs; the dedupe behaviour (default key, the keyed marker and what it does and does not hide, prefer non-personal `DedupeKey` values, set a per-item `DedupeKey` inside ForEach, the identical-items residual, the search-lag residual if Task 4 step 6a recorded one) and the `cj-dedupe-*` label if Task 4 chose labels.
  - Recipes for use cases 1, 2, 3 and 5, including the `jiraKey` invariant Textstring recipe. Use case 1 turns on Forms anti-spam and suggests a rate limit (F-19).
  - **Advanced: Jira-driven publishing (off by default)**, opening with the warning box from design §6.8, then the recipe and the mandatory setup (dedicated workspace, bounded service account, publisher-only membership, Jira JQL and transition guards, own webhook credential). No variant without Request Approval.
  - Markdown vs Plain text, recommending Plain text for public input; the 64 KB cap.
  - **Read Jira webhook event:** outputs labelled "untrusted user text"; recommend `| strip_html` or plain-text targets (F-17). Replay window and the per-node residual on load-balanced sites (F-03).
  - **GDPR (F-06):** form data to Jira is a transfer to a processor; record it as a processing activity (lawful basis, DPA with Atlassian, data residency); run records keep webhook bodies for `Umbraco:Automate:RunCleanup:RetentionDays` (default 90), so set it lower on these sites.
  - Recommend an Automate failure notification on automations using the connection (F-13). "Not affiliated with Atlassian" if Task 5 requires it.
- [ ] **Step 2: Write `SECURITY.md`:** how to report a vulnerability (private GitHub advisory), supported versions, the claims list, and a pointer to the runbook.
- [ ] **Step 3: Write `docs/runbook.md`:** token rotation (create new token, update the config value in each environment, Validate, revoke the old token); webhook secret rotation (Jira and config together; expect 401s in between); suspected exfiltration (revoke, rotate, check Jira audit log, check Automate runs for HTTP Request steps); the prefix guard tripping; **who else can rotate**, written as the literal placeholder `[[SECOND-PERSON]]` until Pascal names them (Task 22 step 8 refuses GA while it is there). **Upgrading Automate** checklist (plan review P19): read the new version's `AllowedConfigurationKeyPrefixes` defaults; confirm the CI latest leg (Task 1a) is green on it; re-run Task 0 step 7's surfaces (health checks, log viewer, Automate health and run views, Management API endpoints in Swagger) on a site with the new version; re-run Task 0 steps 3 and 4 if the resolver changed.
- [ ] **Step 4: Write `umbraco-marketplace.json`** with the title from Task 5, tags, and the Automate category, mirroring Slack's file. Listing stays off until GA.
- [ ] **Step 5: Verify** `dotnet pack -p:AutomateMajor=18 -o out && unzip -l out/*.18.*.nupkg | grep -E "README|logo-128|LICENSE"`. Expected: three matches. `grep -rn "TOKEN\|ATATT" README.md SECURITY.md docs/runbook.md` returns nothing. `grep -n "Secrets:Jira" README.md` returns nothing (no `$Umbraco:Automate:Secrets` setup in 1.0). Run `gitleaks detect --no-git --config eng/gitleaks-jira-credentials.toml` over a scratch folder holding an `appsettings.json` with a fake `Credentials` block: expected one finding. `dotnet test --filter ReadmeTests`: expected PASS. `./scripts/check-packed-readme.sh out/*.18.*.nupkg`: expected exit 0.
- [ ] **Checkpoint (Pascal commits):** `git add README.md CHANGELOG.md SECURITY.md docs/runbook.md eng umbraco-marketplace*.* assets src` / suggested message `docs: add README, security policy, runbook and Marketplace metadata`

### Task 20a: Deploy round-trip of a real connection and authenticator (before beta)

Moved forward from Task 22 step 6 (plan review P11), so the beta never freezes aliases on an unproven Deploy story. Closes Task 0 step 6 if it was UNKNOWN. **Owner: Pascal** (Cloud sandbox access). **Deadline: before Task 21.**

**Files:** Modify `docs/spikes.md`, `README.md` (Deploy section).

**Interfaces:** Consumes the packed `18.0.0` build (Task 18 step 4) and the demo automations. Produces "Deploy transfers Automate entities: yes | no | partly", council tests 4 and 5 results, and the stated residual, consumed by Task 20's README and Task 22 step 6.

- [ ] **Step 1 (Pascal):** on the Umbraco Cloud sandbox (Deploy enabled), install the locally packed `18.0.0` on a dev and a live environment. Set `Umbraco__Automate__Jira__Credentials__agency__Email`/`__ApiToken` and `…__agencywh__WebhookSecret` as Cloud secrets with **different** values per environment.
- [ ] **Step 2:** on dev, create the `community.jira.cloud` connection (`CredentialName` `agency`) and a webhook-triggered automation using `community.jira.signature` (`CredentialName` `agencywh`). Transfer both to live with Deploy. If Deploy does not offer Automate entities, use Automate's export and import instead and record that Deploy does not transfer them.
- [ ] **Step 3: Check the payload.** Inspect the Deploy transfer (or the export file) and the live database rows for the connection and automation. Expected: `agency` and `agencywh` present; no token, email or webhook secret value (`grep` for each value returns nothing).
- [ ] **Step 4: Check per-environment resolution.** Validate the connection on dev and on live. Expected: each shows its own service account's display name. Send a validly signed webhook (signed with each environment's own secret) to each environment: 202 on each; a body signed with dev's secret sent to live: 401.
- [ ] **Step 5: Record** in `docs/spikes.md`: whether Deploy transfers Automate connections and automations (yes, no, partly), council tests 4 and 5 pass or fail, and the residual (for example "Automate entities are not Deploy-aware; move them with export and import"). Put the user-facing outcome in the README's Credentials section.
- [ ] **Step 6: Gate.** Any value found in step 3, or the wrong account in step 4 → stop; fix with a failing test first before Task 21. UNKNOWN is not allowed past this task.
- [ ] **Checkpoint (Pascal commits):** `git add docs/spikes.md README.md` / suggested message `docs: record Deploy round-trip of credentials by name`

---

## M5: Beta, dogfood, GA

### Task 21: Beta release

- [ ] **Step 1:** Re-check design facts marked [verified] that the build relies on (latest Automate 17.x/18.x and CMS patches, ranges still correct, `AllowedConfigurationKeyPrefixes` defaults unchanged in any newer Automate patch) and update `docs/spikes.md`. Confirm the CI latest leg is green on the newest Automate patch of each line. If a newer Automate patch exists, re-run Task 0 steps 3, 4 and 7 (the council test 10 surfaces, plan review P19) on it before tagging. Confirm Task 20a is recorded with no UNKNOWN.
- [ ] **Step 2:** Run `./scripts/test.sh`, `scripts/vulnerable-check.sh` for each line, and the contract tests. Expected: all `Failed: 0`, no vulnerable packages, contract `Passed: 2`.
- [ ] **Step 3 (Pascal):** tag `release/0.0-beta.1` (or the agreed shape) and push. Expected: `pack-release` produces `18.0.0-beta.1` and, if `eng/lines.txt` has 17, `17.0.0-beta.1`; nuspec ranges as in Global Constraints. Aliases are now frozen.
- [ ] **Step 4 (Pascal):** push to NuGet (listed, pre-release).
- [ ] **Checkpoint (Pascal commits):** `git add CHANGELOG.md docs/spikes.md` / suggested message `chore: release 18.0.0-beta.1`

### Task 22: Dogfood on the sandbox Jira (final)

**Files:** none in the package unless bugs are found (each bug: failing test first, then fix, then the checkpoint).

- [ ] **Step 1: Use case 1, form → ticket.** On the demo site, then the agency site, with the beta from NuGet: a Forms "form submitted" trigger (alias from Task 3, Forms anti-spam on) → Create Jira issue in the sandbox project, summary and description bound from form fields, `DescriptionFormat` Plain text, connection `CredentialName` `agency` with the token in an environment variable or Cloud secret. Submit the form. Expected: a ticket in the sandbox project with the field values, the run shows `IssueKey` and `IssueUrl`.
- [ ] **Step 2: Negative and edge paths.** Submit with `[x](javascript:alert(1))` and an emoji in a field (ticket shows literal text); revoke the token (run fails `Authentication` with the 401 message); bad issue type (lists valid types); make the sandbox slow or block it after send with a proxy and let Automate retry (exactly one ticket, the retry reports `WasExisting`).
- [ ] **Step 3: Use case 4,** as Task 17a step 6, on the agency site, in its own workspace. Leave the automation **disabled** afterwards.
- [ ] **Step 4: Isolation check on the real site (council test 1).** An HTTP Request step referencing `$Umbraco:Automate:Jira:Credentials:agency:ApiToken` in a header to a request bin: the literal text arrives, never the token. Delete the step.
- [ ] **Step 5: Leak check (council test 4).** Search server logs, the run detail JSON, the `umbracoAutomate*` database tables and an automation and connection export for the token and webhook secret values. Expected: zero matches. Then `grep -c` on the log for `Authorization`. Expected: `0`.
- [ ] **Step 6: Deploy re-check (council tests 4 and 5)** with the beta from NuGet on the agency's Cloud site, repeating Task 20a steps 3 and 4 by the route Task 20a recorded (Deploy or export and import). Expected: the same results as Task 20a.
- [ ] **Step 7:** Run 2 to 4 weeks. Record issues in GitHub issues. Exit criteria for GA: no open bug labelled `security` or `data`, no duplicate tickets observed, the leak and isolation checks still clean.
- [ ] **Step 8 (Pascal):** GA. First `grep -rn "\[\[SECOND-PERSON\]\]\|TODO\|TBD" docs/runbook.md SECURITY.md README.md` must return nothing (plan review P21). Then tag the release for `17.0.0` + `18.0.0` (or `18.0.0` alone if Task 2 deferred 17), push to NuGet, switch on the Marketplace listing.
- [ ] **Checkpoint (Pascal commits):** `git add -A` / suggested message `chore: release 18.0.0` (and `17.0.0`)

---

## Later (separate plans)

No tasks here. Each gets its own plan when started.
- **P2a Named outcomes**: `Created`/`Failed`, `Transitioned`/`NotAvailable`/`AlreadyInStatus`, once Automate ships PRs #445/#449.
- **P2b OAuth 2.0 (3LO) connection** via `OpenIddict.Client.WebIntegration`'s Atlassian provider.
- **P2c Dynamic createmeta pickers**: Lit editor plus a `ManagementApiControllerBase` endpoint (pitfall S1).
- **P2d Native "Jira issue updated" trigger and dynamic webhooks**, only if HQ routes custom `IWebhookTrigger`s.
- **1.1 items**: Get issue, JQL search, attachments, links, assign by email; a distributed replay cache for load-balanced sites; an opt-in `$Umbraco:Automate:Secrets` credential mode only if adoption demands it, labelled "exposed to F-02".
- **Copy the credential helper** into the Teams packages (their own plans) per `credential-helper-contract.md` §4, once Task 6a has frozen it, if Task 0's gate row allows.

---

## Plan review changes (2026-10-30)

Source: `plan-review.md` (qa-reviewer, 0 blocker, 13 major, 8 minor) and Pascal's plan-review decisions of 2026-10-30 (portfolio README). Every finding is applied; where the fix differs from the reviewer's wording, the reason is given.

| ID | What changed |
|---|---|
| P1 | Task 17a: seed file `Seeds/usecase4.automation.json` plus `UseCase4SeedTests` (disabled; every path to `publishContent` passes `requestApproval`, the second Find and the `UpdateDate` condition; status-change condition; signature authenticator; no name match), with a mutation check. Task 20: `ReadmeTests` (warning box precedes the recipe; Publish Content only inside the advanced section). Task 19: `check-packed-readme.sh` gates the packed README in CI and rejects a packed seed. Task 3 step 3b records the export shape. |
| P2 | Task 17a: every name-match negative moved to `FindContentByKeyIntegrationTests` on the real `InternalIndex`, both lines, with the added "named node plus a different holder", Lucene-special and trashed cases. Task 2 step 3a records whether `Umbraco.Cms.Tests.Integration` is consumable per line and names the replacement (`WebApplicationFactory` over the demo site). |
| P3 | Task 3 step 3a checks `ActionContext` for a ForEach identity. Task 13: default key adds `IterationId` and an input fingerprint; `DedupeKeyTests` (stable, distinct per step, run, automation, item); `Dedupe_SearchFails_NoPost`; `Dedupe_ForEachTwoItems_TwoPosts`. **Applied differently:** the reviewer's fallback was "document the limitation and require `DedupeKey`". An action may not be able to detect that it is inside ForEach, so "require" may be unenforceable and would leave silent data loss. The input fingerprint removes the loss for items with different data; only identical items collapse, which the README documents. Task 14 uses the same rule for comment listing failures. |
| P4 | Task 7: `TryCreate_ReadsCredentialEachCall`, `NamedClient_HasNoDefaultAuthorizationHeader`. Task 7a: `Rotation_NextRequestUsesNewToken`. Task 12a: `Rotation_NextRunUsesNewToken` (action level). Task 16: `Validate_SecretRotated_UsesNewSecret`. |
| P5 | Task 16: `JiraSignature` split out; IL test `JiraSignature_ComparesWithFixedTimeEqualsOnly` plus a banned-API analyser on `SequenceEqual`; header variants (`sha512=`, upper-case prefix, white space; upper-case hex accepted); `Validate_TimestampOutOfRangeOrWrongType_Returns401NotThrow`; `Validate_AnyException_FailsClosed`; `WebhookReplayGuard` singleton with a locked check-and-add and `Validate_ConcurrentSameBody_OnlyOneAccepted`. The captured real delivery stays the non-circular proof. |
| P6 | Task 12: ordinal, case-sensitive key matching after unescaping; `customfield_[0-9]+`; duplicate keys rejected at any depth; ADF sanitised at any depth; `AdditionalFields_BypassShapes_Rejected`, `AdditionalFields_NestedAdf_Sanitised`. Task 12a: `AdditionalFieldsJson_AnyRejection_SendsNoRequest`. |
| P7 | Task 9: `Sanitiser_UnknownMark_Dropped`. Task 10: `Markdown_DisallowedLinkForms_NoLinkMark` (autolink, reference-style, `data:`, leading space), `Markdown_WwwAutolink_IsPlainText`, `Markdown_LinkTitle_Dropped`. |
| P8 | Task 7: `TestHost.Compose` builds the real services through `AddJiraCore` with `FakeJiraHandler` as the primary handler and logging captured at `Trace`; `NamedClient_HandlerChain_HasNoLoggingHandler_AndDisallowsRedirects`; `CapturingLogger_CapturesTrace`. Tasks 7a to 18 use only `TestHost`. |
| P9 | Task 18: the unprovable unit test is replaced by `Webhook_SignatureHeaderDeclaredForStripping`, which says what it proves; the end-to-end proof stays in Task 16 step 5 and Task 22 step 5. Task 4 step 5a: `FixtureScannerTests` with a synthetic allow-list. |
| P10 | Task 0 step 8 is now a gate table (both pass; 18 passes and 17 fails; 18 fails; UNKNOWN steps; time box expired) with Jira and Teams actions per row. Step 9 writes the result into the 03 plan. |
| P11 | New Task 20a (before beta): a Deploy, or export and import, round-trip of a real connection and authenticator, with per-environment secrets; records whether Deploy transfers Automate entities and the residual. Task 0 step 6 has an owner (Pascal) and a deadline (before Task 20a). Task 22 step 6 becomes a re-check. |
| P12 | New Task 1a straight after Task 1: `build-test.yml` (line × floor/latest matrix, `--locked-mode` on the floor leg only, vulnerable check, daily schedule), gitleaks, docs guard, Dependabot, and a branch ruleset. Per-line lock files (Task 1). The isolation tests moved into Task 6a and run on both legs, so every Automate bump re-runs them. Release and nightly workflows stay in Task 19. Org enrolment removed (Pascal: the repo's own CI). |
| P13 | Shared contract written to `../credential-helper-contract.md` (fixed behaviour, per-package parts with Teams enum members, copy checklist, generic suites C01 to C18 and I01 to I06 with isolation controls, version marker, divergence hash). Task 6a builds to it, runs both suites, and freezes it (hash, tag `credential-helper-1.0.0`). **Applied differently:** the reviewer proposed `docs/credential-helper.md` in the repo; per Pascal the contract lives in the private workspace and the repo's `docs/` keeps only design and plan (plus spikes and runbook). Teams copy from the public Jira repo at the tag. The startup check moved into the store file so the hash covers it. |
| P14 | Review Focus rewritten to lead with F-01, F-02, F-03, F-05 and F-04; the `ToString()` and non-ASCII items kept; surrogate, white-space and site-URL items demoted to "also pinned". |
| P15 | Name rule uses `\z` and an ASCII check (contract §1.2); contract C04 and C05 add newline and Unicode cases; Task 8 adds them on Validate. "Save" dropped from design §6.1's criteria rather than adding a save-time validator, because Automate offers no connection save hook this package relies on. |
| P16 | Task 3 step 3 records route values, path and headers seen by the authenticator; fallback replay scope is `SHA-256(lower-cased request path)`. Task 16 `Replay_FallbackScope_UsesPathHash`. |
| P17 | Task 4 captures `comment_updated`; Task 17 `CommentUpdated_SetsPlainCommentBodyAndIssueKey`. Task 3 step 3b records retry delays; Task 4 step 6a compares them with the search lag and records any residual for the README. |
| P18 | Marker is HMAC-SHA256 keyed with the connection ID (first 16 hex), recorded in Task 13 and design §6.2, with README guidance to prefer non-personal keys. Chosen over a new salt secret because it needs no extra config, survives token rotation, and is unknown to Jira users, who are the party the hash hides from. |
| P19 | Task 0 step 7 surfaces added to Task 21 step 1 and to the runbook's "Upgrading Automate" checklist (Task 20). |
| P20 | Design §7.3 rewritten with full signatures for all 11 `IJiraClient` methods (9 always, 2 conditional); plan tasks cite them. `GetMyPermissionsAsync` renamed `GetHeldPermissionsAsync`; `AddCommentAsync` takes a nullable marker; `JiraRequestException.RequestSent` added for Task 14's fallback. |
| P21 | Task 7 split into 7 and 7a; Task 12 split into 12 and 12a. Contract test counts fixed (two tests: `Skipped: 2` / `Passed: 2` in Tasks 19 and 21). Runbook placeholder is the literal `[[SECOND-PERSON]]`, and Task 22 step 8 greps for it before GA. |

Pascal's decisions applied alongside: repo public at Task 1 step 9 after a pre-public check; only `design.md` and `plan.md` copied into `docs/`, enforced by `docs-guard`; S7's core-gap evidence kept in the private workspace (`s7-core-gap-evidence.md`), not `docs/spikes.md`.

Task count: 25 before, 29 after (added 1a, 7a, 12a, 20a).

## Portfolio decisions applied after the plan-review fixes (2026-10-30)

These supersede anything above that conflicts with them.

- **Disclosure:** the repo stays **private** until Umbraco HQ responds to the private Automate security report, agrees to disclosure, or 90 days have passed since it was sent. Task 1 Step 9 (go public, enable CodeQL) is deferred until then.
- **While private:**
  - use Semgrep (in CI) instead of CodeQL;
  - features that need a public repo on GitHub Free (rulesets, environment reviewers) wait;
  - gitleaks, osv-scanner and the vulnerable-package check still run from Task 1.
- **Helper copies:** Teams copies the helper from the private Jira repo at tag `credential-helper-1.0.0`. Pascal has access, so being private doesn't block the copy.
- **Public docs, once public:** `design.md`, `plan.md` and `spikes.md` (plus `runbook.md`, user-facing). Never reviews, transcripts or `s7-core-gap-evidence.md`.

## Disclosure rule dropped (Pascal, 2026-11-03)

This supersedes every "private until HQ responds / disclosure" instruction above.

- **The repo goes public at scaffold (Task 1),** like the other packages, with CodeQL, a branch ruleset, gitleaks and the vulnerable-package check. Semgrep isn't needed.
- **Why:** the Automate secret-scoping behaviour is HQ's documented design (PR #71), so describing it isn't disclosure.
- **The isolation spike and the package-owned credentials stay exactly as planned.** They're still the right control.
- **The helper copy** comes from the public Jira repo at tag `credential-helper-1.0.0`.
- **The core-gap evidence file** can stay in the private workspace. It's not needed in the repo.
- **Announcements and HQ posts** no longer wait for "public". Post them when the plan says.
