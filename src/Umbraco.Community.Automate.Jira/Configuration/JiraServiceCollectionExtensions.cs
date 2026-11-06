using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Umbraco.Community.Automate.Jira.Adf;
using Umbraco.Community.Automate.Jira.Client;
using Umbraco.Community.Automate.Jira.Credentials;

namespace Umbraco.Community.Automate.Jira.Configuration;

/// <summary>
/// Service registrations for the Jira add-on that need no <c>IUmbracoBuilder</c>, so tests can compose the
/// real services without a site.
/// </summary>
public static class JiraServiceCollectionExtensions
{
    private const string ConfigurationSection = "Umbraco:Automate:Jira";

    /// <summary>
    /// Registers the package's services. The authenticator and notification handlers are added by
    /// <see cref="JiraComposer"/>, because they need the Umbraco builder.
    /// </summary>
    public static IServiceCollection AddJiraCore(this IServiceCollection services, IConfiguration configuration)
    {
        // Singleton is safe (pitfall P2): the store holds only singletons and no state of its own.
        services.AddSingleton<JiraCredentialStore>();
        services.AddSingleton<JiraCredentialSourceCheck>();

        // JiraOptions has no Credentials member, so binding the whole Jira section cannot pull a credential in.
        services.Configure<JiraOptions>(configuration.GetSection(ConfigurationSection));
        services.TryAddSingleton(TimeProvider.System);

        // A named client, not a shared default, so the timeout and handlers are ours (design 7.2).
        services.AddHttpClient(JiraClient.HttpClientName)
            // The framework's logging handler can log headers at Trace, and the Authorization header is a credential.
            .RemoveAllLoggers()
            // A redirect would re-send the Authorization header to whatever host Jira's reply names (F-12).
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
            .ConfigureHttpClient((sp, client) =>
                client.Timeout = TimeSpan.FromSeconds(sp.GetRequiredService<IOptions<JiraOptions>>().Value.TimeoutSeconds));

        // The converters are stateless, so singletons are safe (pitfall P2).
        services.AddSingleton<PlainTextAdfConverter>();
        services.AddSingleton<MarkdownAdfConverter>();
        services.AddSingleton<AdfJsonPassthrough>();
        services.AddSingleton<IAdfConverterFactory, AdfConverterFactory>();

        // Scoped, so nothing static or singleton ever captures it (pitfall P2).
        services.AddScoped<IJiraClient, JiraClient>();
        services.AddMemoryCache();
        services.AddScoped<IIssueTypeResolver, IssueTypeResolver>();
        return services;
    }
}
