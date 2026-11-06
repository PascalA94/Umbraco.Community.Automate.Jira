using Microsoft.Extensions.DependencyInjection;
using Umbraco.Automate.Startup.Configuration;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Community.Automate.Jira.Credentials;

namespace Umbraco.Community.Automate.Jira.Spike;

// Spike-only (Task 0 S7). Never packed.
[ComposeAfter(typeof(UmbracoAutomateComposer))]
public sealed class SpikeComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddSingleton<JiraCredentialStore>();
        builder.AddNotificationHandler<UmbracoApplicationStartedNotification, JiraCredentialStoreStartupCheck>();

        // Automate's HTTP client blocks loopback (SSRF guard), which would stop the request
        // before the echo listener sees it. Reference resolution happens before the
        // transport, so swapping only the primary handler does not change what is resolved.
        builder.Services.AddHttpClient("UmbracoAutomate")
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler());
    }
}
