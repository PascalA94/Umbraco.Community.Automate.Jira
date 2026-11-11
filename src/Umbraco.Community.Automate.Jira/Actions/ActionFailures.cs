using System.Diagnostics.CodeAnalysis;
using Umbraco.Automate.Core.Actions;
using Umbraco.Community.Automate.Jira.Client;
using Umbraco.Community.Automate.Jira.Connection;
using Umbraco.Community.Automate.Jira.Credentials;

namespace Umbraco.Community.Automate.Jira.Actions;

/// <summary>The failure results every Jira action returns, so categories and messages are the same everywhere (design 7.4).</summary>
internal static class ActionFailures
{
    /// <summary>A Jira call failed: keep its category and its already-safe message.</summary>
    public static ActionResult FromJira(JiraRequestException ex) => ActionResult.Failed(ex, ex.Category);

    /// <summary>An input check failed before any request was sent.</summary>
    public static ActionResult Validation(string message) =>
        ActionResult.Failed(new JiraRequestException(message, StepRunErrorCategory.Validation), StepRunErrorCategory.Validation);

    /// <summary>
    /// Builds the request target from the step's connection, reading the credential for this run only. Anything
    /// wrong with the connection or its credential is a <c>ConfigurationError</c>: retrying cannot fix it.
    /// </summary>
    public static bool TryGetTarget(
        ActionContext context,
        JiraCredentialStore credentials,
        [NotNullWhen(true)] out JiraTarget? target,
        [NotNullWhen(false)] out ActionResult? failure)
    {
        target = null;
        failure = null;

        if (context.Connection is not { } connection)
        {
            failure = Configuration("This step needs a Jira Cloud connection.");
            return false;
        }

        var settings = connection.GetSettings<JiraConnectionSettings>();
        if (settings is null)
        {
            failure = Configuration("The Jira connection settings could not be read.");
            return false;
        }

        if (!JiraTarget.TryCreate(settings, credentials, connection.Id, out target, out var error))
        {
            failure = Configuration(error!);
            return false;
        }

        return true;
    }

    public static ActionResult Configuration(string message) =>
        ActionResult.Failed(new JiraRequestException(message, StepRunErrorCategory.ConfigurationError), StepRunErrorCategory.ConfigurationError);
}
