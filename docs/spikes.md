# Spikes

Results of the plan's spikes and gates, oldest first.

## Task 0: credential isolation spike S7 (M0 gate 1)

- **Date:** 2026-11-03 to 2026-11-04.
- **Gate row:** "Steps 3 and 4 pass on both, step 5 or 6 UNKNOWN". **GO on both lines (17 and 18)** with package-owned credentials.
  - Step 6 (Umbraco Cloud) is UNKNOWN. It is carried by name to Task 20a, owner Pascal.
  - Until it is closed, the README rotation section says "not verified" for Cloud.
- **Teams (03) and HubSpot (05):** Go, with the same residual. They copy the helper once Task 6a freezes the contract.
- **Reviews:** a fresh-context per-task review by qa-reviewer and security-analyst found no Critical. The fixes are listed at the end of this section.

### Set-up

- **Sites:**
  - `site17`: Umbraco.Cms 17.8.0 + Umbraco.Automate 17.5.1.
  - `site18`: Umbraco.Cms 18.3.0 + Umbraco.Automate 18.5.1.
  - Both use SQLite, were created with `dotnet new umbraco`, and were installed unattended.
  - Automate needs its own connection string, so both set `Umbraco:Automate:UseNamedConnectionString = umbracoDbDSN`.
- **Spike projects:** `Spike.CredentialStore`, and `Spike17` (the same sources built against 17.5.1). They hold:
  - `JiraCredentialStore`, written to `credential-helper-contract.md` §1;
  - a test-only action, `spike.readCredential`. It returns success and the first 8 hex characters of the value's SHA-256, never the value;
  - an endpoint that only answers in Development, on loopback. It creates a one-step automation, runs it through Automate's real executor (reference resolver included), and returns the stored step result.
- **Echo listener:** `EchoListener` on `http://localhost:5099` logs each request's URL and headers.
- **Loopback workaround:** Automate's HTTP client blocks loopback addresses (an SSRF guard), and that guard is only the client's primary handler. The spike replaces that primary handler so the echo listener can receive requests. Reference resolution happens in `ResolveSettings`, before any transport, so this does not change what is resolved.
- **Config values, all fake:**
  - `Umbraco:Automate:Secrets:SpikeX` (the control);
  - `Umbraco:Automate:Jira:Credentials:spike:ApiToken` (the package value), changed between phases to a fresh sentinel so that each scan is unambiguous.

### Step 3: council test 1 (isolation): PASS on 17 and 18

| Step settings (built-in HTTP Request) | Result on both lines (listener log and stored step result) |
|---|---|
| URL `…/pkg-url?q=$Umbraco:Automate:Jira:Credentials:spike:ApiToken` | Arrived literal: the listener shows the reference text. |
| Header `X-Test: Bearer $Umbraco:Automate:Jira:Credentials:spike:ApiToken` | Arrived literal. |
| Header `X-Test: $Umbraco:Automate:Jira:Credentials:spike:ApiToken` (whole value) | Not permitted. The step failed with `ConfigurationError`: "Configuration key 'Umbraco:Automate:Jira:Credentials:spike:ApiToken' is not permitted in settings." Nothing was sent. |
| URL `$Umbraco:Automate:Jira:Credentials:spike:ApiToken` (whole value) | Not permitted, as above. |
| Control: a `Umbraco:Automate:Secrets` reference in a header, and separately in the URL | The control reached the listener from the header, so the harness can see a leak. The URL reference was refused, because `Secrets` keys resolve only into sensitive fields. |
| `spike.readCredential` | `ok: true`. |

The package value appeared 0 times in the listener log, step results and site logs.

### Step 4: council test 2 (guard): PASS on 17 and 18

`Umbraco:Automate` was added to `Umbraco:Automate:AllowedConfigurationKeyPrefixes` (defaults kept), and the site was restarted. Then:
- **The value leaked.** The listener log shows the HTTP Request step sending the package value in both the URL and the header. The allow-list is the only barrier.
- **The store refused.** `spike.readCredential` returned `ok: false` with "Refused: Umbraco:Automate:AllowedConfigurationKeyPrefixes contains 'Umbraco:Automate', which exposes Umbraco:Automate:Jira:Credentials. Remove that entry."
- **The startup check logged.** That line was logged once at Critical (`FTL` in Serilog) on each site.

The entry was then removed.

### Step 5: council test 6 (rotation)

| Provider | Result (both lines) |
|---|---|
| `appsettings.Development.json` (`reloadOnChange`) | The new value was read on the next run, **with no restart**. The SHA-256 prefix changed from `77337dc0` to `2f669228`. |
| Environment variable `Umbraco__Automate__Jira__Credentials__spike__ApiToken` | Read after a restart (`3d7a31d7`), overriding the JSON value. A running process cannot see a changed environment variable, so env-var rotation **needs a restart** by construction. This half was not run. |

### Step 6: council test 5 (per-environment resolution on Umbraco Cloud): UNKNOWN

No Cloud sandbox was available. Owner: Pascal. Deadline: before Task 20a starts. Task 21 may not start while this is UNKNOWN.

### Step 7: council test 10 (other surfaces) and the resolver route

**Surfaces.** On each site a fresh sentinel value was configured and the isolation cases were re-run. Then `spikes/CredentialIsolation/scan-surfaces.py` ran as an admin-group API user and did the following:
- **Plain GETs:** called every GET with no path parameter in the Management API and Automate Management API OpenAPI documents (default page sizes).
- **Single-parameter GETs:** called every single-parameter GET with every GUID seen in earlier responses.
- **Run data:** called Automate run detail, step-run data and trigger data for every run.
- **Version history:** called version history for every automation, workspace and connection.
- **Health checks:** ran every health check group (7 on each site, including "Umbraco Automate").
- **Logs:** read the log viewer API.
- **Files on disk:** searched the log files and the SQLite database, including `-wal` and `-shm`.

| Line | API calls | Sentinel found |
|---|---|---|
| 17 | 4,412 | 0 in responses, 0 in files |
| 18 | 4,829 | 0 in responses, 0 in files |

**Positive control: the scanner can see a stored leak.**
- **How it was set up:** the step 4 runs (allow-list widened) sent the package value through the HTTP Request URL. Automate stores the resolved URL in that step run's input, because the URL is not a sensitive field.
- **What the scan found:** scanning for that value found it once in `GET runs/{id}/step-runs/{stepRunId}/data` and once in the SQLite file, on both lines.
- **Earlier miss:** the first version of the scanner missed it, because it skipped routes with two path parameters. It now fills those from the parent run.

**Limits of the scan:**
- The backoffice UI was not opened in a browser. The scan covers the API that the UI calls.
- POST endpoints (test, validate, export) were not called.
- Routes with non-GUID path parameters were skipped, apart from the version-history entity types listed above. The scan output lists them.
- List endpoints were read at their default page size.

**Resolver.**
- **Constructors checked:** the shipped `Umbraco.Automate.Core` 17.5.1.0 and 18.5.1.0 assemblies were checked by metadata, covering every constructor in Core, Web, Persistence and Startup.
- **What takes `IConfiguration`:** `ConfigurationReferenceResolver(IConfiguration, IOptions<AutomateOptions>)`, plus `UmbracoAmbientAutomateConnection` and `AutomateSchemaInitializer`. The last two read only the database connection string.
- **Result:** no other type serves step settings from configuration.
- **Source at both tags:**
  - The resolver reads `AllowedConfigurationKeyPrefixes` (default `Umbraco:Automate:Secrets`, `Umbraco:Automate:Variables`). It matches segment-aware and case-insensitive, against the list it read at startup.
  - A whole-value reference outside the list throws "not permitted". An embedded one stays literal.
  - `${ }` bindings resolve only `trigger`, `steps`, `previous` and `loop`, after reference resolution.

**Route for the isolation suite (contract §5.2). It is the same on both lines and was executed on both:**
1. Automate's own registration: not usable. `AddUmbracoAutomateCore` is internal and needs `IUmbracoBuilder`.
2. `Umbraco.Automate.Testing`: not usable. `ActionTestHarness` registers a mocked `IEditableModelResolver`, so it never resolves references.
3. **Reflection on the internal types (chosen):**
   - create `Umbraco.Automate.Core.Settings.ConfigurationReferenceResolver(IConfiguration, IOptions<AutomateOptions>)` with `Activator.CreateInstance`;
   - create `Umbraco.Automate.Core.Settings.EditableModelResolver(IConfigurationReferenceResolver)` the same way;
   - then call the public constructors `new ActionInfrastructure(editableModelResolver)` and `new HttpRequestAction(infrastructure, httpClientFactory, Options.Create(new ExecutionOptions()))`;
   - pass settings through `action.ResolveSettings(...)`, as `ActionStepBody` does, then call `ExecuteAsync`;
   - give the `UmbracoAutomate` named client a capturing handler.

   `spikes/CredentialIsolation/ResolverRouteProbe` runs this route against 17.5.1 and 18.5.1. All of I01 to I06 pass on both lines, including both leak controls.

### Residual (for the README and SECURITY.md, the same text for Teams and HubSpot)

- **Widening the allow-list exposes the credential.** If `AllowedConfigurationKeyPrefixes` is widened to cover `Umbraco:Automate:Jira:Credentials`, every automation author can send the credential to any host (step 4).
- **The guard detects but does not prevent.** It refuses the package's own reads and logs Critical at startup.
- **Stored values outlive the fix.** Values resolved while the entry was present stay in run records (default retention 90 days) after the entry is removed. A whole-value reference into a number or boolean field can put the value into the stored run error.
- **What to do:** after any exposure window, rotate the credential and purge the run records from that window.
- **Qualify the claim.** "No Automate step can resolve the Credentials section" holds only while the allow-list does not cover that prefix.
- **Allow-list changes need a restart.** Core's resolver reads the allow-list once at startup.

### Step 8: gate

- **Evidence:**
  - Steps 3 and 4 pass on both lines.
  - No step 7 surface showed the package value.
  - Step 6 is UNKNOWN.
- **Decision:** the first matching row is **"Steps 3 and 4 pass on both, step 5 or 6 UNKNOWN": Go**, with the UNKNOWN carried to Task 20a (owner Pascal).
- **Consequence:** `eng/lines.txt` keeps `17` and `18`.

### Evidence

- **Where it is kept:** the raw evidence (step results as JSON, listener logs, site logs, scan outputs) is git-ignored here. It is kept outside the repo until GA, with a `SHA256SUMS` file covering 94 files.
- **Why it stays out of the repo:** it includes the control value reaching the listener, which the plan keeps out of the repo.
- **Commands:**
  - Re-run a case: `spikes/CredentialIsolation/run-case.sh <17|18> <case> <tag>`.
  - Re-run the route probe: `dotnet run --project spikes/CredentialIsolation/ResolverRouteProbe/Probe18.csproj` (and `Probe17.csproj`).

### Review fixes

- **qa-reviewer:**
  - Retained the listener log and re-ran steps 3 and 4.
  - Added a `Secrets`-in-URL control case.
  - Executed the resolver route instead of only reading it.
  - Removed the scanner's ID cap, scanned SQLite `-wal`/`-shm`, and printed the skipped routes and the sentinel's hash.
  - Corrected the env-var wording.
  - Added a Development-only check on the spike endpoint.
- **security-analyst:**
  - Removed the generated imaging HMAC key and the local admin password from the spike sites' config.
  - Rewrote the residual.
  - Carried four contract changes to Task 6a, all fail-closed or test-only:
    - the guard also checks core's startup snapshot of the allow-list;
    - undefined credential parts are refused;
    - a test pins the `IConfiguration` consumers in Automate;
    - the contract text is updated for `GetExposingPrefixes()` and the `CredentialHelper/` folder.
  - Package-action leak tests (foreign host, redirects, stored step data) go into Task 18.

## Task 1: build switch spike S0 (M0 gate 2)

- **Date:** 2026-11-04.
- **Gate row:** **GO.** One codebase builds both lines. The fallback (18 only on `main`, a `v17` branch) is not needed.
- **TFM per line:** `net10.0` on both 17 and 18, set once in `Directory.Build.props`. One test project covers both.
- **Files that needed `#if`:** none in `src/`. The only `#if AUTOMATE_17` / `AUTOMATE_18` is `tests/Umbraco.Community.Automate.Jira.Tests/BuildSwitchTests.cs`, which exists to prove the constant. The gate allows about 3.
- **What differs between lines:** only the dependency set, the closed ranges, the package version and the `AUTOMATE_n` constant. Both lines compile the same files against `ActionBase`, `ActionResult`, `WebhookAuthenticatorBase`, `AutomateWebhookAuthenticators()` (namespace `Umbraco.Automate.Extensions` on both) and `ActionTestHarness`.
- **Evidence:** `./scripts/test.sh` prints `Passed!  - Failed:     0, Passed:     4` for line 17 and for line 18. `dotnet pack` produces `Umbraco.Community.Automate.Jira.17.0.0.nupkg` and `.18.0.0.nupkg`. The nuspec ranges are `Umbraco.Automate.Core [17.5.1, 18.0.0)` with `Umbraco.Cms.Core` and `Umbraco.Cms.Infrastructure` `[17.8.0, 18.0.0)` on 17, and `[18.5.1, 19.0.0)` with `[18.3.0, 19.0.0)` on 18.
- **Step 4 note:** the plan expects the first run to fail until the test project references `Umbraco.Automate.Testing`. It did not, because `Umbraco.Automate.Core` flows through the project reference, so `BuildSwitchTests` passed at once. The red run is the two tests that need the not-yet-written no-op action and authenticator stub (compile error, types absent).
- **Package versions:** every version lives in `Directory.Packages.props`, not on the `PackageReference` (central package management forbids `Version=` there). The properties `AutomateVersionSpec` and `UmbracoCmsVersionSpec` hold the closed range, or `$(AutomateMajor).*` when `AutomateFloat=true`.
- **Pre-public check (step 8):**
  - `ls docs` shows `design.md`, `plan.md`, `spikes.md` only.
  - The `git log --all` search returns nothing, because the repository has no commits yet. Re-run it on the first commit.
  - Both lock files exist for each project: `packages.17.lock.json` and `packages.18.lock.json` in `src/` and in `tests/`.
  - `grep -rn "SECRET-SPIKE-X" docs` is **not** empty: it matches `docs/plan.md` lines 128, 129 and 163, which name the fake control value in the plan's own instructions. `docs/spikes.md` and `docs/design.md` are clean. The value is a fake sentinel, not a secret. Pascal decides whether to reword those three lines of the plan before the repo goes public.
- **Gitleaks (local, same 8.30.1 as CI):** a `--no-git` scan of the files that will be committed found no leaks.

## Task 1a: CI from day one

- **Status:** the scripts are tested locally. **The first CI run is pending Pascal's push.** Until then these stay **UNKNOWN**: the `floor` and `latest` legs on a Linux runner, the `latest` leg's resolved Automate version on CI (locally both lines resolve `17.5.1` and `18.5.1`), and the F-16 clearance of transitive CVEs in CI. After the push, record the run URL and each leg's resolved `Umbraco.Automate.Core` version here.
- **Local evidence:**
  - `./scripts/vulnerable-check.sh 17` and `18` exit 0, on the locked floor and with `float`.
  - The `latest` commands from `build-test.yml` pass locally on both lines and leave the committed lock files unchanged.
  - `./scripts/docs-guard.sh`: RED (exit 1) with `docs/security-review.md` added by `git add -N`, GREEN (exit 0) once removed.
  - `actionlint` 1.7.12 is clean on both workflows.
- **F-16 transitive pins** (`CentralPackageTransitivePinningEnabled`, in `Directory.Packages.props`). Restoring Umbraco CMS 18.3.0 and Automate flagged the following on the first `vulnerable-check.sh 18` run. Each pin is the lowest patched version on nuget.org:

  | Package | Resolved before | Severity | Advisory | Pinned to |
  |---|---|---|---|---|
  | `OpenTelemetry.Api` | 1.1.0 | moderate | GHSA-g94r-2vxg-569j | 1.15.3 |
  | `MessagePack` | 3.1.8 | moderate | GHSA-qhrr-8q5h-9q3h | 3.1.9 |
  | `System.Security.Cryptography.Xml` | 10.0.10 | high (three advisories) | GHSA-r955-8q5j-3h83, GHSA-7hqp-7c5m-4vq6, GHSA-c399-9853-6jmj | 10.0.13 |

  `System.Security.Cryptography.Pkcs` was not flagged on this restore. Re-check on every Automate or CMS bump.
- **Pinned tools:** `actions/checkout` v7.0.1, `actions/setup-dotnet` v6.0.0 and `github/codeql-action` v4.39.1, each by full commit SHA with the tag in a comment. Checked 2026-11-04 with `git ls-remote`: checkout `3d3c42e…` = tag v7.0.1, setup-dotnet `a98b568…` = tag v6.0.0, codeql-action `9b32540…` = the commit tag v4.39.1 points to. gitleaks 8.30.1 with the SHA-256 from the release's published checksums file.
- **CodeQL** is an advanced-setup workflow (`.github/workflows/codeql.yml`, C#, default queries). It cannot run beside the repository's CodeQL "default setup", so Task 1 step 9 should enable the workflow and leave default setup off. Secret scanning with push protection is still a repository setting.

## Task 2: S1, secret features on Automate 17.5.1 (go/no-go for the 17 line)

- **Date:** 2026-11-04.
- **Gate:** **GO.** All three reflection tests pass on 17.5.1 and on 18.5.1, so `eng/lines.txt` stays `17` and `18`. The 17 line ships at 1.0.
- **`ConnectionTypeAlias` is optional: yes.** `new ActionAttribute("x", "y").ConnectionTypeAlias` is `null`. `StepTypeAttribute.ConnectionTypeAlias` is a `string?` with a public setter and no default. In practice, the demo-only actions `demo.dumpContext` and `demo.failTransient` have no connection, and both registered and ran on both lines. Tasks 3, 17 and 17a can leave the alias off `readWebhookEvent` and `findContentByKey`.
- **Tests:** `tests/Umbraco.Community.Automate.Jira.Tests/Spikes/SecretFeatureSpikeTests.cs`, kept as regression tests (the CI "latest" leg re-runs them on every Automate release).
  - The reflection tests were written first. They passed on first run because they check surface that already exists, so there was no red phase to record.
  - Result per line (`./scripts/test.sh --filter "FullyQualifiedName~SecretFeatureSpikeTests"`):

    | Test | 17.5.1 | 18.5.1 |
    |---|---|---|
    | `EditableModelField_HasIsSensitive` (public bool, readable and writable) | pass | pass |
    | `WebhookAuthenticatorBase_HasCredentialHeaderNames` (virtual) | pass | pass |
    | `ActionAttribute_ConnectionTypeAlias_IsOptional` | pass | pass |

  - Tail: `Passed!  - Failed:     0, Passed:    12, ...` for 17 and for 18. The 12 includes the S6 tests added in Task 3 step 5.
- **Task 0 fallback not needed.** Task 0 did not fall back to option 2, so the "sensitive field holding `$Umbraco:Automate:Secrets:SpikeToken`" repeat check does not apply.

### Step 3: the demo site

- **Project:** `demo/Umbraco.Community.Automate.Jira.TestSite`. It uses SQLite and a `ProjectReference` to `src/Umbraco.Community.Automate.Jira`.
- **Layout (ruling):** one csproj for both lines, with its own `demo/Directory.Build.props` and `demo/Directory.Packages.props`.
  - Why not the root props: `ManagePackageVersionsCentrally` at the root would make the site's own `PackageReference`s fail. The root props also add lock files, pack metadata and the documentation file that a demo does not want.
  - `demo/Directory.Build.props` does not import the root file. It switches on the same `-p:AutomateMajor=17|18` (default 18): CMS 17.8.0 and Automate 17.5.1, or CMS 18.3.0 and Automate 18.5.1. Forms versions are in the same switch (step 4).
  - Per-line `obj17`, `obj18`, `bin17` and `bin18` keep the two restores apart. They are listed in `DefaultItemExcludes` because the SDK only excludes `obj/` and `bin/`; without that, the build fails with CS0579 duplicate attributes.
  - The package project receives the same global property through the `ProjectReference`, so the package is built for the same line.
  - The demo is not in `Umbraco.Community.Automate.Jira.slnx`, so `./scripts/test.sh` does not build it. `dotnet pack` of the package contains no demo file (`unzip -l ...18.0.0.nupkg | grep -ci "demo\|TestSite"` prints `0`).
  - The demo has a transitive pin for `OpenTelemetry.Api 1.15.3` (GHSA-g94r-2vxg-569j), as the root props do.
- **Boot commands.** Run from the repo root. First boot installs unattended; the admin password comes from the environment only and is never written to a file in the repo.

  ```sh
  cd demo/Umbraco.Community.Automate.Jira.TestSite
  export ASPNETCORE_ENVIRONMENT=Development
  export Umbraco__CMS__Unattended__InstallUnattended=true
  export Umbraco__CMS__Unattended__UnattendedUserName="Demo Admin"
  export Umbraco__CMS__Unattended__UnattendedUserEmail="demo-admin@example.invalid"
  export Umbraco__CMS__Unattended__UnattendedUserPassword='<choose a throwaway password>'
  dotnet run -p:AutomateMajor=18      # http://localhost:5200, https://localhost:5500
  # or: dotnet run -p:AutomateMajor=17
  ```

  - **Switching lines:** a SQLite database upgraded by one Umbraco major cannot be opened by the other. Delete `umbraco/Data` (and `umbraco/Logs`) before switching, or keep one folder per line. These paths are git-ignored.
  - **Generated secrets:** `Umbraco:CMS:Imaging:HMACSecretKey` is blank in the committed `appsettings.json`. Umbraco writes a fresh key into that file on first boot, so before booting set `Umbraco__CMS__Imaging__HMACSecretKey` as an environment variable (any random value) and check `git diff demo/` is empty before committing. `appsettings.Local.json` is git-ignored and loaded in Debug builds.
  - **Spike endpoints:** `Spikes/` (controller and demo actions) compiles only with `-p:DemoSpikes=true`. Never run a tunnel to the demo while it is compiled in: the controller's loopback check passes for tunnelled requests.
  - **Calling them (from 2026-11-06):** every `demo/spike/*` request needs the header `X-Demo-Spike: 1`, a `localhost` Host and no foreign `Origin`, so a web page in the developer's browser cannot drive them cross-site (M1 security review). Example: `curl -s -H 'X-Demo-Spike: 1' -X POST 'http://localhost:5200/demo/spike/jira-connection?gateway=true'`.
  - **Port 5500 (HTTPS)** is needed because back-office tokens are only issued over HTTPS. It uses the local .NET dev certificate.
- **Evidence:** `dotnet build -p:AutomateMajor=17` and `-p:AutomateMajor=18` both end `Build succeeded.` with 0 errors. On each line the site answered `GET /` with 200, then ran a webhook automation (Task 3 step 1) end to end.
- **Demo-only code** (never in `src/`): `Spikes/SpikeController.cs` and `Spikes/DemoActions.cs`.
  - The controller answers only in the Development environment and only for loopback callers.
  - It creates an API-user service account, a workspace and automations through Automate's own services, and reads the stored runs back.
  - The pattern is the one from the Task 0 spike's `SpikeRunController`.

### Step 3a: integration-test route (plan review P2)

- **`Umbraco.Cms.Tests.Integration` is published and consumable: yes on 17.8.0 and on 18.3.0.** In a fresh project under each line, `dotnet add package Umbraco.Cms.Tests.Integration --version <v>` and `dotnet add package Umbraco.Automate.Core --version <17.5.1|18.5.1>` restore and `dotnet build` ends `Build succeeded.`. A fixture deriving from `UmbracoIntegrationTest` compiles on both.
- **Constraints Task 17a must plan for:**
  - The package is **NUnit-based**: it depends on `NUnit 3.7.0`, `Moq 4.7.0` and `Microsoft.NET.Test.Sdk >= 18.10.1`. A fresh xUnit project fails restore with `NU1605` (the template's `Microsoft.NET.Test.Sdk 17.14.1` is a downgrade).
  - So the integration project must be separate from `Umbraco.Community.Automate.Jira.Tests` (which is xUnit with `Microsoft.NET.Test.Sdk 17.13.0` in the root `Directory.Packages.props`). It needs NUnit and `Microsoft.NET.Test.Sdk >= 18.10.1`, through its own version or a `VersionOverride`.
  - The package ships `content/appsettings.Tests.json` (SQLite), but NuGet did not copy it to the output in a plain SDK project. Copying it in by hand did not fix the next problem.
- **Not proven (UNKNOWN):** running a test. A trivial `[UmbracoTest(Database = NewSchemaPerTest)]` fixture failed on both lines with `System.ApplicationException : Unsupported test database provider` from `TestDatabaseFactory.Create`. Setting `Tests:Database:DatabaseType` to `SQLite`, `Sqlite`, `LocalDb` or garbage (file or environment variable) gave the same error each time, so the cause is not that setting. I stopped after a time box of about 20 minutes and did not decompile.
- **Route (ruling), per line:**
  - **17 and 18: `Umbraco.Cms.Tests.Integration` in a separate NUnit project is consumable but not yet shown to boot.** Task 17a's first step is to get one fixture running under each line (start with HQ's own test project settings and `TestDatabaseFactory`). If it cannot be made to run within a short time box on a line, that line uses the replacement route.
  - **Replacement route (both lines, already viable):** `tests/Umbraco.Community.Automate.Jira.IntegrationTests` boots `demo/...TestSite` in-process with `WebApplicationFactory<Program>` on a temporary SQLite file. The demo site boots on both lines (step 3), and it needs a `public partial class Program` added to its `Program.cs`. This route is not built yet.
- **Why not decide now:** the plan's condition for the replacement is "consumable: no". The answer is yes, so Task 17a keeps the primary route, with the replacement as the stated fallback.

## Task 3: S2, S5, S6 on the demo site

- **Date:** 2026-11-04.
- **Method:** the demo site's `SpikeController` creates and publishes automations through Automate's services. Webhooks are POSTed with `curl`. Runs are read back through `IAutomationRunService`, and the real Management API was called with a client-credentials token for the export.
- **Result per line:** every answer below was observed on **both** 17.5.1 and 18.5.1 and is identical unless a line says otherwise.

### Step 1: S2, does `${ trigger.body.issue.key }` bind?

- **Webhook URL route:** `POST /automate/webhook/{automationId:guid}`. `WebhookEndpointController` has `[Route("automate/webhook")]` (`Constants.WebhookApi.RoutePath`) and the Web source is identical on both tags. There is **no** `/umbraco/` prefix: `/umbraco/automate/webhook/{id}` returns 404.
- **Setup:** built-in Webhook trigger (`umbracoAutomate.webhook`), authenticator `plain-secret`, then three Log Message steps bound to `KEY=${ trigger.body.issue.key }`, `HEADERS=${ trigger.headers }` and `BODY=${ trigger.body }`.

  ```sh
  curl -sk -X POST -H 'Content-Type: application/json' -H 'X-Webhook-Secret: <secret>' -H 'X-Hub-Signature: sha256=abc' \
    -d '{"webhookEvent":"jira:issue_updated","issue":{"id":"10001","key":"DEMO-123"}}' http://localhost:5200/automate/webhook/<id>   # 202
  ```

- **The design expected "does not bind". It binds, on both lines.** The stored step input is `{"message":"KEY=DEMO-123","logLevel":"Information"}` and the step log is `KEY=DEMO-123`. `trigger.body` holds the raw JSON text, and the binding resolver parses that text when a path goes into it.
  - A BOM does not stop it binding: the BOM-prefixed body still gave `KEY=DEMO-2`. The invalid-UTF-8 body also still bound.
  - **Impact:** Task 17 can bind `trigger.body.issue.key` directly. The package action `readWebhookEvent` is still useful for a typed, validated payload, but it is no longer the only way to read a field. Design §6.5 to §6.8 should say "binds" rather than "does not bind".
- **Wrong or missing secret:** 401 (on `/automate/webhook/{id}`). An unknown automation also gives 401 (from source; not run).
- **`trigger.headers` holds** a JSON object of every request header except those the authenticator lists: `Accept`, `Host`, `User-Agent`, `Content-Type`, `Content-Length`, plus any custom header such as `X-Hub-Signature` or `X-Other`. HTTP/2 pseudo-headers (starting `:`) are dropped. The stored trigger data is `{"method","body","headers","query"}`.
- **Credential header stripping: yes.**
  - `X-Webhook-Secret` (the built-in `PlainSecretWebhookAuthenticator.CredentialHeaderNames`) does not appear in the stored trigger data or in `trigger.headers`.
  - `X-Hub-Signature`, when not listed by the authenticator (the plain-secret run), **is stored**. When a temporary authenticator listed it in `CredentialHeaderNames`, it was absent from the stored headers: `['Accept', 'Host', 'User-Agent', 'Content-Type', 'Content-Length', 'X-Other']`.
  - `CredentialQueryParameterNames` strips query parameters the same way (source: `WebhookEndpointController`; not run, because the Jira authenticator takes no query credential).
  - **Impact:** the Jira authenticator must list `X-Hub-Signature` in `CredentialHeaderNames`, or the signature and body are stored together (the interface remarks say why). Task 18's `Webhook_SignatureHeaderDeclaredForStripping` covers it.
- **Order of events in the endpoint** (from source, matches the runs): payload-size check, authenticator lookup, then `Validate` (before the body is read when `RequiresBody` is false, otherwise after), then the JSON check (422 only for a `*json*` content type with invalid JSON, after authentication), method check, published check, then the credential-stripped output is dispatched.

### Step 2: encoding check (`context.Body` against the raw bytes)

- **Method:** a temporary demo-only authenticator (`RequiresBody` true) logged `Encoding.UTF8.GetBytes(context.Body).Length` and `Request.ContentLength`. The temporary code was deleted afterwards (`Spikes/TempProbeAuthenticator.cs` no longer exists; the demo builds on both lines without it).
- **Result:**

  | Body | `Content-Length` | `Body.Length` (chars) | UTF-8 bytes of `Body` | Equal? |
  |---|---|---|---|---|
  | `é` and an emoji | 49 | 46 | 49 | **yes** |
  | UTF-8 BOM first | 29 | 26 | 26 | **no** (BOM dropped, 3 bytes) |
  | invalid bytes (`\xff\xfe`) | 51 | 51 | 55 | **no** (each invalid byte became U+FFFD, 3 bytes) |

- **Cause:** `WebhookEndpointController` reads with `new StreamReader(Request.Body)`, which strips a BOM and replaces invalid bytes.
- **Decision for Task 16: read the raw bytes, do not trust `context.Body`.** An HMAC over `Body` fails (closed, 401) for a BOM body, and a decoded body that differs from the signed bytes could in theory be accepted (a body with a literal U+FFFD matches a body with invalid bytes). Jira sends plain UTF-8, so this mostly matters as defence in depth.
- **A raw-byte route that works (tested on both lines):** a second temporary authenticator with `RequiresBody => false` (so `Validate` runs before the body is read) found `Request.Body` is a seekable `FileBufferingReadStream`. It read the bytes with `CopyToAsync(...).GetAwaiter().GetResult()` and reset `Position = 0`.
  - Bytes read were 49, 29 and 51, equal to `Content-Length` in all three cases.
  - The run still started and still bound `trigger.body.issue.key`, so the endpoint's own read was not disturbed.
  - Synchronous `CopyTo` throws `InvalidOperationException` (synchronous IO is off).
  - With `RequiresBody` true, `Request.Body` at `Validate` is the endpoint's non-seekable `LimitedStream`, so the raw bytes are not available there. The 413 limit still applies, because the middleware sets the server's `MaxRequestBodySize` before buffering.
- **Task 16 ruling:** `JiraSignatureWebhookAuthenticator` sets `RequiresBody => false`, reads `Request.Body` raw (async copy, bounded by the existing payload limit), verifies the signature over those bytes, and rewinds. `context.Body` is not used. Sync-over-async is acceptable here because the buffered stream is local and already bounded.

### Step 3: authenticator dependencies and request shape

- **Constructor injection works** for `IMemoryCache` (resolved as `MemoryCache`), `TimeProvider` (`SystemTimeProvider`) and a singleton registered in the demo composer. Registration was `builder.AutomateWebhookAuthenticators().Add<T>()`, exactly as `JiraComposer` does, with no separate DI registration of the authenticator.
  - The same singleton instance (`ef5d9a31-...`) was seen on every request, because the authenticator itself is created once for the collection.
  - **Impact:** the authenticator is a shared instance. Keep it stateless apart from injected singletons (pitfall P2). `RequestServices` is not needed for these. A scoped service was not tested; do not inject one.
- **What `Validate` can see** (one request, line 18; line 17 identical):
  - `Request.Path`: `/automate/webhook/7b65ac38-92bb-441e-8cbb-3d58326f6bd8`
  - `Request.RouteValues`: `action=ReceiveWebhook, controller=WebhookEndpoint, automationId=7b65ac38-92bb-441e-8cbb-3d58326f6bd8`
  - Header names: `Accept, Host, User-Agent, Content-Type, Content-Length, X-Hub-Signature, X-Other`. Every request header is visible to `Validate`, credential headers included. Stripping applies only to the stored output.
  - Method: `POST`.
- **Replay scope for Task 16 (ruling): `Request.RouteValues["automationId"]`.** It is present. The fallback `SHA-256(lower-cased Request.Path)` is not needed, but Task 16 keeps it for a missing or non-GUID value (`Replay_FallbackScope_UsesPathHash`). Each webhook URL is unique to its automation, so either is a per-automation scope.

### Step 3a: ForEach iteration identity

- **Method:** a manual-trigger automation with `umbracoAutomate.forEach` over `${ trigger.items }` = `["alpha","beta","alpha"]`, whose body step is the demo action `demo.dumpContext`. That action logs every public property of `ActionContext` (name and value). Step alias `loop` is reserved (`ReservedBindingKeys` contains `loop` and `previous`), so the loop step is aliased `iterate`.
- **Result: `ActionContext` has no iteration identity.**
  - Its properties are `AutomationId, RunId, StepId, ActionAlias, Settings, InputData, CancellationToken, ExecutionContext, Connection, Action, BindingData, MinimumLogLevel, MaxLogEntries, LogEntries`. None names an iteration, index or item.
  - `StepId` is **the same on every iteration** (one distinct body `StepId` for three iterations).
  - The stored body `StepRun`s have `IterationIndex = null` and `IterationTotal = null`. `StepRun.IterationIndex` is only set where the ForEach container tracks its own step run (`ForEachContainerStepBody.TrackStepRun`), not on body steps.
  - **The only identity is `context.BindingData["loop"]`**, an object `{ "item": <the item>, "index": <int> }`, present only inside a loop. Observed: `{"item":"alpha","index":0}`, `{"item":"beta","index":1}`. (The third iteration's log line was cut by Automate's log truncation, ending `… [truncated]`.)
  - Side finding: `${ ... }` in a setting is only resolved when its field has `SupportsBindings = true`. The demo's `Label` field did not, so `${ loop.item }` stayed literal in `Settings`. The Jira settings that take bindings must set it.
- **Decision for Task 13: `IterationId` = `BindingData["loop"].index` when `BindingData` contains `loop`, otherwise `-`.** It is typed as `object`; read the `index` entry defensively (it may be an `int` or a `JsonElement`).
  - Caveats: in nested loops only the innermost `loop` is exposed, so the same index repeats across outer iterations; the input fingerprint (plan P3) separates items with different data. Two identical items in different positions get different indexes, which is a change from the plan's "identical items collapse" wording, but the key stays deterministic per position within a run.
  - A retried or resumed iteration keeps its index, so the key is stable across retries (the property dedupe needs).

### Step 3b: retry delays and export shape

- **Retry delays, measured** with `demo.failTransient` (always fails with an unclassified `IOException`, default `ErrorBehavior = Retry`, no `RetryInterval` or `MaxRetries` set):
  - **Configuration:** `ExecutionOptions.DefaultRetryInterval` = 30 s, fixed (no backoff), `DefaultMaxRetries` = 10 (`WorkflowCompiler` sets `RetryInterval = stepConfig.RetryInterval ?? DefaultRetryInterval`).
  - **Line 18.5.1:** attempts at `10:21:41.82`, `10:22:19.92`, `10:23:00.02`. Gaps: **38.1 s, 40.1 s**.
  - **Line 17.5.1:** attempts at `10:28:29.49`, `10:29:00.01`, `10:29:40.20`, `10:30:19.81`, `10:31:00.01`, `10:31:39.63`, `10:32:19.76`. Gaps: **30.5 s, 40.2 s, 39.6 s, 40.2 s, 39.6 s, 40.1 s**.
  - **Answer for Task 4 step 6a:** first retry about 30 to 38 s after the failure, every later retry about 40 s (30 s plus the workflow host's polling). Fixed interval, no backoff. A step with the default budget gives up after 10 retries, about 6 to 7 minutes.
- **Action aliases** (read from the catalogue API, `GET /umbraco/automate/management/api/v1/catalogue/{actions|control-flows|triggers}`, both lines identical):

  | Name | Alias | Kind |
  |---|---|---|
  | Publish Content | `umbracoAutomate.publishContent` | action |
  | Request Approval | `umbracoAutomate.requestApproval` | action |
  | Get Content Property | `umbracoAutomate.getContentProperty` | action |
  | Find Content (built-in) | `umbracoAutomate.findContent` | action |
  | Condition ("If") | `umbracoAutomate.if` | control flow (also `forEach`, `parallel`, `switch`, `while`) |
  | Log Message | `umbracoAutomate.logMessage` | action |
  | Webhook | `umbracoAutomate.webhook` | trigger |
  | Manual | `umbracoAutomate.manual` | trigger |

  There is no separate "Condition" step alias: a condition is the `umbracoAutomate.if` control flow (routes by outcome `true` or `false`) or a `filter` on a connection.
- **Export JSON shape** (from the real Management API: `GET https://localhost:5500/umbraco/automate/management/api/v1/automations/{id}/export` with a back-office client-credentials token; the same shape from `IAutomationService.ExportAutomationAsync`). Saved as `demo/Umbraco.Community.Automate.Jira.TestSite/Seeds/export-shape.sample.json` (synthetic data only; the bindings in it are illustrative and were not validated against real content). The shape is identical on 17.5.1 and 18.5.1 (only `exportedFrom.version` differs, `17.5.1+929e4e3` and `18.5.1+ba63aaf`).
  - Top level: `formatVersion` (`"1.0"`), `exportedAt`, `exportedFrom {product, version}`, `automation`, `connectionReferences []`. Property names are camelCase.
  - `automation`: `id`, `alias`, `name`, `description`, `trigger {triggerAlias, settings}`, `steps[]`, `connections[]`, `canvasState`, `notificationSettings`.
  - Each step: `id` (GUID, kept), `actionAlias`, `alias`, `name`, `connectionAlias` (the environment-specific connection ID is replaced by an alias), `settings {}`, `inputMappings {}`, `position {x,y}`, `errorBehavior` (a string, `"Terminate"` or `"Retry"`), `retryInterval`, `maxRetries`.
  - Each connection: `sourceStepId`, `targetStepId`, `sourceHandle`, `targetHandle`, `outcome`, `filter`.
    - The trigger is the source `00000000-0000-0000-0000-000000000000`.
    - `outcome` carries the branch: `"true"` or `"false"` from `umbracoAutomate.if`, `"approved"` or `"rejected"` from `umbracoAutomate.requestApproval` (`RequestApprovalAction.ApprovedOutcome` and `RejectedOutcome`; they double as the canvas handle ids).
    - `filter` is `{ groups: [ { conditions: [ { leftOperand, operator, rightOperand } ] } ] }` with `operator` a string such as `"Equals"`. In the sample it gates the `find` to `readProp` edge.
  - **For Task 17a's seed:** a branch is an edge with an `outcome`, so "every path to `publishContent` passes `requestApproval`" is a graph walk over `connections`. The `If` step's own `settings.conditions` holds the condition set, and the Request Approval step's `settings` are `prompt` and `timeoutHours`. `Publish Content` takes `contentKey` and optional `cultures`. `Find Content` takes `name`, `matchMode`, optional `contentTypes`, and returns `matches[]` (`contentKey`, `name`, `contentTypeAlias`, `updateDate` ...) and `limitReached`. `Get Content Property` takes `contentKey`, `propertyAlias`, optional `culture`, and returns `value` and `hasValue`. Request Approval's decision output has `approved`, `outcome`, `comment`.

### Step 4: S5, the Forms "form submitted" trigger

- **Packages:** `Umbraco.Forms` (latest stable on nuget.org at 2026-11-04: **17.6.2** on the 17 line, **18.2.2** on the 18 line) **plus** the Automate add-on **`Umbraco.Forms.Automate`** (**17.0.3** on 17, **18.0.3** on 18). Forms itself does not register triggers; the add-on does. Its dependencies are `Umbraco.Automate.Core [18.0.0, 18.999.999)` and `Umbraco.Forms.Core [18.0.0, 18.999.999)` for 18.0.3. Both are referenced from the demo csproj and build on both lines. Forms runs unlicensed on localhost.
- **Trigger aliases** (catalogue API, group "Forms", both lines):

  | Alias | Name | Description |
  |---|---|---|
  | **`umbracoForms.formSubmitted`** | Form Submitted | Fires when a form entry is submitted. |
  | `umbracoForms.formEntryApproved` | Form Entry Approved | Fires when a form entry is approved. |

  - Output of `formSubmitted`: `formId`, `formName`, `recordUniqueId`, `state`, `created`, `ip`, `memberKey`, `culture`, `recordFieldsJson`, `fields[]`. Its setting is `formIds` (a form picker; blank means all forms).
  - The Forms actions are `umbracoForms.submitForm` and `umbracoForms.exportEntries`.
  - The picker was read from the catalogue API rather than opened in the back office, which gives the same alias list.
- **Version caveat:** Forms 17.6.2 booted with CMS 17.8.0 and Forms 18.2.2 with CMS 18.3.0 (the trigger appeared in each catalogue). The latest 18.x stable lags the 18.3 CMS line slightly. Re-check the aliases when `Umbraco.Forms.Automate` moves past 18.0.3.

### Step 5: S6 on the 17 line

- **Confirmed on 17.5.1 from source and from the DLL: the same as 18.** An action has no retry opt-out. Terminal categories are `Validation`, `ConfigurationError`, `Authentication` and `Cancelled`; everything else (`Timeout`, `ServiceUnavailable`, `RateLimiting`, `Unknown`) is retried.
  - **Source:** `StepTypeAttribute` and `ActionAttribute` have no retry member. `StepConfiguration` has `ErrorBehavior`, `RetryInterval` and `MaxRetries` (per step, set by the automation author, not by the action). `DefaultStepErrorClassifier.IsTerminal` lists exactly the four categories. `StepTypeAttribute.cs`, `StepConfiguration.cs` and `DefaultStepErrorClassifier.cs` are byte-identical between the 17.5.1 and 18.5.1 source trees.
  - **DLL:** two regression tests added to `SecretFeatureSpikeTests`: `ActionAttribute_HasNoRetryOptOut`, and `DefaultStepErrorClassifier_TerminalCategories` (eight cases, reaching the internal classifier by type name). Both pass on both lines.
  - **Run:** `demo.failTransient` returning `ActionResult.Failed(new IOException(...))` was retried (step 3b), as the unclassified category predicts.
- **Consequence for Task 13:** unchanged. An action cannot suppress a retry, so a POST that fails after the request was sent must return a terminal category (`Validation`, `ConfigurationError` or `Authentication`) or rely on the dedupe marker, as planned. Task 13 stays in 1.0 regardless (Pascal, 2026-10-30).

### Evidence

- `./scripts/test.sh`: `Passed!  - Failed:     0, Passed:    19, Skipped:     0, Total:    19` for line 17 and for line 18, then `All lines passed.`
- Raw run output (probe logs, the 17 and 18 ForEach runs and the real-API export) is in the private workspace scratch folder and was not copied into the repo.

## Task 4: S3 and S4 against the sandbox Jira (2026-11-04)

- **Set-up:**
  - The sandbox is Jira Cloud, with project `SBX` and a "Ready to publish" status.
  - Calls used the credential `sandbox`, read from `Umbraco__Automate__Jira__Credentials__sandbox__Email|ApiToken` and never printed.
  - Only one token was available. It is a **scoped** API token. No classic token was available, so the plan's "one scoped and one classic" comparison is half done.
- **Scripts:** `scripts/spike_jira.py` (S3) and `scripts/spike_jira_s4.py` (S4 and the dedupe probes). They print status codes and non-personal fields only. They replace the plan's `scripts/spike-jira.sh`: same calls, but Python, so the credential never reaches a shell argument.
- **Issues created in SBX:**
  - Probes: SBX-5 to SBX-9, plus lag samples.
  - All carry the label `cj-spike`, so they can be found and deleted.

### S3: routes and scopes (scoped token)

| Call | Site route `https://<site>.atlassian.net` | Gateway `https://api.atlassian.com/ex/jira/{cloudId}` |
|---|---|---|
| `GET /myself` | 401 "Client must be authenticated" | **401 "Unauthorized; scope does not match"** (`X-Failure-Category: FAILURE_CLIENT_SCOPE_CHECK`) |
| `GET /issue/createmeta/SBX/issuetypes` | 404 | 200 (Epic, Subtask, Task, Story) |
| `POST /search/jql` | 200 (anonymous, empty) | 200 |
| `POST /issue` | 400 "project doesn't exist or you don't have permission" | 201 (SBX-5) |
| `POST /issue/{key}/comment` | n/a | 201 |
| `GET /issue/{key}/comment?expand=properties` | n/a | 200 |
| `GET /issue/{key}/transitions` | n/a | 200 (To Do 11, In Progress 21, In Review 31, Ready to publish 41, Done 51) |
| `POST /issue/{key}/transitions` | n/a | 204 (to Ready to publish) |
| `GET /mypermissions?permissions=ADMINISTER,ADMINISTER_PROJECTS,SYSTEM_ADMIN` | 200 (anonymous) | 200, all three `false` |
| `GET /_edge/tenant_info` (no `Authorization`) | 200, returns `cloudId` | n/a |

**Findings:**
- **Scoped tokens work only through the gateway.** On the site route Jira treats them as anonymous. So `CloudId` is **required for scoped tokens**. A classic token on the site route was not tested (none available).
- **`/myself` needs `read:jira-user`.** The sandbox token lacks it, so Validate (which calls `/myself`) fails with this token until Pascal adds that scope. Every action call works.
- **`/mypermissions` needs no extra scope.** It returned 200 with the same token, so Task 8's admin-permission warning is in.
- **`/_edge/tenant_info` returns `cloudId` with no credentials**, so Task 8's "blank Cloud ID" help applies.

**Scope list for the README:**
- Required: `read:jira-work` and `write:jira-work` for the actions, and `read:jira-user` for Validate.
- This is inferred from Atlassian's scope documentation plus the scope-check failure above. It has not been verified against a token created with exactly those scopes, which is Pascal's to-do.

**Gate S3: GO for the actions on scoped tokens through the gateway; CONDITIONAL for Validate and the scope list.**
- Every action call works.
- Validate needs `read:jira-user` added to the token.
- The classic-token fallback (site route) stays supported, but was not tested here.
- **UNKNOWN, owner Pascal, deadline before Task 21 (beta):** (1) Validate with a scoped token holding exactly `read:jira-work`, `write:jira-work` and `read:jira-user`; (2) a classic API token on the site route. Until then the README lists the scope set as inferred.

### S4: limits and ADF

- **Text cap:**
  - A description with 32,767 characters gives 201.
  - 32,768 and 40,000 characters give 400 "The entered text is too long. It exceeds the allowed limit of 32767 characters."
  - So `AdfLimits.MaxTextLength = 32767`.
- **Risky ADF:** a description containing `inlineCard`, `mention` and `extension` nodes was **accepted** (201). The stored document kept the `extension` node. Jira does not strip these, so the package's allowlist sanitiser is needed.
- **ADF schema:** `@atlaskit/adf-schema` 57.7.18, published 2026-10-19, from `https://unpkg.com/@atlaskit/adf-schema@57.7.18/dist/json-schema/v1/full.json`, downloaded 2026-11-04. It is saved as `tests/…/Fixtures/adf-schema.json` and is byte-identical to the then-latest 57.7.32.

### Dedupe probes

- **(a) Issue property, then JQL `issue.property[community.jira.dedupe].key = "<m>"`:** the create succeeded (201), but there was **no match after 90 s**. Arbitrary issue properties are not JQL-indexed without an app descriptor.
- **(b) Label `cj-dedupe-<m>`, then JQL `labels = "cj-dedupe-<m>"`:** matched every time. Six samples: 0.3, 0.36, 0.35, 0.24, 0.24 and 0.32 s.
- **(c) Comment created with a `properties` array, then `GET …/comment?expand=properties`:** the property comes back.
- **Decisions:**
  - **Task 13 marker: `Label`** (`cj-dedupe-<hash>`). The label on every created issue was accepted by Pascal (design, "Resolved … after security fixes").
  - **Task 14 strategy: read-first** (comment property).

### Step 6a: search lag against retry delay

The worst observed lag (0.36 s) is far below Automate's first retry delay (30 s, `ExecutionOptions.DefaultRetryInterval`), so the result is: **lag below first retry delay.**

### Step 5: webhooks (not captured live)

- **Why not:** a live capture needs a public tunnel to a local site and a Jira admin to register the webhook. Both are left to Pascal.
- **What was used instead:** `scripts/make-webhook-fixtures.py` writes **synthetic** fixtures in Atlassian's documented shapes (developer.atlassian.com/cloud/jira/platform/webhooks, read 2026-11-04):
  - `issue_created`
  - `issue_updated` with a status change
  - `issue_updated` with only a comment
  - `comment_created`
  - `comment_updated`
  - `issue_updated.signed.json`, self-signed with a fixture-only 36-character secret, non-ASCII body
- **UNKNOWN until a real capture (owner Pascal, deadline before Task 21; the live tunnel test):**
  - the `timestamp` unit (epoch milliseconds per Atlassian's examples; unverified);
  - whether a retry's body is byte-identical;
  - whether comment bodies arrive as a string or as ADF (the parser must accept both).
- **Effect on Task 16:** `Validate_CapturedJiraDelivery_Passes` runs on the self-signed fixture, which is **not** the non-circular proof the plan wants. The live tunnel test is Pascal's to-do.
- **Fixture scanner:** `FixtureScannerTests` passes, and its two self-tests prove it flags unlisted emails, account IDs and display names. The manual mutation (a real-looking email in a scratch fixture) failed naming only `webhooks/scratch.json:2 email`, then passed once removed. gitleaks finds nothing in `Fixtures/` or `scripts/`.

## Task 5: trademark check and alpha reservation (2026-11-04)

- **Display title:** "Jira connector for Umbraco Automate" (the fallback in design §13 item 4). It is a descriptive use, with no Atlassian logo and with the "not affiliated with Atlassian" line in the README.
  - Pascal's 15-minute read of Atlassian's trademark guidance has **not** happened.
  - If the guidance allows "Jira for Umbraco Automate", change the `<Title>` before the alpha push. The package ID is the same either way.
- **Alpha build:** `-p:JiraIdReservation=true` compiles only the appsettings schema class, so the 0.0.1-alpha registers no Automate alias. The spike no-op action is deleted.
- **Pack command (verified 2026-11-04):**
  `dotnet pack src/Umbraco.Community.Automate.Jira -p:AutomateMajor=18 -p:JiraIdReservation=true -p:VersionPrefix=0.0.1 -p:VersionSuffix=alpha "-p:Description=Placeholder%2C not functional. Reserves the package ID." -o out`
  - This gives `Umbraco.Community.Automate.Jira.0.0.1-alpha.nupkg` with Automate `[18.5.1, 19.0.0)` and CMS `[18.3.0, 19.0.0)`.
  - No `community.jira` string is in the assembly.
- **Push (step 4):** Pascal's. Use a NuGet key scoped to this ID with push scope only and an expiry of 90 days or less, then unlist.
