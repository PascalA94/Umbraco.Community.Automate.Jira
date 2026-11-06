using Moq;
using Umbraco.Automate.Core.Actions;
using Umbraco.Community.Automate.Jira.Actions;
using Umbraco.Community.Automate.Jira.Client;
using Umbraco.Community.Automate.Jira.Connection;
using Umbraco.Community.Automate.Jira.Tests.Support;

namespace Umbraco.Community.Automate.Jira.Tests;

public class ActionFailuresTests
{
    private static ActionContext Context(JiraConnectionSettings? settings, Guid? connectionId = null) => new()
    {
        AutomationId = Guid.NewGuid(),
        RunId = Guid.NewGuid(),
        StepId = Guid.NewGuid(),
        ActionAlias = "community.jira.test",
        Settings = null,
        Action = Mock.Of<IAction>(),
        Connection = settings is null ? null : TestConnections.Jira(settings, connectionId),
    };

    [Fact]
    public void ActionFailures_CredentialError_IsConfigurationError()
    {
        var acme = TestTargets.Acme();

        Assert.False(ActionFailures.TryGetTarget(Context(acme.Settings(credentialName: "missing")), acme.Store, out var target, out var failure));

        Assert.Null(target);
        Assert.Equal(ActionResultStatus.Failed, failure!.Status);
        Assert.Equal(StepRunErrorCategory.ConfigurationError, failure.ErrorCategory);
        Assert.Contains("Umbraco:Automate:Jira:Credentials:missing:Email", failure.Exception!.Message);
    }

    [Fact]
    public void BadSite_IsConfigurationError()
    {
        var acme = TestTargets.Acme();

        Assert.False(ActionFailures.TryGetTarget(Context(acme.Settings(site: "https://evil.example")), acme.Store, out _, out var failure));

        Assert.Equal(StepRunErrorCategory.ConfigurationError, failure!.ErrorCategory);
    }

    [Fact]
    public void NoConnection_IsConfigurationError()
    {
        var acme = TestTargets.Acme();

        Assert.False(ActionFailures.TryGetTarget(Context(null), acme.Store, out _, out var failure));

        Assert.Equal(StepRunErrorCategory.ConfigurationError, failure!.ErrorCategory);
    }

    [Fact]
    public void Success_TargetCarriesConnectionId()
    {
        var acme = TestTargets.Acme();
        var id = Guid.NewGuid();

        Assert.True(ActionFailures.TryGetTarget(Context(acme.Settings(), id), acme.Store, out var target, out var failure));

        Assert.Null(failure);
        Assert.Equal(id, target!.ConnectionId);
    }

    [Theory]
    [InlineData(StepRunErrorCategory.Authentication)]
    [InlineData(StepRunErrorCategory.RateLimiting)]
    [InlineData(StepRunErrorCategory.ServiceUnavailable)]
    public void FromJira_KeepsCategoryAndMessage(StepRunErrorCategory category)
    {
        var result = ActionFailures.FromJira(new JiraRequestException("msg", category));

        Assert.Equal(ActionResultStatus.Failed, result.Status);
        Assert.Equal(category, result.ErrorCategory);
        Assert.Equal("msg", result.Exception!.Message);
    }

    [Fact]
    public void Validation_IsValidationFailure()
    {
        var result = ActionFailures.Validation("Project key is required.");

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        Assert.Equal("Project key is required.", result.Exception!.Message);
    }
}
