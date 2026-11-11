using Json.Schema.Generation;

namespace Umbraco.Community.Automate.Jira.Actions;

/// <summary>
/// Output of <c>community.jira.readWebhookEvent</c> (design 6.6). Only these fields, never a dump of the payload
/// (F-06). Field names are permanent once beta ships.
/// </summary>
public sealed class ReadWebhookEventOutput
{
    /// <summary>The event name.</summary>
    [Description("The Jira event, for example jira:issue_updated or comment_created.")]
    public string WebhookEvent { get; set; } = string.Empty;

    /// <summary>The issue key.</summary>
    [Description("The issue key, for example SUP-17. Empty when the event has no issue.")]
    public string? IssueKey { get; set; }

    /// <summary>The issue's numeric ID.</summary>
    [Description("The issue's numeric ID.")]
    public string? IssueId { get; set; }

    /// <summary>The project key.</summary>
    [Description("The project key, for example SUP.")]
    public string? ProjectKey { get; set; }

    /// <summary>The issue type name.</summary>
    [Description("The issue type name, for example Task.")]
    public string? IssueType { get; set; }

    /// <summary>The issue summary. Untrusted user text (F-17).</summary>
    [Description("The issue summary. Untrusted user text: any Jira user can set it. Use plain-text targets or | strip_html.")]
    public string? Summary { get; set; }

    /// <summary>The issue's current status name.</summary>
    [Description("The issue's status name when the event was sent.")]
    public string? Status { get; set; }

    /// <summary>The status before this update, only when this update changed the status.</summary>
    [Description("The previous status, set only when this event changed the status. Empty for comment or field edits.")]
    public string? StatusChangedFrom { get; set; }

    /// <summary>The status after this update, only when this update changed the status.</summary>
    [Description("The new status, set only when this event changed the status. Empty for comment or field edits.")]
    public string? StatusChangedTo { get; set; }

    /// <summary>The Atlassian account ID of the user who caused the event.</summary>
    [Description("The Atlassian account ID of the user who caused the event.")]
    public string? UserAccountId { get; set; }

    /// <summary>That user's display name. Untrusted user text (F-17).</summary>
    [Description("The display name of the user who caused the event. Untrusted user text: users choose their own names.")]
    public string? UserDisplayName { get; set; }

    /// <summary>The changelog items, field name and before and after text only.</summary>
    [Description("The fields this event changed, each with Field, From and To. Untrusted user text: values come from Jira users.")]
    public IReadOnlyList<JiraChange> Changes { get; set; } = [];

    /// <summary>The comment as plain text. Untrusted user text (F-17).</summary>
    [Description("The comment as plain text, for comment events. Untrusted user text: use plain-text targets or | strip_html.")]
    public string? CommentBody { get; set; }

    /// <summary>The event time in epoch milliseconds.</summary>
    [Description("When Jira sent the event, in epoch milliseconds.")]
    public long? Timestamp { get; set; }
}

/// <summary>One changelog item: the field name and its text before and after.</summary>
/// <param name="Field">The field name, for example status.</param>
/// <param name="From">The text before the change, or null.</param>
/// <param name="To">The text after the change, or null.</param>
public sealed record JiraChange(string Field, string? From, string? To);
