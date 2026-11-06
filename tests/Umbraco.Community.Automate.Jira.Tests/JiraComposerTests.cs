using CredentialHelperContract;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Community.Automate.Jira.Configuration;
using Umbraco.Community.Automate.Jira.Credentials;

namespace Umbraco.Community.Automate.Jira.Tests;

public class JiraComposerTests
{
    // Contract section 1.1: the startup check only runs if the composer registers it, and a missing handler
    // fails silently on a site (no log line is exactly what a clean site looks like).
    [Fact]
    public void Compose_RegistersStoreAndStartupCheck()
    {
        var services = new ServiceCollection();
        var builder = AutomateRegistration.CreateBuilder(services, new ConfigurationBuilder().Build());

        new JiraComposer().Compose(builder);

        Assert.Contains(services, d => d.ServiceType == typeof(JiraCredentialStore) && d.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(
            services,
            d => d.ServiceType == typeof(INotificationHandler<UmbracoApplicationStartedNotification>)
                && d.ImplementationType == typeof(JiraCredentialStoreStartupCheck));
    }
}
