using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using Umbraco.Automate.Core.Actions;
using Umbraco.Community.Automate.Jira.Actions;
using Umbraco.Community.Automate.Jira.Client;
using Umbraco.Community.Automate.Jira.Tests.Support;

namespace Umbraco.Community.Automate.Jira.Tests;

public class TransitionIssueActionTests
{
    private const string InProgress = """{"id":"10001","key":"SBX-1","fields":{"status":{"name":"In Progress"}}}""";
    private const string AlreadyDone = """{"id":"10001","key":"SBX-1","fields":{"status":{"name":"Done"}}}""";
    private const string Transitions = """
        {"transitions":[
          {"id":"11","name":"Start","to":{"name":"In Progress"}},
          {"id":"31","name":"Close","to":{"name":"Done"}},
          {"id":"41","name":"Reopen","to":{"name":"To Do"}}
        ]}
        """;

    private static TransitionIssueSettings Settings(string issueKey = "SBX-1", string transition = "Done") => new() { IssueKey = issueKey, Transition = transition };

    private static TransitionIssueAction Action(ActionRig rig) => new(rig.Infrastructure, rig.Scopes);

    private static bool IsPost(RecordedRequest r) => r.Method == HttpMethod.Post;

    private static JsonNode PostedBody(ActionRig rig) => JsonNode.Parse(rig.Fake.Requests.Single(IsPost).Body!)!;

    private static void EnqueueHappyPath(ActionRig rig) =>
        rig.Enqueue(HttpStatusCode.OK, InProgress).EnqueueJson(HttpStatusCode.OK, Transitions).EnqueueJson(HttpStatusCode.NoContent);

    // Matcher

    private static readonly IReadOnlyList<JiraTransition> Available =
    [
        new("11", "Start", "In Progress"),
        new("31", "Close", "Done"),
        new("41", "31", "To Do"),
    ];

    [Fact]
    public void Matcher_IdBeatsName()
    {
        var match = Assert.IsType<TransitionMatch.Matched>(TransitionMatcher.Match(Available, "31"));
        Assert.Equal("31", match.Transition.Id);
    }

    [Fact]
    public void Matcher_NameBeatsStatus()
    {
        IReadOnlyList<JiraTransition> available = [new("1", "Done", "Closed"), new("2", "Finish", "Done")];

        var match = Assert.IsType<TransitionMatch.Matched>(TransitionMatcher.Match(available, "done"));
        Assert.Equal("1", match.Transition.Id);
    }

    [Fact]
    public void Matcher_StatusName_ViaDifferentlyNamedTransition()
    {
        var match = Assert.IsType<TransitionMatch.Matched>(TransitionMatcher.Match(Available, "done"));
        Assert.Equal("Close", match.Transition.Name);
    }

    [Fact]
    public void Matcher_TwoAtStatusLevel_IsAmbiguous()
    {
        IReadOnlyList<JiraTransition> available = [new("1", "Close", "Done"), new("2", "Resolve", "Done")];

        var match = Assert.IsType<TransitionMatch.Ambiguous>(TransitionMatcher.Match(available, "Done"));
        Assert.Equal(["1", "2"], match.Candidates.Select(t => t.Id));
    }

    [Fact]
    public void Matcher_NothingMatches_IsNone() =>
        Assert.IsType<TransitionMatch.None>(TransitionMatcher.Match(Available, "Archive"));

    [Fact]
    public void Matcher_TrimsRequest() =>
        Assert.IsType<TransitionMatch.Matched>(TransitionMatcher.Match(Available, "  Close \n"));

    // Action

    [Fact]
    public void Action_HasPermanentAliasAndConnection()
    {
        var attribute = typeof(TransitionIssueAction).GetCustomAttribute<ActionAttribute>()!;

        Assert.Equal("community.jira.transitionIssue", attribute.Alias);
        Assert.Equal("community.jira.cloud", attribute.ConnectionTypeAlias);
    }

    [Fact]
    public async Task Moves_ToDone()
    {
        var rig = new ActionRig();
        EnqueueHappyPath(rig);

        var result = await rig.RunAsync(Action(rig), Settings());

        Assert.Equal(ActionResultStatus.Success, result.Status);
        var output = Assert.IsType<TransitionIssueOutput>(result.OutputData);
        Assert.Equal("In Progress", output.FromStatus);
        Assert.Equal("Done", output.ToStatus);
        Assert.Equal("31", output.TransitionId);

        var paths = rig.Fake.Requests.Select(r => (r.Method.Method, r.Uri.PathAndQuery.Replace("/ex/jira/" + ActionRig.CloudId, string.Empty, StringComparison.Ordinal))).ToList();
        Assert.Equal(
            [("GET", "/rest/api/3/issue/SBX-1?fields=status"), ("GET", "/rest/api/3/issue/SBX-1/transitions"), ("POST", "/rest/api/3/issue/SBX-1/transitions")],
            paths);

        var expected = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "requests", "transition-issue.json")));
        Assert.True(JsonNode.DeepEquals(expected, PostedBody(rig)), PostedBody(rig).ToJsonString());
    }

    [Fact]
    public async Task NotAvailable_ListsAvailable()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, InProgress).EnqueueJson(HttpStatusCode.OK, Transitions);

        var result = await rig.RunAsync(Action(rig), Settings(transition: "Archive"));

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        var message = result.Exception!.Message;
        Assert.Contains("Archive", message, StringComparison.Ordinal);
        Assert.Contains("Start", message, StringComparison.Ordinal);
        Assert.Contains("Close", message, StringComparison.Ordinal);
        Assert.Contains("Reopen", message, StringComparison.Ordinal);
        Assert.DoesNotContain(rig.Fake.Requests, IsPost);
    }

    [Fact]
    public async Task RetryAfterTimeout_ByTransitionName_SaysItMayHaveMoved()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, InProgress).EnqueueJson(HttpStatusCode.OK, Transitions).EnqueueException(new TaskCanceledException("timeout"));

        var first = await rig.RunAsync(Action(rig), Settings(transition: "Close"));
        Assert.Equal(StepRunErrorCategory.ServiceUnavailable, first.ErrorCategory);

        // Jira did move it. From Done, "Close" is no longer offered.
        rig.Enqueue(HttpStatusCode.OK, AlreadyDone).EnqueueJson(HttpStatusCode.OK, """{"transitions":[{"id":"41","name":"Reopen","to":{"name":"To Do"}}]}""");
        var second = await rig.RunAsync(Action(rig), Settings(transition: "Close"));

        Assert.Equal(StepRunErrorCategory.Validation, second.ErrorCategory);
        Assert.Contains("already be in status Done", second.Exception!.Message, StringComparison.Ordinal);
        Assert.Contains("status name", second.Exception!.Message, StringComparison.Ordinal);
        Assert.Single(rig.Fake.Requests, IsPost);
    }

    [Fact]
    public async Task RetryAfterTimeout_ByStatusName_IsNoOp()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, InProgress).EnqueueJson(HttpStatusCode.OK, Transitions).EnqueueException(new TaskCanceledException("timeout"));
        await rig.RunAsync(Action(rig), Settings(transition: "Done"));

        rig.Enqueue(HttpStatusCode.OK, AlreadyDone);
        var second = await rig.RunAsync(Action(rig), Settings(transition: "Done"));

        Assert.Equal(ActionResultStatus.Success, second.Status);
        Assert.Single(rig.Fake.Requests, IsPost);
    }

    [Theory]
    [InlineData("41")]
    [InlineData("close")]
    public async Task GlobalTransition_AlreadyInItsTarget_IsNoOp(string requested)
    {
        // M2-SEC-02: a global transition is still offered after it ran, so a retry must not apply it twice.
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, AlreadyDone)
            .EnqueueJson(HttpStatusCode.OK, """{"transitions":[{"id":"41","name":"Close","to":{"name":"Done"}},{"id":"11","name":"Reopen","to":{"name":"To Do"}}]}""");

        var result = await rig.RunAsync(Action(rig), Settings(transition: requested));

        Assert.Equal(ActionResultStatus.Success, result.Status);
        var output = Assert.IsType<TransitionIssueOutput>(result.OutputData);
        Assert.Equal("Done", output.FromStatus);
        Assert.Equal("Done", output.ToStatus);
        Assert.Equal(string.Empty, output.TransitionId);
        Assert.Contains(rig.LogMessages, m => m.Contains("already in status", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Fake.Requests, IsPost);
    }

    [Fact]
    public async Task Ambiguous_FailsAndListsCandidates()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, InProgress)
            .EnqueueJson(HttpStatusCode.OK, """{"transitions":[{"id":"1","name":"Close","to":{"name":"Done"}},{"id":"2","name":"Resolve","to":{"name":"Done"}}]}""");

        var result = await rig.RunAsync(Action(rig), Settings());

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        Assert.Contains("Close", result.Exception!.Message, StringComparison.Ordinal);
        Assert.Contains("Resolve", result.Exception!.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(rig.Fake.Requests, IsPost);
    }

    [Fact]
    public async Task AlreadyInStatus_IsNoOp()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, AlreadyDone);

        var result = await rig.RunAsync(Action(rig), Settings(transition: "done"));

        Assert.Equal(ActionResultStatus.Success, result.Status);
        var output = Assert.IsType<TransitionIssueOutput>(result.OutputData);
        Assert.Equal("Done", output.FromStatus);
        Assert.Equal("Done", output.ToStatus);
        Assert.Equal(string.Empty, output.TransitionId);
        Assert.Contains(rig.LogMessages, m => m.Contains("already in status", StringComparison.Ordinal));
        Assert.Single(rig.Fake.Requests);
        Assert.DoesNotContain(rig.Fake.Requests, IsPost);
    }

    [Fact]
    public async Task Comment_SentInSameCall()
    {
        var rig = new ActionRig();
        EnqueueHappyPath(rig);
        var settings = Settings();
        settings.Comment = "Published in Umbraco";

        await rig.RunAsync(Action(rig), settings);

        var comment = PostedBody(rig)["update"]!["comment"]![0]!["add"]!["body"]!;
        Assert.Equal("doc", (string?)comment["type"]);
        Assert.Equal("Published in Umbraco", (string?)comment["content"]![0]!["content"]![0]!["text"]);
        Assert.Single(rig.Fake.Requests, IsPost);
    }

    [Fact]
    public async Task Comment_IsPlainText_NotMarkdown()
    {
        var rig = new ActionRig();
        EnqueueHappyPath(rig);
        var settings = Settings();
        settings.Comment = "**not bold** [x](https://evil.example)";

        await rig.RunAsync(Action(rig), settings);

        var body = PostedBody(rig)["update"]!["comment"]![0]!["add"]!["body"]!.ToJsonString();
        Assert.DoesNotContain("\"strong\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"link\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolution_Sent()
    {
        var rig = new ActionRig();
        EnqueueHappyPath(rig);
        var settings = Settings();
        settings.ResolutionName = " Fixed ";

        await rig.RunAsync(Action(rig), settings);

        Assert.Equal("Fixed", (string?)PostedBody(rig)["fields"]!["resolution"]!["name"]);
    }

    [Fact]
    public async Task IssueKey_Whitespace_IsTrimmed()
    {
        var rig = new ActionRig();
        EnqueueHappyPath(rig);

        var result = await rig.RunAsync(Action(rig), Settings(" SBX-1\n"));

        Assert.Equal(ActionResultStatus.Success, result.Status);
        Assert.All(rig.Fake.Requests, r => Assert.Contains("/rest/api/3/issue/SBX-1", r.Uri.AbsolutePath, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("SUP-1/..")]
    [InlineData("")]
    public async Task IssueKey_DotSegments_FailBeforeHttp(string key)
    {
        var rig = new ActionRig();

        var result = await rig.RunAsync(Action(rig), Settings(key));

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        Assert.Empty(rig.Fake.Requests);
    }

    [Fact]
    public async Task Transition_Blank_FailsBeforeHttp()
    {
        var rig = new ActionRig();

        var result = await rig.RunAsync(Action(rig), Settings(transition: "  "));

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        Assert.Empty(rig.Fake.Requests);
    }

    [Fact]
    public async Task TransitionId_NonNumeric_TreatedAsName()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, InProgress)
            .EnqueueJson(HttpStatusCode.OK, """{"transitions":[{"id":"5","name":"Close","to":{"name":"Done"}},{"id":"6","name":"../5","to":{"name":"Odd"}}]}""")
            .EnqueueJson(HttpStatusCode.NoContent);

        var result = await rig.RunAsync(Action(rig), Settings(transition: "../5"));

        Assert.Equal(ActionResultStatus.Success, result.Status);
        Assert.Equal("6", (string?)PostedBody(rig)["transition"]!["id"]);
        Assert.All(rig.Fake.Requests, r => Assert.DoesNotContain("..", r.Uri.OriginalString, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TransitionId_NonNumeric_NoNameMatch_FailsWithoutPost()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, InProgress).EnqueueJson(HttpStatusCode.OK, Transitions);

        var result = await rig.RunAsync(Action(rig), Settings(transition: "../31"));

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        Assert.DoesNotContain(rig.Fake.Requests, IsPost);
    }

    [Fact]
    public async Task Transitions_NonNumericIdFromJira_IsIgnored()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, InProgress)
            .EnqueueJson(HttpStatusCode.OK, """{"transitions":[{"id":"x/../1","name":"Close","to":{"name":"Done"}}]}""");

        var result = await rig.RunAsync(Action(rig), Settings());

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        Assert.DoesNotContain(rig.Fake.Requests, IsPost);
    }

    [Fact]
    public async Task Missing_404_SaysNotFoundOrNoPermission()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.NotFound);

        var result = await rig.RunAsync(Action(rig), Settings("SBX-9"));

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        Assert.Contains("Issue SBX-9 not found or no permission", result.Exception!.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"fields":{}}""")]
    [InlineData("""{"fields":{"status":{"name":""}}}""")]
    public async Task Status_Unreadable_IsInvalidResponse(string json)
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, json);

        var result = await rig.RunAsync(Action(rig), Settings());

        Assert.Equal(StepRunErrorCategory.InvalidResponse, result.ErrorCategory);
        Assert.DoesNotContain(rig.Fake.Requests, IsPost);
    }

    [Fact]
    public async Task Post_400_IsValidation_WithJiraMessage()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, InProgress).EnqueueJson(HttpStatusCode.OK, Transitions)
            .EnqueueJson(HttpStatusCode.BadRequest, """{"errorMessages":[],"errors":{"resolution":"Field 'resolution' cannot be set."}}""");

        var result = await rig.RunAsync(Action(rig), Settings());

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
    }
}
