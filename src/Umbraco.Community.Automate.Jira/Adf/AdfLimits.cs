namespace Umbraco.Community.Automate.Jira.Adf;

/// <summary>Size and depth limits for ADF going to Jira (design 7.5, F-11).</summary>
internal static class AdfLimits
{
    /// <summary>Jira's text limit for a field, measured in spike S4 (32,768 characters gave 400).</summary>
    public const int MaxTextLength = 32767;

    /// <summary>Markdown and ADF JSON inputs larger than this (UTF-8 bytes) fail before parsing.</summary>
    public const int MaxInputBytes = 65536;

    /// <summary>Block nesting kept by the converters and the sanitiser; deeper content is flattened to text.</summary>
    public const int MaxNesting = 16;

    /// <summary><c>JsonDocumentOptions.MaxDepth</c> wherever JSON from outside is parsed.</summary>
    public const int MaxJsonDepth = 32;

    /// <summary>Appended to text cut at <see cref="MaxTextLength"/>.</summary>
    public const string TruncatedSuffix = " (truncated)";

    /// <summary>
    /// Cuts <paramref name="s"/> to at most <paramref name="max"/> UTF-16 units without splitting a surrogate pair,
    /// which Jira would reject as invalid text.
    /// </summary>
    public static string TruncateOnCodePoint(string s, int max)
    {
        if (s.Length <= max)
        {
            return s;
        }

        var cut = Math.Max(0, max);
        if (cut > 0 && char.IsHighSurrogate(s[cut - 1]))
        {
            cut--;
        }

        return s[..cut];
    }
}
