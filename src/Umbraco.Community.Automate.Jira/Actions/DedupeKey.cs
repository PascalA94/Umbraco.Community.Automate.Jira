using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Umbraco.Automate.Core.Actions;

namespace Umbraco.Community.Automate.Jira.Actions;

/// <summary>Where the dedupe marker is stored on a Jira issue.</summary>
public enum DedupeMarker
{
    /// <summary>An issue property. Not used: spike S4 found plain issue properties are not JQL-searchable.</summary>
    IssueProperty,

    /// <summary>A <c>cj-dedupe-&lt;marker&gt;</c> label, which JQL finds within a second (spike S4).</summary>
    Label,
}

/// <summary>
/// Duplicate protection for creates and comments (design 6.2, F-05, plan review P3 and P18). Automate retries a
/// failed step, and a POST that timed out may still have reached Jira, so every create first searches for its marker.
/// </summary>
internal static class DedupeKey
{
    /// <summary>Set from spike S4's probe, not user configuration.</summary>
    public const DedupeMarker MarkerKind = DedupeMarker.Label;

    /// <summary>
    /// The user's key, trimmed, when set. Otherwise <c>{AutomationId}:{RunId}:{StepId}:{IterationId or -}:{InputFingerprint}</c>:
    /// a retry of the same step and iteration resolves the same inputs and gets the same key, while different runs,
    /// steps, loop iterations or inputs get different keys, so no item silently receives another item's issue.
    /// </summary>
    public static string Effective(string? userKey, ActionContext context, IReadOnlyList<string?> identityInputs)
    {
        if (!string.IsNullOrWhiteSpace(userKey))
        {
            return userKey.Trim();
        }

        return string.Join(
            ':',
            context.AutomationId.ToString("D"),
            context.RunId.ToString("D"),
            context.StepId.ToString("D"),
            IterationId(context) ?? "-",
            Fingerprint(identityInputs));
    }

    /// <summary>
    /// The first 16 lower-case hex characters of HMAC-SHA256 over the key, keyed with the connection ID's bytes. The
    /// raw key never leaves Umbraco, and a Jira user who sees the label cannot confirm a guessed key such as an email
    /// address. It is not encryption: anyone with back-office access can recompute it.
    /// </summary>
    public static string Marker(string effectiveKey, Guid connectionId) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(connectionId.ToByteArray(), Encoding.UTF8.GetBytes(effectiveKey)))[..16];

    /// <summary>
    /// The comment marker also binds the issue (M2 security review SEC-01). Comment properties are readable by
    /// anyone who can browse an issue, so with a user key that repeats across issues, a marker copied from one
    /// issue onto a comment on another would otherwise make that step skip its post.
    /// </summary>
    public static string CommentMarker(string effectiveKey, string issueKey, Guid connectionId) =>
        Marker(issueKey + "\n" + effectiveKey, connectionId);

    // Each input is length-prefixed (a null is -1), so ("ab","c") and ("a","bc") hash differently.
    private static string Fingerprint(IReadOnlyList<string?> inputs)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[4];
        foreach (var input in inputs)
        {
            var bytes = input is null ? [] : Encoding.UTF8.GetBytes(input);
            BinaryPrimitives.WriteInt32BigEndian(length, input is null ? -1 : bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset())[..16];
    }

    // Spike S3a: inside a ForEach, BindingData["loop"] is { item, index }. Its shape is not typed, so each form
    // Automate might hand over is read defensively. A loop whose index cannot be read fails closed (F-05).
    private static string? IterationId(ActionContext context)
    {
        if (context.BindingData is null || !context.BindingData.TryGetValue("loop", out var loop) || loop is null)
        {
            return null;
        }

        return ReadIndex(loop) ?? throw new DedupeKeyUnavailableException(
            "This step runs inside a For each loop, but the loop position could not be read. Set Dedupe key to a value unique to each item, for example ${ loop.item.id }.");
    }

    private static string? ReadIndex(object loop)
    {
        object? index = loop switch
        {
            IReadOnlyDictionary<string, object?> map => map.TryGetValue("index", out var v) ? v : null,
            IDictionary<string, object?> map => map.TryGetValue("index", out var v) ? v : null,
            JsonElement { ValueKind: JsonValueKind.Object } json => json.TryGetProperty("index", out var v) ? v : null,
            System.Text.Json.Nodes.JsonObject node => node["index"] is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<JsonElement>(out var e) ? e : node["index"] is System.Text.Json.Nodes.JsonValue w && w.TryGetValue<int>(out var n) ? n : null,
            string or ValueType => null,
            _ => loop.GetType().GetProperty("index")?.GetValue(loop) ?? loop.GetType().GetProperty("Index")?.GetValue(loop),
        };

        return index switch
        {
            int i => i.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            JsonElement { ValueKind: JsonValueKind.Number } e when e.TryGetInt64(out var n) => n.ToString(CultureInfo.InvariantCulture),
            _ => null,
        };
    }
}

/// <summary>
/// Inside a For each loop whose iteration index cannot be read, a default dedupe key could give identical items the
/// same issue (F-05), so the step fails and asks for an explicit dedupe key instead.
/// </summary>
public sealed class DedupeKeyUnavailableException(string message) : Exception(message);
