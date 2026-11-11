# Security policy

## Reporting a vulnerability

Report it privately through a [GitHub security advisory](https://github.com/PascalA94/Umbraco.Community.Automate.Jira/security/advisories/new) on this repository (Security tab, "Report a vulnerability"). Please do not open a public issue. You should get a reply within a week.

## Supported versions

| Version | Supported |
|---|---|
| `18.x` (latest minor) | Yes |
| `17.x` (latest minor) | Yes |
| Pre-releases (`-beta`) | Until the matching stable release |
| `0.0.1-alpha` | No (an ID reservation with no code) |

## What the package claims

1. The package never sends a credential to a host other than Atlassian's.
2. Automate authors cannot read the raw credentials: no Automate step can resolve the `Umbraco:Automate:Jira:Credentials` section, while Automate's `AllowedConfigurationKeyPrefixes` does not cover it.
3. Automate section access to a workspace still equals **use** of the connected Jira account through this package's actions.
4. Credential names are site-wide, not per workspace.
5. Other server-side code that reads `IConfiguration` can still read the section. Protect it like any other server secret.

The package also:
- never logs the `Authorization` header, request or response bodies, or raw network error text;
- accepts a Jira webhook only with a valid `X-Hub-Signature`, a fresh timestamp and a body not seen before (per server);
- documents Jira-driven publishing only with Request Approval, and seeds it disabled.

The known residuals are listed in the [README](https://github.com/PascalA94/Umbraco.Community.Automate.Jira#readme): the per-server replay memory, the dedupe search lag, and the moment between the approval re-check and publishing.

## Handling an incident

Rotation, suspected exfiltration and the allow-list guard are covered in [RUNBOOK.md](RUNBOOK.md).
