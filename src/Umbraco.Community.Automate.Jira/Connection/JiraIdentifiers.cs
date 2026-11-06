using System.Text;
using System.Text.RegularExpressions;

namespace Umbraco.Community.Automate.Jira.Connection;

/// <summary>
/// Validates Jira identifiers before any URL is built (finding F-09). Every method trims first, and
/// rejects non-ASCII before case-folding, because the Kelvin sign U+212A matches ASCII 'k' under IgnoreCase.
/// Failures carry a fixed message that never echoes the input.
/// </summary>
public static partial class JiraIdentifiers
{
    private const string InvalidIssue = "Issue must be a key such as SUP-17 or a numeric ID.";
    private const string InvalidIssueKey = "Issue must be a key such as SUP-17.";
    private const string InvalidProject = "Project must be a key such as SUP or a numeric ID.";

    /// <summary>An issue key (upper-cased) or a numeric issue ID.</summary>
    public static bool TryIssueKeyOrId(string? input, out string? value, out string? error)
    {
        if (TryUpperAscii(input, out var upper) && (IssueKeyRule().IsMatch(upper) || NumericIdRule().IsMatch(upper)))
        {
            value = upper;
            error = null;
            return true;
        }

        value = null;
        error = InvalidIssue;
        return false;
    }

    /// <summary>An issue key only (upper-cased); numeric IDs are refused.</summary>
    public static bool TryIssueKey(string? input, out string? value, out string? error)
    {
        if (TryUpperAscii(input, out var upper) && IssueKeyRule().IsMatch(upper))
        {
            value = upper;
            error = null;
            return true;
        }

        value = null;
        error = InvalidIssueKey;
        return false;
    }

    /// <summary>A project key (upper-cased) or a numeric project ID.</summary>
    public static bool TryProjectKeyOrId(string? input, out string? value, out string? error)
    {
        if (TryUpperAscii(input, out var upper) && (ProjectKeyRule().IsMatch(upper) || NumericIdRule().IsMatch(upper)))
        {
            value = upper;
            error = null;
            return true;
        }

        value = null;
        error = InvalidProject;
        return false;
    }

    /// <summary>True when the trimmed input is a numeric ID of ASCII digits (for transition IDs and the like).</summary>
    public static bool IsNumericId(string? input) =>
        input is not null && NumericIdRule().IsMatch(input.Trim());

    /// <summary>Quotes a value as a JQL string literal, escaping backslash, quote and line breaks.</summary>
    public static string QuoteJql(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var c in value)
        {
            builder.Append(c switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => c.ToString(),
            });
        }

        return builder.Append('"').ToString();
    }

    private static bool TryUpperAscii(string? input, out string upper)
    {
        upper = string.Empty;
        if (input is null)
        {
            return false;
        }

        var trimmed = input.Trim();
        if (trimmed.Any(c => c > 0x7F))
        {
            return false;
        }

        upper = trimmed.ToUpperInvariant();
        return true;
    }

    // Case is folded to upper before matching, so the patterns need no IgnoreCase. \z, never $.
    [GeneratedRegex(@"^[A-Z][A-Z0-9_]*-[1-9][0-9]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex IssueKeyRule();

    [GeneratedRegex(@"^[A-Z][A-Z0-9_]{1,254}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ProjectKeyRule();

    // [0-9], not \d, which also matches other Unicode digits. 18 digits keeps the value inside a long.
    [GeneratedRegex(@"^[0-9]{1,18}\z", RegexOptions.CultureInvariant)]
    private static partial Regex NumericIdRule();
}
