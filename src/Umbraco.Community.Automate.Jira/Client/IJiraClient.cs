using System.Text.Json.Nodes;
using Umbraco.Community.Automate.Jira.Connection;

namespace Umbraco.Community.Automate.Jira.Client;

/// <summary>
/// The one place that talks to Jira Cloud REST API v3. Each call takes a <see cref="JiraTarget"/> built from
/// the connection and the resolved credential, so the client is not bound to one connection. Failures throw
/// <see cref="JiraRequestException"/>.
/// </summary>
public interface IJiraClient
{
    /// <summary><c>GET /rest/api/3/myself</c>.</summary>
    Task<JiraUser> GetMyselfAsync(JiraTarget target, CancellationToken ct);

    /// <summary><c>GET {site}/_edge/tenant_info</c>, sent without an <c>Authorization</c> header. Null when the site does not report one.</summary>
    Task<Guid?> GetTenantCloudIdAsync(Uri siteBase, CancellationToken ct);

    /// <summary><c>GET /rest/api/3/issue/createmeta/{project}/issuetypes</c>.</summary>
    Task<IReadOnlyList<JiraIssueType>> GetIssueTypesAsync(JiraTarget target, string projectKey, CancellationToken ct);

    /// <summary><c>POST /rest/api/3/issue</c>.</summary>
    Task<JiraCreatedIssue> CreateIssueAsync(JiraTarget target, JsonObject fields, JsonArray? properties, CancellationToken ct);

    /// <summary><c>POST /rest/api/3/search/jql</c>, looking for the dedupe marker (F-05).</summary>
    Task<JiraCreatedIssue?> FindByDedupeAsync(JiraTarget target, string projectKey, string marker, CancellationToken ct);

    /// <summary><c>POST /rest/api/3/issue/{key}/comment</c>.</summary>
    Task<JiraCreatedComment> AddCommentAsync(JiraTarget target, string issueKey, JsonObject adfBody, string? dedupeMarker, CancellationToken ct);

    /// <summary><c>GET /rest/api/3/issue/{key}?fields=status</c>.</summary>
    Task<string> GetIssueStatusAsync(JiraTarget target, string issueKey, CancellationToken ct);

    /// <summary><c>GET /rest/api/3/issue/{key}/transitions</c>.</summary>
    Task<IReadOnlyList<JiraTransition>> GetTransitionsAsync(JiraTarget target, string issueKey, CancellationToken ct);

    /// <summary><c>POST /rest/api/3/issue/{key}/transitions</c>.</summary>
    Task DoTransitionAsync(JiraTarget target, string issueKey, string transitionId, JsonObject? commentAdf, string? resolutionName, CancellationToken ct);

    /// <summary><c>GET /rest/api/3/issue/{key}/comment?orderBy=-created&amp;maxResults=50&amp;expand=properties</c> (F-05, read-first).</summary>
    Task<JiraCreatedComment?> FindCommentByDedupeAsync(JiraTarget target, string issueKey, string dedupeMarker, CancellationToken ct);

    /// <summary><c>GET /rest/api/3/mypermissions?permissions=…</c>, used for the Validate warning only.</summary>
    Task<IReadOnlyList<string>> GetHeldPermissionsAsync(JiraTarget target, IReadOnlyList<string> keys, CancellationToken ct);
}
