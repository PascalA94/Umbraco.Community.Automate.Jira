# Commits to do (Pascal)

Append-only. Each checkpoint lists the files to add and a suggested commit message (no AI attribution). Run them in order. The build paused once (2026-11-03, at the coordinating session's request) and resumed on 2026-11-04.

## Checkpoint 1: Task 0 (credential isolation spike S7)

- **Add:** `git add .gitignore docs/design.md docs/plan.md docs/spikes.md spikes`
- **Message:** `docs: record credential isolation spike S7`
- **Notes:**
  - `spikes/CredentialIsolation/.gitignore` keeps out the raw evidence, logs, databases and build output. The raw evidence is in the private workspace, at `04-automate-jira/s7-evidence-2026-11-04/`.
  - The spike sites' generated imaging HMAC key and local admin password were removed first.
  - An empty folder `spikes/CredentialIsolation/Spike.CredentialStore/CredentialStore/` may be left over. Git ignores it, and you can `rmdir` it.
  - `.gitignore` and `docs/spikes.md` gained Task 1 lines later. Committing them here is fine, because the next checkpoints add them again.

## Checkpoint 2: Task 1 (scaffold, build switch S0)

- **Add:** `git add .gitignore Directory.Build.props Directory.Packages.props global.json NuGet.config Umbraco.Community.Automate.Jira.slnx LICENSE README.md CHANGELOG.md SECURITY.md BannedSymbols.txt umbraco-marketplace.json assets eng src tests scripts/test.sh docs/spikes.md`
- **Message:** `chore: scaffold package with AutomateMajor build switch`
- **Then (Task 1 step 9, Pascal):**
  - Make the repo public.
  - Enable the **CodeQL workflow** (`.github/workflows/codeql.yml`). Do **not** enable CodeQL "default setup", because it conflicts with the workflow.
  - Enable secret scanning with push protection.

## Checkpoint 3: Task 1a (CI)

- **Add:** `git add .github scripts docs/spikes.md`
- **Message:** `ci: build and test both lines on floor and latest Automate, with CVE, secret and docs checks`
- **Then (Pascal):**
  - Make the repo public **before** this first push. The CodeQL job's SARIF upload fails on a private Free-plan repo. Keep CodeQL non-required for fork PRs, because their read-only token can't upload.
  - Push a branch and open a draft PR.
  - Record the run URL and each leg's resolved Automate version in `docs/spikes.md` ("Task 1a"). That run clears F-16.
  - Add the `main` ruleset (Task 1a step 6): require a PR, require the four checks, block force pushes.

## Checkpoint 4: Task 2 (spike S1, demo site)

- **Add:** `git add tests/Umbraco.Community.Automate.Jira.Tests/Spikes eng demo docs/spikes.md`
- **Message:** `test: spike S1 secret features per Automate line`

## Checkpoint 5: Task 3 (spikes S2, S5, S6)

- **Add:** `git add docs/spikes.md`
- **Message:** `docs: record spikes S2, S5, S6`

## Checkpoint 6: Task 4 (spikes S3, S4, fixtures)

- **Add:** `git add scripts/spike_jira.py scripts/spike_jira_s4.py scripts/make-webhook-fixtures.py tests/Umbraco.Community.Automate.Jira.Tests/Fixtures tests/Umbraco.Community.Automate.Jira.Tests/FixtureScannerTests.cs tests/Umbraco.Community.Automate.Jira.Tests/Umbraco.Community.Automate.Jira.Tests.csproj docs/spikes.md`
- **Message:** `docs: record spikes S3, S4 and capture Jira fixtures`
- **Note:** the webhook fixtures are synthetic (no tunnel). See `docs/spikes.md` "Task 4".

## Checkpoint 7: Task 5 (alpha reservation)

- **Add:** `git add -A src tests docs/spikes.md`. The `-A` records the deletion of the spike no-op action and its test.
- **Message:** `chore: reserve package ID with 0.0.1-alpha`
- **Then (Pascal):**
  - Confirm the display title after reading Atlassian's trademark guidance (see `docs/spikes.md` "Task 5").
  - Pack with the command recorded there.
  - Push with a NuGet key scoped to this ID (push only, 90 days or less), then unlist.

## M0 review fixes (commit with checkpoint 7, or separately)

- **Add:** `git add Directory.Build.props Directory.Packages.props src/Umbraco.Community.Automate.Jira/Umbraco.Community.Automate.Jira.csproj src/Umbraco.Community.Automate.Jira/packages.17.lock.json src/Umbraco.Community.Automate.Jira/packages.18.lock.json README.md scripts .github demo tests/Umbraco.Community.Automate.Jira.Tests/FixtureScannerTests.cs tests/Umbraco.Community.Automate.Jira.Tests/Fixtures docs/spikes.md`
- **Message:** `fix: address M0 review (CVE pins build-only, per-line Markdig, fail-closed CVE check, docs-guard history, demo hardening)`

## Checkpoint 8: Task 6 (site URL, cloud ID, identifiers)

- **Add:** `git add src/Umbraco.Community.Automate.Jira/Connection tests/Umbraco.Community.Automate.Jira.Tests/JiraSiteUrlTests.cs tests/Umbraco.Community.Automate.Jira.Tests/JiraIdentifiersTests.cs`
- **Message:** `feat: validate Jira site URL, cloud ID and identifiers`

## Checkpoint 8a: Task 6a (package-owned credential store, credential-helper-contract 1.0.0 FROZEN)

Commit this **before** checkpoint 9, because the tag must point at this commit.

- **Add:** `git add src/Umbraco.Community.Automate.Jira/CredentialHelper src/Umbraco.Community.Automate.Jira/Configuration tests/Umbraco.Community.Automate.Jira.Tests/CredentialHelperContract tests/Umbraco.Community.Automate.Jira.Tests/JiraServiceCollectionExtensionsTests.cs tests/Umbraco.Community.Automate.Jira.Tests/JiraComposerTests.cs tests/Umbraco.Community.Automate.Jira.Tests/Umbraco.Community.Automate.Jira.Tests.csproj tests/Umbraco.Community.Automate.Jira.Tests/packages.17.lock.json tests/Umbraco.Community.Automate.Jira.Tests/packages.18.lock.json Directory.Packages.props`
- **Message:** `feat: add package-owned credential store to credential-helper-contract 1.0.0`
- **Notes:**
  - `Configuration/` also holds Task 7's options binding in `AddJiraCore`. If you want 6a alone, commit checkpoint 9 straight after it.
  - The frozen hash only covers the store and the two suite files, and those are final now.
- **Then (Task 6a step 9, Pascal):**
  - Tag this commit `credential-helper-1.0.0` and push the tag.
  - Tell the Teams and HubSpot owners the contract is frozen. Canonical hash: `39aebb90bfaa7d21e56711a62910483467639d58f7a5b1c38b1019804a71cdba`.

## Checkpoint 9: Tasks 7 and 7a (target, options, named client, client core)

The files are interleaved, so this is one commit.

- **Add:** `git add src tests`
- **Message:** `feat: add Jira target, options, named client and client core with safe logging`

## Paused here (2026-11-04)

- **Why:** Paused at Pascal's request via the coordinating session, at the checkpoint after Task 6a.
- **Next task:** Task 8 (connection type and Validate).
- **Running:** nothing. No subagents or background tasks are active.
- **State:**
  - `./scripts/test.sh` passes 250/250 on lines 17 and 18.
  - credential-helper-contract 1.0.0 is Frozen (hash `39aebb90…cdba`).
  - Checkpoints 1 to 9 above are ready to commit. Run checkpoint 8a before 9, then tag `credential-helper-1.0.0`.
- **Resume:** type "continue".

## Resumed (2026-11-06)

Resumed from the note above to finish M1 (Tasks 8 to 13). From now on there is **one commit per milestone** (portfolio README, token rule 8): the checkpoints below are a record, and the "Milestone commit" block at the end is what to run.

## Checkpoint 10: Task 8 (connection type and Validate)

- **Files:** `src/Umbraco.Community.Automate.Jira/Connection/JiraConnectionType.cs`, `src/Umbraco.Community.Automate.Jira/CredentialHelper/JiraCredentialSourceCheck.cs`, `Client/JiraClient.cs` (`GetHeldPermissionsAsync`), `Configuration/JiraServiceCollectionExtensions.cs`, tests `JiraConnectionTypeTests.cs`, `JiraCredentialSourceCheckTests.cs`, `Support/TestHost.cs`, the C28/C31 pins in `CredentialHelperContract/JiraCredentialStoreContractTests.cs` (the hashed suites are unchanged), and in `demo/`: the `OpenTelemetry.Api` reference and a DemoSpikes-only Validate driver.
- **Message (record only):** `feat: add Jira Cloud connection with one-click validation`
- **Not verified live:** the sandbox env vars were not set in this session, so Validate never reached Jira. See the to-do in the milestone block.

## Checkpoint 11: Task 9 (plain-text ADF converter and safety pass)

- **Files:** `src/Umbraco.Community.Automate.Jira/Adf/` (`JiraTextFormat`, `IAdfConverter` + `AdfResult` + `IAdfConverterFactory`, `AdfConverterFactory`, `PlainTextAdfConverter`, `AdfSanitiser`, `AdfLimits`), `Configuration/JiraServiceCollectionExtensions.cs`, tests `PlainTextAdfConverterTests.cs`, `AdfSanitiserTests.cs`, `Support/AdfSchemaAssert.cs`.
- **Message (record only):** `feat: add plain-text ADF converter and safety pass`

## Checkpoint 12: Task 10 (Markdown converter)

- **Files:** `Adf/MarkdownAdfConverter.cs`, `Adf/AdfInputTooLargeException.cs`, `Adf/AdfConverterFactory.cs`, the src csproj (direct `Markdig` reference), `Directory.Packages.props` and the test csproj (test-only oracle `Meziantou.Framework.AtlassianDataFormat` 1.0.2), the four `packages.*.lock.json` files, tests `MarkdownAdfConverterTests.cs`, `Fixtures/adf/*.md` and `*.golden.json`.
- **Message (record only):** `feat: add Markdown to ADF converter`

## Checkpoint 13: Task 11 (ADF JSON passthrough and ADF to text)

- **Files:** `Adf/AdfJsonPassthrough.cs` (with `AdfFormatException`), `Adf/AdfToText.cs`, `Adf/AdfConverterFactory.cs`, `Configuration/JiraServiceCollectionExtensions.cs`, tests `AdfJsonPassthroughTests.cs`, `AdfToTextTests.cs`.
- **Message (record only):** `feat: add ADF JSON passthrough and ADF flattening`

## Checkpoint 14: Task 12 (issue-type resolver, AdditionalFieldsJson rules, action failure helpers)

- **Files:** `Client/IssueTypeResolver.cs`, `Client/JiraClient.cs` (`GetIssueTypesAsync`), `Actions/ActionFailures.cs`, `Actions/AdditionalFields.cs`, `Configuration/JiraServiceCollectionExtensions.cs`, tests `IssueTypeResolverTests.cs`, `AdditionalFieldsTests.cs`, `ActionFailuresTests.cs`, `Support/TestConnections.cs`.
- **Message (record only):** `feat: add issue-type resolver and AdditionalFieldsJson rules`

## Checkpoint 15: Task 12a (Create issue action)

- **Files:** `Actions/CreateIssueAction.cs`, `Actions/CreateIssueSettings.cs`, `Actions/CreateIssueOutput.cs`, `Client/JiraClient.cs` (`CreateIssueAsync`), tests `CreateIssueActionTests.cs`, `Support/ActionRig.cs`, `Fixtures/requests/create-issue.json`.
- **Message (record only):** `feat: add Create Jira issue action`
- **Not run:** the plan's manual check (create an issue in SBX from the demo site), because the sandbox env vars were not set.

## Checkpoint 16: Task 13 (Create-issue dedupe)

- **Files:** `Actions/DedupeKey.cs`, `Client/JiraClient.cs` (`FindByDedupeAsync`), the Create issue files above, tests `DedupeKeyTests.cs`, `CreateIssueDedupeTests.cs`.
- **Message (record only):** `feat: make Create Jira issue safe to retry`

## M1 review fixes (record)

- **Reviews:** `security-analyst` approved with conditions (no Critical, no High). `qa-reviewer` passed with findings (3 major). Both reviewed the M1 files (Tasks 6 to 13). There is no `git diff` yet because the repo has no commits.
- **Fixed, each with a test that failed first:**
  - ADF JSON with a repeated key: it now fails as Validation and no longer echoes the key.
  - Reserved `cj-dedupe-*` labels are rejected.
  - `AdditionalFieldsJson` is capped at 64 KB.
  - A broken response body maps to ServiceUnavailable (sent).
  - Escaped-key F-04 cases have tests.
  - The schema validator has negative controls.
  - A For each loop without a readable index fails closed.
  - A non-list `labels` value in `AdditionalFieldsJson` fails, so the marker no longer silently replaces it.
  - createmeta now asks for 200 types.
  - The demo `demo/spike/*` endpoints need `X-Demo-Spike: 1`, a localhost Host and no foreign Origin.
- **Message (record only):** `fix: address M1 review (duplicate JSON keys, reserved dedupe labels, body-read failures, loop identity, demo endpoint guard)`

## Paused here (2026-11-06)

- **Why:** M1 is finished, and the rule is one milestone per session.
- **Next task:** Task 14 (Add comment action), the start of M2.
- **Running:** nothing. No subagents, background tasks or demo site are running.
- **State:**
  - `./scripts/test.sh` passes 610/610 on lines 17 and 18.
  - `scripts/vulnerable-check.sh 17` and `18` both exit 0.
  - `docs-guard` is ok.
  - gitleaks finds no leaks in the files `git add -A` would commit.
  - The M1 review is done and its fixes are in. Deferred minors are listed in the ledger (`.superpowers/sdd/plan/progress.md`, lines starting "Final:").
- **Carry into M2 and later:**
  - Task 17's webhook parser must run `JsonGuards.FindDuplicateKey` before `AdfToText.Flatten` (review QA-8).
  - Task 17a's integration project needs a direct `OpenTelemetry.Api` reference, the same as the demo.
- **Not verified live (your to-do, before Task 21):** the sandbox env vars (`JIRA_SANDBOX_SITE`, `Umbraco__Automate__Jira__Credentials__sandbox__Email|ApiToken`) were not set in this session.
  - Task 8 step 5 is still open: Validate success, the classic or gateway message, and the `appsettings.Development.json` warning.
  - Task 12a step 5 is still open: create an issue in SBX from the demo.
  - To run them, export the vars, start the demo with `-p:DemoSpikes=true`, then call `curl -s -H 'X-Demo-Spike: 1' -X POST 'http://localhost:5200/demo/spike/jira-connection?gateway=true'`.
  - Validate will fail with 401 until the token has `read:jira-user` (spike S3).
- **Resume:** run the milestone commit below, then start a fresh session and say "continue".

## Milestone commit (M0 and M1)

The repo has no commits yet, so this first commit holds M0 and M1 together. Checkpoints 1 to 16 above are the record of what is in it.

1. Check nothing unwanted is staged. There should be no `.db`, `umbraco/Data`, `bin/`, `obj/`, `out/`, `.superpowers/`, `appsettings.Local.json`, review or transcript files:

   ```sh
   cd ~/Projects/Umbraco.Community.Automate.Jira
   git status --short | grep -i -E '\.db|sqlite|umbraco/(data|logs)|/bin/|/obj/|^\?\? out/|\.superpowers|appsettings\.local|review|transcript' || echo "clean"
   ```

2. Commit:

   ```sh
   git add -A
   git commit -m "feat: Jira Cloud connection, ADF converters and retry-safe Create issue (M0, M1)" \
     -m "M0: credential isolation spike S7 (go on 17 and 18), AutomateMajor build switch, CI (build and test on floor and latest Automate, vulnerable check, gitleaks, docs guard, CodeQL), demo site, spikes S1 to S6, Jira fixtures, 0.0.1-alpha reservation build." \
     -m "M1: site URL, cloud ID and identifier validation; package-owned credential store (credential-helper-contract 1.0.0, frozen); options, named client and JiraClient with safe logging; community.jira.cloud connection with Validate and the committed-credential warning; plain text, Markdown and ADF JSON converters behind an allowlist ADF sanitiser; issue-type resolver; AdditionalFieldsJson rules; community.jira.createIssue with HMAC-marked, search-first dedupe; M1 review fixes."
   ```

3. Check: `git log -1 --stat --oneline | head -5`

4. Then tag the frozen contract: `git tag credential-helper-1.0.0`. The store and its hashed suites are unchanged since the freeze; C17 is green. Push the tag only when you push the repo (see checkpoints 2 and 3 for the go-public order).

## Resumed (2026-11-06, M2 session)

Resumed from the note above, with the M0 and M1 milestone commit in place (`9f3d302`), to run M2 (Tasks 14 and 15).

## Checkpoint 17: Task 14 (Add comment action)

- **Files:** `Actions/AddComment{Action,Settings,Output}.cs`, `Client/JiraClient.cs` (`AddCommentAsync`, `FindCommentByDedupeAsync`), tests `AddCommentActionTests.cs`, `Fixtures/requests/add-comment.json`.
- **Strategy:** read-first, because spike S4 probe (c) passed. Every attempt lists the 50 newest comments with `expand=properties` and posts only if its `community.jira.dedupe` marker is not there. A failed listing posts nothing.
- **Message (record only):** `feat: add Add Jira comment action`

## Checkpoint 18: Task 15 (Transition issue action)

- **Files:** `Actions/TransitionIssue{Action,Settings,Output}.cs`, `Actions/TransitionMatcher.cs`, `Client/JiraClient.cs` (`GetIssueStatusAsync`, `GetTransitionsAsync`, `DoTransitionAsync`), tests `TransitionIssueActionTests.cs`, `Fixtures/requests/transition-issue.json`.
- **Message (record only):** `feat: add Transition Jira issue action`
- **Not run:** step 5, the manual use case 3 check on the demo site (Content Published → Get Content Property → Add comment → Transition to Done). The sandbox env vars were not set in this session.

## M2 review fixes (record)

- **Reviews:** both read the M2 diff (`9f3d302` to the working tree) and the files it touches.
  - `qa-reviewer`: passed with findings (1 major, 5 minor).
  - `security-analyst`: approved with conditions (no Critical, no High; 2 medium conditions, 3 low).
- **Fixed, each with a test that failed first:**
  - A comment's dedupe marker now includes the issue. The oldest matching comment wins, so a marker copied onto another issue or onto a newer comment can't suppress a post (SEC-01).
  - A matched transition whose target is the current status is now a no-op. A retry can no longer re-apply a global transition by ID or name (SEC-02).
  - A retry by a transition name Jira no longer offers fails with a message saying the issue may already have moved and to use the status name (QA M2-1). The field help now recommends the status name.
  - Comment dedupe now has tests proving it is stable on retry, distinct per step, run, automation and loop item, and fails closed when the loop index can't be read (QA M2-2).
- **Message (record only):** `fix: address M2 review (issue-scoped comment marker, global-transition retries, retry guidance)`
- **Deferred minors and rulings:** listed in the ledger (`.superpowers/sdd/plan/progress.md`, lines starting "M2:").

## Paused here (2026-11-06, after M2)

- **Why:** M2 is finished, and the rule is one milestone per session.
- **Next task:** Task 16 (signature authenticator `community.jira.signature`), the start of M3. It gets a per-task review from `qa-reviewer` and `security-analyst`, and so does Task 17a (find content).
- **Running:** nothing. No subagents, background tasks or demo site are running.
- **State:**
  - `./scripts/test.sh` passes 674/674 on lines 17 and 18.
  - `scripts/vulnerable-check.sh 17` and `18` both exit 0.
  - `docs-guard` is ok.
  - gitleaks finds no leaks in the M2 file set.
- **Carry into M3 and later:**
  - Task 17's parser runs `JsonGuards.FindDuplicateKey` before `AdfToText.Flatten` (from M1).
  - Task 17a's integration project needs a direct `OpenTelemetry.Api` reference.
  - Task 18: add a control-character and bidi strip (`SafeText`) for Jira-supplied names in logs, errors and outputs (SEC-03).
  - Task 20: the README documents three things:
    - the 50-comment dedupe window and that concurrent runs aren't serialised (SEC-04, QA M2-3);
    - that Transition is safest as a status name;
    - that a self-loop transition is skipped.
- **Your decisions and to-dos:**
  - **UNKNOWN (SEC-03 severity):** does the Automate backoffice HTML-encode step log lines and error messages? If not, Jira-supplied names in errors are a stored-XSS question, not a low finding.
  - **Design wording:** design 6.4 says the already-in-status check "makes it idempotent". That now holds for a status name, and for a transition still offered after the move. It doesn't hold for a transition name or ID that Jira stops offering, which fails with guidance. Update 6.4 if you agree.
  - **Live checks (before Task 21):** still open from M1, plus Task 15 step 5. Export `JIRA_SANDBOX_SITE` and `Umbraco__Automate__Jira__Credentials__sandbox__Email|ApiToken`, start the demo with `-p:DemoSpikes=true`, and run use case 3 on SBX.
- **Resume:** run the milestone commit below, then start a fresh session and say "continue".

## Milestone commit (M2)

1. Check nothing unwanted is pending. There should be no `.db`, `umbraco/Data`, `bin/`, `obj/`, `out/`, `.superpowers/`, `appsettings.Local.json`, review or transcript files:

   ```sh
   cd ~/Projects/Umbraco.Community.Automate.Jira
   git status --short | grep -i -E '\.db|sqlite|umbraco/(data|logs)|/bin/|/obj/|^\?\? out/|\.superpowers|appsettings\.local|review|transcript' || echo "clean"
   ```

2. Commit:

   ```sh
   git add -A
   git commit -m "feat: add Add comment and Transition issue actions (M2)" \
     -m "community.jira.addComment with read-first dedupe: an issue-scoped HMAC marker stored as a comment property, the oldest match wins, and a failed listing posts nothing. community.jira.transitionIssue with ID, name and status matching, ambiguity errors, and an already-in-status no-op that also covers global transitions. Plain-text transition comments and an optional resolution. M2 review fixes."
   ```

3. Check: `git log -1 --stat --oneline | head -5`

## Resumed (2026-11-11, M3 session)

Resumed from the note above, with the M2 milestone commit in place (`796766f`), to run M3 (Tasks 16, 17 and 17a). The sandbox env vars were not set in this shell (checked by name only), so nothing here reached Jira.

## Checkpoint 19: Task 16 (signature authenticator `community.jira.signature`)

- **Files:**
  - `Webhooks/` (`JiraSignature.cs`, `JiraSignatureSettings.cs`, `WebhookReplayGuard.cs`, `JiraSignatureWebhookAuthenticator.cs`)
  - `Configuration/JiraServiceCollectionExtensions.cs` (the guard is a singleton)
  - `BannedSymbols.txt`, plus the package csproj (`Microsoft.CodeAnalysis.BannedApiAnalyzers`, RS0030 as an error)
  - `Directory.Packages.props`
  - tests `JiraSignatureTests.cs`, `JiraSignatureWebhookAuthenticatorTests.cs`, `WebhookReplayGuardTests.cs`, `Support/WebhookRig.cs`, `ArchitectureTests.cs` (the IL scan), and the message-only `KeyFor` pin in `CredentialHelperContract/JiraCredentialStoreContractTests.cs` (the store and hashed suites are unchanged)
  - the removed stub test
  - the demo `webhook-jira` scenario
- **Per-task review:**
  - `qa-reviewer`: passed with 6 minor findings.
  - `security-analyst`: approved with conditions (2 medium, 6 low, no Critical).
  - Fixed, each with a test that failed first:
    - **SEC16-1:** `RequiresBody` is now true, so a slow sender can no longer hold a thread. A body whose decoded length differs from `Content-Length` fails closed.
    - **SEC16-2:** the replay guard has its own bounded cache and refuses deliveries when it is full.
    - **The exact window edges.**
    - **QA16-1 to QA16-6.**
    - **SEC16-5, 7 and 8.**
- **Manual check (demo, line 18, locally generated secret, no Jira):**
  - Unsigned, wrongly signed, re-sent, re-sent with a new identifier, and a string timestamp all returned 401.
  - A valid request started one run, with no `X-Hub-Signature` stored.
  - The uninstall simulation returned 401.
- **Message (record only):** `feat: add Jira webhook signature authenticator`

## Checkpoint 20: Task 17 (Read Jira webhook event action)

- **Files:** `Actions/ReadWebhookEvent{Action,Settings,Output}.cs`, tests `ReadWebhookEventActionTests.cs`.
- **Behaviour:**
  - No connection needed.
  - A duplicate key, depth over 32, invalid JSON or a missing `webhookEvent` fails as Validation.
  - A comment body is accepted as a string or as ADF.
  - The status change is found by `fieldId` first.
- **Message (record only):** `feat: add Read Jira webhook event action`
- **Step 6 (yours):** the public HQ issue on `umbraco/Umbraco.Automate`.

## Checkpoint 21: Task 17a (Find content by Jira key, use case 4 recipe)

- **Files:**
  - `Actions/FindContentByKey{Action,Settings,Output}.cs`, `Actions/ActionFailures.cs`
  - tests `FindContentByKeyActionTests.cs`, `UseCase4SeedTests.cs`, and the new `tests/Umbraco.Community.Automate.Jira.IntegrationTests/` (NUnit, Umbraco's integration harness, a real InternalIndex, its own `.gitignore`)
  - `Umbraco.Community.Automate.Jira.slnx`, `Directory.Packages.props`
  - the test csproj, which links the seed
  - `demo/.../Seeds/usecase4.automation.json` (disabled) and `Seeds/SeedComposer.cs` (Development only: creates `article`, and imports the seed as a draft once a `jira-pages` workspace exists)
  - the demo `seed-workspace` and `seed-status` scenarios
  - `docs/spikes.md`
- **Integration route:** the plan's primary route now works. See `docs/spikes.md` "Task 17a" for the SetUpFixture fix.
- **Message (record only):** `feat: add Find content by Jira key action and approval-gated publish recipe`
- **Not run:** step 6, the manual use case 4 recipe on SBX. It needs the sandbox, a webhook (tunnel) and a restricted workspace.

## M3 review fixes (record)

- **Reviews:** both read the M3 diff (`796766f` to the working tree) and the files it touches.
  - `qa-reviewer`: passed with findings (2 major, 6 minor).
  - `security-analyst`: approved with conditions (4 medium, 4 low, no Critical).
- **Fixed, each with a test that failed first (or a mutation that is now caught):**
  - The harness's index and schema files are now ignored.
  - The F-01 seed tests parse the conditions exactly: one AND group, exact operands, `IsNotEmpty` on every recheck operand, and an allowlist of actions. Seven seed mutations now fail.
  - Find uses the property's real alias, trims ASCII whitespace only, and checks Browse.
  - The status change is found by `fieldId`, and the seed also requires the issue's status to equal the new status.
  - The approval prompt names the node key and type, and the Jira account ID.
  - The demo seeder runs in Development only.
- **Message (record only):** `fix: address M3 review (exact F-01 seed checks, status by fieldId, find alias and whitespace, approval prompt)`
- **Deferred minors and rulings:** listed in the ledger (`.superpowers/sdd/plan/progress.md`, lines starting "Task 16", "Task 17", "Task 17a" and "M3").

## Checkpoint: Task 18 (record)

- **Message (record only):** `test: assert secrets never leak; ship appsettings schema`
- `SecretLeakTests` (38 cases: three actions and Validate, each against 200, 400, 401, 429, 500, 302, network error and timeout; signed, wrong, replayed and unsigned webhooks; the signature header declared for stripping; stored settings hold names only).
- The schema now lists `TimeoutSeconds`, `IssueTypeCacheMinutes` and `ReplayWindowMinutes`; credentials have no defaults.
- SEC-03: every name and error message Jira supplies passes through `SafeText` (control characters become spaces, bidi controls are removed).
- `./scripts/test.sh`: 921/921 unit and 11/11 integration on 17 and 18.

## Checkpoint: Task 19 (record)

- **Message (record only):** `ci: add publish, release checks and live contract tests`
- `publish.yml` (tag `v18.<minor>.<patch>[-suffix]` packs every line; trusted publishing behind the `nuget` environment), `release-checks.yml` (by hand: live contract tests in the `jira-sandbox` environment, integration tests on SQL Server), `scripts/pack.sh`, `scripts/check-packed-readme.sh`, the packed-README step in `build-test.yml`.
- Portfolio CI rules that were missing: osv-scanner job and `osv-scanner.toml`, NuGet cache, Dependabot `open-pull-requests-limit: 0`.
- Contract tests: 2 skipped without env on both lines. **Not run live** (no `JIRA_CONTRACT_*` in this session).

## Checkpoint: Task 20 (record)

- **Message (record only):** `docs: add README, security policy, runbook and Marketplace metadata`
- README rewritten for 1.0 (credentials, claims, service account, every action, dedupe, webhooks, recipes, the Advanced publishing section behind its warning, GDPR, configuration). `ReadmeTests` pin the use case 4 section.
- `SECURITY.md`, `RUNBOOK.md` (repo root: `docs/` keeps only design, plan and spikes), `eng/gitleaks-jira-credentials.toml`, `umbraco-marketplace.json`, `umbraco-marketplace-readme.md`, `CHANGELOG.md`, issue templates, `.github/CONTRIBUTING.md`, package description.

## Task 20a (Pascal, not started)

- Deploy round-trip on Umbraco Cloud. Blocks Task 21 (beta). See plan Task 20a. Also check whether Cloud accepts `Umbraco__Automate__Jira__Credentials__…` secrets (see the pause note).

## Paused here (2026-11-11, after M3) (superseded by the M4 note below)

- **Why:** M3 is finished, and the rule is one milestone per session.
- **Next task:** Task 18 (cross-cutting secret-leak test and appsettings schema), the start of M4.
- **Running:** nothing. No subagents, background tasks or demo site are running.
- **State:**
  - `./scripts/test.sh` passes on lines 17 and 18: 871/871 unit tests and 11/11 integration tests each.
  - `scripts/vulnerable-check.sh 17` and `18` both exit 0.
  - The demo builds on 17 and 18.
  - `docs-guard` is ok.
  - gitleaks finds no leaks in the M3 file set.
- **Your decision, before beta (security review SEC-M3-4, medium):**
  - Publish Content publishes whatever draft exists when it runs. A save that lands between the UpdateDate recheck and the publish step is published without approval.
  - Option 1: accept it as a residual and document it in design 6.8 and the README.
  - Option 2: add a package publish step that checks UpdateDate and publishes in one scope. That is a design change (solutions-architect).
- **Carry into M4 and later:**
  - **Task 18:** `SafeText` for Jira-supplied names (SEC-03, from M2). Optionally rate-limit the "secret too short or missing" warning (SEC16-5).
  - **Task 20, the README:**
    - one credential name per Jira webhook (SEC16-4);
    - the duplicate check is only as fresh as the InternalIndex (QA-M3-6, SEC-M3-6);
    - run logs hold Jira account IDs (SEC-M3-8);
    - the per-node replay cache;
    - the M2 items: the 50-comment window, Transition by status name, and the self-loop skip.
  - **For Teams (03):** don't copy the Jira-specific signature parts (UTF-8 key, `sha256=` hex header, epoch-millisecond body timestamp). Copy the fixed guard (SEC16-6).
- **Your to-dos (before Task 21):**
  - **Live checks:** export `JIRA_SANDBOX_SITE` and `Umbraco__Automate__Jira__Credentials__sandbox__Email|ApiToken` (and a 32+ character `…__sbxwh__WebhookSecret`), then run:
    - the M1 and M2 checks (Validate, create issue, use case 3);
    - Task 17a step 6, use case 4 on SBX. Use a `jira-pages` workspace whose service account can publish only the Jira pages subtree. Do not use the demo `seed-workspace` scenario's admin account for this.
  - **Step 6 must also confirm:**
    - the real authoriser against a node outside the start node;
    - that `steps.find.*` bindings resolve;
    - how the If compares the two UpdateDates (sub-second precision);
    - the approver output name (the seed logs `${ steps.approve }` for now);
    - the 72-hour timeout outcome.
  - **Capture a real Jira delivery through a tunnel:** the timestamp unit, whether retry bodies are identical, and the comment body shape. Then replace the synthetic signed fixture.
  - **Task 17 step 6:** raise the public HQ issue.
  - **Still open from M2:**
    - SEC-03: does the backoffice HTML-encode step logs?
    - The design 6.4 wording.
- **Resume:** run the milestone commit below, then start a fresh session and say "continue".

## Milestone commit (M3) (done: c977442)

1. Check nothing unwanted is pending. Use `-uall`, so files inside new folders are listed. There should be no `.db`, `umbraco/Data`, index, `bin/`, `obj/`, `.superpowers/`, `appsettings.Local.json`, review or transcript files:

   ```sh
   cd ~/Projects/Umbraco.Community.Automate.Jira
   git status --short -uall | grep -i -E '\.db|sqlite|umbraco/(data|logs)|ExamineIndexes|TEMP/|/bin/|/obj/|\.superpowers|appsettings\.local|review|transcript' || echo "clean"
   ```

2. Commit:

   ```sh
   git add -A
   git commit -m "feat: add Jira webhook authenticator, webhook parser and Find content by Jira key (M3)" \
     -m "community.jira.signature: one X-Hub-Signature, HMAC-SHA256 compared in fixed time only (banned-API analyser and IL test), secret of 32+ characters from the package credential store, body read by the endpoint and refused if decoding changed it, timestamp window and an atomic replay check in the guard's own bounded cache, fails closed on any exception. community.jira.readWebhookEvent: typed, minimal fields from the webhook body, duplicate keys and deep nesting refused, status change by fieldId, untrusted text labelled. community.jira.findContentByKey: one InternalIndex query and one batch load, exact draft match, counted before permissions, stale types and variant properties fail closed, integration tests on a real index for both lines. Demo: disabled use case 4 seed with approval and UpdateDate recheck, pinned by seed graph tests. M3 review fixes."
   ```

3. Check: `git log -1 --stat --oneline | head -5`

## M4 review fixes (record)

- **Reviews:** both read the M4 diff (`c977442` to the working tree) and the files it touches.
  - `qa-reviewer`: passed with findings (1 major, 10 minor).
  - `security-analyst`: approved with conditions (5 medium, 7 low, no Critical).
- **Fixed, each with a test that failed first or a mutation that is now caught:**
  - The sample gitleaks rules now catch flat keys, `.env` and `launchSettings.json`, and no longer fire on Umbraco's own `Email` settings. `scripts/check-gitleaks-sample.sh` proves it in the gitleaks CI job.
  - Transition's echo of the requested value goes through `SafeText`.
  - The leak tests run each action's real success path, prove the credential was sent, and prove the leak check can fail.
  - Schema tests check the root wiring and that every `JiraOptions` setting is in the schema.
  - Workflows: anchored CHANGELOG check, numeric line guard in publish, no NuGet cache on the job holding the sandbox token.
  - Docs: the GitHub and nuget.org settings the release relies on (CONTRIBUTING), Cloud warnings, the runbook keeps evidence, two more use case 4 residuals, Marketplace wording.
- **Message (record only):** `fix: address M4 review (gitleaks sample rules, leak test coverage, release settings, docs)`
- **Deferred minors:** pin the SQL Server image by digest; name the approver output once confirmed; screenshots and `icon.svg`.

## Paused here (2026-11-11, after M4)

- **Why:** M4 is finished, and the rule is one milestone per session.
- **Next task:** Task 21 (beta), the start of M5. **It is blocked** until your items below are done.
- **Running:** nothing. No subagents, background tasks or demo site are running.
- **State:**
  - `./scripts/test.sh` passes on lines 17 and 18: 929/929 unit tests and 11/11 integration tests each.
  - The contract tests skip (2 per line) without `JIRA_CONTRACT_*`. **They have not run live.**
  - `scripts/vulnerable-check.sh 17` and `18` exit 0. The demo builds on 17 and 18. `docs-guard` ok. actionlint clean.
  - `pack.sh` 17 and 18 plus `check-packed-readme.sh`: ok. gitleaks finds no leaks in the M4 file set.
  - **CI has not run on GitHub.** The new workflows (`publish.yml`, `release-checks.yml`, osv-scanner, the packed-README and gitleaks-sample steps) are only linted.
- **Your decisions (Pascal decides):**
  1. **Umbraco Cloud secret names (new in M4).** Umbraco Cloud's Secrets Management page (read 2026-11-11) lists `UMBRACO_` as a restricted prefix, and its allowed `Umbraco__…` prefixes don't include `Umbraco__Automate__`. Cloud may refuse `Umbraco__Automate__Jira__Credentials__…`. Check it in Task 20a. If Cloud refuses and Support won't allow it, choosing another key source is a design change (solutions-architect, then you).
  2. **SEC-M3-4** (still open from M3): accept the save-between-recheck-and-publish residual (the README now documents it), or add a guarded publish step before beta.
  3. **SEC-M4-7:** name the second person who can rotate credentials (`RUNBOOK.md`, `[[SECOND-PERSON]]`).
- **Your to-dos before Task 21:**
  - **Task 20a:** the Deploy round-trip on an Umbraco Cloud sandbox (plan Task 20a), including whether Cloud accepts the secret names.
  - **GitHub and nuget.org settings** listed in `.github/CONTRIBUTING.md` under "Repository settings the release relies on": the `v*` tag ruleset, the `nuget` and `jira-sandbox` environments (reviewers, branch rules, secrets) and the NuGet trusted publishing policy.
  - **First CI run:** push, open the PR, check every job, then run `release-checks` by hand. Copy the osv-scanner check's exact name into the ruleset.
  - **Contract tests live:** set `JIRA_CONTRACT_SITE`, `JIRA_CONTRACT_EMAIL`, `JIRA_CONTRACT_TOKEN`, `JIRA_CONTRACT_PROJECT` (and `JIRA_CONTRACT_CLOUDID` for the scoped token) for a sandbox-only account, then run `dotnet test tests/Umbraco.Community.Automate.Jira.ContractTests -p:AutomateMajor=18`. Expected: `Passed: 2`. It prints the dedupe search lag.
  - **Carried from M3:** the M1 and M2 live checks, Task 17a step 6 (use case 4 on SBX, with the extra confirmations listed in the M3 note), the real Jira delivery capture through a tunnel, and the scope list check (a token with exactly `read:jira-work`, `write:jira-work` and `read:jira-user`).
  - **Presentation:** 2-3 demo screenshots with fake data for the README and `umbraco-marketplace.json`.
  - Task 22 step 8 greps `docs/runbook.md`. The runbook is now `RUNBOOK.md` at the root, so grep that instead.
- **Resume:** run the milestone commit below, do the blocking items, then start a fresh session and say "continue".

## Milestone commit (M4)

1. Check nothing unwanted is pending. Use `-uall`, so files inside new folders are listed. There should be no `.db`, `umbraco/Data`, index, `bin/`, `obj/`, `.superpowers/`, `appsettings.Local.json`, review or transcript files:

   ```sh
   cd ~/Projects/Umbraco.Community.Automate.Jira
   git status --short -uall | grep -i -E '\.db|sqlite|umbraco/(data|logs)|ExamineIndexes|TEMP/|/bin/|/obj/|\.superpowers|appsettings\.local|review|transcript' || echo "clean"
   ```

   It printed "clean" on 2026-11-11. If anything is listed, stop and check it.

2. Commit:

   ```sh
   git add -A
   git commit -m "feat: harden secrets, add release workflows and 1.0 docs (M4)" \
     -m "Secret-leak tests across every action, Validate and the webhook path, with a positive control. Jira-supplied names and messages pass through SafeText (control and bidi characters). appsettings schema lists every option. CI: publish.yml (one v tag packs every line, trusted publishing behind the nuget environment), release-checks.yml (live contract tests, integration on SQL Server), osv-scanner, packed-README and gitleaks-sample checks, NuGet cache, Dependabot limit 0. Live contract test project. README, SECURITY.md, RUNBOOK.md, CONTRIBUTING, issue templates, Marketplace metadata, sample gitleaks rules. M4 review fixes."
   ```

3. Check: `git log -1 --stat --oneline | head -5`
