using System.Net;
using System.Text.Json.Nodes;
using Umbraco.Automate.Core.Actions;
using Umbraco.Community.Automate.Jira.Actions;
using Umbraco.Community.Automate.Jira.Tests.Support;

namespace Umbraco.Community.Automate.Jira.Tests;

public class CreateIssueDedupeTests
{
    private const string CreatedJson = """{"id":"10042","key":"SBX-17"}""";
    private const string EmptySearch = """{"issues":[]}""";
    private const string FoundSearch = """{"issues":[{"id":"10042","key":"SBX-17","fields":{}}]}""";
    private const string CleanChangelog = """{"startAt":0,"maxResults":100,"total":0,"isLast":true,"values":[]}""";

    private static CreateIssueSettings Settings(string summary = "Summary") => new() { ProjectKey = "SBX", IssueType = "10002", Summary = summary };

    private static CreateIssueAction Action(ActionRig rig) => new(rig.Infrastructure, rig.Scopes);

    private static bool IsSearch(RecordedRequest r) => r.Method == HttpMethod.Post && r.Uri.AbsolutePath.EndsWith("/rest/api/3/search/jql", StringComparison.Ordinal);

    private static bool IsChangelog(RecordedRequest r) => r.Method == HttpMethod.Get && r.Uri.AbsolutePath.EndsWith("/changelog", StringComparison.Ordinal);

    // A changelog in which someone added the marker label after the issue was created.
    private static string LabelAddedChangelog(string label) =>
        $$$"""{"startAt":0,"maxResults":100,"total":1,"isLast":true,"values":[{"id":"1","items":[{"field":"labels","fieldId":"labels","fromString":"triage","toString":"triage {{{label}}}"}]}]}""";

    private static bool IsCreate(RecordedRequest r) => r.Method == HttpMethod.Post && r.Uri.AbsolutePath.EndsWith("/rest/api/3/issue", StringComparison.Ordinal);

    private static string ExpectedMarker(ActionRig rig, CreateIssueSettings settings) =>
        DedupeKey.Marker(DedupeKey.Effective(settings.DedupeKey, rig.LastContext!, [settings.ProjectKey, settings.IssueType, settings.Summary, settings.Description]), rig.ConnectionId);

    [Fact]
    public async Task Dedupe_DefaultsToContextAndInputs_WhenBlank()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, EmptySearch).EnqueueJson(HttpStatusCode.Created, CreatedJson);
        var settings = Settings();

        await rig.RunAsync(Action(rig), settings);

        var marker = ExpectedMarker(rig, settings);
        var search = JsonNode.Parse(rig.Fake.Requests.Single(IsSearch).Body!)!;
        // creator = currentUser(): the creator field cannot be edited, so an issue someone else created never matches
        // (security audit 2026-11-18). A label copied onto another issue this account created is caught by the changelog
        // check (Dedupe_LabelAddedAfterCreate_IsNotAdopted). Oldest first, a handful of candidates.
        Assert.Equal($"project = \"SBX\" AND labels = \"cj-dedupe-{marker}\" AND creator = currentUser() ORDER BY created ASC", (string?)search["jql"]);
        Assert.Equal(10, (int?)search["maxResults"]);
        var labels = JsonNode.Parse(rig.Fake.Requests.Single(IsCreate).Body!)!["fields"]!["labels"]!.AsArray();
        Assert.Contains($"cj-dedupe-{marker}", labels.Select(l => (string?)l));
    }

    [Fact]
    public async Task Dedupe_SearchesBeforeEveryPost()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, EmptySearch).EnqueueJson(HttpStatusCode.Created, CreatedJson);

        await rig.RunAsync(Action(rig), Settings());

        Assert.True(IsSearch(rig.Fake.Requests[0]));
        Assert.True(IsCreate(rig.Fake.Requests[1]));
    }

    [Fact]
    public async Task Dedupe_ExistingIssue_ReturnsItWithoutPost()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, FoundSearch).EnqueueJson(HttpStatusCode.OK, CleanChangelog);

        var result = await rig.RunAsync(Action(rig), Settings());

        Assert.Equal(ActionResultStatus.Success, result.Status);
        var output = Assert.IsType<CreateIssueOutput>(result.OutputData);
        Assert.True(output.WasExisting);
        Assert.Equal("SBX-17", output.IssueKey);
        Assert.Equal("https://acme.atlassian.net/browse/SBX-17", output.IssueUrl);
        Assert.DoesNotContain(rig.Fake.Requests, IsCreate);
    }

    // Security audit 2026-11-18: anyone who can edit issues can move the marker label onto another issue the service
    // account created. This package puts the label on in the create request itself, so a label that the changelog shows
    // being added later was copied, and that issue is never adopted. A new issue is created instead.
    [Fact]
    public async Task Dedupe_LabelAddedAfterCreate_IsNotAdopted()
    {
        var rig = new ActionRig();
        var settings = Settings();
        settings.DedupeKey = "form-123";
        var marker = DedupeKey.Marker("form-123", rig.ConnectionId);
        rig.Enqueue(HttpStatusCode.OK, FoundSearch)
            .EnqueueJson(HttpStatusCode.OK, LabelAddedChangelog("cj-dedupe-" + marker))
            .EnqueueJson(HttpStatusCode.Created, """{"id":"10043","key":"SBX-18"}""");

        var result = await rig.RunAsync(Action(rig), settings);

        var output = Assert.IsType<CreateIssueOutput>(result.OutputData);
        Assert.False(output.WasExisting);
        Assert.Equal("SBX-18", output.IssueKey);
        Assert.Single(rig.Fake.Requests, IsCreate);
    }

    // The candidates come oldest first; a tampered one is skipped and the next clean one is the earlier run's issue.
    [Fact]
    public async Task Dedupe_TamperedCandidateSkipped_CleanOneReturned()
    {
        var rig = new ActionRig();
        var settings = Settings();
        settings.DedupeKey = "form-123";
        var marker = DedupeKey.Marker("form-123", rig.ConnectionId);
        rig.Enqueue(HttpStatusCode.OK, """{"issues":[{"id":"10040","key":"SBX-15","fields":{}},{"id":"10042","key":"SBX-17","fields":{}}]}""")
            .EnqueueJson(HttpStatusCode.OK, LabelAddedChangelog("cj-dedupe-" + marker))
            .EnqueueJson(HttpStatusCode.OK, CleanChangelog);

        var result = await rig.RunAsync(Action(rig), settings);

        Assert.Equal("SBX-17", Assert.IsType<CreateIssueOutput>(result.OutputData).IssueKey);
        Assert.DoesNotContain(rig.Fake.Requests, IsCreate);
        Assert.Equal(2, rig.Fake.Requests.Count(IsChangelog));
    }

    // Jira's label search ignores case, so an upper-case copy is still found and must still be caught.
    [Fact]
    public async Task Dedupe_UpperCaseCopy_IsNotAdopted()
    {
        var rig = new ActionRig();
        var settings = Settings();
        settings.DedupeKey = "form-123";
        var marker = DedupeKey.Marker("form-123", rig.ConnectionId);
        rig.Enqueue(HttpStatusCode.OK, FoundSearch)
            .EnqueueJson(HttpStatusCode.OK, LabelAddedChangelog(("cj-dedupe-" + marker).ToUpperInvariant()))
            .EnqueueJson(HttpStatusCode.Created, """{"id":"10043","key":"SBX-18"}""");

        var result = await rig.RunAsync(Action(rig), settings);

        Assert.False(Assert.IsType<CreateIssueOutput>(result.OutputData).WasExisting);
        Assert.Single(rig.Fake.Requests, IsCreate);
    }

    [Fact]
    public async Task Dedupe_LabelAddedOnSecondChangelogPage_IsNotAdopted()
    {
        var rig = new ActionRig();
        var settings = Settings();
        settings.DedupeKey = "form-123";
        var marker = DedupeKey.Marker("form-123", rig.ConnectionId);
        rig.Enqueue(HttpStatusCode.OK, FoundSearch)
            .EnqueueJson(HttpStatusCode.OK, """{"startAt":0,"maxResults":100,"total":101,"isLast":false,"values":[]}""")
            .EnqueueJson(HttpStatusCode.OK, LabelAddedChangelog("cj-dedupe-" + marker))
            .EnqueueJson(HttpStatusCode.Created, """{"id":"10043","key":"SBX-18"}""");

        var result = await rig.RunAsync(Action(rig), settings);

        Assert.False(Assert.IsType<CreateIssueOutput>(result.OutputData).WasExisting);
        Assert.Equal(2, rig.Fake.Requests.Count(IsChangelog));
    }

    // A page that says neither isLast nor total does not prove the rest of the history is clean.
    [Fact]
    public async Task Dedupe_ChangelogWithoutPagingInfo_IsNotAdopted()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, FoundSearch)
            .EnqueueJson(HttpStatusCode.OK, """{"values":[]}""")
            .EnqueueJson(HttpStatusCode.Created, """{"id":"10043","key":"SBX-18"}""");

        var result = await rig.RunAsync(Action(rig), Settings());

        Assert.False(Assert.IsType<CreateIssueOutput>(result.OutputData).WasExisting);
    }

    // A history longer than the page cap is not read to the end, so the candidate is not adopted.
    [Fact]
    public async Task Dedupe_ChangelogOverPageCap_IsNotAdopted()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, FoundSearch);
        for (var page = 0; page < 20; page++)
        {
            rig.Fake.EnqueueJson(HttpStatusCode.OK, """{"startAt":0,"maxResults":100,"total":5000,"isLast":false,"values":[]}""");
        }

        rig.Fake.EnqueueJson(HttpStatusCode.Created, """{"id":"10043","key":"SBX-18"}""");

        var result = await rig.RunAsync(Action(rig), Settings());

        Assert.False(Assert.IsType<CreateIssueOutput>(result.OutputData).WasExisting);
        Assert.Equal(20, rig.Fake.Requests.Count(IsChangelog));
    }

    // The total tells the last page even without isLast.
    [Fact]
    public async Task Dedupe_ChangelogTotalReached_IsAdopted()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, FoundSearch).EnqueueJson(HttpStatusCode.OK, """{"startAt":0,"maxResults":100,"total":0,"values":[]}""");

        var result = await rig.RunAsync(Action(rig), Settings());

        Assert.True(Assert.IsType<CreateIssueOutput>(result.OutputData).WasExisting);
    }

    // A label change for some other label is not a copy of ours.
    [Fact]
    public async Task Dedupe_OtherLabelChanges_StillAdopted()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, FoundSearch).EnqueueJson(HttpStatusCode.OK, LabelAddedChangelog("triage-2"));

        var result = await rig.RunAsync(Action(rig), Settings());

        Assert.True(Assert.IsType<CreateIssueOutput>(result.OutputData).WasExisting);
        Assert.DoesNotContain(rig.Fake.Requests, IsCreate);
    }

    // As with a failed search (F-05): if the changelog cannot be read, nothing is created.
    [Fact]
    public async Task Dedupe_ChangelogFails_NoPost()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, FoundSearch).EnqueueJson(HttpStatusCode.Forbidden, """{"errorMessages":["no"]}""");

        var result = await rig.RunAsync(Action(rig), Settings());

        Assert.NotEqual(ActionResultStatus.Success, result.Status);
        Assert.DoesNotContain(rig.Fake.Requests, IsCreate);
    }

    [Fact]
    public async Task Post_TimeoutAfterSend_RetryFindsExisting_NoSecondPost()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, EmptySearch).EnqueueException(new TaskCanceledException("timeout"));

        var first = await rig.RunAsync(Action(rig), Settings());
        Assert.Equal(StepRunErrorCategory.ServiceUnavailable, first.ErrorCategory);

        // Automate's retry runs the same step of the same run again.
        rig.Enqueue(HttpStatusCode.OK, FoundSearch).EnqueueJson(HttpStatusCode.OK, CleanChangelog);
        var second = await rig.RunAsync(Action(rig), Settings());

        Assert.Equal(ActionResultStatus.Success, second.Status);
        Assert.True(Assert.IsType<CreateIssueOutput>(second.OutputData).WasExisting);
        Assert.Single(rig.Fake.Requests, IsCreate);
        var searches = rig.Fake.Requests.Where(IsSearch).Select(r => r.Body).ToList();
        Assert.Equal(2, searches.Count);
        Assert.Equal(searches[0], searches[1]);
    }

    public static TheoryData<int, StepRunErrorCategory> SearchFailures() => new()
    {
        { 401, StepRunErrorCategory.Authentication },
        { 403, StepRunErrorCategory.Authentication },
        { 500, StepRunErrorCategory.ServiceUnavailable },
        { 503, StepRunErrorCategory.ServiceUnavailable },
        { 429, StepRunErrorCategory.RateLimiting },
    };

    [Theory]
    [MemberData(nameof(SearchFailures))]
    public async Task Dedupe_SearchFails_NoPost(int status, StepRunErrorCategory category)
    {
        var rig = new ActionRig();
        rig.Enqueue((HttpStatusCode)status, null, ("Retry-After", "60"));

        var result = await rig.RunAsync(Action(rig), Settings());

        Assert.Equal(category, result.ErrorCategory);
        Assert.DoesNotContain(rig.Fake.Requests, IsCreate);
    }

    [Fact]
    public async Task Dedupe_SearchNetworkError_NoPost()
    {
        var rig = new ActionRig();
        rig.Fake.EnqueueException(new HttpRequestException(HttpRequestError.ConnectionError, "boom"));

        var result = await rig.RunAsync(Action(rig), Settings());

        Assert.Equal(StepRunErrorCategory.ServiceUnavailable, result.ErrorCategory);
        Assert.DoesNotContain(rig.Fake.Requests, IsCreate);
    }

    [Fact]
    public async Task Dedupe_SearchUnreadable_NoPost()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, """{"unexpected":true}""");

        var result = await rig.RunAsync(Action(rig), Settings());

        Assert.Equal(StepRunErrorCategory.InvalidResponse, result.ErrorCategory);
        Assert.DoesNotContain(rig.Fake.Requests, IsCreate);
    }

    [Fact]
    public async Task Dedupe_ForEachTwoItems_TwoPosts()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, EmptySearch).EnqueueJson(HttpStatusCode.Created, CreatedJson)
            .EnqueueJson(HttpStatusCode.OK, EmptySearch).EnqueueJson(HttpStatusCode.Created, """{"id":"10043","key":"SBX-18"}""");

        await rig.RunAsync(Action(rig), Settings("Item A"));
        await rig.RunAsync(Action(rig), Settings("Item B"));

        var creates = rig.Fake.Requests.Where(IsCreate).ToList();
        Assert.Equal(2, creates.Count);
        string Marker(RecordedRequest r) => JsonNode.Parse(r.Body!)!["fields"]!["labels"]!.AsArray().Select(l => (string)l!).Single(l => l.StartsWith("cj-dedupe-", StringComparison.Ordinal));
        Assert.NotEqual(Marker(creates[0]), Marker(creates[1]));
    }

    [Fact]
    public async Task Dedupe_ForEachIdenticalItems_IndexKeepsThemApart()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, EmptySearch).EnqueueJson(HttpStatusCode.Created, CreatedJson)
            .EnqueueJson(HttpStatusCode.OK, EmptySearch).EnqueueJson(HttpStatusCode.Created, """{"id":"10043","key":"SBX-18"}""");

        rig.BindingData = new Dictionary<string, object?> { ["loop"] = new Dictionary<string, object?> { ["item"] = "x", ["index"] = 0 } };
        await rig.RunAsync(Action(rig), Settings("Same"));
        rig.BindingData = new Dictionary<string, object?> { ["loop"] = new Dictionary<string, object?> { ["item"] = "x", ["index"] = 1 } };
        await rig.RunAsync(Action(rig), Settings("Same"));

        Assert.Equal(2, rig.Fake.Requests.Count(IsCreate));
        Assert.NotEqual(rig.Fake.Requests.Where(IsSearch).First().Body, rig.Fake.Requests.Where(IsSearch).Last().Body);
    }

    [Fact]
    public async Task Dedupe_LoopWithoutReadableIndex_ConfigurationError_NoRequest()
    {
        var rig = new ActionRig { BindingData = new Dictionary<string, object?> { ["loop"] = "unexpected shape" } };

        var result = await rig.RunAsync(Action(rig), Settings());

        Assert.Equal(StepRunErrorCategory.ConfigurationError, result.ErrorCategory);
        Assert.Contains("Dedupe key", result.Exception!.Message, StringComparison.Ordinal);
        Assert.Empty(rig.Fake.Requests);
    }

    [Fact]
    public async Task Dedupe_MarkerInCreateBody_AsLabel()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, EmptySearch).EnqueueJson(HttpStatusCode.Created, CreatedJson);

        await rig.RunAsync(Action(rig), Settings());

        var body = JsonNode.Parse(rig.Fake.Requests.Single(IsCreate).Body!)!;
        Assert.Matches("^cj-dedupe-[0-9a-f]{16}$", (string?)body["fields"]!["labels"]!.AsArray().Single());
        Assert.Null(body["properties"]);
    }

    [Fact]
    public async Task Dedupe_UserKey_FindsAcrossRuns()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, EmptySearch).EnqueueJson(HttpStatusCode.Created, CreatedJson).EnqueueJson(HttpStatusCode.OK, FoundSearch)
            .EnqueueJson(HttpStatusCode.OK, CleanChangelog);
        var settings = Settings();
        settings.DedupeKey = "form-123";

        await rig.RunAsync(Action(rig), settings);
        rig.RunId = Guid.NewGuid();
        var second = await rig.RunAsync(Action(rig), settings);

        Assert.True(Assert.IsType<CreateIssueOutput>(second.OutputData).WasExisting);
        var searches = rig.Fake.Requests.Where(IsSearch).Select(r => r.Body).ToList();
        Assert.Equal(searches[0], searches[1]);
    }

    [Fact]
    public async Task Dedupe_KeyIsHashed_RawValueNotSent()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, EmptySearch).EnqueueJson(HttpStatusCode.Created, CreatedJson);
        var settings = Settings();
        settings.DedupeKey = "form-123 alice@example.com";

        await rig.RunAsync(Action(rig), settings);

        foreach (var request in rig.Fake.Requests)
        {
            Assert.DoesNotContain("alice", request.Body ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("form-123", request.Body ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("alice", request.Uri.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Dedupe_JqlValueQuotedAndEscaped()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, EmptySearch).EnqueueJson(HttpStatusCode.Created, CreatedJson);
        var settings = Settings();
        settings.ProjectKey = "10010";

        await rig.RunAsync(Action(rig), settings);

        var jql = (string?)JsonNode.Parse(rig.Fake.Requests.Single(IsSearch).Body!)!["jql"];
        Assert.StartsWith("project = \"10010\" AND ", jql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dedupe_LabelAppendedToUserLabels()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, EmptySearch).EnqueueJson(HttpStatusCode.Created, CreatedJson);
        var settings = Settings();
        settings.Labels = "web";

        await rig.RunAsync(Action(rig), settings);

        var labels = JsonNode.Parse(rig.Fake.Requests.Single(IsCreate).Body!)!["fields"]!["labels"]!.AsArray().Select(l => (string?)l).ToList();
        Assert.Equal("web", labels[0]);
        Assert.StartsWith("cj-dedupe-", labels[1], StringComparison.Ordinal);
    }

    [Fact]
    public void DedupeKey_SupportsBindings()
    {
        var field = typeof(CreateIssueSettings).GetProperty(nameof(CreateIssueSettings.DedupeKey))!
            .GetCustomAttributes(typeof(Umbraco.Automate.Core.Settings.FieldAttribute), false).Cast<Umbraco.Automate.Core.Settings.FieldAttribute>().Single();

        Assert.True(field.SupportsBindings);
    }
}
