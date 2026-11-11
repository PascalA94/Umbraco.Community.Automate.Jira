using System.Text.Json;
using System.Text.Json.Nodes;
using Umbraco.Automate.Core.Actions;
using Umbraco.Community.Automate.Jira.Adf;

namespace Umbraco.Community.Automate.Jira.Actions;

/// <summary>
/// Turns a Jira webhook body into typed, bindable fields (design 6.6). Makes no HTTP calls and needs no
/// connection (spike S1: <c>ConnectionTypeAlias</c> is optional).
/// </summary>
/// <remarks>
/// The authenticator has already checked the signature over the raw bytes; this reads the stored body text
/// afterwards, so the two need not be byte-identical (design 6.6).
/// </remarks>
[Action(
    "community.jira.readWebhookEvent",
    "Read Jira webhook event",
    Description = "Reads a Jira webhook body into fields such as the issue key, status change and comment.",
    Group = "Jira",
    Icon = "icon-download-alt")]
public sealed class ReadWebhookEventAction : ActionBase<ReadWebhookEventSettings, ReadWebhookEventOutput>
{
    // A property, not a static field: JsonDocumentOptions is a mutable struct (pitfall P2, ArchitectureTests).
    private static JsonDocumentOptions ParseOptions => new() { MaxDepth = 32 };

    /// <summary>Creates the action.</summary>
    public ReadWebhookEventAction(ActionInfrastructure infrastructure)
        : base(infrastructure)
    {
    }

    /// <inheritdoc />
    public override Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        var body = context.GetSettings<ReadWebhookEventSettings>()?.Body;
        if (string.IsNullOrWhiteSpace(body))
        {
            return Task.FromResult(ActionFailures.Validation("The webhook body is empty."));
        }

        JsonDocument document;
        try
        {
            // F-11: MaxDepth 32. The core endpoint already caps the body at 1 MB.
            document = JsonDocument.Parse(body, ParseOptions);
        }
        catch (JsonException)
        {
            // The parser's message can quote the body, which is untrusted text, so it is not passed on.
            return Task.FromResult(ActionFailures.Validation("The webhook body is not valid JSON, or is nested deeper than 32 levels."));
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Task.FromResult(ActionFailures.Validation("The webhook body is not a JSON object."));
            }

            // M1 review QA-8: a repeated key could make this parser and the next reader see different values.
            if (JsonGuards.FindDuplicateKey(root) is not null)
            {
                return Task.FromResult(ActionFailures.Validation("The webhook body repeats a property name."));
            }

            if (String(root, "webhookEvent") is not { Length: > 0 } webhookEvent)
            {
                return Task.FromResult(ActionFailures.Validation("The body has no webhookEvent, so it is not a Jira webhook."));
            }

            return Task.FromResult(Success(Read(root, webhookEvent)));
        }
    }

    private static ReadWebhookEventOutput Read(JsonElement root, string webhookEvent)
    {
        var issue = Object(root, "issue");
        var fields = Object(issue, "fields");
        var user = Object(root, "user");
        var (changes, status) = ReadChanges(Object(root, "changelog"));

        return new ReadWebhookEventOutput
        {
            WebhookEvent = webhookEvent,
            IssueKey = String(issue, "key"),
            IssueId = String(issue, "id"),
            ProjectKey = String(Object(fields, "project"), "key"),
            IssueType = String(Object(fields, "issuetype"), "name"),
            Summary = String(fields, "summary"),
            Status = String(Object(fields, "status"), "name"),
            StatusChangedFrom = status?.From,
            StatusChangedTo = status?.To,
            UserAccountId = String(user, "accountId"),
            UserDisplayName = String(user, "displayName"),
            Changes = changes,
            CommentBody = ReadComment(Object(root, "comment")),
            Timestamp = root.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.Number && ts.TryGetInt64(out var value) ? value : null,
        };
    }

    // F-06: only field, fromString and toString are output; the IDs and field types are dropped.
    // QA-M3-8: the status change is the item whose fieldId is "status". The display name is used only for an item
    // with no fieldId, so a custom field an admin names "status" cannot pose as the workflow status (F-07).
    private static (List<JiraChange> Changes, JiraChange? Status) ReadChanges(JsonElement? changelog)
    {
        var changes = new List<JiraChange>();
        JiraChange? status = null;
        if (changelog is not { } log || !log.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return (changes, status);
        }

        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || String(item, "field") is not { } field)
            {
                continue;
            }

            var change = new JiraChange(field, String(item, "fromString"), String(item, "toString"));
            changes.Add(change);
            var isStatus = String(item, "fieldId") is { } fieldId ? fieldId == "status" : field == "status";
            if (isStatus && status is null)
            {
                status = change;
            }
        }

        return (changes, status);
    }

    // Spike Task 4: a comment body may arrive as a string or as ADF, so both are flattened to plain text.
    private static string? ReadComment(JsonElement? comment)
    {
        if (comment is not { } c || !c.TryGetProperty("body", out var body))
        {
            return null;
        }

        return body.ValueKind switch
        {
            JsonValueKind.String => body.GetString(),
            JsonValueKind.Object => AdfToText.Flatten(JsonNode.Parse(body.GetRawText(), documentOptions: ParseOptions)),
            _ => null,
        };
    }

    private static JsonElement? Object(JsonElement? parent, string name) =>
        parent is { ValueKind: JsonValueKind.Object } p && p.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    private static string? String(JsonElement? parent, string name) =>
        parent is { ValueKind: JsonValueKind.Object } p && p.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
