using System.Net;
using System.Text.Json;
using Umbraco.Automate.Core.Actions;

namespace Umbraco.Community.Automate.Jira.Client;

/// <summary>Maps Jira responses to <see cref="JiraRequestException"/> (design 7.4).</summary>
internal static class JiraResponseHandler
{
    internal const int MaxMessageLength = 200;

    // Jira can reply with an arbitrarily long list; a step error is not the place for all of it.
    private const int MaxMessages = 20;

    // Depth 32 wherever JSON from outside is parsed (plan Global Constraints). A property, so no static state.
    internal static JsonDocumentOptions ParseOptions => new() { MaxDepth = 32 };

    /// <summary>Cuts to <paramref name="max"/> UTF-16 units without splitting a surrogate pair.</summary>
    internal static string Truncate(string text, int max = MaxMessageLength)
    {
        if (text.Length <= max)
        {
            return text;
        }

        var cut = max;
        if (char.IsHighSurrogate(text[cut - 1]) && char.IsLowSurrogate(text[cut]))
        {
            cut--;
        }

        return text[..cut];
    }

    internal static JiraRequestException Map(HttpStatusCode status, string? body, string? requestId, int? retryAfterSeconds, string? notFoundSubject)
    {
        var code = (int)status;
        JiraRequestException Make(string message, StepRunErrorCategory category) =>
            new(message, category, code, requestId, status == (HttpStatusCode)429 ? retryAfterSeconds : null, requestSent: true);

        switch (code)
        {
            case 401:
                return Make("Jira rejected the credentials (401). The API token may have expired or been revoked.", StepRunErrorCategory.Authentication);
            case 403:
            {
                var detail = string.Join("; ", ReadMessages(body));
                var message = "The Jira account lacks permission for this action (403).";
                return Make(detail.Length == 0 ? message : $"{message} {detail}", StepRunErrorCategory.Authentication);
            }

            case 400:
            {
                var detail = string.Join("; ", ReadMessages(body));
                return Make(detail.Length == 0 ? "Jira rejected the request (400)." : detail, StepRunErrorCategory.Validation);
            }

            case 404:
                return Make($"{notFoundSubject ?? "Issue or project"} not found or no permission.", StepRunErrorCategory.Validation);
            case 429:
                return Make(
                    retryAfterSeconds is { } seconds ? $"Jira rate limit hit. Retry after {seconds} seconds." : "Jira rate limit hit.",
                    StepRunErrorCategory.RateLimiting);
            case >= 300 and < 400:
                return Make("Unexpected redirect from Jira.", StepRunErrorCategory.InvalidResponse);
            case >= 500:
                return Make($"Jira is unavailable ({code}).", StepRunErrorCategory.ServiceUnavailable);
            default:
                return Make($"Unexpected response from Jira ({code}).", StepRunErrorCategory.InvalidResponse);
        }
    }

    /// <summary>A timeout or network failure. Names the host only: the cause's text can echo request details.</summary>
    internal static JiraRequestException Unreachable(string host, bool requestSent) =>
        new($"Couldn't reach Jira ({host}).", StepRunErrorCategory.ServiceUnavailable, requestSent: requestSent);

    internal static JiraRequestException InvalidResponse() =>
        new("Unexpected response from Jira.", StepRunErrorCategory.InvalidResponse, requestSent: true);

    // Jira's shape is { "errorMessages": [...], "errors": { "field": "msg" } }. Jira can echo submitted values in
    // either (F-06), so each entry is cut to 200 characters. Anything that is not that shape yields nothing,
    // and the raw body is never used as a message.
    private static List<string> ReadMessages(string? body)
    {
        var messages = new List<string>();
        if (string.IsNullOrWhiteSpace(body))
        {
            return messages;
        }

        try
        {
            using var doc = JsonDocument.Parse(body, ParseOptions);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return messages;
            }

            if (doc.RootElement.TryGetProperty("errorMessages", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && messages.Count < MaxMessages)
                    {
                        messages.Add(Truncate(item.GetString()!));
                    }
                }
            }

            if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                foreach (var field in errors.EnumerateObject())
                {
                    if (field.Value.ValueKind == JsonValueKind.String && messages.Count < MaxMessages)
                    {
                        messages.Add($"{Truncate(field.Name)}: {Truncate(field.Value.GetString()!)}");
                    }
                }
            }
        }
        catch (JsonException)
        {
            // An unparseable error body is treated as no detail rather than a second failure.
        }

        return messages;
    }
}
