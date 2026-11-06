using Json.Schema.Generation;

namespace Umbraco.Community.Automate.Jira.Actions;

/// <summary>Output of <c>community.jira.transitionIssue</c>. Field names are permanent once beta ships.</summary>
public sealed class TransitionIssueOutput
{
    /// <summary>The status before the step ran.</summary>
    [Description("The issue's status before the step ran.")]
    public string FromStatus { get; set; } = string.Empty;

    /// <summary>The status after the step ran.</summary>
    [Description("The issue's status after the step ran.")]
    public string ToStatus { get; set; } = string.Empty;

    /// <summary>The transition used, or empty when the issue was already in the requested status.</summary>
    [Description("The ID of the transition used. Empty when the issue was already in the requested status.")]
    public string TransitionId { get; set; } = string.Empty;
}
