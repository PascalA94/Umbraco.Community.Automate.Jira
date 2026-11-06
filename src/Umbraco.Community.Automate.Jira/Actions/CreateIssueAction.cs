using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Automate.Core.Actions;
using Umbraco.Community.Automate.Jira.Adf;
using Umbraco.Community.Automate.Jira.Client;
using Umbraco.Community.Automate.Jira.Connection;
using Umbraco.Community.Automate.Jira.Credentials;

namespace Umbraco.Community.Automate.Jira.Actions;

/// <summary>
/// Creates a Jira issue (design 6.2). Every input is checked before any request, the create is protected by a
/// dedupe search, and nothing in <c>AdditionalFieldsJson</c> can override a typed field.
/// </summary>
[Action(
    "community.jira.createIssue",
    "Create Jira issue",
    Description = "Creates an issue in a Jira Cloud project. Safe to retry: a repeated step returns the issue it already created.",
    Group = "Jira",
    Icon = "icon-add",
    ConnectionTypeAlias = "community.jira.cloud")]
public sealed class CreateIssueAction : ActionBase<CreateIssueSettings, CreateIssueOutput>
{
    private const int MaxSummaryLength = 255;

    private const string MarkerLabelPrefix = "cj-dedupe-";

    private readonly IServiceScopeFactory _scopes;

    /// <summary>
    /// Creates the action. Services are resolved per run from a new scope: the client is scoped (pitfall P2) and the
    /// credential store is internal, so neither can be a constructor parameter of a public type.
    /// </summary>
    public CreateIssueAction(ActionInfrastructure infrastructure, IServiceScopeFactory scopes)
        : base(infrastructure) => _scopes = scopes;

    /// <inheritdoc />
    public override async Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        if (context.GetSettings<CreateIssueSettings>() is not { } settings)
        {
            return ActionFailures.Validation("The step has no settings.");
        }

        using var scope = _scopes.CreateScope();
        var services = scope.ServiceProvider;

        // Everything that can be checked without Jira is checked first, so a bad input never costs a request.
        if (!TryBuildFields(settings, services.GetRequiredService<IAdfConverterFactory>(), context, out var fields, out var projectKey, out var failure))
        {
            return failure;
        }

        string effectiveKey;
        try
        {
            effectiveKey = DedupeKey.Effective(settings.DedupeKey, context, [settings.ProjectKey, settings.IssueType, settings.Summary, settings.Description]);
        }
        catch (DedupeKeyUnavailableException ex)
        {
            return ActionResult.Failed(ex, StepRunErrorCategory.ConfigurationError);
        }

        if (!ActionFailures.TryGetTarget(context, services.GetRequiredService<JiraCredentialStore>(), out var target, out failure))
        {
            return failure;
        }

        var client = services.GetRequiredService<IJiraClient>();
        try
        {
            var issueTypeId = await services.GetRequiredService<IIssueTypeResolver>().ResolveAsync(target, projectKey, settings.IssueType, cancellationToken);
            fields["issuetype"] = new JsonObject { ["id"] = issueTypeId };

            var marker = DedupeKey.Marker(effectiveKey, target.ConnectionId);

            // Search first on every attempt: the previous attempt may have created the issue and then timed out.
            if (await client.FindByDedupeAsync(target, projectKey, marker, cancellationToken) is { } existing)
            {
                context.LogInfo($"Found {existing.Key}, created by an earlier attempt; not creating another.");
                return Success(Output(target, existing, wasExisting: true));
            }

            // The marker goes in the same request, so there is no window between creating and tagging.
            var labels = fields["labels"] as JsonArray ?? [];
            labels.Add(MarkerLabelPrefix + marker);
            fields["labels"] = labels;

            var created = await client.CreateIssueAsync(target, fields, properties: null, cancellationToken);
            return Success(Output(target, created, wasExisting: false));
        }
        catch (JiraRequestException ex)
        {
            if (ex.RetryAfterSeconds is { } seconds)
            {
                context.LogWarning($"Jira rate limit hit; it asked to retry after {seconds} seconds.");
            }

            return ActionFailures.FromJira(ex);
        }
    }

    private static CreateIssueOutput Output(JiraTarget target, JiraCreatedIssue issue, bool wasExisting) => new()
    {
        IssueId = issue.Id,
        IssueKey = issue.Key,
        IssueUrl = new Uri(target.SiteBase, "browse/" + Uri.EscapeDataString(issue.Key)).ToString(),
        WasExisting = wasExisting,
    };

    private static bool TryBuildFields(
        CreateIssueSettings settings,
        IAdfConverterFactory converters,
        ActionContext context,
        out JsonObject fields,
        out string projectKey,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out ActionResult? failure)
    {
        fields = [];
        projectKey = string.Empty;
        failure = null;

        if (string.IsNullOrWhiteSpace(settings.ProjectKey) || string.IsNullOrWhiteSpace(settings.IssueType) || string.IsNullOrWhiteSpace(settings.Summary))
        {
            failure = ActionFailures.Validation("Project key, issue type and summary are required.");
            return false;
        }

        if (!JiraIdentifiers.TryProjectKeyOrId(settings.ProjectKey, out var project, out var error))
        {
            failure = ActionFailures.Validation(error!);
            return false;
        }

        projectKey = project!;
        string? parentKey = null;
        if (!string.IsNullOrWhiteSpace(settings.ParentKey) && !JiraIdentifiers.TryIssueKey(settings.ParentKey, out parentKey, out error))
        {
            failure = ActionFailures.Validation("Parent key: " + error);
            return false;
        }

        AdfResult description;
        try
        {
            description = converters.For(settings.DescriptionFormat).Convert(settings.Description);
        }
        catch (Exception ex) when (ex is AdfInputTooLargeException or AdfFormatException or ArgumentOutOfRangeException)
        {
            failure = ActionFailures.Validation("Description: " + ex.Message);
            return false;
        }

        fields["project"] = JiraIdentifiers.IsNumericId(projectKey) ? new JsonObject { ["id"] = projectKey } : new JsonObject { ["key"] = projectKey };

        // A placeholder keeps the field order stable; the ID is filled in once the name has been resolved.
        fields["issuetype"] = null;
        fields["summary"] = Summary(settings.Summary);
        if (description.Document is not null)
        {
            fields["description"] = description.Document;
        }

        if (Labels(settings.Labels) is { Count: > 0 } labels)
        {
            fields["labels"] = new JsonArray([.. labels.Select(l => (JsonNode)l)]);
        }

        if (!string.IsNullOrWhiteSpace(settings.Priority))
        {
            fields["priority"] = new JsonObject { ["name"] = settings.Priority.Trim() };
        }

        if (!string.IsNullOrWhiteSpace(settings.AssigneeAccountId))
        {
            fields["assignee"] = new JsonObject { ["accountId"] = settings.AssigneeAccountId.Trim() };
        }

        if (parentKey is not null)
        {
            fields["parent"] = new JsonObject { ["key"] = parentKey };
        }

        var warnings = new List<string>(description.Warnings);
        if (!AdditionalFields.TryMerge(fields, settings.AdditionalFieldsJson, warnings, out error))
        {
            failure = ActionFailures.Validation("Additional fields: " + error);
            return false;
        }

        // Labels from AdditionalFieldsJson must be a list of strings, or the marker would silently replace them.
        if (fields.TryGetPropertyValue("labels", out var labelsNode)
            && (labelsNode is not JsonArray labelArray || labelArray.Any(l => l is not JsonValue lv || !lv.TryGetValue<string>(out _))))
        {
            failure = ActionFailures.Validation("Additional fields: labels must be a list of strings.");
            return false;
        }

        // The marker label is ours alone: a user-supplied one could point a later dedupe search at another issue.
        if (fields["labels"] is JsonArray all && all.Any(l => l is JsonValue v && v.TryGetValue<string>(out var text) && text.StartsWith(MarkerLabelPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            failure = ActionFailures.Validation($"Labels starting with {MarkerLabelPrefix} are reserved for duplicate protection.");
            return false;
        }

        foreach (var warning in warnings)
        {
            context.LogWarning(warning);
        }

        return true;
    }

    private static string Summary(string summary)
    {
        var oneLine = summary.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return AdfLimits.TruncateOnCodePoint(oneLine, MaxSummaryLength);
    }

    // Jira labels cannot contain spaces and are case-sensitive, so duplicates are removed ordinally.
    private static List<string> Labels(string? labels)
    {
        var result = new List<string>();
        foreach (var part in (labels ?? string.Empty).Split(','))
        {
            var label = part.Trim().Replace(' ', '-');
            if (label.Length > 0 && !result.Contains(label, StringComparer.Ordinal))
            {
                result.Add(label);
            }
        }

        return result;
    }
}
