using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using Umbraco.Automate.Core.Actions;
using Umbraco.Automate.Core.Settings;
using Umbraco.Community.Automate.Jira.Actions;
using Umbraco.Community.Automate.Jira.Adf;
using Umbraco.Community.Automate.Jira.Tests.Support;

namespace Umbraco.Community.Automate.Jira.Tests;

public class CreateIssueActionTests
{
    private const string TypesJson = """{"issueTypes":[{"id":"10001","name":"Bug"},{"id":"10002","name":"Task"}]}""";
    private const string CreatedJson = """{"id":"10042","key":"SBX-17","self":"https://api.atlassian.com/x"}""";
    private const string EmptySearch = """{"issues":[]}""";

    private static CreateIssueSettings Settings() => new()
    {
        ProjectKey = "SBX",
        IssueType = "Task",
        Summary = "Form submitted: contact us",
    };

    private static CreateIssueAction Action(ActionRig rig) => new(rig.Infrastructure, rig.Scopes);

    // From Task 13 every create searches for its dedupe marker first.
    private static void EnqueueHappyPath(ActionRig rig, bool withTypes = true)
    {
        if (withTypes)
        {
            rig.Enqueue(HttpStatusCode.OK, TypesJson);
        }

        rig.Enqueue(HttpStatusCode.OK, EmptySearch).EnqueueJson(HttpStatusCode.Created, CreatedJson);
    }

    private static JsonObject PostedBody(ActionRig rig) =>
        JsonNode.Parse(rig.Fake.Requests.Single(r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath.EndsWith("/rest/api/3/issue", StringComparison.Ordinal)).Body!)!.AsObject();

    private static JsonObject PostedFields(ActionRig rig) => PostedBody(rig)["fields"]!.AsObject();

    [Fact]
    public void Action_HasPermanentAliasAndConnection()
    {
        var attribute = typeof(CreateIssueAction).GetCustomAttribute<ActionAttribute>()!;

        Assert.Equal("community.jira.createIssue", attribute.Alias);
        Assert.Equal("community.jira.cloud", attribute.ConnectionTypeAlias);
    }

    [Fact]
    public async Task Creates_GoldenRequest()
    {
        var rig = new ActionRig();
        EnqueueHappyPath(rig);
        var settings = Settings();
        settings.Description = "Line one\nLine two";
        settings.Labels = "web form, umbraco";
        settings.Priority = "High";
        settings.AssigneeAccountId = "000000:00000000-0000-4000-8000-000000000001";
        settings.ParentKey = "sbx-1";

        var result = await rig.RunAsync(Action(rig), settings);

        Assert.Equal(ActionResultStatus.Success, result.Status);
        var expected = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "requests", "create-issue.json")))!.AsObject();
        var posted = PostedBody(rig);

        // The dedupe marker label is checked in CreateIssueDedupeTests; here it is removed before comparing.
        var labels = posted["fields"]!["labels"]!.AsArray();
        var marker = labels.Single(l => ((string)l!).StartsWith("cj-dedupe-", StringComparison.Ordinal));
        labels.Remove(marker);
        Assert.True(JsonNode.DeepEquals(expected, posted), posted.ToJsonString());

        var output = Assert.IsType<CreateIssueOutput>(result.OutputData);
        Assert.Equal("10042", output.IssueId);
        Assert.Equal("SBX-17", output.IssueKey);
        Assert.Equal("https://acme.atlassian.net/browse/SBX-17", output.IssueUrl);
        Assert.False(output.WasExisting);
    }

    [Fact]
    public async Task NumericIssueType_NoCreatemeta()
    {
        var rig = new ActionRig();
        EnqueueHappyPath(rig, withTypes: false);
        var settings = Settings();
        settings.IssueType = "10002";

        var result = await rig.RunAsync(Action(rig), settings);

        Assert.Equal(ActionResultStatus.Success, result.Status);
        Assert.DoesNotContain(rig.Fake.Requests, r => r.Uri.AbsolutePath.Contains("createmeta", StringComparison.Ordinal));
        Assert.Equal("10002", (string?)PostedFields(rig)["issuetype"]!["id"]);
    }

    [Theory]
    [InlineData("a\r\nb", "a b")]
    [InlineData("  a\nb\rc  ", "a b c")]
    public async Task Summary_NewlinesReplaced(string summary, string expected)
    {
        var rig = new ActionRig();
        EnqueueHappyPath(rig);
        var settings = Settings();
        settings.Summary = summary;

        await rig.RunAsync(Action(rig), settings);

        Assert.Equal(expected, (string?)PostedFields(rig)["summary"]);
    }

    [Fact]
    public async Task Summary_CutTo255()
    {
        var rig = new ActionRig();
        EnqueueHappyPath(rig);
        var settings = Settings();
        settings.Summary = new string('s', 300);

        await rig.RunAsync(Action(rig), settings);

        Assert.Equal(255, ((string)PostedFields(rig)["summary"]!).Length);
    }

    [Fact]
    public async Task Summary_WithEmojiAtBoundary_IsValidUtf16()
    {
        var rig = new ActionRig();
        EnqueueHappyPath(rig);
        var settings = Settings();
        settings.Summary = new string('s', 254) + "😀";

        await rig.RunAsync(Action(rig), settings);

        var summary = (string)PostedFields(rig)["summary"]!;
        Assert.Equal(254, summary.Length);
        Assert.False(char.IsHighSurrogate(summary[^1]));
    }

    [Fact]
    public async Task Labels_Normalised()
    {
        var rig = new ActionRig();
        EnqueueHappyPath(rig);
        var settings = Settings();
        settings.Labels = " a b, ,A b,c ,a b";

        await rig.RunAsync(Action(rig), settings);

        var labels = PostedFields(rig)["labels"]!.AsArray().Select(l => (string)l!).Where(l => !l.StartsWith("cj-dedupe-", StringComparison.Ordinal));
        Assert.Equal(["a-b", "A-b", "c"], labels);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n ")]
    public async Task EmptyDescription_OmitsField(string? description)
    {
        var rig = new ActionRig();
        EnqueueHappyPath(rig);
        var settings = Settings();
        settings.Description = description;

        await rig.RunAsync(Action(rig), settings);

        Assert.False(PostedFields(rig).ContainsKey("description"));
    }

    [Fact]
    public async Task Description_Markdown_Converted()
    {
        var rig = new ActionRig();
        EnqueueHappyPath(rig);
        var settings = Settings();
        settings.Description = "# Heading\n\n- item\n\n[link](https://example.com)";
        settings.DescriptionFormat = JiraTextFormat.Markdown;

        await rig.RunAsync(Action(rig), settings);

        var types = PostedFields(rig)["description"]!["content"]!.AsArray().Select(b => (string?)b!["type"]);
        Assert.Equal(["heading", "bulletList", "paragraph"], types);
    }

    public static TheoryData<string> RejectedAdditionalFields() =>
    [
        """{"project":{"key":"X"}}""",
        """{"Project":{}}""",
        """{"PROJECT":{}}""",
        """{"CUSTOMFIELD_10010":1}""",
        """{"customfield_":1}""",
        """{"customfield_abc":1}""",
        """{"customfield_10010":1,"customfield_10010":2}""",
        """{"labels":["a"],"labels":["b"]}""",
        """{"customfield_1":{"x":1,"x":2}}""",
        """{"assignee":{"accountId":"x"}}""",
        """{"\u0070roject":{}}""",
        """{"proj\u0065ct":{}}""",
        """{"labels":[],"lab\u0065ls":[]}""",
        "[1]",
        "{bad",
    ];

    [Theory]
    [MemberData(nameof(RejectedAdditionalFields))]
    public async Task AdditionalFieldsJson_AnyRejection_SendsNoRequest(string json)
    {
        var rig = new ActionRig();
        var settings = Settings();
        settings.AdditionalFieldsJson = json;

        var result = await rig.RunAsync(Action(rig), settings);

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        Assert.Empty(rig.Fake.Requests);
    }

    [Fact]
    public async Task AdditionalFieldsJson_LabelsWithTypedLabels_Fails()
    {
        var rig = new ActionRig();
        var settings = Settings();
        settings.Labels = "a";
        settings.AdditionalFieldsJson = """{"labels":["b"]}""";

        var result = await rig.RunAsync(Action(rig), settings);

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        Assert.Contains("set labels in one place", result.Exception!.Message);
        Assert.Empty(rig.Fake.Requests);
    }

    [Fact]
    public async Task Description_AdfJsonDuplicateKey_IsValidation_NoRequest()
    {
        var rig = new ActionRig();
        var settings = Settings();
        settings.Description = """{"type":"doc","version":1,"content":[{"type":"text","text":"a","EVIL-KEY":1,"EVIL-KEY":2}]}""";
        settings.DescriptionFormat = JiraTextFormat.AdfJson;

        var result = await rig.RunAsync(Action(rig), settings);

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        Assert.DoesNotContain("EVIL-KEY", result.Exception!.Message, StringComparison.Ordinal);
        Assert.Empty(rig.Fake.Requests);
    }

    [Theory]
    [InlineData("cj-dedupe-0123456789abcdef", null)]
    [InlineData("web, CJ-DEDUPE-x", null)]
    [InlineData(null, """{"labels":["cj-dedupe-0123456789abcdef"]}""")]
    public async Task ReservedDedupeLabel_FromUser_IsValidation_NoRequest(string? labels, string? additional)
    {
        // A user-supplied marker label could steer a later dedupe search to another issue (M1 security review L1).
        var rig = new ActionRig();
        var settings = Settings();
        settings.Labels = labels;
        settings.AdditionalFieldsJson = additional;

        var result = await rig.RunAsync(Action(rig), settings);

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        Assert.Contains("cj-dedupe-", result.Exception!.Message, StringComparison.Ordinal);
        Assert.Empty(rig.Fake.Requests);
    }

    [Fact]
    public async Task AdditionalFieldsJson_Over64KB_IsValidation_NoRequest()
    {
        var rig = new ActionRig();
        var settings = Settings();
        settings.AdditionalFieldsJson = "{\"customfield_1\":\"" + new string('a', AdfLimits.MaxInputBytes) + "\"}";

        var result = await rig.RunAsync(Action(rig), settings);

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        Assert.Empty(rig.Fake.Requests);
    }

    [Theory]
    [InlineData("""{"labels":"urgent"}""")]
    [InlineData("""{"labels":null}""")]
    [InlineData("""{"labels":[1]}""")]
    public async Task AdditionalFieldsJson_LabelsNotStringArray_IsValidation(string json)
    {
        var rig = new ActionRig();
        var settings = Settings();
        settings.AdditionalFieldsJson = json;

        var result = await rig.RunAsync(Action(rig), settings);

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        Assert.Empty(rig.Fake.Requests);
    }

    [Fact]
    public async Task AdditionalFieldsJson_LabelsOnly_MarkerAppended()
    {
        var rig = new ActionRig();
        EnqueueHappyPath(rig);
        var settings = Settings();
        settings.AdditionalFieldsJson = """{"labels":["urgent"]}""";

        await rig.RunAsync(Action(rig), settings);

        var labels = PostedFields(rig)["labels"]!.AsArray().Select(l => (string)l!).ToList();
        Assert.Equal("urgent", labels[0]);
        Assert.StartsWith("cj-dedupe-", labels[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdditionalFieldsJson_Merged()
    {
        var rig = new ActionRig();
        EnqueueHappyPath(rig);
        var settings = Settings();
        settings.AdditionalFieldsJson = """{"customfield_10010":5}""";

        await rig.RunAsync(Action(rig), settings);

        Assert.Equal(5, (int?)PostedFields(rig)["customfield_10010"]);
    }

    [Theory]
    [InlineData(nameof(CreateIssueSettings.AdditionalFieldsJson))]
    [InlineData(nameof(CreateIssueSettings.DescriptionFormat))]
    public void FieldsWithoutBindings(string property)
    {
        var field = typeof(CreateIssueSettings).GetProperty(property)!.GetCustomAttribute<FieldAttribute>()!;

        Assert.False(field.SupportsBindings);
    }

    [Theory]
    [InlineData(nameof(CreateIssueSettings.ProjectKey))]
    [InlineData(nameof(CreateIssueSettings.IssueType))]
    [InlineData(nameof(CreateIssueSettings.Summary))]
    [InlineData(nameof(CreateIssueSettings.Description))]
    [InlineData(nameof(CreateIssueSettings.Labels))]
    [InlineData(nameof(CreateIssueSettings.ParentKey))]
    public void FieldsWithBindings(string property)
    {
        Assert.True(typeof(CreateIssueSettings).GetProperty(property)!.GetCustomAttribute<FieldAttribute>()!.SupportsBindings);
    }

    [Fact]
    public void Description_UsesMarkdownEditor()
    {
        var field = typeof(CreateIssueSettings).GetProperty(nameof(CreateIssueSettings.Description))!.GetCustomAttribute<FieldAttribute>()!;

        Assert.Equal("Umb.PropertyEditorUi.MarkdownEditor", field.EditorUiAlias);
    }

    [Theory]
    [InlineData("sbx-1")]
    [InlineData("../x")]
    [InlineData("S BX")]
    [InlineData("A")]
    public async Task ProjectKey_Invalid_FailsBeforeHttp(string projectKey)
    {
        var rig = new ActionRig();
        var settings = Settings();
        settings.ProjectKey = projectKey;

        var result = await rig.RunAsync(Action(rig), settings);

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        Assert.Empty(rig.Fake.Requests);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../SBX-1")]
    [InlineData("SBX-1/../2")]
    [InlineData("10001")]
    public async Task ParentKey_DotSegments_FailBeforeHttp(string parentKey)
    {
        var rig = new ActionRig();
        var settings = Settings();
        settings.ParentKey = parentKey;

        var result = await rig.RunAsync(Action(rig), settings);

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        Assert.Empty(rig.Fake.Requests);
    }

    [Theory]
    [InlineData(JiraTextFormat.Markdown)]
    [InlineData(JiraTextFormat.AdfJson)]
    public async Task Description_Over64KB_FailsValidationBeforeHttp(JiraTextFormat format)
    {
        var rig = new ActionRig();
        var settings = Settings();
        settings.Description = new string('a', AdfLimits.MaxInputBytes + 1);
        settings.DescriptionFormat = format;

        var result = await rig.RunAsync(Action(rig), settings);

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        Assert.Empty(rig.Fake.Requests);
    }

    [Fact]
    public async Task Description_BadAdfJson_FailsValidationBeforeHttp()
    {
        var rig = new ActionRig();
        var settings = Settings();
        settings.Description = """{"type":"doc","version":2,"content":[]}""";
        settings.DescriptionFormat = JiraTextFormat.AdfJson;

        var result = await rig.RunAsync(Action(rig), settings);

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        Assert.Empty(rig.Fake.Requests);
    }

    [Fact]
    public async Task SanitiserWarnings_AreLogged()
    {
        var rig = new ActionRig();
        EnqueueHappyPath(rig);
        var settings = Settings();
        settings.Description = "[x](javascript:alert(1))";
        settings.DescriptionFormat = JiraTextFormat.Markdown;

        await rig.RunAsync(Action(rig), settings);

        Assert.Contains(rig.LogMessages, m => m.Contains("link", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Jira400_ShowsFieldErrors()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, TypesJson).EnqueueJson(HttpStatusCode.OK, EmptySearch)
            .EnqueueJson(HttpStatusCode.BadRequest, """{"errorMessages":[],"errors":{"priority":"Priority name 'Urgent' is not valid"}}""");

        var result = await rig.RunAsync(Action(rig), Settings());

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        Assert.Contains("priority: Priority name 'Urgent' is not valid", result.Exception!.Message);
    }

    [Fact]
    public async Task Jira429_IsRateLimiting()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, TypesJson).EnqueueJson(HttpStatusCode.OK, EmptySearch)
            .EnqueueJson((HttpStatusCode)429, null, ("Retry-After", "30"));

        var result = await rig.RunAsync(Action(rig), Settings());

        Assert.Equal(StepRunErrorCategory.RateLimiting, result.ErrorCategory);
        Assert.Contains(rig.LogMessages, m => m.Contains("30", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnknownIssueType_ValidationListsTypes()
    {
        var rig = new ActionRig();
        rig.Enqueue(HttpStatusCode.OK, TypesJson);
        var settings = Settings();
        settings.IssueType = "Epic";

        var result = await rig.RunAsync(Action(rig), settings);

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        Assert.Contains("Bug, Task", result.Exception!.Message);
    }

    [Theory]
    [InlineData(nameof(CreateIssueSettings.ProjectKey))]
    [InlineData(nameof(CreateIssueSettings.IssueType))]
    [InlineData(nameof(CreateIssueSettings.Summary))]
    public async Task MissingRequired_FailsBeforeHttp(string property)
    {
        var rig = new ActionRig();
        var settings = Settings();
        typeof(CreateIssueSettings).GetProperty(property)!.SetValue(settings, "  ");

        var result = await rig.RunAsync(Action(rig), settings);

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        Assert.Empty(rig.Fake.Requests);
    }

    [Fact]
    public async Task MissingCredential_IsConfigurationError_NoRequest()
    {
        var rig = new ActionRig();
        rig.Connection = new() { SiteUrl = "https://acme.atlassian.net", CredentialName = "other", CloudId = ActionRig.CloudId };

        var result = await rig.RunAsync(Action(rig), Settings());

        Assert.Equal(StepRunErrorCategory.ConfigurationError, result.ErrorCategory);
        Assert.Empty(rig.Fake.Requests);
    }

    [Fact]
    public async Task Rotation_NextRunUsesNewToken()
    {
        var rig = new ActionRig(token: "TOKEN-ONE");
        EnqueueHappyPath(rig);
        await rig.RunAsync(Action(rig), Settings());
        var firstRunCount = rig.Fake.Requests.Count;

        rig.RotateToken("TOKEN-TWO");
        rig.RunId = Guid.NewGuid();
        rig.Enqueue(HttpStatusCode.OK, EmptySearch).EnqueueJson(HttpStatusCode.Created, CreatedJson);
        await rig.RunAsync(Action(rig), Settings());

        static string Decode(RecordedRequest r) => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(r.Authorization!["Basic ".Length..]));
        var second = rig.Fake.Requests.Skip(firstRunCount).ToList();
        Assert.NotEmpty(second);
        Assert.All(second, r => Assert.EndsWith(":TOKEN-TWO", Decode(r), StringComparison.Ordinal));
        Assert.All(rig.Fake.Requests.Take(firstRunCount), r => Assert.EndsWith(":TOKEN-ONE", Decode(r), StringComparison.Ordinal));
    }
}
