using Json.Schema.Generation;

namespace Umbraco.Community.Automate.Jira.Actions;

/// <summary>Output of <c>community.jira.findContentByKey</c> (design 6.7). Field names are permanent once beta ships.</summary>
public sealed class FindContentByKeyOutput
{
    /// <summary>The matched node's key.</summary>
    [Description("The key of the one node whose Jira key property matches. Bind it into Request Approval and Publish Content.")]
    public Guid ContentKey { get; set; }

    /// <summary>The node's invariant name.</summary>
    [Description("The node's name, for logs and the approval prompt. Editors can change it, so never match on it.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>The node's content type alias.</summary>
    [Description("The node's content type alias.")]
    public string ContentTypeAlias { get; set; } = string.Empty;

    /// <summary>Whether any version of the node is published.</summary>
    [Description("True when a version of the node is currently published.")]
    public bool IsPublished { get; set; }

    /// <summary>When the draft was last saved.</summary>
    [Description("When the draft was last saved. Compare it after approval to make sure the draft did not change while waiting.")]
    public DateTime UpdateDate { get; set; }
}
