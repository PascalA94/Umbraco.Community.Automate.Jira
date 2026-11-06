using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Automate.Core.Actions;
using Umbraco.Community.Automate.Jira.Client;
using Umbraco.Community.Automate.Jira.Connection;
using Umbraco.Community.Automate.Jira.Tests.Support;

namespace Umbraco.Community.Automate.Jira.Tests;

public class JiraClientTests
{
    private const string MyselfJson = """{"accountId":"acc-1","displayName":"Service Account"}""";
    private const string CloudId = "6f9619ff-8b86-d011-b42d-00c04fc964ff";

    private sealed class Rig
    {
        public Rig(string token = "TOKEN-SENTINEL-1")
        {
            Acme = TestTargets.Acme(token);
            Fake = new FakeJiraHandler();
            Provider = TestHost.Compose(primary: Fake);
            Client = Provider.Client();
        }

        public AcmeCredential Acme { get; }

        public FakeJiraHandler Fake { get; }

        public IServiceProvider Provider { get; }

        public IJiraClient Client { get; }

        public JiraClient Internal => (JiraClient)Client;

        public JiraTarget Target(string? cloudId = null) => Acme.Target(cloudId);
    }

    [Fact]
    public async Task GetMyself_ParsesTheUser_AndUsesSiteRoute()
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson(HttpStatusCode.OK, MyselfJson);

        var user = await rig.Client.GetMyselfAsync(rig.Target(), default);

        Assert.Equal(new JiraUser("acc-1", "Service Account"), user);
        var request = Assert.Single(rig.Fake.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://acme.atlassian.net/rest/api/3/myself", request.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task GetMyself_WithCloudId_GoesThroughTheGateway()
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson(HttpStatusCode.OK, MyselfJson);

        await rig.Client.GetMyselfAsync(rig.Target(CloudId), default);

        Assert.Equal($"https://api.atlassian.com/ex/jira/{CloudId}/rest/api/3/myself", rig.Fake.Requests[0].Uri.AbsoluteUri);
    }

    [Fact]
    public async Task GetMyself_MissingAccountId_IsInvalidResponse()
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson(HttpStatusCode.OK, """{"displayName":"x"}""");

        var ex = await Assert.ThrowsAsync<JiraRequestException>(() => rig.Client.GetMyselfAsync(rig.Target(), default));

        Assert.Equal(StepRunErrorCategory.InvalidResponse, ex.Category);
    }

    [Fact]
    public async Task Client_DoesNotFollowRedirects()
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson(HttpStatusCode.Found, null, ("Location", "https://evil.example/"));

        var ex = await Assert.ThrowsAsync<JiraRequestException>(() => rig.Client.GetMyselfAsync(rig.Target(), default));

        Assert.Equal(StepRunErrorCategory.InvalidResponse, ex.Category);
        Assert.Single(rig.Fake.Requests);
    }

    [Fact]
    public async Task TenantInfo_RequestHasNoAuthorizationHeader()
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson(HttpStatusCode.OK, $$"""{"cloudId":"{{CloudId}}"}""");

        var id = await rig.Client.GetTenantCloudIdAsync(new Uri("https://acme.atlassian.net/"), default);

        Assert.Equal(Guid.Parse(CloudId), id);
        var request = Assert.Single(rig.Fake.Requests);
        Assert.Equal("https://acme.atlassian.net/_edge/tenant_info", request.Uri.AbsoluteUri);
        Assert.Null(request.Authorization);
        Assert.False(request.Headers.ContainsKey("Authorization"));
    }

    [Theory]
    [InlineData(200, """{"other":1}""")]
    [InlineData(200, """{"cloudId":"not-a-guid"}""")]
    [InlineData(404, null)]
    public async Task TenantInfo_NoUsableCloudId_ReturnsNull(int status, string? body)
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson((HttpStatusCode)status, body);

        Assert.Null(await rig.Client.GetTenantCloudIdAsync(new Uri("https://acme.atlassian.net/"), default));
    }

    // The unauthenticated route must not become a way to make the server fetch an arbitrary URL.
    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("http://acme.atlassian.net/")]
    [InlineData("https://acme.atlassian.net.evil.example/")]
    [InlineData("https://user@acme.atlassian.net/")]
    public async Task TenantInfo_NonAtlassianSite_IsRejectedWithoutARequest(string site)
    {
        var rig = new Rig();

        var ex = await Assert.ThrowsAsync<JiraRequestException>(() => rig.Client.GetTenantCloudIdAsync(new Uri(site), default));

        Assert.Equal(StepRunErrorCategory.Validation, ex.Category);
        Assert.False(ex.RequestSent);
        Assert.Empty(rig.Fake.Requests);
    }

    [Fact]
    public async Task Requests_CarryBasicAuth_OnTheRequestMessage()
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson(HttpStatusCode.OK, MyselfJson);

        await rig.Client.GetMyselfAsync(rig.Target(), default);

        var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("svc@x.com:TOKEN-SENTINEL-1"));
        Assert.Equal(expected, rig.Fake.Requests[0].Authorization);
    }

    [Fact]
    public async Task Rotation_NextRequestUsesNewToken()
    {
        var rig = new Rig("TOKEN-SENTINEL-1");
        rig.Fake.EnqueueJson(HttpStatusCode.OK, MyselfJson).EnqueueJson(HttpStatusCode.OK, MyselfJson);
        await rig.Client.GetMyselfAsync(rig.Target(), default);

        rig.Acme.SetToken("TOKEN-SENTINEL-2");
        await rig.Client.GetMyselfAsync(rig.Target(), default);

        Assert.Equal(2, rig.Fake.Requests.Count);
        Assert.Equal(Basic("TOKEN-SENTINEL-1"), rig.Fake.Requests[0].Authorization);
        Assert.Equal(Basic("TOKEN-SENTINEL-2"), rig.Fake.Requests[1].Authorization);
        Assert.DoesNotContain(rig.Fake.Requests.Skip(1), r => r.Authorization == Basic("TOKEN-SENTINEL-1"));
    }

    [Fact]
    public void PathSegments_AreEscaped()
    {
        var uri = JiraClient.BuildUri(TestTargets.Acme().Target(), "/rest/api/3/issue/{key}/comment", new Dictionary<string, string> { ["key"] = "A/B?c" }, null);

        Assert.Contains("/rest/api/3/issue/A%2FB%3Fc/comment", uri.AbsoluteUri);
        Assert.Equal("acme.atlassian.net", uri.Host);
        Assert.Equal(string.Empty, uri.Query);
    }

    [Fact]
    public void PathSegments_OnTheGateway_StayUnderTheCloudIdPath()
    {
        var uri = JiraClient.BuildUri(TestTargets.Acme().Target(CloudId), "/rest/api/3/issue/{key}", new Dictionary<string, string> { ["key"] = "SBX-1" }, "fields=status");

        Assert.Equal($"https://api.atlassian.com/ex/jira/{CloudId}/rest/api/3/issue/SBX-1?fields=status", uri.AbsoluteUri);
    }

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("")]
    public void PathSegments_DotAndEmptySegments_AreRefused(string segment)
    {
        var ex = Assert.Throws<JiraRequestException>(
            () => JiraClient.BuildUri(TestTargets.Acme().Target(), "/rest/api/3/issue/{key}", new Dictionary<string, string> { ["key"] = segment }, null));

        Assert.Equal(StepRunErrorCategory.Validation, ex.Category);
        Assert.False(ex.RequestSent);
    }

    [Fact]
    public async Task Jira400_ErrorsTruncatedTo200()
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson(HttpStatusCode.BadRequest, "{\"errorMessages\":[],\"errors\":{\"summary\":\"" + new string('z', 1000) + "\"}}");

        var ex = await Assert.ThrowsAsync<JiraRequestException>(() => rig.Client.GetMyselfAsync(rig.Target(), default));

        Assert.Contains("summary: " + new string('z', 200), ex.Message);
        Assert.DoesNotContain(new string('z', 201), ex.Message);
    }

    [Theory]
    [InlineData(401, null, StepRunErrorCategory.Authentication, "Jira rejected the credentials (401).")]
    [InlineData(403, """{"errorMessages":["nope"]}""", StepRunErrorCategory.Authentication, "The Jira account lacks permission for this action (403).")]
    [InlineData(400, """{"errorMessages":["a"],"errors":{"summary":"b"}}""", StepRunErrorCategory.Validation, "summary: b")]
    [InlineData(404, null, StepRunErrorCategory.Validation, "not found or no permission")]
    [InlineData(503, null, StepRunErrorCategory.ServiceUnavailable, "Jira is unavailable (503).")]
    [InlineData(301, null, StepRunErrorCategory.InvalidResponse, "Unexpected redirect from Jira.")]
    [InlineData(302, null, StepRunErrorCategory.InvalidResponse, "Unexpected redirect from Jira.")]
    [InlineData(307, null, StepRunErrorCategory.InvalidResponse, "Unexpected redirect from Jira.")]
    [InlineData(200, "not json", StepRunErrorCategory.InvalidResponse, "Unexpected response from Jira.")]
    public async Task ErrorMapping_FollowsDesign74(int status, string? body, StepRunErrorCategory category, string message)
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson((HttpStatusCode)status, body);

        var ex = await Assert.ThrowsAsync<JiraRequestException>(() => rig.Client.GetMyselfAsync(rig.Target(), default));

        Assert.Equal(category, ex.Category);
        Assert.Contains(message, ex.Message);
        Assert.True(ex.RequestSent);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task ErrorMapping_429_CarriesRetryAfter()
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson((HttpStatusCode)429, null, ("Retry-After", "7"));

        var ex = await Assert.ThrowsAsync<JiraRequestException>(() => rig.Client.GetMyselfAsync(rig.Target(), default));

        Assert.Equal(StepRunErrorCategory.RateLimiting, ex.Category);
        Assert.Equal(7, ex.RetryAfterSeconds);
        Assert.Contains("Retry after 7 seconds", ex.Message);
        Assert.True(ex.RequestSent);
    }

    [Fact]
    public async Task Response_NestedBeyondMaxDepth_IsInvalidResponse()
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson(HttpStatusCode.OK, new string('[', 40) + new string(']', 40));

        var ex = await Assert.ThrowsAsync<JiraRequestException>(() => rig.Client.GetMyselfAsync(rig.Target(), default));

        Assert.Equal(StepRunErrorCategory.InvalidResponse, ex.Category);
    }

    [Fact]
    public async Task Exception_FromNetworkError_ExcludesInnerMessage()
    {
        var rig = new Rig();
        rig.Fake.EnqueueException(new HttpRequestException("secret-ish TOKEN-SENTINEL-1"));

        var ex = await Assert.ThrowsAsync<JiraRequestException>(() => rig.Client.GetMyselfAsync(rig.Target(), default));

        Assert.Equal(StepRunErrorCategory.ServiceUnavailable, ex.Category);
        Assert.Equal("Couldn't reach Jira (acme.atlassian.net).", ex.Message);
        Assert.Null(ex.InnerException);
        Assert.DoesNotContain("TOKEN-SENTINEL", ex.ToString());
        Assert.True(ex.RequestSent);
    }

    [Fact]
    public async Task Timeout_IsServiceUnavailable_RequestSent()
    {
        var rig = new Rig();
        rig.Fake.EnqueueException(new TaskCanceledException("timed out", new TimeoutException()));

        var ex = await Assert.ThrowsAsync<JiraRequestException>(() => rig.Client.GetMyselfAsync(rig.Target(), default));

        Assert.Equal(StepRunErrorCategory.ServiceUnavailable, ex.Category);
        Assert.Equal("Couldn't reach Jira (acme.atlassian.net).", ex.Message);
        Assert.True(ex.RequestSent);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task CallerCancellation_PropagatesAsOperationCanceled()
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson(HttpStatusCode.OK, MyselfJson);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Client.GetMyselfAsync(rig.Target(), cts.Token));
    }

    [Fact]
    public async Task CallerCancellation_DuringTheCall_IsNotMappedToServiceUnavailable()
    {
        var rig = new Rig();
        using var cts = new CancellationTokenSource();
        rig.Fake.EnqueueException(new TaskCanceledException("caller gave up"));
        await cts.CancelAsync();

        var thrown = await Record.ExceptionAsync(() => rig.Client.GetMyselfAsync(rig.Target(), cts.Token));

        Assert.IsNotType<JiraRequestException>(thrown);
        Assert.IsAssignableFrom<OperationCanceledException>(thrown);
    }

    [Fact]
    public async Task ConnectFailure_RequestNotSent()
    {
        var rig = new Rig();
        rig.Fake.EnqueueException(new HttpRequestException(HttpRequestError.ConnectionError, "connect refused TOKEN-SENTINEL-1"));

        var ex = await Assert.ThrowsAsync<JiraRequestException>(() => rig.Client.GetMyselfAsync(rig.Target(), default));

        Assert.Equal(StepRunErrorCategory.ServiceUnavailable, ex.Category);
        Assert.False(ex.RequestSent);
        Assert.Null(ex.InnerException);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("5")]
    public async Task Get_429_ShortRetryAfter_RetriesOnce(string retryAfter)
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson((HttpStatusCode)429, null, ("Retry-After", retryAfter)).EnqueueJson(HttpStatusCode.OK, MyselfJson);

        var user = await rig.Client.GetMyselfAsync(rig.Target(), default);

        Assert.Equal("acc-1", user.AccountId);
        Assert.Equal(2, rig.Fake.Requests.Count);
        Assert.All(rig.Fake.Requests, r => Assert.Equal(Basic("TOKEN-SENTINEL-1"), r.Authorization));
    }

    [Fact]
    public async Task Get_429_Twice_FailsAfterOneRetry()
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson((HttpStatusCode)429, null, ("Retry-After", "0")).EnqueueJson((HttpStatusCode)429, null, ("Retry-After", "0"));

        var ex = await Assert.ThrowsAsync<JiraRequestException>(() => rig.Client.GetMyselfAsync(rig.Target(), default));

        Assert.Equal(StepRunErrorCategory.RateLimiting, ex.Category);
        Assert.Equal(2, rig.Fake.Requests.Count);
    }

    [Theory]
    [InlineData("6")]
    [InlineData("60")]
    public async Task Get_429_LongRetryAfter_DoesNotRetry(string retryAfter)
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson((HttpStatusCode)429, null, ("Retry-After", retryAfter));

        var ex = await Assert.ThrowsAsync<JiraRequestException>(() => rig.Client.GetMyselfAsync(rig.Target(), default));

        Assert.Equal(StepRunErrorCategory.RateLimiting, ex.Category);
        Assert.Single(rig.Fake.Requests);
    }

    [Fact]
    public async Task Get_429_WithoutRetryAfter_DoesNotRetry()
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson((HttpStatusCode)429);

        await Assert.ThrowsAsync<JiraRequestException>(() => rig.Client.GetMyselfAsync(rig.Target(), default));

        Assert.Single(rig.Fake.Requests);
    }

    [Fact]
    public async Task Post_429_NeverRetries()
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson((HttpStatusCode)429, null, ("Retry-After", "0")).EnqueueJson(HttpStatusCode.OK, "{}");

        var ex = await Assert.ThrowsAsync<JiraRequestException>(
            () => rig.Internal.SendAsync(rig.Target(), HttpMethod.Post, "/rest/api/3/issue", body: new JsonObject { ["a"] = 1 }, ct: default));

        Assert.Equal(StepRunErrorCategory.RateLimiting, ex.Category);
        Assert.Single(rig.Fake.Requests);
    }

    [Fact]
    public async Task Post_SendsTheJsonBody_WithAuthorisationOnTheMessage()
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson(HttpStatusCode.Created, """{"id":"1","key":"SBX-1"}""");

        using var doc = await rig.Internal.SendAsync(rig.Target(), HttpMethod.Post, "/rest/api/3/issue", body: new JsonObject { ["fields"] = new JsonObject { ["summary"] = "Hi" } }, ct: default);

        var request = Assert.Single(rig.Fake.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("""{"fields":{"summary":"Hi"}}""", request.Body);
        Assert.Equal(Basic("TOKEN-SENTINEL-1"), request.Authorization);
        Assert.Equal("SBX-1", doc!.RootElement.GetProperty("key").GetString());
    }

    [Fact]
    public async Task Send_404_UsesTheCallersSubject()
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson(HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<JiraRequestException>(
            () => rig.Internal.SendAsync(rig.Target(), HttpMethod.Get, "/rest/api/3/issue/{key}", new Dictionary<string, string> { ["key"] = "SBX-9" }, notFoundSubject: "Issue SBX-9", ct: default));

        Assert.Equal("Issue SBX-9 not found or no permission.", ex.Message);
    }

    [Fact]
    public async Task Send_EmptySuccessBody_ReturnsNull()
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson(HttpStatusCode.NoContent);

        var doc = await rig.Internal.SendAsync(rig.Target(), HttpMethod.Post, "/rest/api/3/issue/{key}/transitions", new Dictionary<string, string> { ["key"] = "SBX-1" }, body: new JsonObject(), ct: default);

        Assert.Null(doc);
    }

    [Fact]
    public async Task RequestId_FromXARequestId_IsInException()
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson(HttpStatusCode.Unauthorized, null, ("X-ARequestId", "req-abc-123"));

        var ex = await Assert.ThrowsAsync<JiraRequestException>(() => rig.Client.GetMyselfAsync(rig.Target(), default));

        Assert.Equal("req-abc-123", ex.RequestId);
    }

    [Fact]
    public async Task RequestId_Absent_IsNull()
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson(HttpStatusCode.Unauthorized);

        var ex = await Assert.ThrowsAsync<JiraRequestException>(() => rig.Client.GetMyselfAsync(rig.Target(), default));

        Assert.Null(ex.RequestId);
    }

    // A header value is attacker-influenced text that lands in logs and messages: keep it to a safe alphabet.
    [Fact]
    public async Task RequestId_WithUnsafeCharacters_IsDropped()
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson(HttpStatusCode.Unauthorized, null, ("X-ARequestId", "bad id with spaces"));

        var ex = await Assert.ThrowsAsync<JiraRequestException>(() => rig.Client.GetMyselfAsync(rig.Target(), default));

        Assert.Null(ex.RequestId);
    }

    [Fact]
    public async Task Logs_OneLinePerCall_WithMethodHostPathTemplateStatusAndRequestId()
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson(HttpStatusCode.OK, MyselfJson, ("X-ARequestId", "req-9"));

        await rig.Client.GetMyselfAsync(rig.Target(), default);

        var line = Assert.Single(rig.Provider.Logs().Lines, l => l.Contains("/rest/api/3/myself"));
        Assert.Contains("GET", line);
        Assert.Contains("acme.atlassian.net", line);
        Assert.Contains("200", line);
        Assert.Contains("req-9", line);
    }

    [Fact]
    public async Task Logs_UseThePathTemplate_NotTheRawSegment()
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson(HttpStatusCode.NoContent);

        await rig.Internal.SendAsync(rig.Target(), HttpMethod.Post, "/rest/api/3/issue/{key}/transitions", new Dictionary<string, string> { ["key"] = "SBX-77" }, body: new JsonObject(), ct: default);

        var lines = rig.Provider.Logs().Lines;
        Assert.Contains(lines, l => l.Contains("/rest/api/3/issue/{key}/transitions"));
        Assert.DoesNotContain(lines, l => l.Contains("SBX-77"));
    }

    // Plan review P8: nothing the client, the factory or the framework logs may carry the credential.
    [Fact]
    public async Task Logs_ContainNoSentinel_AtTrace()
    {
        var rig = new Rig();
        var target = rig.Target();
        rig.Fake
            .EnqueueJson(HttpStatusCode.OK, MyselfJson, ("X-ARequestId", "req-1"))
            .EnqueueJson(HttpStatusCode.Unauthorized)
            .EnqueueJson(HttpStatusCode.BadRequest, """{"errorMessages":["m"],"errors":{"f":"TOKEN-SENTINEL-1 echoed"}}""")
            .EnqueueJson((HttpStatusCode)429, null, ("Retry-After", "0"))
            .EnqueueJson(HttpStatusCode.OK, MyselfJson)
            .EnqueueException(new HttpRequestException("secret-ish TOKEN-SENTINEL-1"))
            .EnqueueException(new TaskCanceledException("t", new TimeoutException("TOKEN-SENTINEL-1")))
            .EnqueueJson(HttpStatusCode.OK, "not json TOKEN-SENTINEL-1");

        for (var i = 0; i < 7; i++)
        {
            await Record.ExceptionAsync(() => rig.Client.GetMyselfAsync(target, default));
        }

        var lines = rig.Provider.Logs().Lines;
        Assert.NotEmpty(lines);
        Assert.DoesNotContain(lines, l => l.Contains("TOKEN-SENTINEL", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, l => l.Contains("Authorization", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, l => l.Contains("svc@x.com", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, l => l.Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes("svc@x.com:TOKEN-SENTINEL-1"))));
    }

    private static string Basic(string token) => "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"svc@x.com:{token}"));

    [Fact]
    public async Task BodyReadFailure_Get_IsUnreachableRequestSent()
    {
        var rig = new Rig();
        rig.Fake.EnqueueBrokenBody();

        var ex = await Assert.ThrowsAsync<JiraRequestException>(() => rig.Client.GetMyselfAsync(rig.Target(), CancellationToken.None));

        Assert.Equal(StepRunErrorCategory.ServiceUnavailable, ex.Category);
        Assert.True(ex.RequestSent);
        Assert.DoesNotContain("SENTINEL-IO", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(rig.Provider.Logs().Lines, l => l.Contains("SENTINEL-IO", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BodyReadFailure_CreatePost_IsUnreachableRequestSent()
    {
        var rig = new Rig();
        rig.Fake.EnqueueBrokenBody(HttpStatusCode.Created);

        var ex = await Assert.ThrowsAsync<JiraRequestException>(() => rig.Client.CreateIssueAsync(rig.Target(), new JsonObject { ["summary"] = "s" }, null, CancellationToken.None));

        Assert.Equal(StepRunErrorCategory.ServiceUnavailable, ex.Category);
        Assert.True(ex.RequestSent);
    }
}
