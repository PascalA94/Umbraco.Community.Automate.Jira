# Runbook

Operational steps for sites using the Jira connector for Umbraco Automate. "The credential" means the values under `Umbraco:Automate:Jira:Credentials:{name}`.

**Who can rotate:** Pascal Meyer, and [[SECOND-PERSON]].

## Rotate the API token

1. In the Atlassian account of the service account, create a new scoped API token with the same scopes and an expiry of 90 days or less.
2. Update `…:Credentials:{name}:ApiToken` in every environment (environment variable, Umbraco Cloud secret, Key Vault or user-secrets).
3. Restart the site if the value comes from an environment variable or a Cloud secret. JSON files with `reloadOnChange` and reloading Key Vault providers need no restart.
4. Click **Validate** on each connection that uses the name. It should show the service account.
5. Revoke the old token in Atlassian.

## Rotate a webhook secret

The Jira webhook and the site must change together. Deliveries between the two changes get a 401 and start no run.

1. Generate 32 or more random characters, for example `openssl rand -base64 33`.
2. Update `…:Credentials:{name}:WebhookSecret` on the site (restart if needed).
3. Update the secret on the Jira webhook (Settings → System → WebHooks).
4. Make a test change in Jira and check the automation's run list.

## Suspected exfiltration

1. Revoke the API token in Atlassian straight away, then rotate as above. Rotate the webhook secret too.
2. Check the Jira audit log for actions by the service account since the suspected time.
3. In Automate, check recent runs and automations for **HTTP Request** steps, especially any that reference `$Umbraco:Automate:Jira:Credentials` or send data to unknown hosts.
4. Check `Umbraco:Automate:AllowedConfigurationKeyPrefixes` in every environment (see below).
5. Export the affected Automate runs and the Jira audit log to restricted storage. They are the evidence for the investigation and for any personal data breach assessment.
6. Then delete the run records from the exposure window that hold resolved credential values.

## The allow-list guard tripped

Symptom: a `Critical` log at startup, and steps failing with an error naming an `AllowedConfigurationKeyPrefixes` entry.

1. Remove the named entry (`Umbraco`, `Umbraco:Automate`, `Umbraco:Automate:Jira`, an empty or wildcard entry, or anything under `Umbraco:Automate:Jira:Credentials`).
2. Restart the site. Automate reads the allow-list once at startup.
3. Treat the credential as exposed for the time the entry was present: rotate it, export the runs from that window to restricted storage, then delete the run records that hold resolved values.

## Upgrading Automate

1. Read the new version's `AllowedConfigurationKeyPrefixes` defaults in its release notes or source.
2. Check the repository's CI "latest" leg is green on the new version (it floats to the newest Automate patch of each line).
3. On a site with the new version, check that no credential value appears in: health checks, the log viewer, Automate's health and run views, and the Management API endpoints listed in Swagger.
4. If Automate's configuration reference resolver changed, re-run the credential isolation checks (an HTTP Request step referencing `$Umbraco:Automate:Jira:Credentials:{name}:ApiToken` must send the literal text, not the token).
