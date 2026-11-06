using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Umbraco.Automate.Core.Configuration;
using Umbraco.Community.Automate.Jira.Configuration;
using Umbraco.Community.Automate.Jira.Credentials;

namespace Umbraco.Community.Automate.Jira.Tests;

public class JiraServiceCollectionExtensionsTests
{
    // Later tasks (7, 7a, 18) compose the real services through AddJiraCore, so the store must resolve from it
    // with only the singletons Umbraco and Automate already register.
    [Fact]
    public void AddJiraCore_RegistersCredentialStoreAsSingleton_ReadingConfiguredValues()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Umbraco:Automate:Jira:Credentials:agency:ApiToken"] = "VALUE" })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOptions<AutomateOptions>();

        services.AddJiraCore(configuration);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var first = provider.GetRequiredService<JiraCredentialStore>();
        Assert.Same(first, provider.GetRequiredService<JiraCredentialStore>());
        Assert.True(first.TryGet("agency", JiraCredentialPart.ApiToken, out var value, out _));
        Assert.Equal("VALUE", value);
    }
}
