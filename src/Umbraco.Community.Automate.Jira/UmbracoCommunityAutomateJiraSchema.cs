// Schema wrapper consumed by the JsonSchemaGenerate MSBuild task at build time. It describes the
// appsettings.json shape below Umbraco:Automate:Jira so editors get IntelliSense against
// appsettings-schema.Umbraco.Community.Automate.Jira.json.
internal sealed class UmbracoCommunityAutomateJiraSchema
{
    /// <summary>
    /// Configuration container for all Umbraco products.
    /// </summary>
    public required UmbracoDefinition Umbraco { get; set; }

    public sealed class UmbracoDefinition
    {
        /// <summary>
        /// Configuration of Umbraco Automate.
        /// </summary>
        public required UmbracoAutomateDefinition Automate { get; set; }
    }

    public sealed class UmbracoAutomateDefinition
    {
        /// <summary>
        /// Configuration of the Jira add-on.
        /// </summary>
        public required JiraDefinition Jira { get; set; }
    }

    public sealed class JiraDefinition
    {
        /// <summary>
        /// Jira credentials, keyed by the credential name that a Jira connection or webhook authenticator refers to.
        /// The name is 1 to 32 lower-case letters or digits.
        /// </summary>
        public Dictionary<string, JiraCredentialDefinition> Credentials { get; set; } = new();
    }

    public sealed class JiraCredentialDefinition
    {
        /// <summary>
        /// The Atlassian account email used for Jira Cloud basic authentication.
        /// </summary>
        public string? Email { get; set; }

        /// <summary>
        /// The Atlassian API token. Keep it in an environment variable or a secret store, not in a committed file.
        /// </summary>
        public string? ApiToken { get; set; }

        /// <summary>
        /// The shared secret used to verify signed Jira webhook deliveries.
        /// </summary>
        public string? WebhookSecret { get; set; }
    }
}
