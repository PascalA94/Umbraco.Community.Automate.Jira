using Umbraco.Automate.Core.Settings;

namespace Umbraco.Community.Automate.Jira.Webhooks;

/// <summary>
/// Settings for <see cref="JiraSignatureWebhookAuthenticator"/>. Holds a credential name, never a secret.
/// </summary>
/// <remarks>
/// F-15: there is deliberately no field named <c>Secret</c>. If the package is removed, Automate falls back to its
/// plain-secret authenticator and reads these settings against its own schema, whose field is <c>Secret</c>; with
/// none here, that fallback finds an empty secret and fails closed. The field name is permanent from beta.1.
/// </remarks>
public sealed class JiraSignatureSettings
{
    // The OpenAPI source generator on line 18 copies public XML doc comments into IL strings, so this summary must
    // not spell out the credential prefix: the contract scan (OnlyTheStoreFile_NamesThePrefixOrCallsKeyFor) counts it.
    /// <summary>The credential name. The credential store reads the webhook secret for this name.</summary>
    [Field(
        Label = "Credential name",
        Description = "The name of the credential set in configuration. The webhook secret is read from Umbraco:Automate:Jira:Credentials:{name}:WebhookSecret and must be at least 32 characters. Lower-case letters and digits, up to 32.",
        SortOrder = 1,
        SupportsBindings = false,
        IsSensitive = false)]
    public string CredentialName { get; set; } = string.Empty;
}
