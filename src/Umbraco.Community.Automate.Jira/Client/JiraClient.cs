using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Umbraco.Automate.Core.Actions;
using Umbraco.Community.Automate.Jira.Connection;

namespace Umbraco.Community.Automate.Jira.Client;

/// <summary>
/// Talks to Jira Cloud REST API v3 through the named client. The <c>Authorization</c> header is set on each
/// request message from the <see cref="JiraTarget"/>, never on the client, so a rotated credential reaches the
/// next request (plan review P4).
/// </summary>
internal sealed class JiraClient(IHttpClientFactory httpClientFactory, ILogger<JiraClient> logger, TimeProvider timeProvider) : IJiraClient
{
    /// <summary>The named <see cref="HttpClient"/> every Jira call goes through.</summary>
    public const string HttpClientName = "Umbraco.Community.Automate.Jira";

    // A response larger than this is not something Jira sends for the calls this package makes.
    private const int MaxResponseBytes = 4 * 1024 * 1024;

    // GETs retry once on 429, and only when Jira asks for a short wait; longer waits belong to the step's retry policy.
    private const int MaxRetryAfterSecondsToRetry = 5;

    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<JiraUser> GetMyselfAsync(JiraTarget target, CancellationToken ct)
    {
        using var doc = await SendAsync(target, HttpMethod.Get, "/rest/api/3/myself", ct: ct) ?? throw JiraResponseHandler.InvalidResponse();

        var accountId = ReadString(doc.RootElement, "accountId");
        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw JiraResponseHandler.InvalidResponse();
        }

        var displayName = ReadString(doc.RootElement, "displayName");
        return new JiraUser(accountId, JiraResponseHandler.SafeText(string.IsNullOrWhiteSpace(displayName) ? accountId : displayName));
    }

    public async Task<Guid?> GetTenantCloudIdAsync(Uri siteBase, CancellationToken ct)
    {
        // This route is unauthenticated, so the only thing it can leak is a request to a host we chose.
        // The same validation as the connection stops it being used to fetch an arbitrary URL.
        Uri? site = null;
        string? error = null;
        if (!siteBase.IsAbsoluteUri
            || !JiraSiteUrl.TryValidateExact(siteBase.GetLeftPart(UriPartial.Authority), out site, out error))
        {
            throw new JiraRequestException(error ?? "Site URL must look like https://your-site.atlassian.net.", StepRunErrorCategory.Validation, requestSent: false);
        }

        JsonDocument? doc;
        try
        {
            doc = await ExecuteAsync(site!.Host, HttpMethod.Get, BuildUri(site, "/_edge/tenant_info", null, null), "/_edge/tenant_info", authorization: null, body: null, notFoundSubject: null, ct);
        }
        catch (JiraRequestException ex) when (ex.StatusCode == 404)
        {
            return null;
        }

        using (doc)
        {
            return JiraSiteUrl.TryParseCloudId(doc is null ? null : ReadString(doc.RootElement, "cloudId"), out var cloudId, out _) ? cloudId : null;
        }
    }

    public async Task<IReadOnlyList<JiraIssueType>> GetIssueTypesAsync(JiraTarget target, string projectKey, CancellationToken ct)
    {
        using var doc = await SendAsync(
            target,
            HttpMethod.Get,
            "/rest/api/3/issue/createmeta/{project}/issuetypes",
            new Dictionary<string, string> { ["project"] = projectKey },
            query: "maxResults=200",
            notFoundSubject: "Project",
            ct: ct) ?? throw JiraResponseHandler.InvalidResponse();

        // Atlassian's docs name the list "issueTypes"; some responses use the paged "values" shape.
        var root = doc.RootElement;
        JsonElement list = default;
        if (root.ValueKind != JsonValueKind.Object
            || !((root.TryGetProperty("issueTypes", out list) || root.TryGetProperty("values", out list)) && list.ValueKind == JsonValueKind.Array))
        {
            throw JiraResponseHandler.InvalidResponse();
        }

        var types = new List<JiraIssueType>();
        foreach (var item in list.EnumerateArray())
        {
            var id = ReadString(item, "id");
            var name = ReadString(item, "name");
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
            {
                types.Add(new JiraIssueType(id, JiraResponseHandler.SafeText(name)));
            }
        }

        return types;
    }

    public async Task<JiraCreatedIssue> CreateIssueAsync(JiraTarget target, JsonObject fields, JsonArray? properties, CancellationToken ct)
    {
        // Cloned so the caller's objects never get a second parent.
        var body = new JsonObject { ["fields"] = fields.DeepClone() };
        if (properties is { Count: > 0 })
        {
            body["properties"] = properties.DeepClone();
        }

        // Never retried here: a POST that may have reached Jira is only retried by Automate, after the dedupe search.
        using var doc = await SendAsync(target, HttpMethod.Post, "/rest/api/3/issue", body: body, notFoundSubject: "Project", ct: ct)
            ?? throw JiraResponseHandler.InvalidResponse();
        return ReadIssue(doc.RootElement) ?? throw JiraResponseHandler.InvalidResponse();
    }

    public async Task<JiraCreatedIssue?> FindByDedupeAsync(JiraTarget target, string projectKey, string marker, CancellationToken ct)
    {
        // The marker is 16 lower-case hex characters; anything else would not be ours.
        if (marker.Length != 16 || !marker.All(char.IsAsciiHexDigitLower))
        {
            throw new ArgumentException("A dedupe marker is 16 lower-case hex characters.", nameof(marker));
        }

        var jql = $"project = {JiraIdentifiers.QuoteJql(projectKey)} AND labels = {JiraIdentifiers.QuoteJql("cj-dedupe-" + marker)}";
        var body = new JsonObject { ["jql"] = jql, ["maxResults"] = 1, ["fields"] = new JsonArray("key") };

        // A failure here throws, and the action then fails without creating anything (F-05).
        using var doc = await SendAsync(target, HttpMethod.Post, "/rest/api/3/search/jql", body: body, ct: ct)
            ?? throw JiraResponseHandler.InvalidResponse();

        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("issues", out var issues)
            || issues.ValueKind != JsonValueKind.Array)
        {
            throw JiraResponseHandler.InvalidResponse();
        }

        foreach (var issue in issues.EnumerateArray())
        {
            return ReadIssue(issue) ?? throw JiraResponseHandler.InvalidResponse();
        }

        return null;
    }

    private static JiraCreatedIssue? ReadIssue(JsonElement element)
    {
        var id = ReadString(element, "id");
        var key = ReadString(element, "key");

        // The key ends up in a URL and in later steps' bindings, so it must look like a key.
        return string.IsNullOrWhiteSpace(id) || !JiraIdentifiers.IsNumericId(id) || !JiraIdentifiers.TryIssueKey(key, out var validKey, out _)
            ? null
            : new JiraCreatedIssue(id, validKey!);
    }

    /// <summary>The comment property key that carries the dedupe marker (design 6.3).</summary>
    internal const string CommentDedupePropertyKey = "community.jira.dedupe";

    public async Task<JiraCreatedComment> AddCommentAsync(JiraTarget target, string issueKey, JsonObject adfBody, string? dedupeMarker, CancellationToken ct)
    {
        var body = new JsonObject { ["body"] = adfBody.DeepClone() };
        if (dedupeMarker is not null)
        {
            EnsureMarker(dedupeMarker);
            body["properties"] = new JsonArray(new JsonObject
            {
                ["key"] = CommentDedupePropertyKey,
                ["value"] = new JsonObject { ["key"] = dedupeMarker },
            });
        }

        // Never retried here, as with creates: only Automate retries, after the read-first listing.
        using var doc = await SendAsync(
            target,
            HttpMethod.Post,
            "/rest/api/3/issue/{key}/comment",
            new Dictionary<string, string> { ["key"] = issueKey },
            body: body,
            notFoundSubject: "Issue " + issueKey,
            ct: ct) ?? throw JiraResponseHandler.InvalidResponse();

        return ReadComment(doc.RootElement) ?? throw JiraResponseHandler.InvalidResponse();
    }

    public async Task<JiraCreatedComment?> FindCommentByDedupeAsync(JiraTarget target, string issueKey, string dedupeMarker, CancellationToken ct)
    {
        EnsureMarker(dedupeMarker);

        // A failure here throws, and the action then fails without posting (F-05).
        using var doc = await SendAsync(
            target,
            HttpMethod.Get,
            "/rest/api/3/issue/{key}/comment",
            new Dictionary<string, string> { ["key"] = issueKey },
            query: "orderBy=-created&maxResults=50&expand=properties",
            notFoundSubject: "Issue " + issueKey,
            ct: ct) ?? throw JiraResponseHandler.InvalidResponse();

        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("comments", out var comments)
            || comments.ValueKind != JsonValueKind.Array)
        {
            throw JiraResponseHandler.InvalidResponse();
        }

        // The listing is newest first. Anyone who can edit a comment can copy the marker onto it, but only onto a
        // comment newer than the one this step wrote, so the oldest match wins (M2 security review SEC-01).
        JsonElement? oldest = null;
        foreach (var comment in comments.EnumerateArray())
        {
            if (HasMarker(comment, dedupeMarker))
            {
                oldest = comment;
            }
        }

        // Ours by marker but unreadable: posting again could duplicate it, so fail instead.
        return oldest is { } match ? ReadComment(match) ?? throw JiraResponseHandler.InvalidResponse() : null;
    }

    private static bool HasMarker(JsonElement comment, string marker)
    {
        if (comment.ValueKind != JsonValueKind.Object
            || !comment.TryGetProperty("properties", out var properties)
            || properties.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var property in properties.EnumerateArray())
        {
            if (ReadString(property, "key") == CommentDedupePropertyKey
                && property.TryGetProperty("value", out var value)
                && ReadString(value, "key") == marker)
            {
                return true;
            }
        }

        return false;
    }

    private static JiraCreatedComment? ReadComment(JsonElement element)
    {
        var id = ReadString(element, "id");
        return JiraIdentifiers.IsNumericId(id) ? new JiraCreatedComment(id!.Trim()) : null;
    }

    // The marker is 16 lower-case hex characters; anything else would not be ours.
    private static void EnsureMarker(string marker)
    {
        if (marker.Length != 16 || !marker.All(char.IsAsciiHexDigitLower))
        {
            throw new ArgumentException("A dedupe marker is 16 lower-case hex characters.", nameof(marker));
        }
    }

    public async Task<string> GetIssueStatusAsync(JiraTarget target, string issueKey, CancellationToken ct)
    {
        using var doc = await SendAsync(
            target,
            HttpMethod.Get,
            "/rest/api/3/issue/{key}",
            new Dictionary<string, string> { ["key"] = issueKey },
            query: "fields=status",
            notFoundSubject: "Issue " + issueKey,
            ct: ct) ?? throw JiraResponseHandler.InvalidResponse();

        var name = doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("fields", out var fields)
            && fields.ValueKind == JsonValueKind.Object
            && fields.TryGetProperty("status", out var status)
                ? ReadString(status, "name")
                : null;

        return string.IsNullOrWhiteSpace(name) ? throw JiraResponseHandler.InvalidResponse() : JiraResponseHandler.SafeText(name);
    }

    public async Task<IReadOnlyList<JiraTransition>> GetTransitionsAsync(JiraTarget target, string issueKey, CancellationToken ct)
    {
        using var doc = await SendAsync(
            target,
            HttpMethod.Get,
            "/rest/api/3/issue/{key}/transitions",
            new Dictionary<string, string> { ["key"] = issueKey },
            notFoundSubject: "Issue " + issueKey,
            ct: ct) ?? throw JiraResponseHandler.InvalidResponse();

        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("transitions", out var list)
            || list.ValueKind != JsonValueKind.Array)
        {
            throw JiraResponseHandler.InvalidResponse();
        }

        var transitions = new List<JiraTransition>();
        foreach (var item in list.EnumerateArray())
        {
            var id = ReadString(item, "id");
            var name = ReadString(item, "name");
            var to = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("to", out var toElement) ? ReadString(toElement, "name") : null;

            // The ID goes back to Jira in a request body, so only a numeric one is kept.
            if (JiraIdentifiers.IsNumericId(id) && !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(to))
            {
                transitions.Add(new JiraTransition(id!.Trim(), JiraResponseHandler.SafeText(name), JiraResponseHandler.SafeText(to)));
            }
        }

        return transitions;
    }

    public async Task DoTransitionAsync(JiraTarget target, string issueKey, string transitionId, JsonObject? commentAdf, string? resolutionName, CancellationToken ct)
    {
        if (!JiraIdentifiers.IsNumericId(transitionId) || transitionId.Trim() != transitionId)
        {
            throw new ArgumentException("A transition ID is numeric.", nameof(transitionId));
        }

        var body = new JsonObject { ["transition"] = new JsonObject { ["id"] = transitionId } };
        if (commentAdf is not null)
        {
            body["update"] = new JsonObject
            {
                ["comment"] = new JsonArray(new JsonObject { ["add"] = new JsonObject { ["body"] = commentAdf.DeepClone() } }),
            };
        }

        if (resolutionName is not null)
        {
            body["fields"] = new JsonObject { ["resolution"] = new JsonObject { ["name"] = resolutionName } };
        }

        // Jira answers 204 with no body. Never retried here; the action's already-in-status check makes a retry safe.
        using var doc = await SendAsync(
            target,
            HttpMethod.Post,
            "/rest/api/3/issue/{key}/transitions",
            new Dictionary<string, string> { ["key"] = issueKey },
            body: body,
            notFoundSubject: "Issue " + issueKey,
            ct: ct);
    }

    public async Task<IReadOnlyList<string>> GetHeldPermissionsAsync(JiraTarget target, IReadOnlyList<string> keys, CancellationToken ct)
    {
        // Permission keys are Jira constants chosen by this package, never user input; the check keeps it that way.
        if (keys.Count == 0 || keys.Any(k => k.Length == 0 || !k.All(c => char.IsAsciiLetterUpper(c) || c == '_')))
        {
            throw new ArgumentException("Permission keys must be upper-case Jira permission constants.", nameof(keys));
        }

        var query = "permissions=" + Uri.EscapeDataString(string.Join(',', keys));
        using var doc = await SendAsync(target, HttpMethod.Get, "/rest/api/3/mypermissions", query: query, ct: ct) ?? throw JiraResponseHandler.InvalidResponse();

        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("permissions", out var permissions)
            || permissions.ValueKind != JsonValueKind.Object)
        {
            throw JiraResponseHandler.InvalidResponse();
        }

        var held = new List<string>();
        foreach (var key in keys)
        {
            if (permissions.TryGetProperty(key, out var entry)
                && entry.ValueKind == JsonValueKind.Object
                && entry.TryGetProperty("havePermission", out var have)
                && have.ValueKind == JsonValueKind.True)
            {
                held.Add(key);
            }
        }

        return held;
    }

    /// <summary>
    /// Builds a request URI. Each <c>{name}</c> in the template is replaced by the escaped segment, so raw input is
    /// never concatenated into a path. <paramref name="query"/> must already be escaped by the caller.
    /// </summary>
    internal static Uri BuildUri(JiraTarget target, string pathTemplate, IReadOnlyDictionary<string, string>? segments, string? query) =>
        BuildUri(target.ApiBase, pathTemplate, segments, query);

    private static Uri BuildUri(Uri apiBase, string pathTemplate, IReadOnlyDictionary<string, string>? segments, string? query)
    {
        var path = pathTemplate;
        foreach (var (name, value) in segments ?? new Dictionary<string, string>())
        {
            // A dot segment would be normalised by Uri and climb out of the intended path.
            if (value.Length == 0 || value is "." or "..")
            {
                throw new JiraRequestException("A Jira identifier was empty or invalid.", StepRunErrorCategory.Validation);
            }

            path = path.Replace("{" + name + "}", Uri.EscapeDataString(value), StringComparison.Ordinal);
        }

        if (path.Contains('{', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A path template placeholder was not supplied.");
        }

        var relative = path.TrimStart('/') + (query is null ? string.Empty : "?" + query);
        return new Uri(apiBase, relative);
    }

    /// <summary>
    /// Sends one request to <see cref="JiraTarget.ApiBase"/> and returns the parsed JSON, or null for an empty
    /// success body. Later tasks build every call on this. The caller disposes the document.
    /// </summary>
    internal Task<JsonDocument?> SendAsync(
        JiraTarget target,
        HttpMethod method,
        string pathTemplate,
        IReadOnlyDictionary<string, string>? segments = null,
        string? query = null,
        JsonNode? body = null,
        string? notFoundSubject = null,
        CancellationToken ct = default) =>
        ExecuteAsync(
            target.SiteBase.Host,
            method,
            BuildUri(target, pathTemplate, segments, query),
            pathTemplate,
            new AuthenticationHeaderValue("Basic", target.AuthorizationParameter),
            body,
            notFoundSubject,
            ct);

    private async Task<JsonDocument?> ExecuteAsync(
        string siteHost,
        HttpMethod method,
        Uri uri,
        string pathTemplate,
        AuthenticationHeaderValue? authorization,
        JsonNode? body,
        string? notFoundSubject,
        CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            // A fresh message per attempt: a sent message cannot be reused.
            using var request = new HttpRequestMessage(method, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Authorization = authorization;
            if (body is not null)
            {
                request.Content = new StringContent(body.ToJsonString(WriteOptions), Encoding.UTF8, "application/json");
            }

            HttpResponseMessage? received = null;
            HttpResponseMessage response;
            string? text;
            try
            {
                using var http = httpClientFactory.CreateClient(HttpClientName);
                received = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                text = await ReadBoundedAsync(received, ct);
                response = received;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                received?.Dispose();
                throw;
            }
            catch (IOException)
            {
                // The body broke off after Jira answered (HttpIOException is an IOException, not an
                // HttpRequestException). Jira may have acted, so this is "sent", and the text is never passed on.
                received?.Dispose();
                LogFailure(method, uri.Host, pathTemplate, "response body interrupted");
                throw JiraResponseHandler.Unreachable(siteHost, requestSent: true);
            }
            catch (OperationCanceledException)
            {
                received?.Dispose();

                // Not the caller's token, so the HttpClient timeout fired. It may have fired after Jira acted.
                LogFailure(method, uri.Host, pathTemplate, "timeout");
                throw JiraResponseHandler.Unreachable(siteHost, requestSent: true);
            }
            catch (HttpRequestException ex)
            {
                received?.Dispose();
                // The enum is a fixed vocabulary; ex.Message is not, and can echo request details.
                LogFailure(method, uri.Host, pathTemplate, $"network error ({ex.HttpRequestError})");
                throw JiraResponseHandler.Unreachable(siteHost, requestSent: !IsConnectFailure(ex.HttpRequestError));
            }

            using (response)
            {
                var status = (int)response.StatusCode;
                var requestId = ReadRequestId(response);
                logger.Log(
                    status >= 400 ? LogLevel.Warning : LogLevel.Information,
                    "Jira {Method} {Host} {PathTemplate} responded {Status} (request {RequestId})",
                    method.Method,
                    uri.Host,
                    pathTemplate,
                    status,
                    requestId ?? "-");

                if (status is >= 200 and < 300)
                {
                    return ParseSuccess(text);
                }

                var retryAfter = ReadRetryAfterSeconds(response);
                if (status == 429 && method == HttpMethod.Get && attempt == 0 && retryAfter is <= MaxRetryAfterSecondsToRetry)
                {
                    await Task.Delay(TimeSpan.FromSeconds(retryAfter.Value), timeProvider, ct);
                    continue;
                }

                throw JiraResponseHandler.Map(response.StatusCode, text, requestId, retryAfter, notFoundSubject);
            }
        }
    }

    private static JsonDocument? ParseSuccess(string? text)
    {
        if (text is null)
        {
            // Oversized.
            throw JiraResponseHandler.InvalidResponse();
        }

        if (text.Length == 0)
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(text, JiraResponseHandler.ParseOptions);
        }
        catch (JsonException)
        {
            throw JiraResponseHandler.InvalidResponse();
        }
    }

    // Returns null for a body over the cap, so the caller can fail it as an invalid response (the status mapping
    // still runs on error bodies, which are only ever read for their message lists).
    private static async Task<string?> ReadBoundedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > MaxResponseBytes)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static bool IsConnectFailure(HttpRequestError error) =>
        error is HttpRequestError.ConnectionError
            or HttpRequestError.NameResolutionError
            or HttpRequestError.SecureConnectionError
            or HttpRequestError.ProxyTunnelError;

    private static string? ReadRequestId(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("X-ARequestId", out var values))
        {
            return null;
        }

        var value = values.FirstOrDefault();

        // The value lands in logs and messages, so only a conservative alphabet is trusted (no log forging).
        return value is { Length: > 0 and <= 64 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
            ? value
            : null;
    }

    private int? ReadRetryAfterSeconds(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
        {
            return (int)Math.Clamp(Math.Ceiling(delta.TotalSeconds), 0, int.MaxValue);
        }

        if (header?.Date is { } date)
        {
            return (int)Math.Clamp(Math.Ceiling((date - timeProvider.GetUtcNow()).TotalSeconds), 0, int.MaxValue);
        }

        return null;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private void LogFailure(HttpMethod method, string host, string pathTemplate, string failure) =>
        logger.LogWarning("Jira {Method} {Host} {PathTemplate} failed: {Failure}", method.Method, host, pathTemplate, failure);
}
