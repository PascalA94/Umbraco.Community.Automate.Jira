using Umbraco.Automate.Core.Triggers.Webhooks;

namespace Umbraco.Community.Automate.Jira.Webhooks;

/// <summary>
/// Scaffold stub. Rejects every request until the real signature check lands, so that a
/// registered but unfinished authenticator can never start a run (pitfall S4).
/// </summary>
[WebhookAuthenticator("community.jira.signature", "Jira signature")]
public sealed class JiraSignatureWebhookAuthenticator : WebhookAuthenticatorBase<JiraSignatureSettings>
{
    /// <inheritdoc />
    protected override bool Validate(WebhookAuthenticationContext context, JiraSignatureSettings settings) => false;
}

/// <summary>
/// Settings for <see cref="JiraSignatureWebhookAuthenticator"/>. Filled in with the real authenticator.
/// </summary>
public sealed class JiraSignatureSettings
{
}
