using Umbraco.Automate.Core.Actions;

namespace Umbraco.Community.Automate.Jira.Client;

/// <summary>
/// A failed Jira call. The message follows design 7.4 and never carries a request or response body, a header or
/// the text of the underlying network exception, so it is safe to store as a step error.
/// </summary>
public sealed class JiraRequestException : Exception
{
    /// <summary>Creates the exception. There is deliberately no inner-exception parameter: network exception text can echo request details.</summary>
    public JiraRequestException(
        string message,
        StepRunErrorCategory category,
        int? statusCode = null,
        string? requestId = null,
        int? retryAfterSeconds = null,
        bool requestSent = false)
        : base(message)
    {
        Category = category;
        StatusCode = statusCode;
        RequestId = requestId;
        RetryAfterSeconds = retryAfterSeconds;
        RequestSent = requestSent;
    }

    /// <summary>The Automate error category.</summary>
    public StepRunErrorCategory Category { get; }

    /// <summary>The HTTP status, when Jira answered.</summary>
    public int? StatusCode { get; }

    /// <summary>Jira's request ID (<c>X-ARequestId</c>), when it sent a safe one.</summary>
    public string? RequestId { get; }

    /// <summary>Seconds from <c>Retry-After</c> on a 429.</summary>
    public int? RetryAfterSeconds { get; }

    /// <summary>
    /// True once the request may have reached Jira: a response of any status, a timeout, or a network error after
    /// the request started. False for validation failures and connect failures. Add comment's fallback relies on it.
    /// </summary>
    public bool RequestSent { get; }
}
