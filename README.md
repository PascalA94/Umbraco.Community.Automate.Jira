# Umbraco Automate connector for Jira

[![NuGet](https://img.shields.io/nuget/v/Umbraco.Community.Automate.Jira?label=NuGet)](https://www.nuget.org/packages/Umbraco.Community.Automate.Jira)
[![Downloads](https://img.shields.io/nuget/dt/Umbraco.Community.Automate.Jira?label=Downloads)](https://www.nuget.org/packages/Umbraco.Community.Automate.Jira)
[![Licence: MIT](https://img.shields.io/github/license/PascalA94/Umbraco.Community.Automate.Jira?label=Licence)](https://github.com/PascalA94/Umbraco.Community.Automate.Jira/blob/main/LICENSE)

Create Jira issues, add comments and move issues between statuses from [Umbraco Automate](https://docs.umbraco.com/), and start automations from signed Jira webhooks.

A community package, not made or supported by Umbraco HQ. It is not affiliated with, endorsed by or sponsored by Atlassian. "Jira" is a trademark of Atlassian.

**Requires** Umbraco CMS 17.8+ with Umbraco Automate 17.5.1+, or Umbraco CMS 18.3+ with Umbraco Automate 18.5.1+. Jira Cloud only (not Data Center).

## Quick start

About 15 minutes if you have used Automate before. Each step links to the full section.

1. **Install** the line that matches your site: `dotnet add package Umbraco.Community.Automate.Jira --version 18.*` (or `17.*` on Umbraco 17). See [Install](#install).
2. **Make a Jira token.** Use a service account that is not a Jira admin, and create a scoped API token with `read:jira-work`, `write:jira-work` and `read:jira-user` that expires in 90 days or less. See [Service account requirements](#service-account-requirements).
3. **Store it in configuration** under a credential name such as `agency`, as environment variables or secrets, never in a committed `appsettings.json`:
   ```
   Umbraco__Automate__Jira__Credentials__agency__Email
   Umbraco__Automate__Jira__Credentials__agency__ApiToken
   ```
   See [Credentials](#credentials).
4. **Add the connection.** In Automation, Connections, create a **Jira Cloud (API token)** connection with your site URL and the credential name, then click **Test connection**. It shows the Cloud ID that a scoped token needs. See [The connection](#the-connection).
5. **Add a workspace** with an API user as its service account and the Jira connection allowed. For Umbraco Forms triggers, give the API user's group the **Forms** section.
6. **Build an automation:** a trigger, then **Create Jira issue**. Set **Error behaviour** to **Retry** and set a **Dedupe key**, for example `${ trigger.recordUniqueId }` for a form. See [Recipes](#recipes) and [Duplicate protection](#duplicate-protection).

Starting automations from Jira (webhooks) is optional: see [Inbound webhooks](#inbound-webhooks).

## Contents

- [Quick start](#quick-start)
- [Install](#install)
- [Credentials](#credentials)
- [What the package does and does not protect](#what-the-package-does-and-does-not-protect)
- [Service account requirements](#service-account-requirements)
- [The connection](#the-connection)
- [Actions](#actions)
- [Duplicate protection](#duplicate-protection)
- [Text formats](#text-formats)
- [Inbound webhooks](#inbound-webhooks)
- [Recipes](#recipes)
- [Advanced: Jira-driven publishing (off by default)](#advanced-jira-driven-publishing-off-by-default)
- [Personal data (GDPR)](#personal-data-gdpr)
- [Configuration reference](#configuration-reference)
- [Security](#security)

## Install

Pick the package line that matches your Umbraco major. One codebase builds both.

| Package version | Umbraco Automate | Umbraco CMS |
|---|---|---|
| `18.x` | `[18.5.1, 19.0.0)` | `[18.3.0, 19.0.0)` |
| `17.x` | `[17.5.1, 18.0.0)` | `[17.8.0, 18.0.0)` |

```bash
dotnet add package Umbraco.Community.Automate.Jira --version 18.*   # Umbraco 18
dotnet add package Umbraco.Community.Automate.Jira --version 17.*   # Umbraco 17
```

The package registers itself through a composer. There is nothing to add to `Program.cs`.

## Credentials

The Jira email, API token and webhook secret never go on the connection or in an automation. They live in configuration under a **credential name**, and only this package reads them:

```json
{
  "Umbraco": {
    "Automate": {
      "Jira": {
        "Credentials": {
          "agency":   { "Email": "svc-umbraco@agency.example", "ApiToken": "<from a secret store>" },
          "agencywh": { "WebhookSecret": "<32 or more random characters>" }
        }
      }
    }
  }
}
```

The connection and the webhook authenticator store only the name (`agency`, `agencywh`). A name is 1 to 32 lower-case letters or digits. Use a different name for each Jira webhook (see [Inbound webhooks](#inbound-webhooks)).

**Never commit these values.** Set them in one of these places instead:

| Where | Key format |
|---|---|
| Environment variables | `Umbraco__Automate__Jira__Credentials__agency__ApiToken` |
| Azure Key Vault | `Umbraco--Automate--Jira--Credentials--agency--ApiToken` |
| `dotnet user-secrets` (local development) | `dotnet user-secrets set "Umbraco:Automate:Jira:Credentials:agency:ApiToken" "<token>"` |
| Umbraco Cloud secrets | `Umbraco__Automate__Jira__Credentials__agency__ApiToken` (see the note below) |

**Umbraco Cloud note.** Umbraco Cloud's [Secrets Management](https://docs.umbraco.com/umbraco-cloud/set-up/project-settings/secrets-management) page joins setting names with double underscores, as above. Cloud accepted a secret named `Umbraco__Automate__Jira__Credentials__{name}__ApiToken` when this was checked on 2026-11-11, although that page lists `UMBRACO_` as a restricted prefix. Cloud passes secrets to the site as environment variables, set per environment, so each environment reads its own value after a restart. If Cloud refuses the name on your project, ask Umbraco Support to allow the `Umbraco__Automate__Jira__Credentials__` prefix, as that page suggests for conflicts.
- **Do not work round a refusal by putting the token in `appsettings*.json`.** Cloud deploys from git, so the value would stay in the repository history (Validate warns about this).
- Use **environment** secrets, not **shared** secrets: shared secrets reach every environment, so development would hold the live token. Give each environment its own service account and token.

**Moving connections and automations with Umbraco Deploy.** Deploy moves Automate connections, automations and workspaces through Umbraco's [Umbraco.Deploy.Automate](https://docs.umbraco.com/umbraco-automate/add-ons/deploy) add-on.
- Install that add-on and this package on every environment. If this package is missing on the target, the import fails and names it.
- A Jira connection's settings (site URL, credential name, Cloud ID) hold no secret, so they transfer as they are. Each environment then reads its own token from its own secrets under the same credential name. Set the same credential names on every environment, with different values.
- A webhook automation keeps its credential name. Set `…__{name}__WebhookSecret` on each environment, and give each environment's Jira webhook its own URL and secret.
- Without Deploy, use Automate's export and import. The same rules apply.
- This follows Umbraco's Deploy add-on documentation (read 2026-11-11). It has not yet been run end to end on a Cloud project.

**Rotation.** The package reads the credential on every request and never caches it.
- A JSON file with `reloadOnChange` (such as `appsettings.Development.json`) and a reloading Key Vault provider are picked up on the next run, without a restart.
- A changed environment variable (and so an Umbraco Cloud secret) is only seen after the site restarts.
- The steps are in [RUNBOOK.md](https://github.com/PascalA94/Umbraco.Community.Automate.Jira/blob/main/RUNBOOK.md).

**Committed credentials.** The connection's Validate button warns when a credential comes from an `appsettings*.json` file. Sample [gitleaks](https://github.com/gitleaks/gitleaks) rules in [`eng/gitleaks-jira-credentials.toml`](https://github.com/PascalA94/Umbraco.Community.Automate.Jira/blob/main/eng/gitleaks-jira-credentials.toml) flag an API token or webhook secret in a `Credentials` block of a JSON file, or set by its full key in settings, `launchSettings.json` or `.env` files. Copy them into your site's gitleaks config. They are a sample, not a guarantee.

## What the package does and does not protect

1. The package never sends a credential to a host other than Atlassian's. Site URLs must be `https://<site>.atlassian.net`, the gateway host is fixed, and redirects are never followed.
2. Automate authors cannot read the raw credentials: no Automate step can resolve the `Credentials` section. This holds only while Automate's `AllowedConfigurationKeyPrefixes` does not cover `Umbraco:Automate:Jira:Credentials` (see below).
3. Anyone with Automate access to a workspace can still **use** the connected Jira account through this package's actions.
4. Credential names are site-wide, not per workspace. An author in any workspace can name any configured credential.
5. Other server-side code (custom code, other packages, anything that reads `IConfiguration`) can still read the section. Protect it like any other server secret.

**Do not widen `AllowedConfigurationKeyPrefixes`** to `Umbraco`, `Umbraco:Automate`, `Umbraco:Automate:Jira` or anything under `Umbraco:Automate:Jira:Credentials`. If it is widened, every automation author can send the credential to any host. The package detects this: it refuses its own credential reads and logs a `Critical` message at startup. It cannot stop other steps. After any such exposure, rotate the credentials and delete the run records and log entries from that time, because resolved values stay in stored runs and in Umbraco's logs (the log files and anywhere they are shipped). Automate reads the allow-list once at startup, so a change needs a restart.

## Service account requirements

All of these are required:

- A dedicated Atlassian account for this site only, never a person's account.
- Jira product access only, with project roles only on the projects the automations use.
- Never a Jira admin or an organisation admin.
- A **scoped** API token used through the Atlassian gateway (set the connection's Cloud ID), with an expiry of 90 days or less.
- Token scopes: `read:jira-work` and `write:jira-work` for the actions, plus `read:jira-user` for Validate. This list is inferred from Atlassian's scope documentation and has not yet been checked with a token holding exactly these scopes.

Validate warns when:
- the token is used on the site URL instead of the gateway (a classic, unscoped token);
- the account holds Jira admin permissions;
- the credential is read from an `appsettings*.json` file.

## The connection

**Jira Cloud (API token)**, alias `community.jira.cloud`.

| Field | Notes |
|---|---|
| Site URL | `https://<site>.atlassian.net`. No bindings. |
| Credential name | The name under `Umbraco:Automate:Jira:Credentials`. Not the token. |
| Cloud ID | A GUID. Required for scoped tokens, which only work through the gateway. Leave it blank and click Validate to be shown your site's Cloud ID. |

Validate shows which Atlassian account the connection acts as, by which route, and that anyone with access to the workspace can use it.

## Actions

Every action reports errors in Automate's categories (validation, authentication, rate limiting and so on). Messages name configuration keys, never values, and never include tokens or request bodies. Names and messages that come from Jira have control characters and text-direction characters removed before they reach a log or an output.

### Create Jira issue (`community.jira.createIssue`)

| Input | Notes |
|---|---|
| Project key | For example `SUP`. |
| Issue type | Name (for example `Task`) or numeric ID. An unknown name fails and lists the valid types. |
| Summary | Newlines become spaces; cut to 255 characters. |
| Description | Optional. See [Text formats](#text-formats). |
| Description format | Plain text (default), Markdown or ADF JSON. |
| Labels | Comma-separated. Labels starting `cj-dedupe-` are reserved. |
| Priority | A priority name. |
| Assignee account ID | An Atlassian account ID, not an email address. |
| Parent key | For sub-tasks and epic children. |
| Additional fields (JSON) | A JSON object. Allowed keys only: `customfield_<number>`, `components`, `fixVersions`, `versions`, `duedate`, `environment`, `labels`. **No bindings.** Unknown, protected or duplicate keys fail before anything is sent. |
| Dedupe key | Optional. See [Duplicate protection](#duplicate-protection). |

Outputs: `IssueId`, `IssueKey`, `IssueUrl`, `WasExisting`.

### Add Jira comment (`community.jira.addComment`)

Inputs: Issue key, Comment, Comment format (default Plain text), Dedupe key. Outputs: `CommentId`, `CommentUrl`, `WasExisting`.

### Transition Jira issue (`community.jira.transitionIssue`)

| Input | Notes |
|---|---|
| Issue key | |
| Transition | A target **status name** (for example `Done`), a transition name (for example `Close`) or a numeric transition ID. Matched in that order of precision: ID, then transition name, then status name. |
| Comment | Optional plain text, added with the transition. |
| Resolution | Optional resolution name. The transition's screen must include it. |

Outputs: `FromStatus`, `ToStatus`, `TransitionId`.

- **Use the status name.** If the issue is already in the status you name, the step succeeds and does nothing. That makes a retry safe after a transition that timed out but went through. A transition name or ID cannot be traced once the issue has moved, so a retry fails with a message saying the issue may already have moved.
- A transition whose target is the issue's current status (a "self-loop", for example an "edit resolution" transition) is skipped as already done. Use Jira for those.

### Read Jira webhook event (`community.jira.readWebhookEvent`)

Reads a Jira webhook body (default `${ trigger.body }`) into: `WebhookEvent`, `IssueKey`, `IssueId`, `ProjectKey`, `IssueType`, `Summary`, `Status`, `StatusChangedFrom`, `StatusChangedTo`, `UserAccountId`, `UserDisplayName`, `Changes` (field, from, to), `CommentBody` (plain text) and `Timestamp`.

- **`Summary`, `CommentBody`, `UserDisplayName` and `Changes` are untrusted user text.** Any Jira user can write them. Use `| strip_html` or plain-text targets when you put them in an email, a page or a message.
- `StatusChangedFrom` and `StatusChangedTo` are set only when the status really changed in this event.
- Bodies nested deeper than 32 levels, or with duplicate keys, fail.

### Find content by Jira key (`community.jira.findContentByKey`)

Finds the one content node whose Jira-key property equals an issue key. It matches the property value, never the node name, because any editor can rename a node.

| Input | Notes |
|---|---|
| Issue key | Trimmed and upper-cased. Must look like `SUP-17`. |
| Property alias | Default `jiraKey`. An invariant Textstring property. No bindings. |
| Content types | Required. Only these types are searched. A deleted type fails the step. |

Outputs: `ContentKey`, `Name`, `ContentTypeAlias`, `IsPublished`, `UpdateDate`.

- It fails unless exactly one node matches. Duplicates are counted before permissions, so a duplicate the service account cannot see still stops the run.
- It searches drafts too, through the internal Examine index. The count is only as fresh as that index: a node saved a moment ago, or an index being rebuilt, may not be counted yet.
- **Keep the key unique.** Use an invariant Textstring property called `jiraKey` on the content types involved, filled in only by people who may publish them, and check for duplicates when you copy a node (the copy keeps the key).

## Duplicate protection

Automate retries failed steps. To stop a retry creating a second issue or comment, Create issue and Add comment always check first:

- Each step works out a key. With no Dedupe key set, the key is made from the automation, run, step, the For each position and the step's inputs. Retries of the same step resolve the same key, so they find what the first attempt created.
- Set **Dedupe key** to widen this across runs, for example to a form entry ID, so a re-run returns the same issue. Automate's **Replay** starts a new run, so without a Dedupe key, replaying a completed run creates a second issue; with one, the replay returns the first issue with `WasExisting` true. Prefer non-personal values: only a short keyed hash of the key is sent to Jira (as a `cj-dedupe-…` label on issues, and as a comment property on comments), but anyone with backoffice access could recompute it.
- **Inside a For each loop**, set a per-item Dedupe key. Without one, two items in different positions get different keys; with a constant Dedupe key, every item gets the first item's issue.
- Recreating the connection starts cross-run protection afresh, because the hash is keyed with the connection's ID.
- **If the check cannot run** (Jira down, rate limited, rejected), the step fails and posts nothing. Automate's retry runs the check again.

Residuals:
- **Search lag.** Jira's search is eventually consistent. In tests the label was searchable within 0.4 seconds, well inside Automate's first retry delay of 30 seconds, but a retry inside that lag could still create a duplicate.
- **Comments: 50 newest only.** Add comment checks the 50 newest comments on the issue. On a very busy issue an older match is missed.
- **Concurrent runs** with the same Dedupe key at the same moment are not serialised, so both can create.

## Text formats

- **Plain text** (default): what you type is what Jira shows. Blank lines make paragraphs. **Use it for anything from a public form.**
- **Markdown**: headings, lists, emphasis, code and links. Only `http`, `https` and `mailto` links are kept.
- **ADF JSON**: an Atlassian Document Format document, checked against an allow-list. Cards, mentions, extensions, media and unknown nodes or marks are removed or turned into text.
- Markdown and ADF JSON inputs over 64 KB fail. Jira's own limit is 32,767 characters of text; longer text is cut and marked "(truncated)".

## Inbound webhooks

Jira webhooks start an automation through Automate's built-in **Webhook** trigger with the **Jira webhook secret** authenticator (`community.jira.signature`).

1. In the automation, add a Webhook trigger and choose the Jira webhook secret authenticator. Set its credential name, for example `agencywh`.
2. Set `Umbraco:Automate:Jira:Credentials:agencywh:WebhookSecret` to 32 or more random characters.
3. In Jira (Settings → System → WebHooks), create a webhook to `https://<your site>/automate/webhook/<automation ID>` with the same secret, a JQL filter scoped to one project, and only the events you need. Copy the full **Webhook URL** shown on the trigger: a URL without the `/automate/webhook/<automation ID>` path posts to the site's home page, and neither Jira nor Umbraco reports an error.

What the authenticator checks:
- exactly one `X-Hub-Signature: sha256=<hex>` header, compared in fixed time;
- a body `timestamp` within the replay window (default 120 minutes, `ReplayWindowMinutes`);
- that the same body has not already been accepted for this automation within the window.

Anything else gets a 401 and no run starts. The signature header is never stored with the run.

Residuals:
- **Use one credential name per Jira webhook.** A captured delivery for one automation can be replayed to another automation that uses the same credential name, because Jira signs only the body.
- **The replay memory is per server.** On a load-balanced site a replay sent to a different server inside the window is accepted. A shared cache is planned for 1.1.
- Jira's own retries and duplicate deliveries reuse the body, so they are refused as replays. A run that failed to start after authentication is not delivered again inside the window.

## Recipes

### Use case 1: form submission to a Jira ticket

1. Trigger: Umbraco Forms **Form Submitted** (`umbracoForms.formSubmitted`, needs `Umbraco.Forms.Automate`). **Turn on Forms anti-spam** (honeypot or CAPTCHA) on the form, and consider a per-site rate limit in front of the form: every submission creates an issue. The Forms triggers only appear in the trigger picker when the workspace's service account is in a user group with access to the **Forms** section.
2. **Create Jira issue**: Project key `SUP`, Issue type `Task`, Summary and Description bound from the form fields (for example `${ trigger.fields.summary }`, where `summary` is the field alias), Description format **Plain text**, Dedupe key `${ trigger.recordUniqueId }`. Set **Error behaviour** to **Retry** (new steps start on **Terminate** in the designer): with Terminate, a create whose response is lost fails the run even though Jira made the issue, and nothing retries it. Replaying that run with the Dedupe key set returns the issue instead of making a second one. With Retry, Automate can still mark a run **Failed** after a later attempt of the step succeeded (seen on Automate 18.x): open the run and check the last attempt of the step, which shows the issue key and `WasExisting`.
3. Add an Automate failure notification to the automation, so a broken token or a full project does not fail silently.

### Use case 2: comment on an existing issue

Use **Add Jira comment** with the issue key bound from an earlier step (for example a content property). Set a Dedupe key if the same event can run twice.

### Use case 3: move an issue when content changes

Use **Transition Jira issue** with **Transition** set to the target status name (`Done`), so retries are safe.

### Use case 5: react to a Jira event

Webhook trigger with the Jira webhook secret authenticator, then **Read Jira webhook event**, then a condition on `WebhookEvent` and `StatusChangedTo`, then your steps. Treat `Summary`, `CommentBody` and `UserDisplayName` as untrusted text.

### The `jiraKey` property

For use case 4 and any recipe that ties content to an issue, add an invariant **Textstring** property with the alias `jiraKey` to the content types involved. It must vary by neither culture nor segment. Fill it with the issue key, for example `SUP-17`.

## Advanced: Jira-driven publishing (off by default)

> **Warning.** Anyone who can move a Jira issue into this status can ask for this content to be published. Anyone in this Automate workspace can approve it. Set both up as described below before you enable it.

This recipe lets a Jira status change **request** publication of the matching page, and an Umbraco user **decides**. There is no variant without Request Approval. The demo site seeds it as a disabled automation.

Required setup, all of it:
- **A dedicated Automate workspace** for this automation. Its service account may publish **only** the intended subtree (its start node), never the whole site.
- **Workspace members are only users who may publish that subtree.** Automate lets any member of the workspace approve, and does not check their publish rights on the node.
- **Jira side:** the webhook's JQL is scoped to one project; the "Ready to publish" transition has a workflow condition limiting it to a named group; the webhook has its own credential name with a 32+ character random secret.
- Run logs from this recipe hold Jira account IDs. Automate keeps runs for `Umbraco:Automate:RunCleanup:RetentionDays` (default 90 days).

The recipe, in order:

1. **Webhook** trigger with the **Jira webhook secret** authenticator and its own credential name.
2. **Read Jira webhook event** (`${ trigger.body }`).
3. **Condition**: `WebhookEvent` is `jira:issue_updated`, `StatusChangedTo` is `Ready to publish`, and `StatusChangedFrom` is not `StatusChangedTo`. Otherwise the run ends, so comments and label edits publish nothing.
4. **Find content by Jira key**: Issue key from step 2, Content types set to the types involved. It fails unless exactly one node matches. Never use it inside For each.
5. **Log Message**: the Jira account ID, the issue key and the content key. Not the Jira display name: any Jira user can change theirs, so it proves nothing.
6. **Request Approval**: a prompt naming the issue key, the content key and type, and the Jira account ID, telling the approver to open the node and review the current draft. Set a timeout (for example 72 hours) so an unanswered request is rejected.
7. If rejected: end (optionally **Add Jira comment** "Publication rejected in Umbraco").
8. If approved: **Find content by Jira key** again, then a **Condition** that the content key and `UpdateDate` are the same as in step 4. If the draft changed while waiting, end and log it.
9. **Publish Content** with the content key.
10. **Log Message**: who approved, the content key and the issue key.

What is published is the draft as it was when approved. Residuals:
- A save that lands in the moment between the step 8 check and step 9 is published without a second review.
- How precisely the step 8 condition compares the two `UpdateDate` values has not been checked yet. If it compares to the second, a save in the same second as step 4 would not be noticed.
- Find content by Jira key counts duplicates from the Examine index, so a duplicate saved a moment ago, or during an index rebuild, may not be counted (see [Find content by Jira key](#find-content-by-jira-key-communityjirafindcontentbykey)).

Keep the subtree's editors to people trusted to publish it.

## Personal data (GDPR)

- Sending form data to Jira is a transfer to a processor (Atlassian), possibly outside your region. Record it as a processing activity: the lawful basis, the data processing agreement with Atlassian, and where the data is held.
- Prefer Plain text and send only the fields you need.
- Automate stores the full webhook body with each run, and keeps runs for `Umbraco:Automate:RunCleanup:RetentionDays` (default 90 days). On sites that send public form data to Jira, or take Jira webhooks, set it lower.
- Run logs from webhook recipes can hold Jira account IDs.

## Configuration reference

All optional, under `Umbraco:Automate:Jira` (the package ships an `appsettings` schema for IntelliSense):

| Setting | Default | Notes |
|---|---|---|
| `TimeoutSeconds` | `30` | Per Jira request. Values below 1 become 1. |
| `IssueTypeCacheMinutes` | `10` | How long a project's issue types are cached. |
| `ReplayWindowMinutes` | `120` | Webhook timestamp window and replay memory. Clamped to 10..1440. |
| `Credentials:{name}:Email` / `ApiToken` / `WebhookSecret` | none | See [Credentials](#credentials). |

## Security

Report a vulnerability privately through [GitHub security advisories](https://github.com/PascalA94/Umbraco.Community.Automate.Jira/security/advisories/new). See [SECURITY.md](https://github.com/PascalA94/Umbraco.Community.Automate.Jira/blob/main/SECURITY.md) and the [runbook](https://github.com/PascalA94/Umbraco.Community.Automate.Jira/blob/main/RUNBOOK.md) for rotation and incident steps.

## Building

```bash
./scripts/test.sh                                   # tests every line in eng/lines.txt
./scripts/pack.sh 18 out                            # packs the 18 line
```

See [CONTRIBUTING.md](https://github.com/PascalA94/Umbraco.Community.Automate.Jira/blob/main/.github/CONTRIBUTING.md).

## Licence

MIT. See [LICENSE](https://github.com/PascalA94/Umbraco.Community.Automate.Jira/blob/main/LICENSE).
