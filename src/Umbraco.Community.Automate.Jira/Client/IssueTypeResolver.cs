using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Umbraco.Automate.Core.Actions;
using Umbraco.Community.Automate.Jira.Configuration;
using Umbraco.Community.Automate.Jira.Connection;

namespace Umbraco.Community.Automate.Jira.Client;

/// <summary>Turns an issue type name into its ID for a project (design 6.2).</summary>
public interface IIssueTypeResolver
{
    /// <summary>
    /// Returns the issue type ID. A numeric input is returned as it is, with no request. A name is matched
    /// case-insensitively against the project's types, cached per connection and project. An unknown name throws
    /// <see cref="JiraRequestException"/> with <c>Validation</c>, listing the valid names.
    /// </summary>
    Task<string> ResolveAsync(JiraTarget target, string projectKey, string nameOrId, CancellationToken ct);
}

/// <summary>
/// Scoped because it uses the scoped <see cref="IJiraClient"/> (pitfall P2). The cache is the singleton
/// <see cref="IMemoryCache"/>, so lookups are shared across scopes; there is no static state.
/// </summary>
internal sealed class IssueTypeResolver(IJiraClient client, IMemoryCache cache, IOptions<JiraOptions> options) : IIssueTypeResolver
{
    public async Task<string> ResolveAsync(JiraTarget target, string projectKey, string nameOrId, CancellationToken ct)
    {
        var wanted = nameOrId?.Trim() ?? string.Empty;
        if (wanted.Length == 0)
        {
            throw new JiraRequestException("Issue type is required.", StepRunErrorCategory.Validation);
        }

        if (JiraIdentifiers.IsNumericId(wanted))
        {
            return wanted;
        }

        var types = await GetTypesAsync(target, projectKey, ct);
        var match = types.FirstOrDefault(t => string.Equals(t.Name.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            return match.Id;
        }

        // The requested name is not echoed: it can come from a binding.
        var valid = types.Count == 0 ? "none" : string.Join(", ", types.Select(t => t.Name));
        throw new JiraRequestException($"That issue type is not available in project {projectKey}. Valid types: {valid}.", StepRunErrorCategory.Validation);
    }

    private async Task<IReadOnlyList<JiraIssueType>> GetTypesAsync(JiraTarget target, string projectKey, CancellationToken ct)
    {
        var key = $"jira:types:{target.ConnectionId:D}:{projectKey.ToUpperInvariant()}";
        if (cache.TryGetValue(key, out IReadOnlyList<JiraIssueType>? cached) && cached is not null)
        {
            return cached;
        }

        // A failed lookup throws before anything is cached, so the next run asks again.
        var types = await client.GetIssueTypesAsync(target, projectKey, ct);
        var minutes = options.Value.IssueTypeCacheMinutes;
        if (minutes > 0)
        {
            cache.Set(key, types, TimeSpan.FromMinutes(minutes));
        }

        return types;
    }
}
