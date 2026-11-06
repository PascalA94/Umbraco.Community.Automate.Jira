using System.Globalization;
using System.Text.RegularExpressions;

namespace Umbraco.Community.Automate.Jira.Connection;

/// <summary>
/// Validates the Jira Cloud site URL and cloud ID before any request is built (security review F-12).
/// The site URL takes no bindings, so this is the only barrier between an author and the host the
/// package sends a credential to.
/// </summary>
public static partial class JiraSiteUrl
{
    private const string SiteSuffix = ".atlassian.net";

    private const string InvalidSiteUrl = "Site URL must look like https://your-site.atlassian.net.";

    private const string InvalidCloudId = "Cloud ID must be a GUID such as 6f9619ff-8b86-d011-b42d-00c04fc964ff.";

    /// <summary>Trims and validates a site URL, returning the canonical root URI (lower case, no trailing slash).</summary>
    public static bool TryNormalise(string? input, out Uri? siteUri, out string? error)
    {
        siteUri = null;
        if (input is null)
        {
            error = InvalidSiteUrl;
            return false;
        }

        return TryValidateExact(input.Trim(), out siteUri, out error);
    }

    /// <summary>Parses a cloud ID. Blank is valid and yields <c>null</c>.</summary>
    public static bool TryParseCloudId(string? input, out Guid? cloudId, out string? error)
    {
        cloudId = null;
        error = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return true;
        }

        // "D" only: it is the form Atlassian issues, and it keeps braces and path text out of a URL segment.
        if (Guid.TryParseExact(input.Trim(), "D", out var parsed))
        {
            cloudId = parsed;
            return true;
        }

        error = InvalidCloudId;
        return false;
    }

    /// <summary>
    /// Regex-plus-<see cref="Uri"/> validation without trimming. Internal so a test can prove the
    /// <c>\z</c> anchor rejects a trailing newline on its own, not because a caller trimmed first.
    /// </summary>
    internal static bool TryValidateExact(string input, out Uri? siteUri, out string? error)
    {
        siteUri = null;
        error = InvalidSiteUrl;

        // Under IgnoreCase the Kelvin sign U+212A folds to 'k', so non-ASCII is refused before the regex.
        if (input.Any(c => c > 0x7F) || !SiteUrlRule().IsMatch(input))
        {
            return false;
        }

        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.UserInfo.Length != 0
            || !uri.IsDefaultPort
            || uri.AbsolutePath != "/"
            || uri.Query.Length != 0
            || uri.Fragment.Length != 0)
        {
            return false;
        }

        var host = uri.Host.ToLower(CultureInfo.InvariantCulture);
        if (!host.EndsWith(SiteSuffix, StringComparison.Ordinal))
        {
            return false;
        }

        // Exactly one label before the suffix: a.b.atlassian.net is not a site.
        var label = host[..^SiteSuffix.Length];
        if (label.Length == 0 || label.Contains('.'))
        {
            return false;
        }

        siteUri = new Uri($"https://{host}", UriKind.Absolute);
        error = null;
        return true;
    }

    // \z, not $: $ also matches before a final newline.
    [GeneratedRegex(@"^https://[a-z0-9-]+\.atlassian\.net/?\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SiteUrlRule();
}
