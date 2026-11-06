using System.Text;
using Umbraco.Community.Automate.Jira.Connection;
using Umbraco.Community.Automate.Jira.Tests.Support;

namespace Umbraco.Community.Automate.Jira.Tests;

public class JiraTargetTests
{
    private const string CloudId = "6f9619ff-8b86-d011-b42d-00c04fc964ff";

    [Fact]
    public void TryCreate_NoCloudId_UsesSiteBase()
    {
        var acme = TestTargets.Acme();

        Assert.True(JiraTarget.TryCreate(acme.Settings(), acme.Store, Guid.NewGuid(), out var target, out var error));

        Assert.Null(error);
        Assert.False(target!.UsesGateway);
        Assert.Equal("https://acme.atlassian.net/", target.ApiBase.AbsoluteUri);
        Assert.Equal("acme.atlassian.net", target.SiteBase.Host);
        Assert.Equal("acme", target.CredentialName);
    }

    [Fact]
    public void TryCreate_CloudId_UsesGatewayBase()
    {
        var acme = TestTargets.Acme();

        Assert.True(JiraTarget.TryCreate(acme.Settings(CloudId), acme.Store, Guid.NewGuid(), out var target, out _));

        Assert.True(target!.UsesGateway);
        Assert.Equal($"https://api.atlassian.com/ex/jira/{CloudId}/", target.ApiBase.AbsoluteUri);
        // Issue links always use the site, so the site base survives the gateway route.
        Assert.Equal("acme.atlassian.net", target.SiteBase.Host);
    }

    [Fact]
    public void TryCreate_CarriesConnectionIdAndNormalisesCredentialName()
    {
        var acme = TestTargets.Acme();
        var id = Guid.NewGuid();

        Assert.True(JiraTarget.TryCreate(acme.Settings(credentialName: " ACME "), acme.Store, id, out var target, out _));

        Assert.Equal(id, target!.ConnectionId);
        Assert.Equal("acme", target.CredentialName);
    }

    [Fact]
    public void TryCreate_BadSite_ReturnsErrorWithoutToken()
    {
        var acme = TestTargets.Acme();

        Assert.False(JiraTarget.TryCreate(acme.Settings(site: "http://evil.example"), acme.Store, Guid.NewGuid(), out var target, out var error));

        Assert.Null(target);
        Assert.NotNull(error);
        Assert.DoesNotContain("TOKEN-SENTINEL", error);
    }

    [Fact]
    public void TryCreate_BadCloudId_ReturnsError()
    {
        var acme = TestTargets.Acme();

        Assert.False(JiraTarget.TryCreate(acme.Settings("../evil"), acme.Store, Guid.NewGuid(), out var target, out var error));

        Assert.Null(target);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryCreate_MissingCredential_ReturnsKeyNamedError()
    {
        var acme = TestTargets.Acme();

        Assert.False(JiraTarget.TryCreate(acme.Settings(credentialName: "nope"), acme.Store, Guid.NewGuid(), out var target, out var error));

        Assert.Null(target);
        Assert.Contains("Umbraco:Automate:Jira:Credentials:nope:Email", error);
    }

    [Fact]
    public void TryCreate_MissingApiToken_NamesTheTokenKey()
    {
        var acme = TestTargets.Acme();
        acme.Config[AcmeCredential.TokenKey] = null;

        Assert.False(JiraTarget.TryCreate(acme.Settings(), acme.Store, Guid.NewGuid(), out _, out var error));

        Assert.Contains(AcmeCredential.TokenKey, error);
    }

    [Fact]
    public void TryCreate_BadCredentialName_DoesNotEchoTheName()
    {
        var acme = TestTargets.Acme();

        Assert.False(JiraTarget.TryCreate(acme.Settings(credentialName: "pasted:TOKEN-SENTINEL-9"), acme.Store, Guid.NewGuid(), out _, out var error));

        Assert.DoesNotContain("TOKEN-SENTINEL-9", error);
    }

    [Fact]
    public void TryCreate_ReadsCredentialEachCall()
    {
        var acme = TestTargets.Acme("TOKEN-SENTINEL-1");
        Assert.True(JiraTarget.TryCreate(acme.Settings(), acme.Store, Guid.NewGuid(), out var first, out _));

        acme.SetToken("TOKEN-SENTINEL-2");
        Assert.True(JiraTarget.TryCreate(acme.Settings(), acme.Store, Guid.NewGuid(), out var second, out _));

        Assert.Equal($"{AcmeCredential.Email}:TOKEN-SENTINEL-1", Decode(first!.AuthorizationParameter));
        Assert.Equal($"{AcmeCredential.Email}:TOKEN-SENTINEL-2", Decode(second!.AuthorizationParameter));
    }

    [Fact]
    public void JiraTarget_ToString_ExcludesCredentials()
    {
        var target = TestTargets.Acme().Target();

        var text = target.ToString();

        Assert.Equal("Jira acme.atlassian.net", text);
        Assert.DoesNotContain("TOKEN-SENTINEL", text);
        Assert.DoesNotContain(AcmeCredential.Email, text);
    }

    [Fact]
    public void JiraTarget_IsASealedClass_NotARecord()
    {
        // A record would generate a ToString and Equals over every member, credential included.
        Assert.True(typeof(JiraTarget).IsSealed);
        Assert.Null(typeof(JiraTarget).GetMethod("<Clone>$"));
    }

    private static string Decode(string parameter) => Encoding.UTF8.GetString(Convert.FromBase64String(parameter));
}
