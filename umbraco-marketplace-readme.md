# Jira connector for Umbraco Automate

Connect Umbraco Automate to Jira Cloud.

- **Create Jira issue**, **Add Jira comment** and **Transition Jira issue** actions. Retries check for what an earlier attempt created (see the README for the limits).
- **Jira webhook secret** authenticator for Automate's Webhook trigger: checks Jira's signature, the timestamp and replays.
- **Read Jira webhook event** turns a Jira webhook into bindable fields.
- **Find content by Jira key** finds the one page tied to an issue, by a property, never by name.
- The Jira API token stays in configuration (environment variables, Key Vault or user-secrets) under a name. Automations hold only the name.

Requires Umbraco 17.8+ with Automate 17.5.1+, or Umbraco 18.3+ with Automate 18.5.1+. Jira Cloud only.

A community package, not made or supported by Umbraco HQ, and not affiliated with Atlassian. Full documentation: https://github.com/PascalA94/Umbraco.Community.Automate.Jira#readme
