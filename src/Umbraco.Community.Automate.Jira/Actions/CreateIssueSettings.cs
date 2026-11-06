using Umbraco.Automate.Core.Settings;
using Umbraco.Community.Automate.Jira.Adf;

namespace Umbraco.Community.Automate.Jira.Actions;

/// <summary>Settings for <c>community.jira.createIssue</c> (design 6.2). Field names are permanent once beta ships.</summary>
public sealed class CreateIssueSettings
{
    /// <summary>The project key, for example <c>SUP</c>, or its numeric ID.</summary>
    [Field(Label = "Project key", Description = "The Jira project key, for example SUP.", SupportsBindings = true, SortOrder = 1)]
    public string ProjectKey { get; set; } = string.Empty;

    /// <summary>The issue type name (for example Task) or numeric ID.</summary>
    [Field(Label = "Issue type", Description = "An issue type name such as Task, or its numeric ID.", SupportsBindings = true, SortOrder = 2)]
    public string IssueType { get; set; } = string.Empty;

    /// <summary>The summary. Newlines become spaces, and it is cut to 255 characters.</summary>
    [Field(Label = "Summary", Description = "One line. Newlines become spaces and it is cut to 255 characters.", SupportsBindings = true, SortOrder = 3)]
    public string Summary { get; set; } = string.Empty;

    /// <summary>The description, read in <see cref="DescriptionFormat"/>.</summary>
    [Field(Label = "Description", Description = "Optional. Plain text unless you choose another format below.", EditorUiAlias = "Umb.PropertyEditorUi.MarkdownEditor", SupportsBindings = true, SortOrder = 4)]
    public string? Description { get; set; }

    /// <summary>How the description is read. No bindings: a run must not be able to switch Markdown on.</summary>
    [Field(
        Label = "Description format",
        Description = "Plain text is safest for values from forms or the public. Markdown interprets formatting in bound values.",
        EditorUiAlias = "Umb.PropertyEditorUi.Dropdown",
        EditorConfig = """[{ "alias": "items", "value": ["PlainText", "Markdown", "AdfJson"] }]""",
        SupportsBindings = false,
        SortOrder = 5)]
    public JiraTextFormat DescriptionFormat { get; set; } = JiraTextFormat.PlainText;

    /// <summary>Comma-separated labels. Spaces become hyphens; empty and repeated labels are dropped.</summary>
    [Field(Label = "Labels", Description = "Comma-separated. Spaces become hyphens.", SupportsBindings = true, SortOrder = 6)]
    public string? Labels { get; set; }

    /// <summary>A priority name, sent as <c>{ "name": … }</c>.</summary>
    [Field(Label = "Priority", Description = "Optional priority name, for example High.", SupportsBindings = true, SortOrder = 7)]
    public string? Priority { get; set; }

    /// <summary>An Atlassian account ID, sent as <c>{ "accountId": … }</c>.</summary>
    [Field(Label = "Assignee account ID", Description = "Optional Atlassian account ID (not an email address).", SupportsBindings = true, SortOrder = 8)]
    public string? AssigneeAccountId { get; set; }

    /// <summary>The parent issue key, for sub-tasks and epic children.</summary>
    [Field(Label = "Parent key", Description = "Optional parent issue key, for sub-tasks and epic children.", SupportsBindings = true, SortOrder = 9)]
    public string? ParentKey { get; set; }

    /// <summary>A JSON object merged into <c>fields</c>. No bindings (F-04): a bound value spliced into JSON is an injection point.</summary>
    [Field(
        Label = "Additional fields (JSON)",
        Description = "Optional JSON object. Allowed keys: customfield_<number>, components, fixVersions, versions, duedate, environment, labels. Bindings are not supported here.",
        EditorUiAlias = "Umb.PropertyEditorUi.TextArea",
        SupportsBindings = false,
        SortOrder = 10)]
    public string? AdditionalFieldsJson { get; set; }

    /// <summary>Widens duplicate protection across runs. Only a hash of it is sent to Jira.</summary>
    [Field(
        Label = "Dedupe key",
        Description = "Optional. Runs with the same key return the same issue instead of creating another. Prefer a non-personal value such as a form entry ID. Inside a For each loop, use a per-item value.",
        SupportsBindings = true,
        SortOrder = 11)]
    public string? DedupeKey { get; set; }
}
