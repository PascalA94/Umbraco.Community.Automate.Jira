using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Umbraco.Automate.Core.Triggers.Webhooks;
using Umbraco.Community.Automate.Jira.Credentials;

namespace Umbraco.Community.Automate.Jira.Webhooks;

/// <summary>
/// Authenticates Jira webhooks for Automate's built-in Webhook trigger: one <c>X-Hub-Signature</c> header,
/// HMAC-SHA256 over the raw body with the package-owned webhook secret, then a timestamp window and a replay
/// check (design 6.5, F-03, F-15). Anything unexpected fails closed, so an unsigned request never starts a run.
/// </summary>
/// <remarks>
/// Automate creates one instance for the authenticator collection (spike Task 3 step 3), so this class keeps no
/// state of its own; the replay guard is an injected singleton (pitfall P2).
/// Internal with a public constructor: the dependencies are internal, and DI needs only the constructor public.
/// </remarks>
[WebhookAuthenticator(
    "community.jira.signature",
    "Jira webhook secret",
    Description = "Checks Jira's X-Hub-Signature with a webhook secret set in configuration, and rejects replays.")]
internal sealed class JiraSignatureWebhookAuthenticator(
    JiraCredentialStore store,
    WebhookReplayGuard replayGuard,
    ILogger<JiraSignatureWebhookAuthenticator> logger) : WebhookAuthenticatorBase<JiraSignatureSettings>
{
    /// <summary>The header Jira signs deliveries with.</summary>
    public const string SignatureHeader = "X-Hub-Signature";

    /// <summary>Atlassian's guidance is a long random secret; shorter ones are refused (F-15).</summary>
    public const int MinimumSecretLength = 32;

    // SEC16-1: true, so Automate's endpoint reads the body asynchronously (bounded by its payload limit) before
    // Validate runs. Validate is synchronous; reading the socket here would block a thread for as long as a slow
    // client kept sending. The cost (spike Task 3 step 2): the endpoint's StreamReader drops a BOM and replaces
    // invalid UTF-8, so the text is re-encoded and any length change from Content-Length fails closed.
    /// <inheritdoc />
    public override bool RequiresBody => true;

    // The stored trigger output drops these, so a run never holds a body-plus-signature pair (design 6.5 step 7).
    /// <inheritdoc />
    public override IReadOnlyCollection<string> CredentialHeaderNames => [SignatureHeader];

    /// <inheritdoc />
    protected override bool Validate(WebhookAuthenticationContext context, JiraSignatureSettings settings)
    {
        try
        {
            return ValidateCore(context.Request, context.Body, settings);
        }
        catch (Exception ex)
        {
            // Type only: an exception message from configuration or the request could carry a value (plan P5).
            logger.LogWarning("Jira webhook rejected: the check threw {ExceptionType}.", ex.GetType().FullName);
            return false;
        }
    }

    private bool ValidateCore(HttpRequest request, string? text, JiraSignatureSettings? settings)
    {
        if (settings is null)
        {
            return Reject(request, "no authenticator settings");
        }

        // Exactly one header value; two could be checked differently by different readers (F-15).
        if (!request.Headers.TryGetValue(SignatureHeader, out var values) || values.Count != 1)
        {
            return Reject(request, "missing or repeated signature header");
        }

        if (!JiraSignature.TryParseHeader(values[0], out var signature))
        {
            return Reject(request, "malformed signature header");
        }

        if (!TryGetSecret(settings.CredentialName, out var secret))
        {
            return false;
        }

        if (string.IsNullOrEmpty(text))
        {
            return Reject(request, "empty body");
        }

        var body = Encoding.UTF8.GetBytes(text);

        // The decoded text no longer matches what was sent (a BOM or invalid UTF-8 was rewritten), so its bytes
        // are not the signed bytes. Refused rather than guessed at; Jira sends plain UTF-8.
        if (request.ContentLength is { } sent && sent != body.Length)
        {
            return Reject(request, "body was altered by decoding (BOM or invalid UTF-8)", LogLevel.Information);
        }

        if (!JiraSignature.Verify(body, secret, signature))
        {
            return Reject(request, "signature mismatch", LogLevel.Information);
        }

        // Only after the HMAC has passed, so unauthenticated traffic cannot fill the replay cache.
        if (!replayGuard.TryAccept(ReplayScope(request), body, out var reason))
        {
            return Reject(request, reason!.TrimEnd('.'), LogLevel.Information);
        }

        return true;
    }

    private bool TryGetSecret(string? credentialName, out string secret)
    {
        secret = string.Empty;

        // The store's errors name the configuration key or the allow-list entry, never a value.
        if (!store.TryGet(credentialName, JiraCredentialPart.WebhookSecret, out var value, out var error))
        {
            logger.LogWarning("Jira webhook rejected: {Error}", error);
            return false;
        }

        if (value!.Length < MinimumSecretLength)
        {
            store.TryNormaliseName(credentialName, out var normalised, out _);
            logger.LogWarning(
                "Jira webhook rejected: the secret at {Key} is shorter than {MinimumLength} characters.",
                store.KeyFor(normalised!, JiraCredentialPart.WebhookSecret),
                MinimumSecretLength);
            return false;
        }

        secret = value;
        return true;
    }

    // Spike Task 3 step 3: the route value is the automation ID, one per webhook URL. The path hash is the fallback
    // if Automate ever stops supplying it (plan P16). Both are per-automation scopes.
    private static string ReplayScope(HttpRequest request)
    {
        var routeValue = request.RouteValues.TryGetValue("automationId", out var value) ? value : null;
        if (routeValue is Guid id || Guid.TryParse(routeValue as string, out id))
        {
            return id.ToString("D");
        }

        var path = (request.Path.Value ?? string.Empty).ToLowerInvariant();
        return "path-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
    }

    // SEC16-5: malformed requests log at Debug so unauthenticated noise cannot flood the log; a bad signature or a
    // replay logs at Information. The trace identifier ties a line to the request; no header or body value is logged.
    private bool Reject(HttpRequest request, string reason, LogLevel level = LogLevel.Debug)
    {
        logger.Log(level, "Jira webhook rejected: {Reason} (request {TraceId}).", reason, request.HttpContext?.TraceIdentifier);
        return false;
    }
}
