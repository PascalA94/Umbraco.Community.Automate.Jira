using System.Reflection;
using Umbraco.Automate.Core.Actions;
using Umbraco.Automate.Core.Execution;
using Umbraco.Automate.Core.Settings;
using Umbraco.Automate.Core.Triggers.Webhooks;

namespace Umbraco.Community.Automate.Jira.Tests.Spikes;

/// <summary>
/// Spike S1 (plan Task 2), kept as regression tests: the Automate surface the package relies on for
/// secret handling must exist on every line and on every later Automate release (the CI "latest" leg).
/// If one fails on a line, that line cannot ship (plan Task 2 step 4).
/// </summary>
public class SecretFeatureSpikeTests
{
    // IsSensitive drives encryption at rest, run-log masking and stripping on export, so the
    // connection's non-secret settings must be able to opt in.
    [Fact]
    public void EditableModelField_HasIsSensitive()
    {
        var property = typeof(EditableModelFieldAttribute).GetProperty(nameof(EditableModelFieldAttribute.IsSensitive));

        Assert.NotNull(property);
        Assert.Equal(typeof(bool), property.PropertyType);
        Assert.True(property.CanRead);
        Assert.True(property.CanWrite);
        Assert.True(property.SetMethod!.IsPublic);
    }

    // Without this the signature header would be stored in the trigger output of every run.
    [Fact]
    public void WebhookAuthenticatorBase_HasCredentialHeaderNames()
    {
        var property = typeof(WebhookAuthenticatorBase<>).GetProperty(
            nameof(IWebhookAuthenticator.CredentialHeaderNames),
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(property);
        Assert.True(property.GetMethod!.IsVirtual);
        Assert.False(property.GetMethod.IsFinal);
    }

    // Actions that need no connection (readWebhookEvent, findContentByKey) leave the alias unset.
    [Fact]
    public void ActionAttribute_ConnectionTypeAlias_IsOptional()
    {
        var attribute = new ActionAttribute("x", "y");

        Assert.Null(attribute.ConnectionTypeAlias);
    }

    // Spike S6 (plan Task 3 step 5): an action cannot opt out of retries, so the package relies on
    // the error category alone to make a failure terminal. If a release adds an opt-out, Task 13's
    // "never retry a POST" handling should use it.
    [Fact]
    public void ActionAttribute_HasNoRetryOptOut()
    {
        var retryMembers = typeof(ActionAttribute).GetProperties()
            .Select(property => property.Name)
            .Where(name => name.Contains("Retry", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(retryMembers);
    }

    // Terminal categories decide which failures are never retried (plan Task 3 step 5, security review F-05).
    [Theory]
    [InlineData(StepRunErrorCategory.Validation, true)]
    [InlineData(StepRunErrorCategory.ConfigurationError, true)]
    [InlineData(StepRunErrorCategory.Authentication, true)]
    [InlineData(StepRunErrorCategory.Cancelled, true)]
    [InlineData(StepRunErrorCategory.Timeout, false)]
    [InlineData(StepRunErrorCategory.ServiceUnavailable, false)]
    [InlineData(StepRunErrorCategory.RateLimiting, false)]
    [InlineData(StepRunErrorCategory.Unknown, false)]
    public void DefaultStepErrorClassifier_TerminalCategories(StepRunErrorCategory category, bool expectedTerminal)
    {
        // The default classifier is internal; it is reached by type name because it is what Automate registers.
        var type = typeof(IStepErrorClassifier).Assembly.GetType("Umbraco.Automate.Core.Execution.DefaultStepErrorClassifier");
        Assert.NotNull(type);
        var classifier = (IStepErrorClassifier)Activator.CreateInstance(type, nonPublic: true)!;

        Assert.Equal(expectedTerminal, classifier.IsTerminal(category));
    }
}
