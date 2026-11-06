using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using Umbraco.Automate.Core.Actions;
using Umbraco.Community.Automate.Jira.Actions;
using Umbraco.Community.Automate.Jira.Adf;
using Umbraco.Community.Automate.Jira.Tests.Support;

namespace Umbraco.Community.Automate.Jira.Tests;

public class AddCommentActionTests
{
    private const string NoComments = """{"startAt":0,"maxResults":50,"total":0,"comments":[]}""";
    private const string CreatedJson = """{"id":"10500","self":"https://api.atlassian.com/x","body":{"type":"doc","version":1,"content":[]}}""";

    private static AddCommentSettings Settings(string issueKey = "SBX-1", string body = "Published in Umbraco") => new() { IssueKey = issueKey, Body = body };

    private static AddCommentAction Action(ActionRig rig) => new(rig.Infrastructure, rig.Scopes);

    private static bool IsListing(RecordedRequest r) => r.Method == HttpMethod.Get && r.Uri.AbsolutePath.EndsWith("/comment", StringComparison.Ordinal);

    private static bool IsPost(RecordedRequest r) => r.Method == HttpMethod.Post && r.Uri.AbsolutePath.EndsWith("/comment", StringComparison.Ordinal);

    private static string ExpectedMarker(ActionRig rig, AddCommentSettings settings) =>
        DedupeKey.CommentMarker(DedupeKey.Effective(settings.DedupeKey, rig.LastContext!, [settings.IssueKey, settings.Body]), settings.IssueKey.Trim().ToUpperInvariant(), rig.ConnectionId);

    private static string ListingWith(string id, string marker) =>
        $$$"""{"comments":[{"id":"9","properties":[]},{"id":"{{{id}}}","properties":[{"key":"community.jira.dedupe","value":{"key":"{{{marker}}}"}}]}]}""";

    [Fact]
    public void Action_HasPermanentAliasAndConnection()
    {
        var attribute = typeof(AddCommentAction).GetCustomAttribute<ActionAttribute>()!;

        Assert.Equal("community.jira.addComment", attribute.Alias);
        Assert.Equal("community.jira.cloud", attribute.ConnectionTypeAlias);
    }

    [Fact]
    public void BodyFormat_DoesNotSupportBindings()
    {
        var field = typeof(AddCommentSettings).GetProperty(nameof(AddCommentSettings.BodyFormat))!.GetCustomAttribute<Umbraco.Automate.Core.Settings.FieldAttribute>()!;

        Assert.False(field.SupportsBindings);
        Assert.Equal(JiraTextFormat.PlainText, new AddCommentSettings().BodyFormat);
    }

    [Fact]
    public async Task Adds_GoldenRequest()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, NoComments).EnqueueJson(HttpStatusCode.Created, CreatedJson);
        var settings = Settings(body: "Line one\nLine two");

        var result = await rig.RunAsync(Action(rig), settings);

        Assert.Equal(ActionResultStatus.Success, result.Status);
        var listing = rig.Fake.Requests[0];
        Assert.True(IsListing(listing));
        Assert.Equal("/ex/jira/" + ActionRig.CloudId + "/rest/api/3/issue/SBX-1/comment", listing.Uri.AbsolutePath);
        Assert.Equal("?orderBy=-created&maxResults=50&expand=properties", listing.Uri.Query);

        var expected = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "requests", "add-comment.json")))!.AsObject();
        expected["properties"]![0]!["value"]!["key"] = ExpectedMarker(rig, settings);
        var posted = JsonNode.Parse(rig.Fake.Requests.Single(IsPost).Body!)!;
        Assert.True(JsonNode.DeepEquals(expected, posted), posted.ToJsonString());

        var output = Assert.IsType<AddCommentOutput>(result.OutputData);
        Assert.Equal("10500", output.CommentId);
        Assert.Equal("https://acme.atlassian.net/browse/SBX-1?focusedCommentId=10500", output.CommentUrl);
        Assert.False(output.WasExisting);
    }

    [Fact]
    public async Task Body_Markdown_IsConverted()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, NoComments).EnqueueJson(HttpStatusCode.Created, CreatedJson);
        var settings = Settings(body: "**bold**");
        settings.BodyFormat = JiraTextFormat.Markdown;

        await rig.RunAsync(Action(rig), settings);

        var posted = JsonNode.Parse(rig.Fake.Requests.Single(IsPost).Body!)!;
        Assert.Contains("\"strong\"", posted["body"]!.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_404_SaysNotFoundOrNoPermission()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.NotFound, """{"errorMessages":["Issue does not exist or you do not have permission to see it."]}""");

        var result = await rig.RunAsync(Action(rig), Settings("SBX-9"));

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        Assert.Contains("Issue SBX-9 not found or no permission", result.Exception!.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(rig.Fake.Requests, IsPost);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t ")]
    public async Task EmptyBodyAfterTrim_FailsBeforeHttp(string body)
    {
        var rig = new ActionRig();

        var result = await rig.RunAsync(Action(rig), Settings(body: body));

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        Assert.Empty(rig.Fake.Requests);
    }

    [Fact]
    public async Task IssueKey_Whitespace_IsTrimmed()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, NoComments).EnqueueJson(HttpStatusCode.Created, CreatedJson);

        var result = await rig.RunAsync(Action(rig), Settings(" SBX-1\n"));

        Assert.Equal(ActionResultStatus.Success, result.Status);
        Assert.All(rig.Fake.Requests, r => Assert.EndsWith("/rest/api/3/issue/SBX-1/comment", r.Uri.AbsolutePath, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task IssueKey_Blank_FailsBeforeHttp(string key)
    {
        var rig = new ActionRig();

        var result = await rig.RunAsync(Action(rig), Settings(key));

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        Assert.Empty(rig.Fake.Requests);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("SUP-1/..")]
    [InlineData("SUP-1?x=1")]
    public async Task IssueKey_DotSegments_FailBeforeHttp(string key)
    {
        var rig = new ActionRig();

        var result = await rig.RunAsync(Action(rig), Settings(key));

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        Assert.Empty(rig.Fake.Requests);
    }

    [Fact]
    public async Task AddComment_TimeoutAfterSend_IsNotBlindlyReposted()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, NoComments).EnqueueException(new TaskCanceledException("timeout"));

        var first = await rig.RunAsync(Action(rig), Settings());
        Assert.Equal(StepRunErrorCategory.ServiceUnavailable, first.ErrorCategory);

        // Automate retries the same step of the same run; Jira had saved the first comment.
        var marker = ExpectedMarker(rig, Settings());
        rig.Enqueue(HttpStatusCode.OK, ListingWith("10500", marker));
        var second = await rig.RunAsync(Action(rig), Settings());

        Assert.Equal(ActionResultStatus.Success, second.Status);
        var output = Assert.IsType<AddCommentOutput>(second.OutputData);
        Assert.True(output.WasExisting);
        Assert.Equal("10500", output.CommentId);
        Assert.Single(rig.Fake.Requests, IsPost);
    }

    [Fact]
    public async Task Dedupe_OtherMarker_IsNotAMatch()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, ListingWith("10500", "0123456789abcdef")).EnqueueJson(HttpStatusCode.Created, CreatedJson);

        var result = await rig.RunAsync(Action(rig), Settings());

        Assert.False(Assert.IsType<AddCommentOutput>(result.OutputData).WasExisting);
        Assert.Single(rig.Fake.Requests, IsPost);
    }

    [Fact]
    public async Task Dedupe_UserKey_SameKeyAcrossRuns_FindsExisting()
    {
        var rig = new ActionRig();
        var settings = Settings();
        settings.DedupeKey = "  entry-42 ";
        var marker = DedupeKey.CommentMarker("entry-42", "SBX-1", rig.ConnectionId);
        rig.Enqueue(HttpStatusCode.OK, ListingWith("10777", marker));
        rig.RunId = Guid.NewGuid();

        var result = await rig.RunAsync(Action(rig), settings);

        Assert.True(Assert.IsType<AddCommentOutput>(result.OutputData).WasExisting);
        Assert.DoesNotContain(rig.Fake.Requests, IsPost);
    }

    [Fact]
    public async Task Dedupe_MarkerCopiedFromOtherIssue_IsNotAMatch()
    {
        // M2-SEC-01: a user key that is the same on every issue must not let a marker seen on SBX-2 suppress SBX-1's comment.
        var rig = new ActionRig();
        var settings = Settings();
        settings.DedupeKey = "approval-notice";
        rig.Enqueue(HttpStatusCode.OK, ListingWith("900", DedupeKey.CommentMarker("approval-notice", "SBX-2", rig.ConnectionId)))
            .EnqueueJson(HttpStatusCode.Created, CreatedJson);

        var result = await rig.RunAsync(Action(rig), settings);

        Assert.False(Assert.IsType<AddCommentOutput>(result.OutputData).WasExisting);
        Assert.Single(rig.Fake.Requests, IsPost);
    }

    [Fact]
    public async Task Dedupe_MarkerCopiedOntoNewerComment_ReturnsOldest()
    {
        // M2-SEC-01: a copied marker can only be on a newer comment, so the oldest match is the one this step wrote.
        var rig = new ActionRig();
        var settings = Settings();
        settings.DedupeKey = "approval-notice";
        var marker = DedupeKey.CommentMarker("approval-notice", "SBX-1", rig.ConnectionId);
        rig.Enqueue(
            HttpStatusCode.OK,
            $$$"""{"comments":[{"id":"900","properties":[{"key":"community.jira.dedupe","value":{"key":"{{{marker}}}"}}]},{"id":"500","properties":[{"key":"community.jira.dedupe","value":{"key":"{{{marker}}}"}}]}]}""");

        var result = await rig.RunAsync(Action(rig), settings);

        Assert.Equal("500", Assert.IsType<AddCommentOutput>(result.OutputData).CommentId);
        Assert.DoesNotContain(rig.Fake.Requests, IsPost);
    }

    [Fact]
    public async Task AddComment_ConnectFailure_StaysTransient()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, NoComments).EnqueueException(new HttpRequestException(HttpRequestError.ConnectionError, "boom"));

        var result = await rig.RunAsync(Action(rig), Settings());

        Assert.Equal(StepRunErrorCategory.ServiceUnavailable, result.ErrorCategory);
    }

    public static TheoryData<int, StepRunErrorCategory> ListingFailures() => new()
    {
        { 401, StepRunErrorCategory.Authentication },
        { 403, StepRunErrorCategory.Authentication },
        { 500, StepRunErrorCategory.ServiceUnavailable },
        { 429, StepRunErrorCategory.RateLimiting },
    };

    [Theory]
    [MemberData(nameof(ListingFailures))]
    public async Task AddComment_ListingFails_NoPost(int status, StepRunErrorCategory category)
    {
        var rig = new ActionRig();
        rig.Enqueue((HttpStatusCode)status, null, ("Retry-After", "60"));

        var result = await rig.RunAsync(Action(rig), Settings());

        Assert.Equal(category, result.ErrorCategory);
        Assert.DoesNotContain(rig.Fake.Requests, IsPost);
    }

    [Fact]
    public async Task AddComment_ListingNetworkError_NoPost()
    {
        var rig = new ActionRig();
        rig.Fake.EnqueueException(new HttpRequestException(HttpRequestError.ConnectionError, "boom"));

        var result = await rig.RunAsync(Action(rig), Settings());

        Assert.Equal(StepRunErrorCategory.ServiceUnavailable, result.ErrorCategory);
        Assert.DoesNotContain(rig.Fake.Requests, IsPost);
    }

    [Theory]
    [InlineData("""{"unexpected":true}""")]
    [InlineData("""{"comments":[{"id":"not-a-number","properties":[{"key":"community.jira.dedupe","value":{"key":"MARKER"}}]}]}""")]
    public async Task AddComment_ListingUnreadable_NoPost(string listing)
    {
        var rig = new ActionRig();
        var settings = Settings();

        // First run only to learn the marker the action will use.
        rig.Enqueue(HttpStatusCode.OK, NoComments).EnqueueJson(HttpStatusCode.Created, CreatedJson);
        await rig.RunAsync(Action(rig), settings);
        var marker = ExpectedMarker(rig, settings);
        var before = rig.Fake.Requests.Count(IsPost);

        rig.Enqueue(HttpStatusCode.OK, listing.Replace("MARKER", marker, StringComparison.Ordinal));
        var result = await rig.RunAsync(Action(rig), settings);

        Assert.Equal(StepRunErrorCategory.InvalidResponse, result.ErrorCategory);
        Assert.Equal(before, rig.Fake.Requests.Count(IsPost));
    }

    [Fact]
    public async Task Body_Markdown_Over64KB_FailsValidationBeforeHttp()
    {
        var rig = new ActionRig();
        var settings = Settings(body: new string('a', 64 * 1024 + 1));
        settings.BodyFormat = JiraTextFormat.Markdown;

        var result = await rig.RunAsync(Action(rig), settings);

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        Assert.Empty(rig.Fake.Requests);
    }

    [Fact]
    public async Task Body_AdfJsonInvalid_FailsValidationBeforeHttp()
    {
        var rig = new ActionRig();
        var settings = Settings(body: """{"type":"doc","version":1,"content":[],"content":[]}""");
        settings.BodyFormat = JiraTextFormat.AdfJson;

        var result = await rig.RunAsync(Action(rig), settings);

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        Assert.Empty(rig.Fake.Requests);
    }

    [Fact]
    public async Task Dedupe_DistinctPerStepAndBody()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, NoComments).EnqueueJson(HttpStatusCode.Created, CreatedJson);
        await rig.RunAsync(Action(rig), Settings(body: "one"));
        var first = ExpectedMarker(rig, Settings(body: "one"));

        rig.Enqueue(HttpStatusCode.OK, NoComments).EnqueueJson(HttpStatusCode.Created, CreatedJson);
        await rig.RunAsync(Action(rig), Settings(body: "two"));
        var second = ExpectedMarker(rig, Settings(body: "two"));

        Assert.NotEqual(first, second);
        var markers = rig.Fake.Requests.Where(IsPost).Select(r => (string?)JsonNode.Parse(r.Body!)!["properties"]![0]!["value"]!["key"]).ToList();
        Assert.Equal([first, second], markers);
    }

    private static async Task<string> PostedMarker(ActionRig rig, AddCommentSettings settings)
    {
        rig.Enqueue(HttpStatusCode.OK, NoComments).EnqueueJson(HttpStatusCode.Created, CreatedJson);
        await rig.RunAsync(Action(rig), settings);
        return (string)JsonNode.Parse(rig.Fake.Requests.Last(IsPost).Body!)!["properties"]![0]!["value"]!["key"]!;
    }

    private static Dictionary<string, object?> Loop(int index) =>
        new() { ["loop"] = new Dictionary<string, object?> { ["item"] = "same", ["index"] = index } };

    [Fact]
    public async Task Dedupe_StableOnRetry_DistinctPerStepRunAndLoopItem()
    {
        var rig = new ActionRig();
        var baseline = await PostedMarker(rig, Settings());
        Assert.Equal(baseline, await PostedMarker(rig, Settings()));

        rig.StepId = Guid.NewGuid();
        var otherStep = await PostedMarker(rig, Settings());
        rig.RunId = Guid.NewGuid();
        var otherRun = await PostedMarker(rig, Settings());
        rig.AutomationId = Guid.NewGuid();
        var otherAutomation = await PostedMarker(rig, Settings());

        rig.BindingData = Loop(0);
        var item0 = await PostedMarker(rig, Settings());
        rig.BindingData = Loop(1);
        var item1 = await PostedMarker(rig, Settings());

        var all = new[] { baseline, otherStep, otherRun, otherAutomation, item0, item1 };
        Assert.Equal(all.Length, all.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Dedupe_LoopIndexUnreadable_FailsClosed()
    {
        var rig = new ActionRig { BindingData = new Dictionary<string, object?> { ["loop"] = "unexpected shape" } };

        var result = await rig.RunAsync(Action(rig), Settings());

        Assert.Equal(StepRunErrorCategory.ConfigurationError, result.ErrorCategory);
        Assert.Empty(rig.Fake.Requests);
    }

    [Fact]
    public async Task Rotation_NextRunUsesNewToken()
    {
        var rig = new ActionRig("TOKEN-A");
        rig.Enqueue(HttpStatusCode.OK, NoComments).EnqueueJson(HttpStatusCode.Created, CreatedJson);
        await rig.RunAsync(Action(rig), Settings(body: "one"));

        rig.RotateToken("TOKEN-B");
        rig.Enqueue(HttpStatusCode.OK, NoComments).EnqueueJson(HttpStatusCode.Created, CreatedJson);
        await rig.RunAsync(Action(rig), Settings(body: "two"));

        var auths = rig.Fake.Requests.Select(r => r.Authorization!).ToList();
        Assert.Equal(auths[0], auths[1]);
        Assert.NotEqual(auths[1], auths[2]);
        Assert.Equal(auths[2], auths[3]);
    }
}
