using Microsoft.Extensions.DependencyInjection;
using Umbraco.Automate.Core.Actions;
using Umbraco.Community.Automate.Jira.Adf;
using Umbraco.Community.Automate.Jira.Client;
using Umbraco.Community.Automate.Jira.Connection;
using Umbraco.Community.Automate.Jira.Credentials;

namespace Umbraco.Community.Automate.Jira.Actions;

/// <summary>
/// Adds a comment to a Jira issue (design 6.3). Safe to retry: spike S4 probe (c) showed comment properties come
/// back on the comment listing, so every attempt lists the newest comments for its marker before posting (read-first).
/// </summary>
[Action(
    "community.jira.addComment",
    "Add Jira comment",
    Description = "Adds a comment to a Jira Cloud issue. Safe to retry: a repeated step returns the comment it already added.",
    Group = "Jira",
    Icon = "icon-chat",
    ConnectionTypeAlias = "community.jira.cloud")]
public sealed class AddCommentAction : ActionBase<AddCommentSettings, AddCommentOutput>
{
    private readonly IServiceScopeFactory _scopes;

    /// <summary>Creates the action. Services are resolved per run from a new scope, as in <see cref="CreateIssueAction"/>.</summary>
    public AddCommentAction(ActionInfrastructure infrastructure, IServiceScopeFactory scopes)
        : base(infrastructure) => _scopes = scopes;

    /// <inheritdoc />
    public override async Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        if (context.GetSettings<AddCommentSettings>() is not { } settings)
        {
            return ActionFailures.Validation("The step has no settings.");
        }

        if (!JiraIdentifiers.TryIssueKeyOrId(settings.IssueKey, out var issueKey, out var error))
        {
            return ActionFailures.Validation("Issue key: " + error);
        }

        using var scope = _scopes.CreateScope();
        var services = scope.ServiceProvider;

        AdfResult body;
        try
        {
            body = services.GetRequiredService<IAdfConverterFactory>().For(settings.BodyFormat).Convert(settings.Body);
        }
        catch (Exception ex) when (ex is AdfInputTooLargeException or AdfFormatException or ArgumentOutOfRangeException)
        {
            return ActionFailures.Validation("Comment: " + ex.Message);
        }

        if (body.Document is null)
        {
            return ActionFailures.Validation("Comment is empty.");
        }

        string effectiveKey;
        try
        {
            effectiveKey = DedupeKey.Effective(settings.DedupeKey, context, [settings.IssueKey, settings.Body]);
        }
        catch (DedupeKeyUnavailableException ex)
        {
            return ActionResult.Failed(ex, StepRunErrorCategory.ConfigurationError);
        }

        if (!ActionFailures.TryGetTarget(context, services.GetRequiredService<JiraCredentialStore>(), out var target, out var failure))
        {
            return failure;
        }

        foreach (var warning in body.Warnings)
        {
            context.LogWarning(warning);
        }

        var client = services.GetRequiredService<IJiraClient>();
        var marker = DedupeKey.CommentMarker(effectiveKey, issueKey!, target.ConnectionId);
        try
        {
            // Listed first on every attempt: an earlier attempt may have saved the comment and then timed out.
            // A failed listing throws, so nothing is posted (F-05).
            if (await client.FindCommentByDedupeAsync(target, issueKey!, marker, cancellationToken) is { } existing)
            {
                context.LogInfo($"Found comment {existing.Id} on {issueKey}, added by an earlier attempt; not adding another.");
                return Success(Output(target, issueKey!, existing, wasExisting: true));
            }

            var created = await client.AddCommentAsync(target, issueKey!, body.Document, marker, cancellationToken);
            return Success(Output(target, issueKey!, created, wasExisting: false));
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

    private static AddCommentOutput Output(JiraTarget target, string issueKey, JiraCreatedComment comment, bool wasExisting) => new()
    {
        CommentId = comment.Id,
        CommentUrl = new Uri(target.SiteBase, "browse/" + Uri.EscapeDataString(issueKey) + "?focusedCommentId=" + Uri.EscapeDataString(comment.Id)).ToString(),
        WasExisting = wasExisting,
    };
}
