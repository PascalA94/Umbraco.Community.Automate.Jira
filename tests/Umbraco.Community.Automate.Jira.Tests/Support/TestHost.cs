using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Automate.Core.Configuration;
using Umbraco.Community.Automate.Jira.Client;
using Umbraco.Community.Automate.Jira.Configuration;

namespace Umbraco.Community.Automate.Jira.Tests.Support;

/// <summary>
/// Builds the package's services through the real <c>AddJiraCore</c>, so tests exercise the composition
/// a site gets. Resolve <see cref="IJiraClient"/> and <c>IHttpClientFactory</c> only through here, never by
/// <c>new HttpClient(fake)</c>, or the handler chain under test is not the real one.
/// </summary>
internal static class TestHost
{
    public static IServiceProvider Compose(Action<IConfigurationBuilder>? config = null, HttpMessageHandler? primary = null, Action<IServiceCollection>? services = null)
    {
        var builder = new ConfigurationBuilder();
        config?.Invoke(builder);
        IConfiguration configuration = builder.Build();

        var logs = new CapturingLoggerProvider();
        var collection = new ServiceCollection();
        collection.AddSingleton(configuration);
        collection.AddSingleton(logs);
        collection.AddLogging(l => l.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        // The store's three dependencies: IConfiguration above, plus AutomateOptions as IOptions and IOptionsMonitor.
        collection.AddOptions<AutomateOptions>();

        collection.AddJiraCore(configuration);

        if (primary is not null)
        {
            collection.AddHttpClient(JiraClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => primary);
        }

        services?.Invoke(collection);
        return collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public static CapturingLoggerProvider Logs(this IServiceProvider provider) => provider.GetRequiredService<CapturingLoggerProvider>();

    /// <summary>The client is scoped (pitfall P2), so tests resolve it from a scope that lives as long as the test.</summary>
    public static IJiraClient Client(this IServiceProvider provider) =>
        provider.CreateScope().ServiceProvider.GetRequiredService<IJiraClient>();
}
