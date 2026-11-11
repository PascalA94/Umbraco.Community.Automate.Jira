using Umbraco.Automate.Core.Settings;

namespace Umbraco.Community.Automate.Jira.Actions;

/// <summary>Settings for <c>community.jira.findContentByKey</c> (design 6.7). Field names are permanent once beta ships.</summary>
public sealed class FindContentByKeySettings
{
    /// <summary>The Jira issue key to look for, for example <c>SUP-17</c>.</summary>
    [Field(Label = "Issue key", Description = "The Jira issue key, for example SUP-17. Usually bound to the Read Jira webhook event step's IssueKey.", SupportsBindings = true, SortOrder = 1)]
    public string IssueKey { get; set; } = string.Empty;

    // No bindings: the alias goes into the index query, so a run must never be able to choose it.
    /// <summary>The alias of the invariant property that holds the Jira key.</summary>
    [Field(Label = "Property alias", Description = "The alias of the invariant Textstring property that holds the Jira key. Defaults to jiraKey.", SupportsBindings = false, SortOrder = 2)]
    public string PropertyAlias { get; set; } = "jiraKey";

    // No bindings, and required: an empty list must never mean "search every type" (the built-in's behaviour).
    /// <summary>The content types to search, as a comma-separated list of content type keys.</summary>
    [Field(Label = "Content types", Description = "Required. Only these content types are searched.", EditorUiAlias = "Umb.PropertyEditorUi.DocumentTypePicker", SupportsBindings = false, SortOrder = 3)]
    public string ContentTypes { get; set; } = string.Empty;
}
