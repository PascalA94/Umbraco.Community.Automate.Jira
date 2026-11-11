using System.Reflection;
using Json.Schema.Generation;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Umbraco.Automate.Core.Actions;
using Umbraco.Automate.Core.Settings;
using Umbraco.Community.Automate.Jira.Actions;

namespace Umbraco.Community.Automate.Jira.Tests;

public class ReadWebhookEventActionTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "webhooks", name));

    private static async Task<ActionResult> RunAsync(string? body)
    {
        // No IJiraClient or Jira services anywhere: the action must not need them.
        var action = new ReadWebhookEventAction(new ActionInfrastructure(Mock.Of<IEditableModelResolver>()));
        var context = new ActionContext
        {
            AutomationId = Guid.NewGuid(),
            RunId = Guid.NewGuid(),
            StepId = Guid.NewGuid(),
            ActionAlias = action.Alias,
            Settings = new ReadWebhookEventSettings { Body = body! },
            Action = action,
            BindingData = new Dictionary<string, object?>(),
        };
        return await action.ExecuteAsync(context, CancellationToken.None);
    }

    private static async Task<ReadWebhookEventOutput> ParseAsync(string body)
    {
        var result = await RunAsync(body);
        Assert.Equal(ActionResultStatus.Success, result.Status);
        return Assert.IsType<ReadWebhookEventOutput>(result.OutputData);
    }

    private static async Task AssertValidationAsync(string? body)
    {
        var result = await RunAsync(body);
        Assert.NotEqual(ActionResultStatus.Success, result.Status);
        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
    }

    [Fact]
    public void Alias_AndNoConnection()
    {
        var attribute = typeof(ReadWebhookEventAction).GetCustomAttribute<ActionAttribute>()!;

        Assert.Equal("community.jira.readWebhookEvent", attribute.Alias);
        Assert.Equal("Read Jira webhook event", attribute.Name);

        // Spike S1: ConnectionTypeAlias is optional, so the parser needs no Jira connection.
        Assert.Null(attribute.ConnectionTypeAlias);
    }

    [Fact]
    public void Settings_BodyDefaultsToTriggerBody()
    {
        Assert.Equal("${ trigger.body }", new ReadWebhookEventSettings().Body);
        var field = typeof(ReadWebhookEventSettings).GetProperty(nameof(ReadWebhookEventSettings.Body))!.GetCustomAttribute<FieldAttribute>()!;
        Assert.True(field.SupportsBindings);
    }

    [Fact]
    public async Task IssueUpdated_StatusChange_SetsStatusChangedTo()
    {
        var output = await ParseAsync(Fixture("issue_updated_status.json"));

        Assert.Equal("jira:issue_updated", output.WebhookEvent);
        Assert.Equal("To Do", output.StatusChangedFrom);
        Assert.Equal("Ready to publish", output.StatusChangedTo);
        Assert.Equal("Ready to publish", output.Status);
    }

    // F-07: a comment or label edit while the issue sits in the status must not look like a status change.
    [Fact]
    public async Task IssueUpdated_NoStatusChange_StatusChangedToNull()
    {
        var output = await ParseAsync(Fixture("issue_updated_comment_only.json"));

        Assert.Null(output.StatusChangedFrom);
        Assert.Null(output.StatusChangedTo);
        Assert.Empty(output.Changes);
        Assert.Equal("Copy approved by marketing.", output.CommentBody);
    }

    [Fact]
    public async Task CommentCreated_SetsPlainCommentBodyAndIssueKey()
    {
        var output = await ParseAsync(Fixture("comment_created.json"));

        Assert.Equal("comment_created", output.WebhookEvent);
        Assert.Equal("SBX-17", output.IssueKey);
        Assert.Equal("Copy approved by marketing.", output.CommentBody);
    }

    [Fact]
    public async Task CommentUpdated_SetsPlainCommentBodyAndIssueKey()
    {
        var output = await ParseAsync(Fixture("comment_updated.json"));

        Assert.Equal("comment_updated", output.WebhookEvent);
        Assert.Equal("SBX-17", output.IssueKey);
        Assert.Equal("Copy approved by marketing. Hero image swapped.", output.CommentBody);
    }

    // Spike Task 4 left open whether comment bodies arrive as a string or as ADF, so both are accepted.
    [Fact]
    public async Task CommentBodyAsAdf_IsFlattenedToPlainText()
    {
        const string body = """
            {"webhookEvent":"comment_created","issue":{"key":"SBX-3"},
             "comment":{"body":{"type":"doc","version":1,"content":[
               {"type":"paragraph","content":[{"type":"text","text":"Hello "},{"type":"text","text":"there","marks":[{"type":"strong"}]}]},
               {"type":"paragraph","content":[{"type":"text","text":"Second line"}]}]}}}
            """;

        var output = await ParseAsync(body);

        Assert.Contains("Hello there", output.CommentBody);
        Assert.Contains("Second line", output.CommentBody);
        Assert.DoesNotContain("strong", output.CommentBody);
    }

    [Fact]
    public async Task IssueCreated_CommonFields()
    {
        var output = await ParseAsync(Fixture("issue_created.json"));

        Assert.Equal("jira:issue_created", output.WebhookEvent);
        Assert.Equal("SBX-17", output.IssueKey);
        Assert.Equal("10017", output.IssueId);
        Assert.Equal("SBX", output.ProjectKey);
        Assert.Equal("Task", output.IssueType);
        Assert.Equal("Publish the spring campaign page", output.Summary);
        Assert.NotNull(output.UserAccountId);
        Assert.NotNull(output.UserDisplayName);
    }

    [Fact]
    public async Task UnknownEvent_StillParsesCommonFields()
    {
        var output = await ParseAsync("""{"webhookEvent":"jira:issue_deleted","timestamp":5,"issue":{"id":"9","key":"ABC-9","fields":{"summary":"S"}}}""");

        Assert.Equal("jira:issue_deleted", output.WebhookEvent);
        Assert.Equal("ABC-9", output.IssueKey);
        Assert.Equal("S", output.Summary);
    }

    [Fact]
    public async Task MissingOptionalParts_AreNull()
    {
        var output = await ParseAsync("""{"webhookEvent":"jira:issue_updated"}""");

        Assert.Null(output.IssueKey);
        Assert.Null(output.IssueId);
        Assert.Null(output.ProjectKey);
        Assert.Null(output.Summary);
        Assert.Null(output.Status);
        Assert.Null(output.UserAccountId);
        Assert.Null(output.UserDisplayName);
        Assert.Null(output.CommentBody);
        Assert.Null(output.Timestamp);
        Assert.Null(output.StatusChangedTo);
        Assert.Empty(output.Changes);
    }

    // Tolerant lookups: a part of the wrong shape is treated as missing, not as a crash.
    [Fact]
    public async Task WrongShapedParts_AreNull()
    {
        var output = await ParseAsync("""{"webhookEvent":"jira:issue_updated","issue":"x","user":[1],"changelog":{"items":"no"},"comment":5,"timestamp":"12"}""");

        Assert.Null(output.IssueKey);
        Assert.Null(output.UserAccountId);
        Assert.Empty(output.Changes);
        Assert.Null(output.CommentBody);
        Assert.Null(output.Timestamp);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("[1,2]")]
    [InlineData("\"text\"")]
    public async Task NotJson_FailsValidation(string? body)
    {
        await AssertValidationAsync(body);
    }

    [Theory]
    [InlineData("""{"issue":{"key":"A-1"}}""")]
    [InlineData("""{"webhookEvent":""}""")]
    [InlineData("""{"webhookEvent":5}""")]
    public async Task NoWebhookEvent_FailsValidation(string body)
    {
        await AssertValidationAsync(body);
    }

    [Fact]
    public async Task DeeplyNestedJson_FailsValidation()
    {
        await AssertValidationAsync($$"""{"webhookEvent":"x","deep":{{new string('[', 40)}}{{new string(']', 40)}}}""");
    }

    // Carried from M1 review QA-8: a repeated key could make two readers see different values.
    [Theory]
    [InlineData("""{"webhookEvent":"a","webhookEvent":"b"}""")]
    [InlineData("""{"webhookEvent":"a","comment":{"body":"x","body":"y"}}""")]
    [InlineData("""{"webhookEvent":"a","issue":{"key":"A-1","key":"A-2"}}""")]
    public async Task DuplicateKey_FailsValidation(string body)
    {
        await AssertValidationAsync(body);
    }

    [Fact]
    public async Task DuplicateKey_MessageDoesNotEchoTheBody()
    {
        var result = await RunAsync("""{"webhookEvent":"a","secretish-name":"1","secretish-name":"2"}""");

        Assert.DoesNotContain("secretish", result.Exception?.Message ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Timestamp_Parsed()
    {
        var output = await ParseAsync(Fixture("issue_updated_status.json"));

        Assert.Equal(1762248600000, output.Timestamp);
    }

    [Fact]
    public async Task Changes_OnlyFieldFromTo()
    {
        var output = await ParseAsync(Fixture("issue_updated_status.json"));

        var change = Assert.Single(output.Changes);
        Assert.Equal(new JiraChange("status", "To Do", "Ready to publish"), change);
        Assert.Equal(["Field", "From", "To"], typeof(JiraChange).GetProperties().Select(p => p.Name).Order().ToArray());
    }

    [Fact]
    public async Task Changes_MultipleItems_StatusPickedByField()
    {
        var output = await ParseAsync("""
            {"webhookEvent":"jira:issue_updated","changelog":{"items":[
              {"field":"labels","fromString":"","toString":"web"},
              {"field":"status","fromString":"In Progress","toString":"Done","from":"3","to":"10001"},
              {"field":"summary","fromString":null,"toString":"New"}]}}
            """);

        Assert.Equal(3, output.Changes.Count);
        Assert.Equal("In Progress", output.StatusChangedFrom);
        Assert.Equal("Done", output.StatusChangedTo);
        Assert.Null(output.Changes[2].From);
    }

    // QA-M3-8: a custom field an admin names "status" must not stand in for the real status change (F-07).
    [Fact]
    public async Task StatusChange_PrefersFieldIdOverName()
    {
        var output = await ParseAsync("""
            {"webhookEvent":"jira:issue_updated","changelog":{"items":[
              {"field":"status","fieldId":"customfield_10050","fromString":"x","toString":"Ready to publish"},
              {"field":"labels","fieldId":"labels","fromString":"","toString":"web"}]}}
            """);

        Assert.Null(output.StatusChangedFrom);
        Assert.Null(output.StatusChangedTo);
        Assert.Equal(2, output.Changes.Count);
    }

    [Fact]
    public async Task StatusChange_ByFieldId_IsFound()
    {
        var output = await ParseAsync("""
            {"webhookEvent":"jira:issue_updated","changelog":{"items":[
              {"field":"status","fieldId":"customfield_10050","fromString":"x","toString":"y"},
              {"field":"Status","fieldId":"status","fromString":"To Do","toString":"Ready to publish"}]}}
            """);

        Assert.Equal("To Do", output.StatusChangedFrom);
        Assert.Equal("Ready to publish", output.StatusChangedTo);
    }

    // Design 6.6: only the listed fields are output (F-06); nothing like a raw field dump.
    [Fact]
    public void Output_HasOnlyTheDesignFields()
    {
        string[] expected =
        [
            "Changes", "CommentBody", "IssueId", "IssueKey", "IssueType", "ProjectKey", "Status", "StatusChangedFrom",
            "StatusChangedTo", "Summary", "Timestamp", "UserAccountId", "UserDisplayName", "WebhookEvent",
        ];

        Assert.Equal(expected, typeof(ReadWebhookEventOutput).GetProperties().Select(p => p.Name).Order().ToArray());
    }

    // F-17: text any Jira user controls is labelled as such where editors bind it.
    [Theory]
    [InlineData(nameof(ReadWebhookEventOutput.Summary))]
    [InlineData(nameof(ReadWebhookEventOutput.CommentBody))]
    [InlineData(nameof(ReadWebhookEventOutput.UserDisplayName))]
    [InlineData(nameof(ReadWebhookEventOutput.Changes))]
    public void UntrustedFields_DescriptionsSayUntrusted(string property)
    {
        var description = typeof(ReadWebhookEventOutput).GetProperty(property)!.GetCustomAttribute<DescriptionAttribute>();

        Assert.NotNull(description);
        Assert.Contains("untrusted", description!.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MakesNoHttpCalls()
    {
        // The action takes only ActionInfrastructure; with no service provider at all it still parses.
        Assert.Single(typeof(ReadWebhookEventAction).GetConstructors());
        Assert.All(typeof(ReadWebhookEventAction).GetConstructors()[0].GetParameters(), p => Assert.Equal(typeof(ActionInfrastructure), p.ParameterType));

        var output = await ParseAsync(Fixture("issue_created.json"));
        Assert.Equal("SBX-17", output.IssueKey);
    }
}
