using Microsoft.Extensions.DependencyInjection;
using Umbraco.Automate.Core.Connections;
using Umbraco.Community.Automate.Jira.Client;
using Umbraco.Community.Automate.Jira.Credentials;

namespace Umbraco.Community.Automate.Jira.Connection;

/// <summary>
/// The <c>community.jira.cloud</c> connection: a Jira Cloud site plus the name of a package-owned credential.
/// Validate resolves the credential, calls <c>/myself</c> and reports what the connection can do (design 6.1).
/// </summary>
[ConnectionType(
    "community.jira.cloud",
    "Jira Cloud (API token)",
    Description = "Connects to a Jira Cloud site with an API token kept in configuration, never on the connection.",
    Group = "Jira",
    Icon = "icon-link")]
public sealed class JiraConnectionType : ConnectionTypeBase<JiraConnectionSettings>
{
    private static IReadOnlyList<string> AdminPermissions => ["ADMINISTER", "ADMINISTER_PROJECTS", "SYSTEM_ADMIN"];

    private readonly IServiceScopeFactory _scopes;

    /// <summary>
    /// Creates the connection type. Automate builds it through DI with a public constructor, and the credential
    /// store is internal, so services are resolved per validation from a scope; the client is scoped anyway
    /// (pitfall P2), and nothing is resolved or fetched at construction (pitfall P7).
    /// </summary>
    public JiraConnectionType(ConnectionTypeInfrastructure infrastructure, IServiceScopeFactory scopes)
        : base(infrastructure)
    {
        _scopes = scopes;
    }

    /// <inheritdoc />
    public override async Task<ConnectionValidationResult> ValidateAsync(object? settings, CancellationToken cancellationToken)
    {
        if (settings is not JiraConnectionSettings typed)
        {
            return ConnectionValidationResult.Failure("Enter the site URL and credential name.");
        }

        using var scope = _scopes.CreateScope();
        var credentials = scope.ServiceProvider.GetRequiredService<JiraCredentialStore>();

        // Validate has no saved connection to name, and the ID only keys caches and dedupe markers.
        if (!JiraTarget.TryCreate(typed, credentials, Guid.Empty, out var target, out var error))
        {
            return ConnectionValidationResult.Failure(error!);
        }

        var client = scope.ServiceProvider.GetRequiredService<IJiraClient>();
        var sourceCheck = scope.ServiceProvider.GetRequiredService<JiraCredentialSourceCheck>();

        JiraUser user;
        try
        {
            user = await client.GetMyselfAsync(target, cancellationToken);
        }
        catch (JiraRequestException ex)
        {
            return ConnectionValidationResult.Failure(await FailureMessageAsync(client, target, ex, cancellationToken));
        }

        var site = target.SiteBase.Host;
        var route = target.UsesGateway ? "the Atlassian gateway" : "the site route";
        var parts = new List<string>
        {
            $"Connected to {site} as {user.DisplayName} via {route}.",
            $"Credential '{target.CredentialName}' is site-wide: any workspace's connection can name it.",
            $"Anyone with Automate access to this workspace can use this connection to act in Jira as {user.DisplayName}.",
        };

        try
        {
            var held = await client.GetHeldPermissionsAsync(target, AdminPermissions, cancellationToken);
            if (held.Count > 0)
            {
                parts.Add($"Warning: the service account is a Jira admin ({string.Join(", ", held)}). Remove admin rights.");
            }
        }
        catch (JiraRequestException)
        {
            // A warning must never fail an otherwise working connection.
            parts.Add("Couldn't check whether the service account has Jira admin rights.");
        }

        if (!target.UsesGateway)
        {
            var cloudId = await TryGetCloudIdAsync(client, target, cancellationToken);
            parts.Add("Warning: this is a classic (unscoped) token. Use a scoped token through the gateway"
                + (cloudId is { } id ? $" (this site's Cloud ID is {id:D})." : "."));
        }

        foreach (var part in new[] { JiraCredentialPart.Email, JiraCredentialPart.ApiToken })
        {
            if (sourceCheck.FindCommittedFile(target.CredentialName, part) is { } file)
            {
                parts.Add($"Warning: Credential '{target.CredentialName}' is read from {file}. Move it to environment variables, Umbraco Cloud secrets or Key Vault.");
                break;
            }
        }

        return ConnectionValidationResult.Success(string.Join(" ", parts));
    }

    private static async Task<string> FailureMessageAsync(IJiraClient client, JiraTarget target, JiraRequestException ex, CancellationToken ct)
    {
        if (ex.StatusCode != 401)
        {
            return ex.Message;
        }

        if (target.UsesGateway)
        {
            // Atlassian answers a missing scope with the same 401 as a bad token (spike S3).
            return ex.Message + " A scoped token also needs the read:jira-user scope for this check.";
        }

        // Scoped tokens only work through the gateway, which needs the Cloud ID (spike S3).
        return await TryGetCloudIdAsync(client, target, ct) is { } cloudId
            ? $"{ex.Message} If this is a scoped token, it needs the gateway: this site's Cloud ID is {cloudId:D}; paste it into Cloud ID."
            : ex.Message;
    }

    private static async Task<Guid?> TryGetCloudIdAsync(IJiraClient client, JiraTarget target, CancellationToken ct)
    {
        try
        {
            return await client.GetTenantCloudIdAsync(target.SiteBase, ct);
        }
        catch (JiraRequestException)
        {
            return null;
        }
    }
}
