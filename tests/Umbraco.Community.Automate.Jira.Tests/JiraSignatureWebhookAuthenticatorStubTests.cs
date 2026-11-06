using Microsoft.AspNetCore.Http;
using Umbraco.Automate.Core.Triggers.Webhooks;
using Umbraco.Community.Automate.Jira.Webhooks;

namespace Umbraco.Community.Automate.Jira.Tests;

public class JiraSignatureWebhookAuthenticatorStubTests
{
    // Alias is permanent once -beta.1 ships (plan, Global Constraints).
    [Fact]
    public void Alias_IsFrozenValue()
    {
        IWebhookAuthenticator authenticator = new JiraSignatureWebhookAuthenticator();

        Assert.Equal("community.jira.signature", authenticator.Alias);
    }

    // Pitfall S4: until the real signature check lands, nothing may start a run.
    [Fact]
    public void Validate_Stub_RejectsEverything()
    {
        IWebhookAuthenticator authenticator = new JiraSignatureWebhookAuthenticator();
        var context = new WebhookAuthenticationContext { Request = new DefaultHttpContext().Request, Body = "{}" };

        Assert.False(authenticator.Validate(context, null));
    }
}
