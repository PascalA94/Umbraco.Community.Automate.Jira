using Json.Schema.Generation;

namespace Umbraco.Community.Automate.Jira.Actions;

/// <summary>Output of <c>community.jira.addComment</c>. Field names are permanent once beta ships.</summary>
public sealed class AddCommentOutput
{
    /// <summary>The comment's numeric ID.</summary>
    [Description("The comment's numeric ID, for example 10500.")]
    public string CommentId { get; set; } = string.Empty;

    /// <summary>A link to the comment on the Jira site.</summary>
    [Description("A link to the comment on the Jira site.")]
    public string CommentUrl { get; set; } = string.Empty;

    /// <summary>True when the dedupe check found a comment this step had already added.</summary>
    [Description("True when an existing comment with the same dedupe key was returned instead of adding one.")]
    public bool WasExisting { get; set; }
}
