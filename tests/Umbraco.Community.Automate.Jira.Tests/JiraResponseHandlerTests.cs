using System.Net;
using Umbraco.Automate.Core.Actions;
using Umbraco.Community.Automate.Jira.Client;

namespace Umbraco.Community.Automate.Jira.Tests;

public class JiraResponseHandlerTests
{
    private const string Host = "acme.atlassian.net";

    [Fact]
    public void Truncate_ShortTextIsUnchanged()
    {
        Assert.Equal("short", JiraResponseHandler.Truncate("short", 200));
    }

    [Fact]
    public void Truncate_CutsToTheLimit()
    {
        Assert.Equal(200, JiraResponseHandler.Truncate(new string('x', 1000), 200).Length);
    }

    // Plan Task 9 and 12a name the same hazard: a cut between the halves of a surrogate pair leaves an invalid
    // string that fails later, far from its cause.
    [Fact]
    public void Truncate_DoesNotSplitASurrogatePair()
    {
        var text = new string('a', 199) + "\U0001F600" + "tail";

        var cut = JiraResponseHandler.Truncate(text, 200);

        Assert.Equal(new string('a', 199), cut);
        Assert.False(char.IsHighSurrogate(cut[^1]));
    }

    [Fact]
    public void Truncate_KeepsAPairThatFitsWhole()
    {
        var text = new string('a', 198) + "\U0001F600" + "tail";

        Assert.Equal(new string('a', 198) + "\U0001F600", JiraResponseHandler.Truncate(text, 200));
    }

    [Fact]
    public void Map_400_ListsErrorMessagesThenFieldErrors()
    {
        var ex = JiraResponseHandler.Map(HttpStatusCode.BadRequest, """{"errorMessages":["a"],"errors":{"summary":"b"}}""", "req-1", null, null);

        Assert.Equal(StepRunErrorCategory.Validation, ex.Category);
        Assert.Contains("a", ex.Message);
        Assert.Contains("summary: b", ex.Message);
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("req-1", ex.RequestId);
        Assert.True(ex.RequestSent);
    }

    [Fact]
    public void Map_400_CutsEveryErrorMessageTo200()
    {
        var long1 = new string('m', 1000);
        var long2 = new string('f', 1000);

        var ex = JiraResponseHandler.Map(HttpStatusCode.BadRequest, "{\"errorMessages\":[\"" + long1 + "\"],\"errors\":{\"summary\":\"" + long2 + "\"}}", null, null, null);

        Assert.Contains(new string('m', 200), ex.Message);
        Assert.DoesNotContain(new string('m', 201), ex.Message);
        Assert.Contains("summary: " + new string('f', 200), ex.Message);
        Assert.DoesNotContain(new string('f', 201), ex.Message);
    }

    [Theory]
    [InlineData("<html>secret-ish body</html>")]
    [InlineData("")]
    [InlineData("{\"errorMessages\":\"not an array\"}")]
    public void Map_400_UnusableBody_IsGenericAndNeverEchoesTheBody(string body)
    {
        var ex = JiraResponseHandler.Map(HttpStatusCode.BadRequest, body, null, null, null);

        Assert.Equal(StepRunErrorCategory.Validation, ex.Category);
        Assert.DoesNotContain("secret-ish", ex.Message);
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public void Map_400_BodyNestedBeyondMaxDepth_DoesNotThrow()
    {
        var body = new string('[', 100) + new string(']', 100);

        var ex = JiraResponseHandler.Map(HttpStatusCode.BadRequest, body, null, null, null);

        Assert.Equal(StepRunErrorCategory.Validation, ex.Category);
    }

    [Fact]
    public void Map_403_AppendsErrorMessages()
    {
        var ex = JiraResponseHandler.Map(HttpStatusCode.Forbidden, """{"errorMessages":["No create permission"]}""", null, null, null);

        Assert.Equal(StepRunErrorCategory.Authentication, ex.Category);
        Assert.StartsWith("The Jira account lacks permission for this action (403).", ex.Message);
        Assert.Contains("No create permission", ex.Message);
    }

    [Fact]
    public void Map_404_NamesTheSubject()
    {
        var ex = JiraResponseHandler.Map(HttpStatusCode.NotFound, null, null, null, "Issue SBX-9");

        Assert.Equal(StepRunErrorCategory.Validation, ex.Category);
        Assert.Equal("Issue SBX-9 not found or no permission.", ex.Message);
    }

    [Fact]
    public void Map_429_CarriesRetryAfter()
    {
        var ex = JiraResponseHandler.Map((HttpStatusCode)429, null, null, 7, null);

        Assert.Equal(StepRunErrorCategory.RateLimiting, ex.Category);
        Assert.Equal(7, ex.RetryAfterSeconds);
        Assert.Equal("Jira rate limit hit. Retry after 7 seconds.", ex.Message);
    }

    [Fact]
    public void Map_429_WithoutRetryAfter_HasNoNumberInTheMessage()
    {
        var ex = JiraResponseHandler.Map((HttpStatusCode)429, null, null, null, null);

        Assert.Null(ex.RetryAfterSeconds);
        Assert.Equal("Jira rate limit hit.", ex.Message);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(599)]
    public void Map_5xx_IsServiceUnavailable(int status)
    {
        var ex = JiraResponseHandler.Map((HttpStatusCode)status, "<html>boom</html>", null, null, null);

        Assert.Equal(StepRunErrorCategory.ServiceUnavailable, ex.Category);
        Assert.Equal($"Jira is unavailable ({status}).", ex.Message);
    }

    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(307)]
    [InlineData(308)]
    public void Map_3xx_IsInvalidResponse(int status)
    {
        var ex = JiraResponseHandler.Map((HttpStatusCode)status, null, null, null, null);

        Assert.Equal(StepRunErrorCategory.InvalidResponse, ex.Category);
        Assert.Equal("Unexpected redirect from Jira.", ex.Message);
    }

    [Theory]
    [InlineData(409)]
    [InlineData(410)]
    public void Map_OtherClientErrors_AreInvalidResponse(int status)
    {
        var ex = JiraResponseHandler.Map((HttpStatusCode)status, null, null, null, null);

        Assert.Equal(StepRunErrorCategory.InvalidResponse, ex.Category);
        Assert.Contains(status.ToString(), ex.Message);
    }

    [Fact]
    public void Network_NeverCarriesTheCauseAndNamesTheHostOnly()
    {
        var ex = JiraResponseHandler.Unreachable(Host, requestSent: false);

        Assert.Equal(StepRunErrorCategory.ServiceUnavailable, ex.Category);
        Assert.Equal("Couldn't reach Jira (acme.atlassian.net).", ex.Message);
        Assert.Null(ex.InnerException);
        Assert.False(ex.RequestSent);
        Assert.Null(ex.StatusCode);
    }
}
