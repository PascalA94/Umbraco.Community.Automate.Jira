using Umbraco.Automate.Core.Settings;

namespace Umbraco.Community.Automate.Jira.Actions;

/// <summary>Settings for <c>community.jira.transitionIssue</c> (design 6.4). Field names are permanent once beta ships.</summary>
public sealed class TransitionIssueSettings
{
    /// <summary>The issue key, for example <c>SUP-17</c>, or its numeric ID.</summary>
    [Field(Label = "Issue key", Description = "The Jira issue key, for example SUP-17.", SupportsBindings = true, SortOrder = 1)]
    public string IssueKey { get; set; } = string.Empty;

    /// <summary>A transition ID, transition name or target status name.</summary>
    [Field(Label = "Transition", Description = "The status to move to, such as Done (safest: a retry sees it is already there), or a transition name such as Close, or a numeric transition ID.", SupportsBindings = true, SortOrder = 2)]
    public string Transition { get; set; } = string.Empty;

    /// <summary>Plain text, added as a comment in the same call.</summary>
    [Field(Label = "Comment", Description = "Optional. Plain text, added as a comment when the issue moves.", EditorUiAlias = "Umb.PropertyEditorUi.TextArea", SupportsBindings = true, SortOrder = 3)]
    public string? Comment { get; set; }

    /// <summary>A resolution name, sent as <c>fields.resolution.name</c>.</summary>
    [Field(Label = "Resolution", Description = "Optional resolution name, for example Fixed. The transition's screen must include it.", SupportsBindings = true, SortOrder = 4)]
    public string? ResolutionName { get; set; }
}
