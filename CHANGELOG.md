# Changelog

Versions follow `<Umbraco major>.<minor>.<patch>`. One tag ships the 17 and 18 lines.

## Unreleased

## 18.0.0-beta.1

- Connection `community.jira.cloud` on package-owned credentials (`Umbraco:Automate:Jira:Credentials:{name}`), with Validate warnings for classic tokens, admin accounts and committed credentials.
- Actions: Create Jira issue and Add Jira comment (retry-safe duplicate protection), Transition Jira issue, Read Jira webhook event, Find content by Jira key.
- Webhook authenticator `community.jira.signature`: `X-Hub-Signature` HMAC-SHA256, timestamp window, replay check.
- Plain text, Markdown and ADF JSON input, all through an ADF allow-list.
- Names and messages from Jira are cleaned of control and text-direction characters.
- `appsettings` schema for `Umbraco:Automate:Jira`.
