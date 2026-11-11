using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Automate.Core.Actions;
using Umbraco.Community.Automate.Jira.Adf;
using Umbraco.Community.Automate.Jira.Client;
using Umbraco.Community.Automate.Jira.Connection;
using Umbraco.Community.Automate.Jira.Credentials;

namespace Umbraco.Community.Automate.Jira.Actions;

/// <summary>
/// Moves a Jira issue through its workflow (design 6.4). It needs no dedupe marker: an issue already in the
/// requested status, or in the target of the matched transition, is a successful no-op, so a retry after a
/// timed-out transition posts nothing. One gap remains: a transition name or ID that Jira no longer offers once
/// the issue has moved cannot be traced, so that retry fails with Validation and says the issue may have moved.
/// </summary>
[Action(
    "community.jira.transitionIssue",
    "Transition Jira issue",
    Description = "Moves a Jira Cloud issue to another status, for example Done. Does nothing if it is already in the status you name.",
    Group = "Jira",
    Icon = "icon-navigation-right",
    ConnectionTypeAlias = "community.jira.cloud")]
public sealed class TransitionIssueAction : ActionBase<TransitionIssueSettings, TransitionIssueOutput>
{
    private const int MaxListed = 20;

    private readonly IServiceScopeFactory _scopes;

    /// <summary>Creates the action. Services are resolved per run from a new scope, as in <see cref="CreateIssueAction"/>.</summary>
    public TransitionIssueAction(ActionInfrastructure infrastructure, IServiceScopeFactory scopes)
        : base(infrastructure) => _scopes = scopes;

    /// <inheritdoc />
    public override async Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        if (context.GetSettings<TransitionIssueSettings>() is not { } settings)
        {
            return ActionFailures.Validation("The step has no settings.");
        }

        if (!JiraIdentifiers.TryIssueKeyOrId(settings.IssueKey, out var issueKey, out var error))
        {
            return ActionFailures.Validation("Issue key: " + error);
        }

        if (string.IsNullOrWhiteSpace(settings.Transition))
        {
            return ActionFailures.Validation("Transition is required.");
        }

        var requested = settings.Transition.Trim();
        var resolution = string.IsNullOrWhiteSpace(settings.ResolutionName) ? null : settings.ResolutionName.Trim();

        using var scope = _scopes.CreateScope();
        var services = scope.ServiceProvider;

        // Always plain text: the comment is usually a bound value, and there is no format switch here (design 6.4).
        AdfResult comment;
        try
        {
            comment = services.GetRequiredService<IAdfConverterFactory>().For(JiraTextFormat.PlainText).Convert(settings.Comment);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return ActionFailures.Validation("Comment: " + ex.Message);
        }

        if (!ActionFailures.TryGetTarget(context, services.GetRequiredService<JiraCredentialStore>(), out var target, out var failure))
        {
            return failure;
        }

        foreach (var warning in comment.Warnings)
        {
            context.LogWarning(warning);
        }

        var client = services.GetRequiredService<IJiraClient>();
        try
        {
            var current = await client.GetIssueStatusAsync(target, issueKey!, cancellationToken);

            // Checked before matching, so a retry after a transition that timed out but went through succeeds.
            if (string.Equals(current.Trim(), requested, StringComparison.OrdinalIgnoreCase))
            {
                context.LogInfo($"{issueKey} is already in status {current}; nothing to do.");
                return Success(new TransitionIssueOutput { FromStatus = current, ToStatus = current, TransitionId = string.Empty });
            }

            var available = await client.GetTransitionsAsync(target, issueKey!, cancellationToken);
            switch (TransitionMatcher.Match(available, requested))
            {
                // A global transition is offered from every status, including its own target, so after a POST that
                // timed out but went through, a retry by ID or name still matches it. Applying it again would repeat
                // the comment, the resolution and Jira's webhooks (M2 security review SEC-02).
                case TransitionMatch.Matched { Transition: var transition }
                    when string.Equals(transition.ToStatusName.Trim(), current.Trim(), StringComparison.OrdinalIgnoreCase):
                    context.LogInfo($"{issueKey} is already in status {current}, the target of {transition.Name}; nothing to do.");
                    return Success(new TransitionIssueOutput { FromStatus = current, ToStatus = current, TransitionId = string.Empty });

                case TransitionMatch.Matched { Transition: var transition }:
                    await client.DoTransitionAsync(target, issueKey!, transition.Id, comment.Document, resolution, cancellationToken);
                    return Success(new TransitionIssueOutput { FromStatus = current, ToStatus = transition.ToStatusName, TransitionId = transition.Id });

                case TransitionMatch.Ambiguous { Candidates: var candidates }:
                    return ActionFailures.Validation(
                        $"\"{Short(requested)}\" matches more than one transition on {issueKey}: {Describe(candidates)}. Use the transition name or ID instead.");

                default:
                    // Without stored state a transition name or ID cannot be traced once the issue has moved, so the
                    // message says so: a retry after a timed-out but successful move lands here (QA review M2-1).
                    var listed = available.Count == 0 ? "No transitions are available." : $"Available: {Describe(available)}.";
                    return ActionFailures.Validation(
                        $"\"{Short(requested)}\" is not available on {issueKey} from status {current}. {listed} "
                        + $"If an earlier attempt already ran, the issue may already be in status {current}. "
                        + "Set Transition to the target status name (for example Done) so a retry can tell.");
            }
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

    // Names come from Jira and are already cut by the client; the list is capped so a huge workflow cannot bloat the error.
    private static string Describe(IReadOnlyList<JiraTransition> transitions)
    {
        var listed = string.Join(", ", transitions.Take(MaxListed).Select(t => $"{t.Name} (to {t.ToStatusName}, ID {t.Id})"));
        return transitions.Count > MaxListed ? $"{listed} and {transitions.Count - MaxListed} more" : listed;
    }

    // The requested value is a bound input, often from a webhook (untrusted text), so it is echoed cleaned and short.
    private static string Short(string value) => JiraResponseHandler.SafeText(value);
}
