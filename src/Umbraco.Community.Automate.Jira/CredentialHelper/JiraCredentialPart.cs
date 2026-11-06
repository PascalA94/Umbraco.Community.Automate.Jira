namespace Umbraco.Community.Automate.Jira.Credentials;

/// <summary>
/// The credential values the package reads from configuration for one credential name.
/// </summary>
// Public only because the contract test classes must be public for xUnit v2 and name this enum as a type argument.
public enum JiraCredentialPart
{
    /// <summary>The service account email.</summary>
    Email,

    /// <summary>The Atlassian API token.</summary>
    ApiToken,

    /// <summary>The webhook signing secret.</summary>
    WebhookSecret,
}
