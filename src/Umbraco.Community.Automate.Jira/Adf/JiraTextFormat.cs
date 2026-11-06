namespace Umbraco.Community.Automate.Jira.Adf;

/// <summary>How a text setting is turned into Atlassian Document Format (design 7.5).</summary>
public enum JiraTextFormat
{
    /// <summary>Blank lines make paragraphs, single newlines make line breaks. Nothing is interpreted. The safe default for bound values.</summary>
    PlainText = 0,

    /// <summary>A small Markdown subset: headings, lists, code, quotes, rules and basic marks.</summary>
    Markdown = 1,

    /// <summary>An ADF document, checked and passed through the same safety pass.</summary>
    AdfJson = 2,
}
