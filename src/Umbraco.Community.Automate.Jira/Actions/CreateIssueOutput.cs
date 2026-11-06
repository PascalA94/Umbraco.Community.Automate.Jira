using Json.Schema.Generation;

namespace Umbraco.Community.Automate.Jira.Actions;

/// <summary>Output of <c>community.jira.createIssue</c>. Field names are permanent once beta ships.</summary>
public sealed class CreateIssueOutput
{
    /// <summary>The issue's numeric ID.</summary>
    [Description("The issue's numeric ID, for example 10042.")]
    public string IssueId { get; set; } = string.Empty;

    /// <summary>The issue key.</summary>
    [Description("The issue key, for example SUP-17.")]
    public string IssueKey { get; set; } = string.Empty;

    /// <summary>A browse link on the Jira site.</summary>
    [Description("A link to the issue on the Jira site.")]
    public string IssueUrl { get; set; } = string.Empty;

    /// <summary>True when the dedupe search found an issue this step had already created.</summary>
    [Description("True when an existing issue with the same dedupe key was returned instead of creating one.")]
    public bool WasExisting { get; set; }
}
