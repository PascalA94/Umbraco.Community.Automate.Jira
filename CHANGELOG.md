# Changelog

Versions follow `<Umbraco major>.<minor>.<patch>`. One tag ships the 17 and 18 lines. A tag v19.* ships the 19 line only.

## 19.0.0-rc.1

Release candidate of the 19 line. Built against Umbraco 19.0.0-rc1, not yet verified on 19 stable.

- New 19 line for Umbraco CMS 19 and Umbraco Automate 19 (release candidates) on .NET 11. Lines 17 and 18 are unchanged.
- Find content by Jira key uses Umbraco Search's draft content index on 19 (Examine is gone from Umbraco 19). The index only proposes candidates; the exact value check and the duplicate rule decide, as before. Members-only pages are still counted. If more than 2000 nodes look like the key, the step fails rather than guess. After upgrading a site, rebuild the Umbraco Search indexes.
- The comment and issue format settings use the single-choice dropdown (`Umb.PropertyEditorUi.Dropdown.Single`) on 19, where the plain dropdown is always multi-select.

## Unreleased

- Credential helper contract 1.0.3: test scope for Automate 19. No change for users.

## 18.0.0

First stable release. The package code is the same as `18.0.0-beta.1`; this release adds what the live check on a site against Jira Cloud found.

- Checked live: form submission to a Jira issue, hostile text kept literal, an unknown issue type (lists the valid types), a rejected token (no retry), a response lost after Jira created the issue (the retry returns the same issue), Replay with a Dedupe key, configuration isolation (Automate refuses to resolve the credential key in other steps), a search of logs, database, exports and run data for the token (none found), and real Jira Cloud webhook deliveries (issue updated with a status change, comment created) through the signature check into Read Jira webhook event. Line 18 ran all of these; line 17 ran Validate, Create Jira issue and the real webhook deliveries.
- New package icon (an issue card with an arrow, in Umbraco navy and pink), Marketplace screenshots, and a Quick start at the top of the README.
- README: the Forms triggers need the workspace service account to have the Forms section; form fields bind as `${ trigger.fields.<alias> }`; set Error behaviour to Retry, because new steps start on Terminate; Replay starts a new run, so set a Dedupe key to make it return the same issue; Automate can mark a run Failed after a later attempt of the step succeeded.

## 18.0.0-beta.1

- Connection `community.jira.cloud` on package-owned credentials (`Umbraco:Automate:Jira:Credentials:{name}`), with Validate warnings for classic tokens, admin accounts and committed credentials.
- Actions: Create Jira issue and Add Jira comment (retry-safe duplicate protection), Transition Jira issue, Read Jira webhook event, Find content by Jira key.
- Webhook authenticator `community.jira.signature`: `X-Hub-Signature` HMAC-SHA256, timestamp window, replay check.
- Plain text, Markdown and ADF JSON input, all through an ADF allow-list.
- Names and messages from Jira are cleaned of control and text-direction characters.
- `appsettings` schema for `Umbraco:Automate:Jira`.
