using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Community.Automate.Jira.Client;
using Umbraco.Community.Automate.Jira.Configuration;
using Umbraco.Community.Automate.Jira.Tests.Support;

namespace Umbraco.Community.Automate.Jira.Tests;

public class JiraCompositionTests
{
    // Council test 10: a credential must never be bindable into an options object that diagnostics could dump.
    [Fact]
    public void JiraOptions_HasNoCredentialsMember()
    {
        var offenders = typeof(JiraOptions)
            .GetMembers(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(m => m.Name)
            .Where(n => new[] { "Credential", "Token", "Secret" }.Any(w => n.Contains(w, StringComparison.OrdinalIgnoreCase)));

        Assert.Empty(offenders);
    }

    [Fact]
    public void JiraOptions_Defaults()
    {
        var options = new JiraOptions();

        Assert.Equal(30, options.TimeoutSeconds);
        Assert.Equal(10, options.IssueTypeCacheMinutes);
        Assert.Equal(120, options.ReplayWindowMinutes);
    }

    [Theory]
    [InlineData("5", 10)]
    [InlineData("10", 10)]
    [InlineData("600", 600)]
    [InlineData("1440", 1440)]
    [InlineData("99999", 1440)]
    [InlineData("-3", 10)]
    public void ReplayWindowMinutes_IsClampedTo10To1440(string configured, int expected)
    {
        var provider = TestHost.Compose(c => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Umbraco:Automate:Jira:ReplayWindowMinutes"] = configured }));

        Assert.Equal(expected, provider.GetRequiredService<IOptions<JiraOptions>>().Value.ReplayWindowMinutes);
    }

    [Fact]
    public void JiraOptions_BindFromTheJiraSection_AndIgnoreTheCredentialsSection()
    {
        var provider = TestHost.Compose(c => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Umbraco:Automate:Jira:TimeoutSeconds"] = "7",
            ["Umbraco:Automate:Jira:IssueTypeCacheMinutes"] = "3",
            [AcmeCredential.TokenKey] = "TOKEN-SENTINEL-1",
        }));

        var options = provider.GetRequiredService<IOptions<JiraOptions>>().Value;

        Assert.Equal(7, options.TimeoutSeconds);
        Assert.Equal(3, options.IssueTypeCacheMinutes);
    }

    [Fact]
    public void NamedClient_TimeoutComesFromOptions()
    {
        var provider = TestHost.Compose(c => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Umbraco:Automate:Jira:TimeoutSeconds"] = "7" }));

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(JiraClient.HttpClientName);

        Assert.Equal(TimeSpan.FromSeconds(7), client.Timeout);
    }

    [Fact]
    public void NamedClient_NameIsTheDocumentedConstant()
    {
        Assert.Equal("Umbraco.Community.Automate.Jira", JiraClient.HttpClientName);
    }

    // Plan review P4: a default header would freeze the token at client creation and break rotation.
    [Fact]
    public void NamedClient_HasNoDefaultAuthorizationHeader()
    {
        var client = TestHost.Compose().GetRequiredService<IHttpClientFactory>().CreateClient(JiraClient.HttpClientName);

        Assert.Null(client.DefaultRequestHeaders.Authorization);
        Assert.False(client.DefaultRequestHeaders.Contains("Authorization"));
        Assert.Empty(client.DefaultRequestHeaders);
    }

    // Plan review P8: the framework's logging handler can log headers at Trace.
    [Fact]
    public void NamedClient_HandlerChain_HasNoLoggingHandler_AndDisallowsRedirects()
    {
        var factory = TestHost.Compose().GetRequiredService<IHttpMessageHandlerFactory>();

        HttpMessageHandler? handler = factory.CreateHandler(JiraClient.HttpClientName);
        HttpMessageHandler? innermost = null;
        while (handler is not null)
        {
            Assert.DoesNotContain("Logging", handler.GetType().Name);
            innermost = handler;
            handler = (handler as DelegatingHandler)?.InnerHandler;
        }

        var sockets = Assert.IsType<SocketsHttpHandler>(innermost);
        Assert.False(sockets.AllowAutoRedirect);
    }

    [Fact]
    public void CapturingLogger_CapturesTrace()
    {
        var provider = TestHost.Compose();

        provider.GetRequiredService<ILoggerFactory>().CreateLogger("probe").LogTrace("trace-line-{Value}", 42);

        Assert.Contains(provider.Logs().Lines, l => l.Contains("trace-line-42"));
    }

    [Fact]
    public void AddJiraCore_RegistersClientScoped()
    {
        var provider = TestHost.Compose();

        // Scoped (pitfall P2): two scopes must not share an instance, and the root must not hand one out.
        var first = provider.Client();
        var second = provider.Client();

        Assert.NotSame(first, second);
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IJiraClient>());
    }

    [Fact]
    public void IJiraClient_DeclaresTheElevenMethodsOfDesign73_EachEndingInACancellationToken()
    {
        var methods = typeof(IJiraClient).GetMethods();

        Assert.Equal(
            new[]
            {
                "AddCommentAsync", "CreateIssueAsync", "DoTransitionAsync", "FindByDedupeAsync", "FindCommentByDedupeAsync",
                "GetHeldPermissionsAsync", "GetIssueStatusAsync", "GetIssueTypesAsync", "GetMyselfAsync", "GetTenantCloudIdAsync",
                "GetTransitionsAsync",
            },
            methods.Select(m => m.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.All(methods, m => Assert.Equal(typeof(CancellationToken), m.GetParameters()[^1].ParameterType));
    }
}
