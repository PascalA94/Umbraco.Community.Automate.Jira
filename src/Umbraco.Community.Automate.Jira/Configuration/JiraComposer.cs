using Umbraco.Automate.Extensions;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Community.Automate.Jira.Credentials;
using Umbraco.Community.Automate.Jira.Webhooks;

namespace Umbraco.Community.Automate.Jira.Configuration;

/// <summary>
/// Registers the Jira add-on with Umbraco and Automate.
/// </summary>
public sealed class JiraComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddJiraCore(builder.Config);
        builder.AddNotificationHandler<UmbracoApplicationStartedNotification, JiraCredentialStoreStartupCheck>();

        // Authenticators are not attribute-discovered, unlike actions and connection types.
        builder.AutomateWebhookAuthenticators().Add<JiraSignatureWebhookAuthenticator>();
    }
}
