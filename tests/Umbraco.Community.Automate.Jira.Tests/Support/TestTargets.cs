using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using Umbraco.Automate.Core.Configuration;
using Umbraco.Community.Automate.Jira.Connection;
using Umbraco.Community.Automate.Jira.Credentials;

namespace Umbraco.Community.Automate.Jira.Tests.Support;

/// <summary>An in-memory credential called <c>acme</c> and a real store over it.</summary>
internal sealed class AcmeCredential
{
    public const string Email = "svc@x.com";
    public const string TokenKey = "Umbraco:Automate:Jira:Credentials:acme:ApiToken";
    public const string EmailKey = "Umbraco:Automate:Jira:Credentials:acme:Email";

    public AcmeCredential(string token)
    {
        Config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [EmailKey] = Email, [TokenKey] = token })
            .Build();

        var options = new AutomateOptions();
        var monitor = new Mock<IOptionsMonitor<AutomateOptions>>();
        monitor.SetupGet(m => m.CurrentValue).Returns(options);
        Store = new JiraCredentialStore(Config, monitor.Object, Options.Create(options));
    }

    public IConfigurationRoot Config { get; }

    public JiraCredentialStore Store { get; }

    /// <summary>Rotates the token the way a reloading provider would.</summary>
    public void SetToken(string token)
    {
        Config[TokenKey] = token;
        Config.Reload();
    }

    public JiraConnectionSettings Settings(string? cloudId = null, string site = "https://acme.atlassian.net", string credentialName = "acme") =>
        new() { SiteUrl = site, CredentialName = credentialName, CloudId = cloudId };

    public JiraTarget Target(string? cloudId = null)
    {
        Assert.True(JiraTarget.TryCreate(Settings(cloudId), Store, Guid.NewGuid(), out var target, out var error), error);
        return target!;
    }
}

internal static class TestTargets
{
    public static AcmeCredential Acme(string token = "TOKEN-SENTINEL-1") => new(token);
}
