using Umbraco.Automate.Core.Settings;
using Umbraco.Community.Automate.Jira.Adf;

namespace Umbraco.Community.Automate.Jira.Actions;

/// <summary>Settings for <c>community.jira.addComment</c> (design 6.3). Field names are permanent once beta ships.</summary>
public sealed class AddCommentSettings
{
    /// <summary>The issue key, for example <c>SUP-17</c>, or its numeric ID.</summary>
    [Field(Label = "Issue key", Description = "The Jira issue key, for example SUP-17.", SupportsBindings = true, SortOrder = 1)]
    public string IssueKey { get; set; } = string.Empty;

    /// <summary>The comment, read in <see cref="BodyFormat"/>.</summary>
    [Field(Label = "Comment", Description = "Plain text unless you choose another format below.", EditorUiAlias = "Umb.PropertyEditorUi.MarkdownEditor", SupportsBindings = true, SortOrder = 2)]
    public string Body { get; set; } = string.Empty;

    /// <summary>How the comment is read. No bindings: a run must not be able to switch Markdown on.</summary>
    [Field(
        Label = "Comment format",
        Description = "Plain text is safest for values from forms or the public. Markdown interprets formatting in bound values.",
        EditorUiAlias = "Umb.PropertyEditorUi.Dropdown",
        EditorConfig = """[{ "alias": "items", "value": ["PlainText", "Markdown", "AdfJson"] }]""",
        SupportsBindings = false,
        SortOrder = 3)]
    public JiraTextFormat BodyFormat { get; set; } = JiraTextFormat.PlainText;

    /// <summary>Widens duplicate protection across runs. Only a hash of it is sent to Jira.</summary>
    [Field(
        Label = "Dedupe key",
        Description = "Optional. Runs with the same key return the same comment instead of adding another. Prefer a non-personal value. Inside a For each loop, use a per-item value.",
        SupportsBindings = true,
        SortOrder = 4)]
    public string? DedupeKey { get; set; }
}
