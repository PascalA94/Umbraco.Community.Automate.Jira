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
        /// The timeout for each Jira request, in seconds. Default 30; values below 1 are raised to 1.
        /// </summary>
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// How long the issue types of a project are cached, in minutes. Default 10.
        /// </summary>
        public int IssueTypeCacheMinutes { get; set; } = 10;

        /// <summary>
        /// The accepted age of a signed Jira webhook delivery, and how long a delivery is remembered to refuse a
        /// replay, in minutes. Default 120; clamped to 10..1440. The replay memory is per node.
        /// </summary>
        public int ReplayWindowMinutes { get; set; } = 120;

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
