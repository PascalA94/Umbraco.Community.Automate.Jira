using System.Diagnostics.CodeAnalysis;
using System.Text;
using Umbraco.Community.Automate.Jira.Credentials;

namespace Umbraco.Community.Automate.Jira.Connection;

/// <summary>
/// Everything one Jira request is built from: where to send it, and the credential to send. A class, not a
/// record, so no generated <c>ToString</c> or equality ever walks the credential.
/// </summary>
public sealed class JiraTarget
{
    // The gateway host is a constant: it is the only host besides the validated site a credential may reach.
    private const string GatewayRoot = "https://api.atlassian.com/ex/jira/";

    private JiraTarget(Uri siteBase, Uri apiBase, Guid connectionId, bool usesGateway, string credentialName, string authorizationParameter)
    {
        SiteBase = siteBase;
        ApiBase = apiBase;
        ConnectionId = connectionId;
        UsesGateway = usesGateway;
        CredentialName = credentialName;
        AuthorizationParameter = authorizationParameter;
    }

    /// <summary>The validated site, for example <c>https://acme.atlassian.net</c>. Issue links always use it.</summary>
    public Uri SiteBase { get; }

    /// <summary>Where requests go, with a trailing slash: the gateway when a cloud ID is set, otherwise the site.</summary>
    public Uri ApiBase { get; }

    /// <summary>The connection this target was built for.</summary>
    public Guid ConnectionId { get; }

    /// <summary>True when requests go through the Atlassian gateway (scoped tokens need it).</summary>
    public bool UsesGateway { get; }

    /// <summary>The normalised credential name. A name, never a value.</summary>
    public string CredentialName { get; }

    /// <summary>The Basic credentials, base64 of <c>email:token</c>. Internal so nothing outside the package can read it.</summary>
    internal string AuthorizationParameter { get; }

    /// <summary>
    /// Validates the connection settings and resolves the credential through the store. The credential is read on
    /// every call and never cached, so a rotated token reaches the next request. Store errors are returned as they
    /// are (they name a config key, never a value), and no HTTP request is made.
    /// </summary>
    internal static bool TryCreate(
        JiraConnectionSettings settings,
        JiraCredentialStore credentials,
        Guid connectionId,
        [NotNullWhen(true)] out JiraTarget? target,
        out string? error)
    {
        target = null;

        // Validate the site before touching credentials: a bad site must never cost a credential read.
        if (!JiraSiteUrl.TryNormalise(settings.SiteUrl, out var site, out error))
        {
            return false;
        }

        if (!JiraSiteUrl.TryParseCloudId(settings.CloudId, out var cloudId, out error))
        {
            return false;
        }

        if (!credentials.TryNormaliseName(settings.CredentialName, out var name, out error))
        {
            return false;
        }

        if (!credentials.TryGet(name, JiraCredentialPart.Email, out var email, out error)
            || !credentials.TryGet(name, JiraCredentialPart.ApiToken, out var token, out error))
        {
            return false;
        }

        var apiBase = cloudId is { } id
            ? new Uri($"{GatewayRoot}{id:D}/")
            : new Uri(site!.GetLeftPart(UriPartial.Authority) + "/");
        var parameter = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:{token}"));

        target = new JiraTarget(site!, apiBase, connectionId, cloudId is not null, name!, parameter);
        error = null;
        return true;
    }

    /// <summary>The host only, so a log line or exception text can name the target without any credential.</summary>
    public override string ToString() => $"Jira {SiteBase.Host}";
}
