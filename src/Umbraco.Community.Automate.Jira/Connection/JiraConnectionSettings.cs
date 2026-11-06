using Umbraco.Automate.Core.Settings;

namespace Umbraco.Community.Automate.Jira.Connection;

/// <summary>
/// Settings for the <c>community.jira.cloud</c> connection. Nothing here is a secret: the email and
/// API token live in configuration and are read by the credential store, never stored on the connection.
/// </summary>
public sealed class JiraConnectionSettings
{
    /// <summary>The Jira Cloud site, for example <c>https://acme.atlassian.net</c>.</summary>
    // No bindings: the host a credential is sent to must not be steerable from run data.
    [Field(Label = "Site URL", Description = "Your Jira Cloud site, for example https://acme.atlassian.net.", SupportsBindings = false, SortOrder = 1)]
    public string SiteUrl { get; set; } = string.Empty;

    /// <summary>The name of a package-owned credential in configuration (not the credential itself).</summary>
    [Field(Label = "Credential name", Description = "The name of the credential configured under Umbraco:Automate:Jira:Credentials. This is a name, not the token.", SupportsBindings = false, IsSensitive = false, SortOrder = 2)]
    public string CredentialName { get; set; } = string.Empty;

    /// <summary>The Atlassian cloud ID (a GUID). Needed for scoped API tokens, which use the Atlassian gateway.</summary>
    [Field(Label = "Cloud ID", Description = "Optional GUID. Scoped API tokens need it; leave blank for a classic token on the site URL.", SupportsBindings = false, SortOrder = 3)]
    public string? CloudId { get; set; }
}
