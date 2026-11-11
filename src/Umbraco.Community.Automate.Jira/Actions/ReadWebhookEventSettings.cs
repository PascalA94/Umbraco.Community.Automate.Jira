using Umbraco.Automate.Core.Settings;

namespace Umbraco.Community.Automate.Jira.Actions;

/// <summary>Settings for <c>community.jira.readWebhookEvent</c>. Field names are permanent once beta ships.</summary>
public sealed class ReadWebhookEventSettings
{
    /// <summary>The webhook body. Defaults to the Webhook trigger's raw body.</summary>
    [Field(
        Label = "Webhook body",
        Description = "The Jira webhook JSON. Leave as ${ trigger.body } when the automation starts from a Webhook trigger.",
        SupportsBindings = true,
        SortOrder = 1)]
    public string Body { get; set; } = "${ trigger.body }";
}
