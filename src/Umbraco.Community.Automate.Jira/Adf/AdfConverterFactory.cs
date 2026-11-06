namespace Umbraco.Community.Automate.Jira.Adf;

/// <summary>Holds one instance of each converter. They are stateless, so a singleton is safe (pitfall P2).</summary>
internal sealed class AdfConverterFactory(PlainTextAdfConverter plainText, MarkdownAdfConverter markdown, AdfJsonPassthrough adfJson) : IAdfConverterFactory
{
    public IAdfConverter For(JiraTextFormat format) => format switch
    {
        JiraTextFormat.PlainText => plainText,
        JiraTextFormat.Markdown => markdown,
        JiraTextFormat.AdfJson => adfJson,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown text format."),
    };
}
